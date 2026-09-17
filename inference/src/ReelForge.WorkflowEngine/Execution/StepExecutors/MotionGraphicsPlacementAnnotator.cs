using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using ReelForge.Shared.Data.OutputSchemas;
using ReelForge.Shared.Workflows;
using ReelForge.WorkflowEngine.Services.Storage;
using ReelForge.WorkflowEngine.Services.Video;

namespace ReelForge.WorkflowEngine.Execution.StepExecutors;

/// <summary>
/// Annotates each <c>view.placements</c> entry the <c>AgentType.MotionGraphicsPlanner</c> step is
/// about to be shown with a boolean <c>inEdit</c> flag: does this placement candidate's
/// server-resolved source-timeline window survive the cut the story editor already decided?
///
/// <para>
/// <b>Why this lives here and not in <see cref="VideoAnalyzeStepExecutor"/>.</b> The placement
/// candidates themselves (<c>view.placements</c>, ids <c>p{n}</c>) are built by the
/// <c>VideoAnalyze</c> step, which runs BEFORE the story editor's decision exists — at that point
/// there is no "kept span" set to intersect against, so the survival question is structurally
/// unanswerable there. It first becomes answerable at the moment the planner's own prompt is
/// assembled: by then the analyze step's artifact AND the story editor's
/// <see cref="VideoEditDecisionOutput"/> are both sitting in
/// <see cref="StepExecutionContext.StepOutputHistory"/>. Without this, a placement the cut removes
/// is only ever discovered POST-hoc, by <c>VideoCompileStepExecutor</c> silently dropping the
/// planned overlay with reason <c>cut_away</c> — a wasted overlay (and, when the planner rendered
/// a Remotion asset for it, a wasted sandbox render).
/// </para>
///
/// <para>
/// <b>Display-only.</b> Nothing is filtered or removed: an <c>inEdit: false</c> candidate is still
/// fully offered, still in <see cref="VideoAnalysisArtifact.OfferedPlacementIds"/>, and still
/// legal for the model to choose. The flag is a visible pre-decision signal, not a gate — the only
/// thing that changes is that the model can now see what it is trading away.
/// </para>
///
/// <para>
/// <b>Never fails the step.</b> Every failure mode (no analyze step in history, no decision in
/// history, an unreadable artifact, malformed JSON) degrades to "no annotation added" and leaves
/// the prompt byte-identical to what it would have been. A hint about overlay placement must never
/// be able to take down the step it is hinting for.
/// </para>
/// </summary>
public interface IMotionGraphicsPlacementAnnotator
{
    /// <summary>
    /// Resolves the analysis artifact + the story editor's decision out of
    /// <paramref name="context"/>'s step history and, on success, installs an annotated copy of the
    /// <c>VideoAnalyze</c> step's output envelope as a prompt-only override (see
    /// <see cref="StepExecutionContext.SetPromptOutputOverride"/>). A no-op on any failure.
    /// </summary>
    Task AnnotateAsync(StepExecutionContext context, CancellationToken ct);
}

/// <inheritdoc cref="IMotionGraphicsPlacementAnnotator"/>
public sealed class MotionGraphicsPlacementAnnotator : IMotionGraphicsPlacementAnnotator
{
    private static readonly JsonSerializerOptions ArtifactJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions DecisionJsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly JsonSerializerOptions EnvelopeJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IProjectFileWorkspace _workspace;
    private readonly VideoEditingOptions _options;
    private readonly ILogger<MotionGraphicsPlacementAnnotator> _logger;

    public MotionGraphicsPlacementAnnotator(
        IProjectFileWorkspace workspace,
        IOptions<VideoEditingOptions> options,
        ILogger<MotionGraphicsPlacementAnnotator> logger)
    {
        _workspace = workspace;
        _options = options.Value;
        _logger = logger;
    }

    public async Task AnnotateAsync(StepExecutionContext context, CancellationToken ct)
    {
        VideoScratchSpace? scratch = null;
        try
        {
            StepOutputHistoryEntry? analyzeEntry = FindAnalyzeEntry(context);
            if (analyzeEntry is null)
            {
                _logger.LogDebug(
                    "MotionGraphicsPlanner step {StepOrder}: no prior VideoAnalyze step with placements and an artifact key in history; skipping inEdit annotation",
                    context.Step.StepOrder);
                return;
            }

            VideoEditDecisionOutput? decision = FindDecision(context, analyzeEntry.StepOrder);
            if (decision is null)
            {
                _logger.LogDebug(
                    "MotionGraphicsPlanner step {StepOrder}: no prior story-editor decision in history; skipping inEdit annotation",
                    context.Step.StepOrder);
                return;
            }

            scratch = VideoScratchSpace.Create(_options, context.Execution.Id, context.Step.Id, _logger);
            string localArtifactPath = scratch.GetPath("analysis-for-graphics.json");
            await _workspace.DownloadStorageKeyToFileAsync(
                context.Execution.ProjectId, analyzeEntry.ArtifactStorageKey!, localArtifactPath, ct);

            string artifactJson = await File.ReadAllTextAsync(localArtifactPath, ct);
            VideoAnalysisArtifact? artifact = JsonSerializer.Deserialize<VideoAnalysisArtifact>(artifactJson, ArtifactJsonOptions);
            if (artifact?.Placements is not { Count: > 0 })
                return;

            string? annotated = Annotate(analyzeEntry.Output, artifact, decision);
            if (annotated is null)
                return;

            context.SetPromptOutputOverride(analyzeEntry.StepOrder, annotated);
            _logger.LogInformation(
                "MotionGraphicsPlanner step {StepOrder}: annotated {PlacementCount} placement candidate(s) from step {AnalyzeStepOrder} with inEdit survival flags",
                context.Step.StepOrder,
                artifact.Placements.Count,
                analyzeEntry.StepOrder);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Soft-failure discipline, same as every graphics-specific failure in
            // VideoCompileStepExecutor.ResolveGraphicsAsync: the planner still runs, just without
            // the hint.
            _logger.LogWarning(
                ex,
                "MotionGraphicsPlanner step {StepOrder}: inEdit annotation failed; the planner prompt is unchanged",
                context.Step.StepOrder);
        }
        finally
        {
            scratch?.Dispose();
        }
    }

    // ---------------------------------------------------------------------
    // History resolution. Deliberately duck-typed rather than config-driven: unlike a
    // VideoCompile step (which carries an explicit AnalysisStepOrder/Decision ExtractInputRef), an
    // Agent step has no per-step config to point at its inputs, so the two inputs are recognized
    // by SHAPE — the analyze envelope by its view.placements array plus a non-null
    // ArtifactStorageKey, the decision by a non-empty "keep" array. A miss degrades to
    // "no annotation", never to a wrong annotation.
    // ---------------------------------------------------------------------

    /// <summary>
    /// The latest prior step whose output is a <c>{view, meta}</c> envelope carrying a non-empty
    /// <c>view.placements</c> array AND an <see cref="StepOutputHistoryEntry.ArtifactStorageKey"/>.
    /// <c>LastOrDefault</c> (over a descending step order) for the same reason
    /// <c>VideoCompileStepExecutor.ResolveAnalysisArtifactKeyAsync</c> uses it: a ReviewLoop
    /// loop-back can leave a stale duplicate entry for the same StepOrder in history, and the
    /// freshest one must win.
    /// </summary>
    private static StepOutputHistoryEntry? FindAnalyzeEntry(StepExecutionContext context) =>
        context.StepOutputHistory
            .Where(h => h.StepOrder < context.Step.StepOrder
                && !string.IsNullOrWhiteSpace(h.ArtifactStorageKey)
                && HasPlacements(h.Output))
            .OrderBy(h => h.StepOrder)
            .LastOrDefault();

    private static bool HasPlacements(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return false;

        try
        {
            JsonNode? root = JsonNode.Parse(output);
            return root?["view"]?["placements"] is JsonArray { Count: > 0 };
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// The latest prior step (excluding the analyze step itself) whose output contains a
    /// <see cref="VideoEditDecisionOutput"/> with at least one <c>Keep</c> span. Uses the same
    /// <c>RobustJsonExtractor</c> hardening <c>VideoCompileStepExecutor.ResolveDecisionJson</c>
    /// applies, since this is the same raw agent completion text (which may carry trailing prose,
    /// a markdown fence, or leaked tool-call-closing tokens around the JSON).
    /// </summary>
    private static VideoEditDecisionOutput? FindDecision(StepExecutionContext context, int analyzeStepOrder)
    {
        foreach (StepOutputHistoryEntry entry in context.StepOutputHistory
            .Where(h => h.StepOrder < context.Step.StepOrder && h.StepOrder != analyzeStepOrder)
            .OrderByDescending(h => h.StepOrder))
        {
            VideoEditDecisionOutput? decision = TryParseDecision(entry.Output);
            if (decision is not null)
                return decision;
        }

        return null;
    }

    internal static VideoEditDecisionOutput? TryParseDecision(string? rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
            return null;

        string? extracted = RobustJsonExtractor.ExtractJsonObject(rawOutput);
        if (extracted is null)
            return null;

        try
        {
            VideoEditDecisionOutput? decision = JsonSerializer.Deserialize<VideoEditDecisionOutput>(extracted, DecisionJsonOptions);
            // An explicit `"keep": null` overwrites the `= new()` initializer with a real null —
            // the same System.Text.Json behavior VideoCompileStepExecutor guards against.
            if (decision?.Keep is not { Count: > 0 })
                return null;

            return decision;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ---------------------------------------------------------------------
    // The actual survival computation.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Resolves the decision's <c>Keep</c> spans to source-timeline windows and rewrites
    /// <paramref name="envelopeJson"/> with an <c>inEdit</c> boolean on every
    /// <c>view.placements</c> entry. Returns null when the envelope has no annotatable placements
    /// array (so the caller leaves the prompt untouched).
    /// </summary>
    internal static string? Annotate(string envelopeJson, VideoAnalysisArtifact artifact, VideoEditDecisionOutput decision)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(envelopeJson);
        }
        catch (JsonException)
        {
            return null;
        }

        if (root?["view"]?["placements"] is not JsonArray placementsArray || placementsArray.Count == 0)
            return null;

        IReadOnlyList<VideoCompileStepExecutor.ResolvedSpan> keptSpans = ResolveKeptSpans(artifact, decision);
        Dictionary<string, VideoAnalysisPlacement> byId = new(StringComparer.Ordinal);
        foreach (VideoAnalysisPlacement placement in artifact.Placements ?? [])
            byId[placement.Id] = placement;

        foreach (JsonNode? node in placementsArray)
        {
            if (node is not JsonObject placementNode)
                continue;

            string? id = placementNode["id"]?.GetValue<string>();
            if (id is null || !byId.TryGetValue(id, out VideoAnalysisPlacement? placement))
                continue;

            placementNode["inEdit"] = IsInEdit(keptSpans, placement);
        }

        return root.ToJsonString(EnvelopeJsonOptions);
    }

    /// <summary>
    /// Does this placement's server-resolved source window survive the cut?
    ///
    /// <para>
    /// This is not a reimplementation of the intersection math — it calls the very function
    /// <c>VideoCompileStepExecutor</c> itself uses to decide whether a planned overlay is dropped
    /// with reason <c>cut_away</c> (<see cref="VideoCompileStepExecutor.MapSourceWindowToOutput"/>),
    /// so the flag cannot disagree with the later drop decision about what "cut away" means —
    /// including its multi-source discipline, where a window is only ever tested against kept spans
    /// belonging to its OWN source clip.
    /// </para>
    ///
    /// <para>
    /// Two deliberate, documented differences from the compile-time check, both of which only ever
    /// make this flag slightly CONSERVATIVE (a placement reported <c>false</c> could in principle
    /// still squeeze in, never the reverse):
    /// <list type="bullet">
    /// <item>The spans here are the raw resolved <c>[FromId.Start, ToId.End)</c> windows, without
    /// <c>PrePaddingMs</c>/<c>PostPaddingMs</c>, coalescing, or frame quantization — that
    /// normalization lives on <see cref="VideoCompileStepConfig"/>, which belongs to a step that has
    /// not run yet and whose padding only ever WIDENS a kept span.</item>
    /// <item>The window tested is the placement's own offered window, not the model's eventual
    /// <c>Duration</c>-extended one. Both are anchored at the same <c>StartSec</c> and the extended
    /// one is never shorter, so it can only overlap MORE.</item>
    /// </list>
    /// </para>
    /// </summary>
    internal static bool IsInEdit(
        IReadOnlyList<VideoCompileStepExecutor.ResolvedSpan> keptSpans, VideoAnalysisPlacement placement) =>
        VideoCompileStepExecutor.MapSourceWindowToOutput(
            keptSpans, placement.StartSec, placement.EndSec, placement.SourceIndex) is not null;

    /// <summary>
    /// Resolves the decision's <c>Keep</c> spans to source-timeline
    /// <see cref="VideoCompileStepExecutor.ResolvedSpan"/>s using the SAME id-to-time index
    /// <c>VideoCompileStepExecutor</c> builds (<c>BuildIdTimeIndex</c>) — so an id family that is
    /// deliberately not resolvable there (placements, music candidates, look groups, words) is not
    /// resolvable here either. Unknown ids, mixed-source spans, and non-positive spans are silently
    /// skipped rather than reported: this is a prompt hint, and compile remains the authority that
    /// turns any of those into a real, diagnosable step failure.
    ///
    /// <para>
    /// <c>Requested*</c> and <c>Snapped*</c> are deliberately set to the same values — there is no
    /// frame rate to quantize against at this point (that is compile's job) and
    /// <c>MapSourceWindowToOutput</c> reads only the <c>Snapped*</c> pair.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<VideoCompileStepExecutor.ResolvedSpan> ResolveKeptSpans(
        VideoAnalysisArtifact artifact, VideoEditDecisionOutput decision)
    {
        Dictionary<string, (double Start, double End, int SourceIndex)> idTimes =
            VideoCompileStepExecutor.BuildIdTimeIndex(artifact);
        HashSet<string> offered = new(artifact.OfferedIds, StringComparer.Ordinal);

        List<VideoCompileStepExecutor.ResolvedSpan> spans = [];
        foreach (VideoEditKeepSpan span in decision.Keep)
        {
            if (string.IsNullOrEmpty(span.FromId) || string.IsNullOrEmpty(span.ToId))
                continue;
            if (!offered.Contains(span.FromId) || !offered.Contains(span.ToId))
                continue;
            if (!idTimes.TryGetValue(span.FromId, out (double Start, double End, int SourceIndex) from)
                || !idTimes.TryGetValue(span.ToId, out (double Start, double End, int SourceIndex) to))
                continue;
            if (from.SourceIndex != to.SourceIndex)
                continue;
            if (to.End <= from.Start)
                continue;

            spans.Add(new VideoCompileStepExecutor.ResolvedSpan(
                from.Start, to.End, from.Start, to.End, StartFrame: 0, EndFrame: 0, from.SourceIndex));
        }

        // MapSourceWindowToOutput walks spans in list order accumulating output-timeline duration,
        // so hand it the same chronological, per-source order compile's own normalization produces.
        return spans
            .OrderBy(s => s.SourceIndex)
            .ThenBy(s => s.SnappedStart)
            .ToList();
    }
}
