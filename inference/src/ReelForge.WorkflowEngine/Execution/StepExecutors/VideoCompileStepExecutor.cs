using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Data.OutputSchemas;
using ReelForge.Shared.Workflows;
using ReelForge.WorkflowEngine.Data;
using ReelForge.WorkflowEngine.Services.Storage;
using ReelForge.WorkflowEngine.Services.Video;

namespace ReelForge.WorkflowEngine.Execution.StepExecutors;

/// <summary>
/// Executes <see cref="StepType.VideoCompile"/> steps: deterministic, non-LLM resolution of an
/// editorial decision's opaque ids to frame-accurate times against a VideoAnalyze artifact,
/// followed by an ffmpeg cut. See plan §4.3.
///
/// Deliberately has NO dependency on <c>IChatClient</c>, <c>IAgentRegistry</c>, or any
/// transcription abstraction — compile never talks to a model or performs ASR, only code and
/// ffmpeg. Never throws: every path, including an unexpected exception, returns a
/// <see cref="StepExecutionResult"/> whose Output is valid JSON.
/// </summary>
public class VideoCompileStepExecutor : IStepExecutor
{
    private static readonly JsonSerializerOptions ConfigJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions ArtifactJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions DecisionJsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly JsonSerializerOptions EnvelopeJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Two resolved spans within this of each other (seconds) are treated as touching.</summary>
    private const double AdjacencyEpsilonSec = 1e-6;

    // R11: workflow-author-supplied config still reaches ffmpeg argv, so it is validated exactly
    // as strictly as model output would be, even though the frontend already constrains its
    // selects to these values.
    private static readonly HashSet<string> AllowedVideoCodecs =
        new(StringComparer.OrdinalIgnoreCase) { "libx264", "libx265", "libvpx-vp9" };

    private static readonly HashSet<string> AllowedAudioCodecs =
        new(StringComparer.OrdinalIgnoreCase) { "aac", "libmp3lame", "copy" };

    private static readonly HashSet<string> AllowedPresets = new(StringComparer.OrdinalIgnoreCase)
    {
        "ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow"
    };

    // Phase 3 (motion graphics) — same allowlist discipline as the codec/preset sets above: this
    // is workflow-author-supplied config that still reaches ffmpeg's drawtext/drawbox filter
    // string, so it is validated exactly as strictly.
    private static readonly HashSet<string> NamedOverlayFontColors =
        new(StringComparer.OrdinalIgnoreCase) { "white", "black", "yellow" };

    private static readonly Regex HexColorPattern = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    private static readonly HashSet<string> AllowedOverlayBoxColors =
        new(StringComparer.OrdinalIgnoreCase) { "black@0.45", "black@0.6", "white@0.4", "none" };

    // Phase 3 (motion graphics) rendered-asset overlays: RenderedAssetStorageKey is a
    // model-authored string (see MotionGraphicsOverlay.RenderedAssetStorageKey's doc comment), and
    // its extension is used to build a local scratch file path. The prefix check on the key itself
    // (expectedAssetKeyPrefix, in ResolveGraphicsAsync) and VideoScratchSpace.GetPath's own
    // containment check already stop this from escaping scratch space, but the extension is
    // otherwise trusted verbatim — allowlisted here to the formats RenderVideoAndUploadToStorage's
    // own documented recipe actually produces, same allowlist discipline as the codec/preset/color
    // sets above, rather than trusting whatever Path.GetExtension happens to return.
    private static readonly HashSet<string> AllowedRenderedAssetExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".webm", ".mp4", ".mov" };

    // Cut transitions (see docs/video-editing.md "Cut transitions"): same allowlist discipline as
    // OverlayBoxColor/OverlayFontColor above — ProgramFadeColor is workflow-author config that
    // still reaches ffmpeg's fade= filter string.
    private static readonly HashSet<string> AllowedProgramFadeColors =
        new(StringComparer.OrdinalIgnoreCase) { "black", "white" };

    /// <summary>Process-lifetime cache of whether the ffmpeg build on PATH has the drawtext filter (needs libfreetype) — probed once, never per-step.</summary>
    private static bool? _drawtextAvailableCache;

    private static readonly SemaphoreSlim DrawtextProbeLock = new(1, 1);

    /// <summary>Test-only hook: resets the process-lifetime drawtext-availability cache so each test case gets its own fresh probe against its own mocked <see cref="IVideoToolRunner"/>.</summary>
    internal static void ResetDrawtextAvailabilityCacheForTests() => _drawtextAvailableCache = null;

    /// <summary>Process-lifetime cache of whether the ffmpeg build on PATH's <c>amix</c> filter exposes a <c>normalize</c> option — probed once, mirroring <see cref="_drawtextAvailableCache"/> exactly. Without <c>normalize=0</c>, <c>amix</c> silently halves the dialogue level, so this is a hard requirement for music, not cosmetic.</summary>
    private static bool? _amixNormalizeAvailableCache;

    private static readonly SemaphoreSlim AmixProbeLock = new(1, 1);

    /// <summary>Test-only hook, mirroring <see cref="ResetDrawtextAvailabilityCacheForTests"/>.</summary>
    internal static void ResetAmixNormalizeCacheForTests() => _amixNormalizeAvailableCache = null;

    /// <summary>Process-lifetime cache of whether the ffmpeg build on PATH exposes both the <c>xfade</c> and <c>acrossfade</c> filters — probed once, mirroring <see cref="_drawtextAvailableCache"/>/<see cref="_amixNormalizeAvailableCache"/> exactly. Only ever probed when <see cref="VideoCompileStepConfig.TransitionPolicy"/> is not <see cref="VideoTransitionPolicy.Off"/> — the default (transitions-off) path pays no probe overhead at all.</summary>
    private static bool? _xfadeAvailableCache;

    private static readonly SemaphoreSlim XfadeProbeLock = new(1, 1);

    /// <summary>Test-only hook, mirroring <see cref="ResetDrawtextAvailabilityCacheForTests"/>.</summary>
    internal static void ResetXfadeAvailabilityCacheForTests() => _xfadeAvailableCache = null;

    /// <summary>Process-lifetime cache of whether the ffmpeg build on PATH exposes both the <c>perspective</c> and <c>alphamerge</c> filters (tracked screen inserts — see docs/video-editing.md "Tracked screen inserts (Phase 5)") — probed once, mirroring the drawtext/amix/xfade caches exactly. Only ever probed when <see cref="VideoCompileStepConfig.EnableInserts"/> is on.</summary>
    private static bool? _perspectiveAvailableCache;

    private static readonly SemaphoreSlim PerspectiveProbeLock = new(1, 1);

    /// <summary>Test-only hook, mirroring <see cref="ResetDrawtextAvailabilityCacheForTests"/>.</summary>
    internal static void ResetPerspectiveAvailabilityCacheForTests() => _perspectiveAvailableCache = null;

    /// <summary>Process-lifetime cache of whether the ffmpeg build on PATH exposes the <c>colorbalance</c>/<c>colorlevels</c>/<c>eq</c>/<c>hue</c> filters the colour-grade chain composes (see docs/video-editing.md "Color grading") — probed once, mirroring the drawtext/amix/xfade/perspective caches exactly. Only ever probed when <see cref="VideoCompileStepConfig.EnableColorGrade"/> is on.</summary>
    private static bool? _colorGradeFiltersAvailableCache;

    private static readonly SemaphoreSlim ColorGradeProbeLock = new(1, 1);

    /// <summary>Test-only hook, mirroring <see cref="ResetDrawtextAvailabilityCacheForTests"/>.</summary>
    internal static void ResetColorGradeFiltersCacheForTests() => _colorGradeFiltersAvailableCache = null;

    /// <summary>
    /// Above this many segments, the select/aselect filtergraph is written to a scratch file and
    /// passed via <c>-filter_complex_script</c> instead of inline <c>-filter_complex</c>, to avoid
    /// argv length limits (R20). <see cref="VideoCompileStepConfig.MaxSegments"/> defaults to 200,
    /// so this is a real path, not a hypothetical one.
    /// </summary>
    private const int FilterComplexScriptThreshold = 64;

    private readonly IVideoToolRunner _videoToolRunner;
    private readonly IMediaProbe _mediaProbe;
    private readonly IProjectFileWorkspace _workspace;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly VideoEditingOptions _options;
    private readonly ILogger<VideoCompileStepExecutor> _logger;

    public VideoCompileStepExecutor(
        IVideoToolRunner videoToolRunner,
        IMediaProbe mediaProbe,
        IProjectFileWorkspace workspace,
        IServiceScopeFactory scopeFactory,
        IOptions<VideoEditingOptions> options,
        ILogger<VideoCompileStepExecutor> logger)
    {
        _videoToolRunner = videoToolRunner;
        _mediaProbe = mediaProbe;
        _workspace = workspace;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    public StepType StepType => StepType.VideoCompile;

    public async Task<StepExecutionResult> ExecuteAsync(StepExecutionContext context)
    {
        Stopwatch sw = Stopwatch.StartNew();
        WorkflowStep step = context.Step;
        VideoScratchSpace? scratch = null;

        try
        {
            VideoCompileStepConfig? config;
            try
            {
                if (string.IsNullOrWhiteSpace(step.VideoCompileConfigJson))
                    return Failure(context, sw, "CONFIG_INVALID", "VideoCompile step has no VideoCompileConfigJson configured.");

                config = JsonSerializer.Deserialize<VideoCompileStepConfig>(step.VideoCompileConfigJson, ConfigJsonOptions);
            }
            catch (JsonException ex)
            {
                return Failure(context, sw, "CONFIG_INVALID", $"VideoCompileConfigJson is not valid JSON: {ex.Message}");
            }

            if (config is null)
                return Failure(context, sw, "CONFIG_INVALID", "VideoCompileConfigJson deserialized to null.");

            if (config.Decision.From != ExtractInputSource.Previous && config.Decision.From != ExtractInputSource.Step)
            {
                return Failure(
                    context, sw, "CONFIG_INVALID",
                    $"VideoCompile Decision.From must be Previous or Step; got '{config.Decision.From}'.");
            }

            if (config.MusicPlan is not null &&
                config.MusicPlan.From != ExtractInputSource.Previous && config.MusicPlan.From != ExtractInputSource.Step)
            {
                return Failure(
                    context, sw, "CONFIG_INVALID",
                    $"VideoCompile MusicPlan.From must be Previous or Step; got '{config.MusicPlan.From}'.");
            }

            if (config.GraphicsPlan is not null &&
                config.GraphicsPlan.From != ExtractInputSource.Previous && config.GraphicsPlan.From != ExtractInputSource.Step)
            {
                return Failure(
                    context, sw, "CONFIG_INVALID",
                    $"VideoCompile GraphicsPlan.From must be Previous or Step; got '{config.GraphicsPlan.From}'.");
            }

            if (config.ColorGradePlan is not null &&
                config.ColorGradePlan.From != ExtractInputSource.Previous && config.ColorGradePlan.From != ExtractInputSource.Step)
            {
                return Failure(
                    context, sw, "CONFIG_INVALID",
                    $"VideoCompile ColorGradePlan.From must be Previous or Step; got '{config.ColorGradePlan.From}'.");
            }

            if (config.SfxPlan is not null &&
                config.SfxPlan.From != ExtractInputSource.Previous && config.SfxPlan.From != ExtractInputSource.Step)
            {
                return Failure(
                    context, sw, "CONFIG_INVALID",
                    $"VideoCompile SfxPlan.From must be Previous or Step; got '{config.SfxPlan.From}'.");
            }

            // Sound effects (see docs/video-editing.md "Sound effects"): the only two HARD
            // failures in this whole addition, both pure config errors caught up front —
            // everything else about SFX is soft-failure ("no SFX applied" / "drop this one cue",
            // cut proceeds). Mirrors MUSIC_REQUIRES_REENCODE/MUSIC_REQUIRES_AUDIO_REENCODE's
            // reasoning exactly: cues are mixed into the audio filtergraph, which stream-copy /
            // AudioCodec=copy do not have.
            if (config.EnableSfx && config.Mode == VideoCompileMode.StreamCopy)
            {
                return Failure(
                    context, sw, "SFX_REQUIRES_REENCODE",
                    "EnableSfx=true requires Mode=Reencode — stream-copy has no audio filtergraph to mix cues into.");
            }

            if (config.EnableSfx && string.Equals(config.AudioCodec, "copy", StringComparison.OrdinalIgnoreCase))
            {
                return Failure(
                    context, sw, "SFX_REQUIRES_AUDIO_REENCODE",
                    "EnableSfx=true is incompatible with AudioCodec=copy — the mixed audio must be encoded.");
            }

            // Color grading (see docs/video-editing.md "Color grading"): the one HARD failure in
            // the whole addition, a pure config error caught up front — everything else about the
            // grade is soft-failure ("no grade applied", cut proceeds). Mirrors
            // GRAPHICS_REQUIRE_REENCODE's reasoning exactly: the eq/colorbalance/colorlevels/hue
            // chain has no stream-copy equivalent.
            if (config.EnableColorGrade && config.Mode == VideoCompileMode.StreamCopy)
            {
                return Failure(
                    context, sw, "COLOR_GRADE_REQUIRES_REENCODE",
                    "EnableColorGrade=true requires Mode=Reencode — the colour-grade filter chain has no stream-copy equivalent.");
            }

            // Phase 3 (motion graphics): drawbox/drawtext filters require the reencode
            // filtergraph — stream-copy has no filtergraph at all. Checked up front, before any
            // artifact/decision resolution work, since this is a pure config error.
            if (config.EnableGraphics && config.Mode == VideoCompileMode.StreamCopy)
            {
                return Failure(
                    context, sw, "GRAPHICS_REQUIRE_REENCODE",
                    "EnableGraphics=true requires Mode=Reencode — drawtext/drawbox filters have no stream-copy equivalent.");
            }

            if (config.EnableGraphics && !IsValidOverlayFontColor(config.OverlayFontColor))
            {
                return Failure(
                    context, sw, "CODEC_NOT_ALLOWED",
                    $"OverlayFontColor '{config.OverlayFontColor}' is not in the allowlist (white/black/yellow/#RRGGBB).");
            }

            if (config.EnableGraphics && !AllowedOverlayBoxColors.Contains(config.OverlayBoxColor))
            {
                return Failure(
                    context, sw, "CODEC_NOT_ALLOWED",
                    $"OverlayBoxColor '{config.OverlayBoxColor}' is not in the allowlist.");
            }

            // Tracked screen inserts (see docs/video-editing.md "Tracked screen inserts (Phase
            // 5)"): the one HARD failure in the whole addition, a pure config error caught up
            // front — everything else about inserts is soft-failure ("no insert applied", cut
            // proceeds). Mirrors GRAPHICS_REQUIRE_REENCODE's reasoning exactly: the animated
            // perspective/alphamerge/overlay filtergraph has no stream-copy equivalent.
            if (config.EnableInserts && config.Mode == VideoCompileMode.StreamCopy)
            {
                return Failure(
                    context, sw, "INSERTS_REQUIRE_REENCODE",
                    "EnableInserts=true requires Mode=Reencode — the tracked-insert perspective/overlay filtergraph has no stream-copy equivalent.");
            }

            // Background music (see docs/video-editing.md "Background music"): the only two HARD
            // failures in this whole addition, both pure config errors caught up front before any
            // resolution work — everything else about music is soft-failure ("no music applied",
            // cut proceeds). Mirrors GRAPHICS_REQUIRE_REENCODE's own reasoning exactly.
            if (config.EnableMusic && config.Mode == VideoCompileMode.StreamCopy)
            {
                return Failure(
                    context, sw, "MUSIC_REQUIRES_REENCODE",
                    "EnableMusic=true requires Mode=Reencode — stream-copy has no audio filtergraph to mix into.");
            }

            if (config.EnableMusic && string.Equals(config.AudioCodec, "copy", StringComparison.OrdinalIgnoreCase))
            {
                return Failure(
                    context, sw, "MUSIC_REQUIRES_AUDIO_REENCODE",
                    "EnableMusic=true is incompatible with AudioCodec=copy — the mixed audio must be encoded.");
            }

            // Cut transitions (see docs/video-editing.md "Cut transitions"): the same
            // Reencode-only discipline as GRAPHICS_REQUIRE_REENCODE/MUSIC_REQUIRES_REENCODE above —
            // fade/xfade/acrossfade filters have no stream-copy equivalent. Any transition policy
            // beyond Off, or any program fade duration, trips this.
            bool wantsTransitions =
                config.TransitionPolicy != VideoTransitionPolicy.Off ||
                config.ProgramFadeInMs > 0 || config.ProgramFadeOutMs > 0 ||
                config.ProgramAudioFadeInMs > 0 || config.ProgramAudioFadeOutMs > 0;
            if (wantsTransitions && config.Mode == VideoCompileMode.StreamCopy)
            {
                return Failure(
                    context, sw, "TRANSITIONS_REQUIRE_REENCODE",
                    "TransitionPolicy != Off (or a ProgramFade*Ms > 0) requires Mode=Reencode — fade/xfade/acrossfade " +
                    "filters have no stream-copy equivalent.");
            }

            if ((config.ProgramAudioFadeInMs > 0 || config.ProgramAudioFadeOutMs > 0) &&
                string.Equals(config.AudioCodec, "copy", StringComparison.OrdinalIgnoreCase))
            {
                return Failure(
                    context, sw, "PROGRAM_AUDIO_FADE_REQUIRES_AUDIO_REENCODE",
                    "ProgramAudioFadeInMs/ProgramAudioFadeOutMs > 0 is incompatible with AudioCodec=copy — the faded audio must be encoded.");
            }

            if (wantsTransitions && !AllowedProgramFadeColors.Contains(config.ProgramFadeColor))
            {
                return Failure(
                    context, sw, "CODEC_NOT_ALLOWED",
                    $"ProgramFadeColor '{config.ProgramFadeColor}' is not in the allowlist (black/white).");
            }

            // ---- Resolve the analyze step's FULL artifact (never the bounded view) ----

            await context.ReportProgressAsync("Resolving analysis artifact");
            (string? analysisArtifactKey, string? analysisError) = await ResolveAnalysisArtifactKeyAsync(context, config);
            if (analysisArtifactKey is null)
                return Failure(context, sw, "ANALYSIS_NOT_FOUND", analysisError ?? "Could not resolve the VideoAnalyze artifact.");

            scratch = VideoScratchSpace.Create(_options, context.Execution.Id, step.Id, _logger);

            string localArtifactPath = scratch.GetPath("analysis.json");
            await _workspace.DownloadStorageKeyToFileAsync(
                context.Execution.ProjectId, analysisArtifactKey, localArtifactPath, context.CancellationToken);

            VideoAnalysisArtifact? artifact;
            try
            {
                string artifactJson = await File.ReadAllTextAsync(localArtifactPath, context.CancellationToken);
                artifact = JsonSerializer.Deserialize<VideoAnalysisArtifact>(artifactJson, ArtifactJsonOptions);
            }
            catch (JsonException ex)
            {
                return Failure(context, sw, "ANALYSIS_INVALID", $"Analysis artifact is not valid JSON: {ex.Message}");
            }

            if (artifact is null)
                return Failure(context, sw, "ANALYSIS_INVALID", "Analysis artifact deserialized to null.");

            // ---- Resolve the editorial decision ----

            await context.ReportProgressAsync("Resolving editorial decision");
            (string? decisionJson, string? decisionError) = ResolveDecisionJson(context, config.Decision);
            if (decisionJson is null)
                return Failure(context, sw, "DECISION_UNRESOLVED", decisionError ?? "Could not resolve the editorial decision input.");

            VideoEditDecisionOutput? decision;
            try
            {
                decision = DeserializeWithQuoteFallback<VideoEditDecisionOutput>(decisionJson, DecisionJsonOptions);
            }
            catch (JsonException ex)
            {
                return Failure(context, sw, "DECISION_INVALID", $"Decision input is not valid JSON: {ex.Message}");
            }

            // An explicit `"keep": null` in the JSON overwrites the `= new()` default with a real
            // null (System.Text.Json does not run property initializers for an explicit JSON
            // null), which would otherwise NRE past this point instead of degrading to EMPTY_KEEP.
            if (decision is not null)
                decision.Keep ??= [];

            if (decision is null || decision.Keep.Count == 0)
                return Failure(context, sw, "EMPTY_KEEP", "Decision has no Keep spans; nothing to compile.");

            context.RecordResolvedInput(BuildResolvedInputDescriptor(config, decision));

            // ---- Reject unknown ids (must be in offeredIds — actually offered, not merely present) ----

            HashSet<string> offeredIdSet = new(artifact.OfferedIds, StringComparer.Ordinal);
            Dictionary<string, (double Start, double End, int SourceIndex)> idTimes = BuildIdTimeIndex(artifact);

            List<(double Start, double End, int SourceIndex)> rawSpans = new(decision.Keep.Count);
            foreach (VideoEditKeepSpan span in decision.Keep)
            {
                if (!offeredIdSet.Contains(span.FromId) || !offeredIdSet.Contains(span.ToId))
                {
                    string badId = !offeredIdSet.Contains(span.FromId) ? span.FromId : span.ToId;
                    return Failure(
                        context, sw, "UNKNOWN_ID",
                        $"Keep span references id '{badId}', which was not among the ids offered to the story editor. " +
                        "Only ids that appeared in the bounded analysis view may be referenced.");
                }

                (double fromStart, _, int fromSourceIndex) = idTimes[span.FromId];
                (_, double toEnd, int toSourceIndex) = idTimes[span.ToId];

                // Multi-source addition: a single Keep span is a contiguous run within ONE
                // physical source clip — it can never bridge two different clips (see
                // docs/video-editing.md "Multiple source clips"). Resolved server-side from each
                // id's own recorded SourceIndex, never trusted from the model (there is no source
                // field on VideoEditKeepSpan for the model to get wrong in the first place).
                if (fromSourceIndex != toSourceIndex)
                {
                    return Failure(
                        context, sw, "MIXED_SOURCE_SPAN",
                        $"Keep span from '{span.FromId}' (source {fromSourceIndex}) to '{span.ToId}' (source {toSourceIndex}) " +
                        "spans two different source clips. A single Keep span must stay within one clip — express a " +
                        "cross-clip edit as a sequence of separate single-clip Keep spans instead.");
                }

                if (toEnd <= fromStart)
                {
                    return Failure(
                        context, sw, "INVALID_SPAN",
                        $"Keep span from '{span.FromId}' to '{span.ToId}' resolves to a non-positive duration " +
                        $"([{fromStart:F3}, {toEnd:F3})). ToId must not precede FromId.");
                }

                rawSpans.Add((fromStart, toEnd, fromSourceIndex));
            }

            // ---- Normalize: reject reordering/overlap, coalesce, pad, clamp, drop, cap.
            // Multi-source addition: ordering/overlap/coalescing only ever compare a span against
            // the IMMEDIATELY PRECEDING span IN LIST ORDER — when that neighbor belongs to a
            // DIFFERENT source clip, there is no shared timeline to be "out of order" or
            // "overlapping" on, so the check (and coalescing) is simply skipped at that boundary.
            // A single-source config's spans are always same-source neighbors, so this reduces to
            // exactly the original single-timeline behavior. ----

            for (int i = 1; i < rawSpans.Count; i++)
            {
                if (rawSpans[i].SourceIndex == rawSpans[i - 1].SourceIndex && rawSpans[i].Start < rawSpans[i - 1].Start)
                {
                    return Failure(
                        context, sw, "SPANS_OUT_OF_ORDER",
                        $"Keep spans must be listed in chronological order within the same source clip; span {i} starts " +
                        $"at {rawSpans[i].Start:F3}s, before span {i - 1} which starts at {rawSpans[i - 1].Start:F3}s " +
                        "(both from the same clip). Reordering kept spans within one clip is not supported — list them " +
                        "in the order they should play, or switch to a different source clip for out-of-order material.");
                }
            }

            List<(double Start, double End, int SourceIndex)> nonOverlapping = new();
            foreach ((double start, double end, int sourceIndex) in rawSpans)
            {
                if (nonOverlapping.Count > 0 && nonOverlapping[^1].SourceIndex == sourceIndex &&
                    start < nonOverlapping[^1].End - AdjacencyEpsilonSec)
                {
                    return Failure(
                        context, sw, "SPANS_OVERLAP",
                        $"Keep spans overlap: [{nonOverlapping[^1].Start:F3}, {nonOverlapping[^1].End:F3}) and " +
                        $"[{start:F3}, {end:F3}) (both from source {sourceIndex}).");
                }

                nonOverlapping.Add((start, end, sourceIndex));
            }

            List<(double Start, double End, int SourceIndex)> coalesced = CoalesceAdjacent(nonOverlapping);

            double prePadSec = Math.Max(0, config.PrePaddingMs) / 1000.0;
            double postPadSec = Math.Max(0, config.PostPaddingMs) / 1000.0;

            List<(double Start, double End, int SourceIndex)> padded = coalesced
                .Select(s =>
                {
                    // Multi-source addition: clamp against THIS span's OWN source clip's duration,
                    // never a single artifact-wide duration — GetSourceMedia falls back to the
                    // legacy top-level Media for a pre-multi-source artifact (source 0 only), so a
                    // single-source config clamps exactly as it always did.
                    double sourceDurationSec = GetSourceMedia(artifact, s.SourceIndex).DurationSec;
                    return (
                        Start: Math.Clamp(s.Start - prePadSec, 0, sourceDurationSec),
                        End: Math.Clamp(s.End + postPadSec, 0, sourceDurationSec),
                        s.SourceIndex);
                })
                .Where(s => s.End > s.Start)
                .ToList();

            // Padding can push neighboring same-source spans into each other; coalesce again post-padding.
            padded = CoalesceAdjacent(padded);

            double minSegmentSec = Math.Max(0, config.MinSegmentMs) / 1000.0;
            List<(double Start, double End, int SourceIndex)> aboveMinLength = padded.Where(s => s.End - s.Start >= minSegmentSec).ToList();

            int droppedOverCap = 0;
            List<(double Start, double End, int SourceIndex)> finalSpans = aboveMinLength;
            int maxSegments = Math.Clamp(config.MaxSegments, 1, 500);
            if (finalSpans.Count > maxSegments)
            {
                droppedOverCap = finalSpans.Count - maxSegments;
                _logger.LogWarning(
                    "VideoCompile step {StepOrder}: {Count} segments exceed MaxSegments={Max}; dropping the last {Dropped}.",
                    step.StepOrder, finalSpans.Count, maxSegments, droppedOverCap);
                finalSpans = finalSpans.Take(maxSegments).ToList();
            }

            if (finalSpans.Count == 0)
            {
                return Failure(
                    context, sw, "NO_SEGMENTS_REMAINING",
                    "After padding/clamping and dropping spans shorter than MinSegmentMs, no segments remained to compile.");
            }

            double totalOutputSeconds = finalSpans.Sum(s => s.End - s.Start);

            // Multi-source addition: "how much of the source was retained" now means "against the
            // sum of every DISTINCT clip actually referenced by the resolved cut list" — for a
            // single source this sum has exactly one term, so retainedRatio is byte-identical to
            // before this addition.
            List<int> usedSourceIndices = finalSpans.Select(s => s.SourceIndex).Distinct().OrderBy(i => i).ToList();
            double totalSourceDurationSec = usedSourceIndices.Sum(idx => GetSourceMedia(artifact, idx).DurationSec);
            double retainedRatio = totalSourceDurationSec > 0 ? totalOutputSeconds / totalSourceDurationSec : 0;

            string? expectError = EvaluateExpect(config.Expect, totalOutputSeconds, retainedRatio);
            if (expectError is not null)
                return Failure(context, sw, "EXPECT_FAILED", expectError);

            bool isMultiSource = usedSourceIndices.Count > 1;

            // Multi-source addition: concatenating clips from different physical files losslessly
            // (StreamCopy) has no correctness-preserving equivalent here — ffmpeg's concat
            // filter/demuxer both require matching codec parameters across inputs, which
            // independently-encoded source files are not guaranteed to share, and normalizing them
            // first (scale/fps/format) is itself a re-encode. Checked as a hard, pre-encode config
            // error (not a soft degrade) — the same discipline GRAPHICS_REQUIRE_REENCODE above
            // already established for a different Reencode-only combination.
            if (isMultiSource && config.Mode == VideoCompileMode.StreamCopy)
            {
                return Failure(
                    context, sw, "MULTI_SOURCE_REQUIRES_REENCODE",
                    $"The resolved cut list references {usedSourceIndices.Count} distinct source clips; " +
                    "Mode=StreamCopy has no multi-clip equivalent (losslessly concatenating independently-encoded " +
                    "files requires matching codec parameters ffmpeg cannot guarantee across them) — set Mode=Reencode.");
            }

            // ---- Frame-quantize on the exact rational fps of EACH span's OWN source clip (never
            // a single artifact-wide fps, and never a collapsed double) ----

            List<ResolvedSpan> resolvedSpans = finalSpans.Select(s =>
            {
                VideoAnalysisMedia sourceMedia = GetSourceMedia(artifact, s.SourceIndex);
                int fpsNum = sourceMedia.FpsNum;
                int fpsDen = sourceMedia.FpsDen;
                bool hasValidFps = fpsNum > 0 && fpsDen > 0;

                if (!hasValidFps)
                    return new ResolvedSpan(s.Start, s.End, s.Start, s.End, 0, 0, s.SourceIndex);

                long startFrame = ToStartFrame(s.Start, fpsNum, fpsDen);
                long endFrame = ToEndFrame(s.End, fpsNum, fpsDen);
                double snappedStart = FrameToSec(startFrame, fpsNum, fpsDen);
                double snappedEnd = FrameToSec(endFrame, fpsNum, fpsDen);
                return new ResolvedSpan(s.Start, s.End, snappedStart, snappedEnd, startFrame, endFrame, s.SourceIndex);
            }).ToList();

            // The "canonical" clip every multi-source encode normalizes toward (scale/pad/fps for
            // video, sample rate/channel layout for audio) and every graphics-geometry computation
            // reads dimensions from — deliberately the FIRST kept span's own source clip, a
            // deterministic choice independent of how many clips exist or how OfferedIds happened
            // to be ordered. For a single source this is trivially that one source.
            VideoAnalysisMedia canonicalMedia = GetSourceMedia(artifact, resolvedSpans[0].SourceIndex);

            // ---- Validate codec/preset allowlist, clamp CRF, sanitize output filename (R11) ----

            if (!AllowedVideoCodecs.Contains(config.VideoCodec))
                return Failure(context, sw, "CODEC_NOT_ALLOWED", $"VideoCodec '{config.VideoCodec}' is not in the allowlist.");
            if (!AllowedAudioCodecs.Contains(config.AudioCodec))
                return Failure(context, sw, "CODEC_NOT_ALLOWED", $"AudioCodec '{config.AudioCodec}' is not in the allowlist.");
            if (!AllowedPresets.Contains(config.Preset))
                return Failure(context, sw, "CODEC_NOT_ALLOWED", $"Preset '{config.Preset}' is not in the allowlist.");

            int crf = Math.Clamp(config.Crf, 0, 51);
            string outputFileName = SanitizeOutputFileName(config.OutputFileName);

            if (config.Mode == VideoCompileMode.StreamCopy && !config.AllowKeyframeSnapping)
            {
                return Failure(
                    context, sw, "STREAMCOPY_REQUIRES_KEYFRAME_SNAPPING",
                    "Mode=StreamCopy requires AllowKeyframeSnapping=true (stream-copy cuts can only land on keyframes).");
            }

            // ---- Resolve + download every DISTINCT source clip the resolved cut list actually
            // references (never every clip the artifact merely analyzed — only the ones actually
            // kept). Multi-source addition: when the artifact recorded its own per-source storage
            // keys (VideoAnalysisArtifact.Sources — every artifact produced by the current
            // VideoAnalyzeStepExecutor), those are used directly; a source index NOT found there
            // (only possible for index 0, on a legacy pre-multi-source artifact) falls back to
            // re-deriving it the original way, by walking the VideoAnalyze step's own config.
            //
            // Moved ahead of graphics/music/EDL resolution (this used to run right before
            // encoding) so sourceHasAudioByIndex — and the hasDialogueAudioInOutput flag derived
            // from it just below — is known before ResolveMusicAsync/BuildEdl run, letting both
            // honestly reflect whether the final output actually has any dialogue audio to duck
            // against or report (see bug-group-C fixes below). One behavior change from the
            // reorder: a SOURCE_UNRESOLVED failure below no longer carries an edlStorageKey — the
            // EDL is written after this succeeds now, so there is nothing yet to point at. ----

            var localPathBySource = new Dictionary<int, string>();
            var sourceHasAudioByIndex = new Dictionary<int, bool>();
            foreach (int idx in usedSourceIndices)
            {
                string? key = GetRecordedSourceStorageKey(artifact, idx);
                if (key is null)
                {
                    if (idx != 0)
                    {
                        return Failure(
                            context, sw, "SOURCE_UNRESOLVED",
                            $"Source index {idx} has no recorded storage key in the analysis artifact.");
                    }

                    (string? legacyKey, string? sourceError) = await ResolveSourceStorageKeyAsync(context, config);
                    if (legacyKey is null)
                        return Failure(context, sw, "SOURCE_UNRESOLVED", sourceError ?? "Could not resolve the source video to cut.");

                    key = legacyKey;
                }

                await context.ReportProgressAsync(
                    usedSourceIndices.Count > 1 ? $"Downloading source {idx} ({localPathBySource.Count + 1}/{usedSourceIndices.Count})" : "Downloading source");

                string localPath = scratch.GetPath(
                    $"source-{idx}" + Path.GetExtension(key) switch { "" => ".mp4", var e => e });
                await _workspace.DownloadStorageKeyToFileAsync(context.Execution.ProjectId, key, localPath, context.CancellationToken);
                localPathBySource[idx] = localPath;

                // Real stock/B-roll footage routinely ships with no audio stream at all — probing
                // here (once per distinct source, cheap) is what lets the encode methods below
                // build a video-only filtergraph instead of crashing ffmpeg on a "[N:a]" that
                // matches no streams. Mirrors the exact ProbeAsync/AudioCodec-null pattern already
                // used for the background-music track in ResolveMusicAsync.
                MediaProbeResult sourceProbe = await _mediaProbe.ProbeAsync(localPath, context.CancellationToken);
                sourceHasAudioByIndex[idx] = sourceProbe.AudioCodec is not null;
            }

            // Whether the FINAL OUTPUT will have any dialogue audio at all: single-source drops
            // audio only when that one clip lacks it; multi-source now keeps real dialogue for
            // every span whose OWN source has it and synthesizes matching-duration silence
            // (anullsrc) only for the span(s) whose source doesn't — see
            // EncodeReencodeMultiSourceAsync. Audio is dropped for the WHOLE output only in the
            // degenerate case where NOT ONE referenced clip has an audio stream at all (nothing
            // real to preserve, so there is no point adding an all-silent track). Threaded into
            // ResolveMusicAsync (skip ducking/lift-window computation against dialogue that will
            // not exist in the output) and the new "audio" EDL/outputSummary block below (report
            // the degrade instead of leaving it silent).
            bool anySourceHasAudio = usedSourceIndices.Any(i => sourceHasAudioByIndex[i]);
            bool sourceHasAudio = sourceHasAudioByIndex[usedSourceIndices[0]];
            bool hasDialogueAudioInOutput = isMultiSource ? anySourceHasAudio : sourceHasAudio;

            // Multi-source addition: which resolved spans will actually carry synthesized silence
            // instead of their own source's real audio — computed purely for honest EDL reporting
            // (see the "audio" node below), never fed back into the encode filtergraph decision
            // itself (EncodeReencodeMultiSourceAsync re-derives this per span from
            // sourceHasAudioByIndex directly).
            List<int> syntheticSilenceSegmentIndices = isMultiSource && anySourceHasAudio
                ? resolvedSpans
                    .Select((s, i) => (s, i))
                    .Where(t => !sourceHasAudioByIndex[t.s.SourceIndex])
                    .Select(t => t.i)
                    .ToList()
                : [];

            // ---- Cut transitions (see docs/video-editing.md "Cut transitions"): deterministic,
            // zero-LLM per-seam treatment planning + the resulting output timeline. Computed here —
            // BEFORE graphics/music resolution — because both of those need the REAL output
            // timeline (accounting for crossfade overlap), not the raw resolvedSpans concatenation.
            // Wrapped in its own try/catch: any unexpected exception here must never fail the whole
            // compile (per the executor's own "never throws" guarantee) — it degrades to
            // TransitionPolicy=Off behavior for this run instead (empty seamPlans, an all-hard-cut
            // timeline that is byte-identical to the pre-transitions compile path). ----

            IReadOnlyList<SeamPlan> seamPlans;
            OutputTimeline timeline;
            bool xfadeAvailable = false;
            bool transitionSegmentCountOverCap = false;
            try
            {
                if (config.TransitionPolicy != VideoTransitionPolicy.Off)
                    xfadeAvailable = await IsXfadeAvailableAsync(context.CancellationToken);

                IReadOnlyList<SeamFacts> seamFacts = SeamFactsBuilder.Build(artifact, resolvedSpans);
                seamPlans = SeamTransitionPlanner.Plan(
                    seamFacts, resolvedSpans, config, xfadeAvailable, out transitionSegmentCountOverCap);
                timeline = OutputTimeline.Build(resolvedSpans, seamPlans);
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "VideoCompile step {StepOrder}: transition planning failed unexpectedly; falling back to no transitions.",
                    step.StepOrder);
                seamPlans = [];
                xfadeAvailable = false;
                transitionSegmentCountOverCap = false;
                timeline = OutputTimeline.Build(resolvedSpans, BuildHardCutSeamsForFallback(resolvedSpans.Count));
            }

            JsonObject? transitionsNode = null;
            if (config.TransitionPolicy != VideoTransitionPolicy.Off)
            {
                transitionsNode = BuildTransitionsNode(
                    config, seamPlans, xfadeAvailable, transitionSegmentCountOverCap);
            }

            ProgramFadeResolved? programFade = ProgramEnvelopeFilterBuilder.IsEnabled(config)
                ? ProgramEnvelopeFilterBuilder.Resolve(config, timeline.TotalSec)
                : null;
            JsonObject? programFadeNode = programFade is null ? null : BuildProgramFadeNode(programFade);

            // ---- Phase 3 (motion graphics): resolve & validate the plan, purely soft-failure.
            // Only even attempted when EnableGraphics=true — when false (the default), nothing
            // below this point differs from the pre-Phase-3 compile path at all, which is the
            // load-bearing backward-compatibility guarantee of this whole phase. ----

            List<ResolvedOverlay> resolvedOverlays = [];
            JsonObject? graphicsNode = null;
            if (config.EnableGraphics)
            {
                await context.ReportProgressAsync("Resolving motion graphics plan");
                (resolvedOverlays, graphicsNode) = await ResolveGraphicsAsync(
                    context, config, artifact, resolvedSpans, timeline, scratch, canonicalMedia, context.CancellationToken);
            }

            // ---- Background music (see docs/video-editing.md "Background music"): purely
            // soft-failure, exactly like graphics — a missing/bad plan, an unofferred track id, or
            // an unavailable amix build all degrade to "no music applied", never to a failed
            // compile. Only even attempted when EnableMusic=true — when false (the default),
            // nothing below this point differs from the pre-music compile path at all. ----

            ResolvedMusic? resolvedMusic = null;
            JsonObject? musicNode = null;
            if (config.EnableMusic)
            {
                await context.ReportProgressAsync("Resolving background music");
                (resolvedMusic, musicNode) = await ResolveMusicAsync(
                    context, config, artifact, resolvedSpans, timeline, scratch, hasDialogueAudioInOutput, context.CancellationToken);
            }

            // ---- Tracked screen inserts (see docs/video-editing.md "Tracked screen inserts
            // (Phase 5)"): purely soft-failure, exactly like graphics/music above — a missing/bad
            // plan, an unoffered region id, low tracking confidence, or a cut-away region all
            // degrade to "no insert applied", never to a failed compile. Only even attempted when
            // EnableInserts=true — when false (the default), nothing below this point differs
            // from the pre-inserts compile path at all. ----

            List<ResolvedScreenInsert> resolvedInserts = [];
            JsonObject? insertsNode = null;
            if (config.EnableInserts)
            {
                await context.ReportProgressAsync("Resolving screen inserts");

                // v1 limitation, stated plainly (docs/video-editing.md): the animated corner-pin
                // keyframe math is only wired into the single-source select-path encode; a
                // multi-source compile or one with overlapping seam transitions routes to the
                // segmented concat encode, where inserts are skipped (soft, reported) rather than
                // composited wrongly against a differently-assembled timeline.
                bool segmentedEncode = isMultiSource || seamPlans.Any(s => s.OverlapSec > 0);
                (resolvedInserts, insertsNode) = await ResolveInsertsAsync(
                    context, config, artifact, timeline, scratch, canonicalMedia, segmentedEncode, context.CancellationToken);
            }

            // ---- Color grading (see docs/video-editing.md "Color grading"): purely
            // soft-failure, exactly like graphics/music/inserts above — a missing/bad plan, an
            // unknown look word, or unavailable filters all degrade to "no grade applied", never
            // to a failed compile. Only even attempted when EnableColorGrade=true — when false
            // (the default), nothing below this point differs from the pre-grade compile path at
            // all. Every number in the resulting filter chain comes from
            // ColorGradeFilterBuilder's first-party tables; the plan contributes only words. ----

            string? colorGradeFilter = null;
            JsonObject? colorGradeNode = null;
            if (config.EnableColorGrade)
            {
                await context.ReportProgressAsync("Resolving color grade plan");
                (colorGradeFilter, colorGradeNode) = await ResolveColorGradeAsync(
                    context, config, context.CancellationToken);
            }

            // ---- Sound effects (see docs/video-editing.md "Sound effects"): purely
            // soft-failure, exactly like graphics/music/inserts/grade above — a missing/bad
            // plan, an unoffered clip/anchor id, a cut-away anchor, or a bad clip file all
            // degrade to "no SFX applied" / "drop this one cue", never to a failed compile.
            // Only even attempted when EnableSfx=true — when false (the default), nothing
            // below this point differs from the pre-SFX compile path at all. ----

            List<ResolvedSfxCue> resolvedSfx = [];
            JsonObject? sfxNode = null;
            if (config.EnableSfx)
            {
                await context.ReportProgressAsync("Resolving sound effects plan");
                // A cue is layered OVER the base audio mix, so it needs a base to mix into:
                // real dialogue audio, or (failing that) an applied music bed. When neither
                // exists the output has no audio track at all, and ResolveSfxAsync degrades
                // every cue (no_base_audio) rather than synthesizing an SFX-only track.
                bool hasBaseAudio = hasDialogueAudioInOutput || resolvedMusic is not null;
                (resolvedSfx, sfxNode) = await ResolveSfxAsync(
                    context, config, artifact, timeline, scratch, hasBaseAudio, context.CancellationToken);
            }

            // ---- Audio degrade report (bug group C): every OTHER degrade path in this feature
            // (graphics.reason, music.dropped, meta.transcription.degraded) records itself in the
            // EDL/outputSummary — this one previously didn't, so a silent-video deliverable could
            // be a silent surprise. Always present (unlike graphics/music, which are conditional on
            // EnableGraphics/EnableMusic), since audio isn't opt-in the way those phases are. ----

            var audioNode = new JsonObject
            {
                ["applied"] = hasDialogueAudioInOutput,
                ["reason"] = hasDialogueAudioInOutput ? null : JsonValue.Create("no_audio_stream_in_source")
            };

            if (syntheticSilenceSegmentIndices.Count > 0)
            {
                // Mixed multi-source case: "applied: true" alone would wrongly imply every segment
                // kept its own original sound — some instead carry synthesized silence (see
                // EncodeReencodeMultiSourceAsync) because their own source clip never had an audio
                // stream to begin with. Surfaced explicitly, mirroring how graphics/music already
                // report partial/degraded application (graphics.droppedOverlays, music.dropped)
                // rather than collapsing to a single boolean.
                audioNode["syntheticSilenceSegmentCount"] = syntheticSilenceSegmentIndices.Count;
                audioNode["syntheticSilenceSegmentIndices"] =
                    new JsonArray(syntheticSilenceSegmentIndices.Select(i => (JsonNode)JsonValue.Create(i)).ToArray());
            }

            // ---- Write the EDL audit artifact ----

            // Cut transitions: how much total output-timeline time the planned crossfade overlaps
            // removed, purely for audit visibility (edl.transitionOverlapSec) — never fed back into
            // retainedRatio/Expect, which still read the pre-quantization totalOutputSeconds
            // computed far above, untouched by this addition.
            double frameQuantizedSumSec = resolvedSpans.Sum(s => s.SnappedEnd - s.SnappedStart);
            double transitionOverlapSec = Math.Max(0, frameQuantizedSumSec - timeline.TotalSec);

            await context.ReportProgressAsync("Writing edit decision list");
            string edlLocalPath = scratch.GetPath("edl.json");
            JsonObject edl = BuildEdl(
                config, analysisArtifactKey, resolvedSpans, retainedRatio, totalOutputSeconds, droppedOverCap, crf, graphicsNode, musicNode, audioNode,
                transitions: transitionsNode, programFade: programFadeNode, transitionOverlapSec: transitionOverlapSec,
                inserts: insertsNode, colorGrade: colorGradeNode, sfx: sfxNode);
            await File.WriteAllTextAsync(edlLocalPath, edl.ToJsonString(EnvelopeJsonOptions), context.CancellationToken);

            string edlFileName = $"video-analysis/{context.Execution.Id:D}/step-{step.StepOrder}-edl.json";
            string edlStorageKey = await _workspace.UploadArtifactAsync(
                context.Execution.ProjectId, edlLocalPath, edlFileName, "application/json", context.CancellationToken);

            string encodedLocalPath = scratch.GetPath(outputFileName);
            TimeSpan timeout = TimeSpan.FromSeconds(_options.CompileTimeoutSeconds);

            // Cut transitions: any seam whose planned treatment actually overlaps (SoftCut/
            // Dissolve/DipToBlack/WhipBlur) forces the segmented (concat+xfade) graph even for a
            // single source, since the select/aselect filter used by the original single-source
            // path cannot express a crossfade at all.
            bool needsSegmentedForTransitions = seamPlans.Any(s => s.OverlapSec > 0);

            await context.ReportProgressAsync("Encoding", 0);
            VideoToolResult encodeResult;
            if (isMultiSource || needsSegmentedForTransitions)
            {
                // Guaranteed Mode=Reencode by the MULTI_SOURCE_REQUIRES_REENCODE/
                // TRANSITIONS_REQUIRE_REENCODE checks above.
                encodeResult = await EncodeReencodeSegmentedAsync(
                    scratch, localPathBySource, encodedLocalPath, resolvedSpans, config.VideoCodec, config.AudioCodec, config.Preset, crf,
                    canonicalMedia, timeout, context.CancellationToken,
                    overlays: resolvedOverlays, graphicsConfig: resolvedOverlays.Count > 0 ? config : null,
                    progressContext: context, totalOutputSeconds: timeline.TotalSec,
                    music: resolvedMusic, sourceHasAudioByIndex: sourceHasAudioByIndex,
                    seamPlans: seamPlans, timeline: timeline, transitionConfig: config, programFade: programFade,
                    colorGradeFilter: colorGradeFilter, sfx: resolvedSfx);
            }
            else
            {
                // Exactly one distinct source referenced and every seam is non-overlapping — the
                // ORIGINAL single-input select/aselect code path, completely unchanged when the
                // source has audio AND no transitions are configured, so a single-source
                // (or single-clip-in-practice) compile with TransitionPolicy=Off stays byte-identical
                // to before this addition.
                string localVideoPath = localPathBySource[usedSourceIndices[0]];
                encodeResult = config.Mode == VideoCompileMode.Reencode
                    ? await EncodeReencodeAsync(
                        scratch, localVideoPath, encodedLocalPath, resolvedSpans, config.VideoCodec, config.AudioCodec, config.Preset, crf,
                        timeout, context.CancellationToken,
                        overlays: resolvedOverlays, probedWidth: canonicalMedia.Width, probedHeight: canonicalMedia.Height,
                        graphicsConfig: resolvedOverlays.Count > 0 ? config : null,
                        progressContext: context, totalOutputSeconds: timeline.TotalSec,
                        music: resolvedMusic, sourceHasAudio: sourceHasAudio,
                        seamPlans: seamPlans, timeline: timeline, transitionConfig: config, programFade: programFade,
                        inserts: resolvedInserts, colorGradeFilter: colorGradeFilter, sfx: resolvedSfx)
                    : await EncodeStreamCopyAsync(scratch, localVideoPath, encodedLocalPath, resolvedSpans, timeout, context.CancellationToken);
            }

            if (!encodeResult.Succeeded)
            {
                return Failure(
                    context, sw, "ENCODE_FAILED",
                    $"ffmpeg encode failed (exitCode={encodeResult.ExitCode}, timedOut={encodeResult.TimedOut}): {Truncate(encodeResult.StdErr)}",
                    edlStorageKey);
            }

            // ---- Upload the compiled video ----

            await context.ReportProgressAsync("Uploading compiled video", 100);
            string outputStorageKey;
            if (config.RegisterProjectFile)
            {
                ProjectWorkspaceFile uploaded = await _workspace.UploadBinaryFileAsync(
                    context.Execution.ProjectId, encodedLocalPath, outputFileName, "video/mp4",
                    SummaryStatus.Done, FileIndexingStatus.NotIndexed, context.CancellationToken, category: "outputFiles");
                outputStorageKey = uploaded.StorageKey;
            }
            else
            {
                string keyedFileName = $"{context.Execution.Id:D}/{outputFileName}";
                outputStorageKey = await _workspace.UploadArtifactAsync(
                    context.Execution.ProjectId, encodedLocalPath, keyedFileName, "video/mp4", context.CancellationToken,
                    category: "outputFiles");
            }

            var outputSummary = new JsonObject
            {
                ["status"] = "completed",
                ["outputStorageKey"] = outputStorageKey,
                ["artifactStorageKey"] = edlStorageKey,
                ["segments"] = resolvedSpans.Count,
                // Cut transitions: the REAL output-timeline length, accounting for crossfade
                // overlap — timeline.TotalSec is byte-identical to the pre-transitions
                // totalOutputSeconds when every seam has zero overlap (TransitionPolicy=Off).
                ["outputDurationSec"] = timeline.TotalSec,
                ["retainedRatio"] = retainedRatio,
                ["droppedSegmentsOverCap"] = droppedOverCap,
                ["sentenceCheck"] = BuildSentenceCheck(artifact, decision)
            };

            if (graphicsNode is not null)
                outputSummary["graphics"] = JsonNode.Parse(graphicsNode.ToJsonString(EnvelopeJsonOptions));

            if (musicNode is not null)
                outputSummary["music"] = JsonNode.Parse(musicNode.ToJsonString(EnvelopeJsonOptions));

            if (insertsNode is not null)
                outputSummary["inserts"] = JsonNode.Parse(insertsNode.ToJsonString(EnvelopeJsonOptions));

            if (colorGradeNode is not null)
                outputSummary["colorGrade"] = JsonNode.Parse(colorGradeNode.ToJsonString(EnvelopeJsonOptions));

            if (sfxNode is not null)
                outputSummary["sfx"] = JsonNode.Parse(sfxNode.ToJsonString(EnvelopeJsonOptions));

            outputSummary["audio"] = JsonNode.Parse(audioNode.ToJsonString(EnvelopeJsonOptions));

            if (transitionsNode is not null)
                outputSummary["transitions"] = JsonNode.Parse(transitionsNode.ToJsonString(EnvelopeJsonOptions));

            if (programFadeNode is not null)
                outputSummary["programFade"] = JsonNode.Parse(programFadeNode.ToJsonString(EnvelopeJsonOptions));

            return new StepExecutionResult
            {
                Output = outputSummary.ToJsonString(EnvelopeJsonOptions),
                NextStepIndex = context.CurrentStepIndex + 1,
                NewIterationCount = context.IterationCount,
                DurationMs = sw.ElapsedMilliseconds,
                TokensUsed = 0,
                Status = StepStatus.Completed,
                ArtifactStorageKey = edlStorageKey,
                OutputStorageKey = outputStorageKey
            };
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VideoCompile step {StepOrder} failed unexpectedly", step.StepOrder);
            return Failure(context, sw, "UNEXPECTED_ERROR", $"Unexpected error: {ex.Message}");
        }
        finally
        {
            scratch?.Dispose();
        }
    }

    // ---------------------------------------------------------------------
    // Analysis artifact / decision / source resolution
    // ---------------------------------------------------------------------

    private async Task<(string? Key, string? Error)> ResolveAnalysisArtifactKeyAsync(
        StepExecutionContext context, VideoCompileStepConfig config)
    {
        if (config.AnalysisStepResultId.HasValue)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            WorkflowEngineDbContext db = scope.ServiceProvider.GetRequiredService<WorkflowEngineDbContext>();

            WorkflowStepResult? result = await db.WorkflowStepResults
                .Include(r => r.WorkflowExecution)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == config.AnalysisStepResultId.Value, context.CancellationToken);

            if (result is null)
                return (null, $"AnalysisStepResultId '{config.AnalysisStepResultId.Value}' was not found.");

            // R23: this is an authorization boundary, not merely a lookup — a cross-execution
            // reference must never be allowed to read another project's analysis artifact.
            if (result.WorkflowExecution.ProjectId != context.Execution.ProjectId)
            {
                _logger.LogWarning(
                    "VideoCompile step {StepOrder}: AnalysisStepResultId {ResultId} belongs to project {OtherProject}, " +
                    "not the current execution's project {ProjectId}; refusing to resolve.",
                    context.Step.StepOrder, config.AnalysisStepResultId.Value, result.WorkflowExecution.ProjectId,
                    context.Execution.ProjectId);
                return (null, $"AnalysisStepResultId '{config.AnalysisStepResultId.Value}' does not belong to this project.");
            }

            return string.IsNullOrWhiteSpace(result.ArtifactStorageKey)
                ? (null, $"AnalysisStepResultId '{config.AnalysisStepResultId.Value}' has no ArtifactStorageKey.")
                : (result.ArtifactStorageKey, null);
        }

        // LastOrDefault, not FirstOrDefault: after a ReviewLoop loop-back re-executes an earlier
        // step, StepOutputHistory can (absent the executor's own pruning, kept here as
        // belt-and-braces) hold two entries for the same StepOrder — the stale first-iteration
        // one and the fresh one. An explicit step-order reference must always resolve to that
        // step's MOST RECENT output.
        StepOutputHistoryEntry? entry = context.StepOutputHistory
            .LastOrDefault(h => h.StepOrder == config.AnalysisStepOrder);

        return entry is null || string.IsNullOrWhiteSpace(entry.ArtifactStorageKey)
            ? (null, $"Step {config.AnalysisStepOrder} in this execution did not produce an ArtifactStorageKey.")
            : (entry.ArtifactStorageKey, null);
    }

    /// <summary>
    /// Resolves an <see cref="ExtractInputRef"/> (<c>Previous</c>/<c>Step</c> only) to the raw
    /// JSON output of that step. Generic over WHICH input it is resolving — used unchanged for
    /// <see cref="VideoCompileStepConfig.Decision"/> (via the <paramref name="label"/> default,
    /// preserving this method's exact prior signature/behavior for that caller) and, with
    /// <paramref name="label"/> set to <c>"GraphicsPlan"</c>, for
    /// <see cref="VideoCompileStepConfig.GraphicsPlan"/> (Phase 3) as well.
    /// </summary>
    private static (string? Json, string? Error) ResolveDecisionJson(
        StepExecutionContext context, ExtractInputRef decisionRef, string label = "Decision")
    {
        string? content = decisionRef.From switch
        {
            ExtractInputSource.Previous => context.StepOutputHistory
                .LastOrDefault(h => !string.IsNullOrWhiteSpace(h.Output))?.Output,
            // LastOrDefault, not FirstOrDefault — see the identical rationale on
            // ResolveAnalysisArtifactKeyAsync's StepOutputHistory lookup above: a loop-back can
            // leave a stale duplicate StepOrder entry in history, and an explicit step-order
            // reference must resolve to the freshest one.
            ExtractInputSource.Step => decisionRef.StepOrder.HasValue
                ? context.StepOutputHistory.LastOrDefault(h => h.StepOrder == decisionRef.StepOrder.Value)?.Output
                : null,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(content))
            return (null, $"{label} input resolved to empty content.");

        // The stored step output is the agent's raw completion text, not a re-serialized clean
        // JSON value — a reasoning-capable model can (and, observed live against a real vLLM/Qwen3
        // deployment, does) emit valid JSON and then keep going: trailing prose, a stray markdown
        // fence, or — the exact case that broke this — leaked tool-call-closing-tag tokens
        // (`</invoke>...</tool_call>`) after the JSON object, once the agent has any tools bound.
        // A bare JsonSerializer.Deserialize<T> over the whole string chokes on that trailing
        // content even though the JSON itself is perfectly valid, so extract just the balanced
        // {...} object first and ignore everything outside it.
        // Uses the LAST balanced object, not the first (ExtractJsonObject) — also observed live:
        // VideoStoryEditor's own chain-of-thought reasoning routinely contains small, complete,
        // informal brace-pair notation ("{s0,s0}") thousands of characters before the real final
        // decision JSON, which ExtractJsonObject's first-match strategy would latch onto instead
        // of the real answer. See RobustJsonExtractor.ExtractLastJsonObject's doc comment.
        string? extracted = RobustJsonExtractor.ExtractLastJsonObject(content);
        return extracted is null
            ? (null, $"{label} input did not contain a recognizable JSON object.")
            : (extracted, null);
    }

    /// <inheritdoc cref="RobustJsonExtractor.ExtractJsonObject"/>
    internal static string? ExtractJsonObject(string raw) => RobustJsonExtractor.ExtractJsonObject(raw);

    /// <summary>
    /// Deserializes a Decision/GraphicsPlan/ColorGradePlan/MusicPlan/SfxPlan JSON object, falling
    /// back to <see cref="RobustJsonExtractor.NormalizeQuotedStrings"/> and retrying once if the
    /// strict parse fails. Needed because <see cref="ResolveDecisionJson"/>'s own prose/fence
    /// stripping (<see cref="RobustJsonExtractor.ExtractJsonObject"/>) only strips what surrounds a
    /// balanced <c>{...}</c> object — it never repairs single-quoted, Python-dict-style content
    /// INSIDE it, the exact near-miss-JSON failure mode <see cref="VisionShotCaptioner"/> already
    /// guards against for vision captions, observed live here from a story-editor decision instead.
    /// Rethrows the original <see cref="JsonException"/> (preserving every existing call site's own
    /// catch/degrade behavior unchanged) when normalization can't help either.
    /// </summary>
    private static T? DeserializeWithQuoteFallback<T>(string json, JsonSerializerOptions options) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, options);
        }
        catch (JsonException ex)
        {
            string? normalized = RobustJsonExtractor.NormalizeQuotedStrings(json);
            return normalized is not null
                ? JsonSerializer.Deserialize<T>(normalized, options)
                : throw ex;
        }
    }

    /// <summary>
    /// The source video is whatever the referenced VideoAnalyze step actually analyzed —
    /// <see cref="VideoCompileStepConfig"/> does not carry its own <see cref="VideoSourceRef"/>
    /// so as not to duplicate (and risk drifting from) the analyze step's own config. This reads
    /// that step's <c>VideoAnalyzeConfigJson.Source</c> and resolves it exactly the way
    /// <c>VideoAnalyzeStepExecutor</c> would have resolved it itself. This matters most for
    /// <see cref="VideoSourceKind.ProjectFile"/> (an uploaded video with no prior step output at
    /// all): a naive "search StepOutputHistory for any OutputStorageKey" heuristic can never
    /// resolve that case, since a ProjectFile source is never represented as a step output.
    /// </summary>
    private async Task<(string? Key, string? Error)> ResolveSourceStorageKeyAsync(
        StepExecutionContext context, VideoCompileStepConfig config)
    {
        VideoAnalyzeStepConfig? analyzeConfig;
        List<StepOutputHistoryEntry>? historyBeforeAnalyzeStep = null;

        if (config.AnalysisStepResultId.HasValue)
        {
            // Cross-execution reference (R23-scoped): the analyze step's definition lives in
            // workflow_steps, which persists independently of any one execution, so it can still
            // be read even though context.AllSteps only covers the CURRENT execution's workflow.
            using IServiceScope scope = _scopeFactory.CreateScope();
            WorkflowEngineDbContext db = scope.ServiceProvider.GetRequiredService<WorkflowEngineDbContext>();

            WorkflowStepResult? result = await db.WorkflowStepResults
                .Include(r => r.WorkflowExecution)
                .Include(r => r.WorkflowStep)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == config.AnalysisStepResultId.Value, context.CancellationToken);

            if (result is null || result.WorkflowExecution.ProjectId != context.Execution.ProjectId)
            {
                // Already validated (and logged) by ResolveAnalysisArtifactKeyAsync above, which
                // runs first and would have failed the step before reaching here in practice.
                return (null, $"AnalysisStepResultId '{config.AnalysisStepResultId.Value}' could not be resolved.");
            }

            analyzeConfig = DeserializeAnalyzeConfig(result.WorkflowStep?.VideoAnalyzeConfigJson);
            // Cross-execution StepOutput/PreviousStepOutput resolution would require walking the
            // OTHER execution's step-output history, which this executor does not have loaded.
            // Rather than guess, only ProjectFile (self-contained; no execution history needed)
            // is supported cross-execution; other kinds fail with an explicit diagnostic below.
        }
        else
        {
            WorkflowStep? analyzeStep = context.AllSteps.FirstOrDefault(s => s.StepOrder == config.AnalysisStepOrder);
            if (analyzeStep is null)
                return (null, $"Step {config.AnalysisStepOrder} (expected to be the VideoAnalyze step) was not found in this workflow.");

            analyzeConfig = DeserializeAnalyzeConfig(analyzeStep.VideoAnalyzeConfigJson);
            historyBeforeAnalyzeStep = context.StepOutputHistory
                .Where(h => h.StepOrder < config.AnalysisStepOrder)
                .ToList();
        }

        if (analyzeConfig is null)
        {
            return (null,
                $"Step {config.AnalysisStepOrder}'s VideoAnalyzeConfigJson is missing or invalid; cannot determine the source video to compile.");
        }

        VideoSourceRef source = analyzeConfig.Source;
        switch (source.Kind)
        {
            case VideoSourceKind.ProjectFile:
            {
                if (!source.ProjectFileId.HasValue)
                    return (null, "The analyze step's Source=ProjectFile has no ProjectFileId.");

                IReadOnlyList<ProjectWorkspaceFile> files =
                    await _workspace.ListFilesAsync(context.Execution.ProjectId, context.CancellationToken);
                ProjectWorkspaceFile? file = files.FirstOrDefault(f => f.Id == source.ProjectFileId.Value);
                return file is null
                    ? (null, $"ProjectFile '{source.ProjectFileId.Value}' was not found in this project.")
                    : (file.StorageKey, null);
            }

            case VideoSourceKind.StepOutput:
            {
                if (historyBeforeAnalyzeStep is null)
                    return (null, "Cross-execution AnalysisStepResultId with Source=StepOutput is not supported; use ProjectFile for cross-execution recompiles.");
                if (!source.StepOrder.HasValue)
                    return (null, "The analyze step's Source=StepOutput has no StepOrder.");

                StepOutputHistoryEntry? entry = historyBeforeAnalyzeStep
                    .FirstOrDefault(h => h.StepOrder == source.StepOrder.Value);
                return entry is null || string.IsNullOrWhiteSpace(entry.OutputStorageKey)
                    ? (null, $"Step {source.StepOrder.Value} did not produce a video/media OutputStorageKey.")
                    : (entry.OutputStorageKey, null);
            }

            case VideoSourceKind.PreviousStepOutput:
            {
                if (historyBeforeAnalyzeStep is null)
                    return (null, "Cross-execution AnalysisStepResultId with Source=PreviousStepOutput is not supported; use ProjectFile for cross-execution recompiles.");

                StepOutputHistoryEntry? entry = historyBeforeAnalyzeStep
                    .LastOrDefault(h => !string.IsNullOrWhiteSpace(h.OutputStorageKey));
                return entry is null
                    ? (null, "The analyze step's Source=PreviousStepOutput, but no step before it produced a video/media OutputStorageKey.")
                    : (entry.OutputStorageKey, null);
            }

            default:
                return (null, $"Unknown VideoSourceKind '{source.Kind}'.");
        }
    }

    private static VideoAnalyzeStepConfig? DeserializeAnalyzeConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<VideoAnalyzeStepConfig>(json, ConfigJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ---------------------------------------------------------------------
    // Id -> time resolution, normalization
    // ---------------------------------------------------------------------

    /// <summary>
    /// How far forward (seconds) a transcript segment's raw ASR end time may be extended to reach
    /// a following detected silence gap — see <see cref="ExtendSegmentEndTowardNextSilence"/>.
    /// </summary>
    internal const double MaxSegmentEndExtensionSec = 1.0;

    /// <summary>
    /// Root cause (evidence-based, see docs/video-editing.md): Whisper-family ASR segment end
    /// timestamps can land slightly BEFORE the actual trailing audio genuinely stops — a trailing
    /// word or syllable that is present in the source audio but excluded from the segment's own
    /// <c>EndSec</c>. When a Keep span's <c>ToId</c> resolves to a transcript segment, trusting
    /// that raw <c>EndSec</c> verbatim is exactly what produced an edit that audibly stopped
    /// mid-sentence even when the story editor's chosen ids were otherwise reasonable. This never
    /// trusts the MODEL with a time value (the rushcut invariant is unchanged — the model still
    /// only ever chooses an id) — it corrects a purely deterministic ASR-boundary artifact using
    /// data the system itself already computed: <see cref="ISilenceDetector"/>'s independently
    /// detected silence gaps are a physically grounded signal for "speech has actually stopped"
    /// that a segment's own timestamp is not. Extends <paramref name="rawEndSec"/> forward only,
    /// and only up to <see cref="MaxSegmentEndExtensionSec"/>, to the start of the nearest silence
    /// gap that begins at or after it — never backward, never unbounded (a segment with no nearby
    /// following silence gap, e.g. because ASR degraded or the segment truly runs into more
    /// speech, is left exactly as reported).
    /// </summary>
    internal static double ExtendSegmentEndTowardNextSilence(
        double rawEndSec, IReadOnlyList<VideoAnalysisSilenceSpan> silenceSpans)
    {
        double best = rawEndSec;
        double bestGap = double.MaxValue;
        foreach (VideoAnalysisSilenceSpan gap in silenceSpans)
        {
            if (gap.StartSec < rawEndSec)
                continue;

            double delta = gap.StartSec - rawEndSec;
            if (delta < bestGap)
            {
                bestGap = delta;
                best = gap.StartSec;
            }
        }

        return bestGap <= MaxSegmentEndExtensionSec ? best : rawEndSec;
    }

    /// <summary>
    /// Multi-source addition: every id also resolves to the <see cref="VideoAnalysisShot.SourceIndex"/>
    /// (etc.) of the clip it came from, computed here server-side from the artifact's own id-space —
    /// never trusted from the model, exactly like every other id resolution in this executor. This
    /// is how <c>ExecuteAsync</c> validates that a single <c>Keep</c> span's <c>FromId</c>/<c>ToId</c>
    /// never straddle two different physical source clips.
    /// </summary>
    /// <remarks>
    /// <c>internal</c> (not private) so <see cref="MotionGraphicsPlacementAnnotator"/> can resolve
    /// the story editor's <c>Keep</c> span ids against the EXACT same id-to-time index this
    /// executor uses, rather than growing a second, drift-prone copy of the same id-namespace
    /// rules (which ids resolve, which deliberately do not, how a segment's end is extended).
    /// </remarks>
    internal static Dictionary<string, (double Start, double End, int SourceIndex)> BuildIdTimeIndex(VideoAnalysisArtifact artifact)
    {
        var index = new Dictionary<string, (double Start, double End, int SourceIndex)>(StringComparer.Ordinal);
        foreach (VideoAnalysisShot s in artifact.Shots)
            index[s.Id] = (s.StartSec, s.EndSec, s.SourceIndex);
        foreach (VideoAnalysisSilenceSpan s in artifact.SilenceSpans)
            index[s.Id] = (s.StartSec, s.EndSec, s.SourceIndex);
        foreach (VideoAnalysisSegment s in artifact.Segments)
            index[s.Id] = (s.StartSec, ExtendSegmentEndTowardNextSilence(s.EndSec, artifact.SilenceSpans), s.SourceIndex);
        // Words are deliberately excluded — never offered to the model in v1, so never resolvable here.
        //
        // Phase 3 (motion graphics) placements (artifact.Placements, ids "p{n}") are ALSO
        // deliberately excluded — they are a completely separate id namespace used only for
        // overlay planning (see ResolveGraphicsAsync), never for cut-anchor resolution. A `Keep`
        // span naming a placement id must fail UNKNOWN_ID exactly like any other id this index
        // does not contain (see VideoCompileStepExecutorTests) — adding placements here would
        // silently let a story-editor "Keep" span reference an id it was never meant to resolve.
        //
        // Background-music candidates (artifact.MusicCandidates, ids "m{n}") are excluded for the
        // exact same reason — a completely separate id namespace used only by ResolveMusicAsync to
        // resolve a MusicPlanOutput.TrackId to a ProjectFileId, never a time. A `Keep` span naming
        // a music-track id must also fail UNKNOWN_ID.
        //
        // Sound-effect clip candidates (artifact.SfxCandidates, ids "x{n}") are excluded for the
        // same reason again — an SFX-clip id names a FILE (resolved by ResolveSfxAsync to a
        // ProjectFileId), never a moment. Note the inverse relationship for SFX ANCHOR ids: an
        // SfxCue.AnchorId deliberately IS one of the cut-anchor ids this index resolves
        // (s{n}/g{n}/t{n}) — "this sound fires when this item begins" is the whole anchoring
        // model — but it is validated against OfferedIds by ResolveSfxAsync, never accepted
        // merely for existing here.
        //
        // Look groups (artifact.LookGroups, ids "k{n}") are excluded for the same reason as
        // placements and music candidates — but note the stronger property: k{n} is a purely
        // DESCRIPTIVE namespace that is never OFFERED to any agent at all (there is no
        // OfferedLookIds list, deliberately). No structured output in this feature has a field
        // that can name a look group, so a Keep span containing one can only come from a
        // hallucination, and must fail UNKNOWN_ID like any other.
        return index;
    }

    // ---------------------------------------------------------------------
    // Multi-source addition: per-source media lookup + a compile-time error code for the one
    // config combination that has no valid encode strategy.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Resolves the <see cref="VideoAnalysisMedia"/> (duration/fps/dimensions) that governs source
    /// clip <paramref name="sourceIndex"/> — every per-clip computation (frame quantization,
    /// padding clamps, graphics geometry) must call this instead of reading the artifact's
    /// top-level <see cref="VideoAnalysisArtifact.Media"/> directly once more than one source is
    /// possible. Falls back to the top-level <see cref="VideoAnalysisArtifact.Media"/> when
    /// <see cref="VideoAnalysisArtifact.Sources"/> is null/empty (a true legacy, pre-multi-source
    /// artifact, where every item's <c>SourceIndex</c> is always 0 and that top-level field IS that
    /// one source's own media) or the requested index is unexpectedly absent.
    /// </summary>
    private static VideoAnalysisMedia GetSourceMedia(VideoAnalysisArtifact artifact, int sourceIndex)
    {
        VideoAnalysisSourceInfo? info = artifact.Sources?.FirstOrDefault(s => s.SourceIndex == sourceIndex);
        return info?.Media ?? artifact.Media;
    }

    /// <summary>
    /// The storage key <see cref="VideoAnalyzeStepExecutor"/> itself recorded for source clip
    /// <paramref name="sourceIndex"/> at analysis time — null when the artifact predates the
    /// multi-source addition (<see cref="VideoAnalysisArtifact.Sources"/> is null/empty), in which
    /// case the caller falls back to re-deriving the single source's key the old way (walking the
    /// VideoAnalyze step's own config, via <see cref="ResolveSourceStorageKeyAsync"/>) — the exact
    /// resolution path every artifact used before this field existed.
    /// </summary>
    private static string? GetRecordedSourceStorageKey(VideoAnalysisArtifact artifact, int sourceIndex) =>
        artifact.Sources?.FirstOrDefault(s => s.SourceIndex == sourceIndex)?.StorageKey;

    /// <summary>
    /// Whether a transcript segment's own text reads as a finished sentence. The implementation
    /// now lives in <see cref="TranscriptPunctuation"/>, shared verbatim with
    /// <see cref="VideoAnalyzeStepExecutor"/> so the per-segment <c>endsSentence</c> flag offered
    /// in the bounded view and this compile-time check can never disagree; the name is kept here
    /// because it is this executor's established internal API.
    /// </summary>
    internal static bool EndsWithSentenceTerminalPunctuation(string text) =>
        TranscriptPunctuation.EndsSentence(text);

    /// <summary>
    /// Leading words that, when capitalized, signal the START of a new clause/topic rather than the
    /// continuation of the previous one. Deliberately a short, common-case list — a cheap lexical
    /// signal, not a discourse parser: the point is to stop the purely-temporal proximity test in
    /// <see cref="BuildSentenceCheck"/> from calling `"But one of the big things is…"`, 0.44s after
    /// a segment that ended on a complete sentence, a "continuation" of that sentence.
    /// </summary>
    private static readonly HashSet<string> NewThoughtLeadingMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Actually", "Additionally", "Also", "Alright", "And", "Anyway", "Besides", "But",
        "Finally", "First", "However", "Meanwhile", "Moreover", "Nevertheless", "Next",
        "Now", "Okay", "Otherwise", "Plus", "So", "Then", "Therefore", "Well", "Yet"
    };

    /// <summary>Leading quote/paren/dash characters stripped before reading a segment's first word.</summary>
    private static readonly char[] LeadingWrapperChars = ['"', '\'', '“', '‘', '(', '[', '-', '–', '—', '…', '.'];

    /// <summary>
    /// True when <paramref name="text"/> opens with a CAPITALIZED discourse/contrast marker from
    /// <see cref="NewThoughtLeadingMarkers"/> — ASR output capitalizes what it segmented as a new
    /// sentence, so "But …" reads as a new thought while a lowercase "but …" reads as a clause the
    /// previous segment was still in the middle of.
    /// </summary>
    internal static bool StartsWithNewThoughtMarker(string? text)
    {
        string trimmed = (text ?? string.Empty).TrimStart().TrimStart(LeadingWrapperChars).TrimStart();
        if (trimmed.Length == 0 || !char.IsUpper(trimmed[0]))
            return false;

        int end = 0;
        while (end < trimmed.Length && char.IsLetter(trimmed[end]))
            end++;

        return NewThoughtLeadingMarkers.Contains(trimmed[..end]);
    }

    /// <summary>
    /// Deterministic evidence for <c>AgentType.VideoReviewAgent</c> (see docs/video-editing.md
    /// "review loop"): whether the LAST kept span's <c>ToId</c> resolves to a transcript segment
    /// whose own text reads as a complete sentence. This is a cheap, reliable, non-LLM check —
    /// deliberately computed here in code rather than asked of the review model, exactly like
    /// every other id-anchored fact in this feature is resolved deterministically rather than
    /// trusted from a model. Always present in the compile step's output JSON (`applicable: false`
    /// when it does not apply — no transcript, or the last kept id is not a segment — rather than
    /// omitted, so a consumer never has to distinguish "not computed" from "not present").
    ///
    /// <para>
    /// The check also reports how much its own punctuation signal can be trusted for the source
    /// clip the last kept segment came from (<c>punctuationRatio</c>/<c>punctuationReliable</c>/
    /// <c>punctuationSampleSize</c>, see <see cref="TranscriptPunctuation"/>). Without that,
    /// <c>endsAtSentenceBoundary: false</c> reads identically whether the edit really cuts a
    /// sentence in half or the ASR deployment simply never emits full stops — and the review
    /// agent's hard score cap would fire unconditionally on an unpunctuated source regardless of
    /// edit quality (observed in production on a source where only 12% of segments were
    /// punctuated). Recomputed here from the artifact's own full segment list rather than read
    /// from a persisted field, so it works identically on artifacts written before this existed.
    /// </para>
    /// </summary>
    private static JsonObject BuildSentenceCheck(VideoAnalysisArtifact artifact, VideoEditDecisionOutput decision)
    {
        var result = new JsonObject { ["applicable"] = false };

        if (decision.Keep.Count == 0)
            return result;

        string lastToId = decision.Keep[^1].ToId;
        if (!lastToId.StartsWith('t'))
            return result;

        List<VideoAnalysisSegment> segments = artifact.Segments.ToList();
        int idx = segments.FindIndex(s => s.Id == lastToId);
        if (idx < 0 || string.IsNullOrWhiteSpace(segments[idx].Text))
            return result;

        VideoAnalysisSegment lastSegment = segments[idx];
        bool endsAtSentenceBoundary = EndsWithSentenceTerminalPunctuation(lastSegment.Text);

        // A near-contiguous following segment (in the FULL artifact, not merely the offered view)
        // is a signal Whisper's own segmentation split what was really one sentence in two —
        // evidence for the review agent, not itself a correction (the compile step never trusts an
        // id the story editor was not actually offered/did not choose).
        //
        // Timing proximity ALONE is not enough: two consecutive, unrelated sentences in continuous
        // speech are also typically <1s apart, so the gap test on its own reported "continues" for
        // an edit that had in fact stopped at a perfectly good point — feeding the review agent a
        // false "ends mid-sentence" verdict. Both a tight gap AND a next segment that does not open
        // with a new-thought discourse marker must now agree before this claims continuation.
        bool nextSegmentContinues = false;
        if (!endsAtSentenceBoundary && idx + 1 < segments.Count)
        {
            VideoAnalysisSegment next = segments[idx + 1];
            bool nearContiguous = next.StartSec - lastSegment.EndSec < 1.0;
            nextSegmentContinues = nearContiguous && !StartsWithNewThoughtMarker(next.Text);
        }

        // How trustworthy trailing punctuation is on THIS source clip (never the whole artifact:
        // one clip can come from a punctuating transcriber and another not, and only the clip the
        // last kept segment belongs to says anything about this particular cut).
        TranscriptPunctuation.Stats punctuation =
            TranscriptPunctuation.Summarize(segments.Where(s => s.SourceIndex == lastSegment.SourceIndex));

        result["applicable"] = true;
        result["lastKeptId"] = lastToId;
        result["lastSegmentText"] = lastSegment.Text;
        result["endsAtSentenceBoundary"] = endsAtSentenceBoundary;
        result["nextSegmentContinues"] = nextSegmentContinues;
        result["src"] = lastSegment.SourceIndex;
        result["punctuationRatio"] = punctuation.Ratio is { } ratio ? (JsonNode)TranscriptPunctuation.Round(ratio) : null;
        result["punctuationReliable"] = punctuation.Reliable;
        result["punctuationSampleSize"] = punctuation.SegmentCount;
        return result;
    }

    /// <summary>
    /// Merges adjacent/touching spans in list order. Multi-source addition: two spans are only
    /// ever coalesced when they share the SAME <c>SourceIndex</c> — merging spans from two
    /// different physical clips into one "span" would be meaningless (there is no single file to
    /// cut that merged range from). A single-source list is unaffected (every neighbor always
    /// shares the same, only, source).
    /// </summary>
    private static List<(double Start, double End, int SourceIndex)> CoalesceAdjacent(
        IReadOnlyList<(double Start, double End, int SourceIndex)> spans)
    {
        List<(double Start, double End, int SourceIndex)> result = new();
        foreach ((double start, double end, int sourceIndex) in spans)
        {
            if (result.Count > 0 && result[^1].SourceIndex == sourceIndex && start <= result[^1].End + AdjacencyEpsilonSec)
            {
                (double prevStart, double prevEnd, int prevSource) = result[^1];
                result[^1] = (prevStart, Math.Max(prevEnd, end), prevSource);
            }
            else
            {
                result.Add((start, end, sourceIndex));
            }
        }

        return result;
    }

    // ---------------------------------------------------------------------
    // Frame-exact rational arithmetic (R9) — internal so tests can assert exact frame numbers.
    // ---------------------------------------------------------------------

    internal static long ToStartFrame(double startSec, int fpsNum, int fpsDen) =>
        (long)Math.Floor(startSec * fpsNum / fpsDen);

    internal static long ToEndFrame(double endSec, int fpsNum, int fpsDen) =>
        (long)Math.Ceiling(endSec * fpsNum / fpsDen);

    internal static double FrameToSec(long frame, int fpsNum, int fpsDen) =>
        frame * (double)fpsDen / fpsNum;

    // internal (not private) so MapSourceToOutputSec/MapSourceWindowToOutput — internal so
    // dedicated tests can construct fixtures directly (see plan §4.4) — can expose it too.
    // SourceIndex (multi-source addition) defaults to 0 so every existing 6-arg test fixture
    // construction (single-source, implicitly source 0) keeps compiling and behaving unchanged.
    internal sealed record ResolvedSpan(
        double RequestedStart, double RequestedEnd, double SnappedStart, double SnappedEnd, long StartFrame, long EndFrame,
        int SourceIndex = 0);

    // ---------------------------------------------------------------------
    // Phase 3 (motion graphics): source-timeline -> output-timeline mapping. This is the single
    // most important correctness function in Phase 3 (see docs/video-editing.md "Motion graphics
    // (Phase 3)") — a placement's window was resolved against the SOURCE video, but drawtext's
    // `enable=`/`alpha=` expressions run against the OUTPUT video's own timeline (the one the
    // select/setpts filtergraph produces), which is shorter than the source and has every cut
    // gap removed. Both methods work purely from `spans` — already sorted, non-overlapping, using
    // their SnappedStart/SnappedEnd (the ACTUAL frame-quantized times that determine the real
    // output timeline, never the pre-quantization requested times) — by walking the spans and
    // accumulating output-timeline duration up to the point of interest.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Maps a source-timeline second to its position in the compiled output timeline. Returns
    /// <c>null</c> when <paramref name="sourceSec"/> falls inside a CUT region (no corresponding
    /// output frame exists) — including before the first kept span or after the last.
    /// </summary>
    /// <param name="sourceIndex">
    /// Multi-source addition: which source clip <paramref name="sourceSec"/> is a moment of.
    /// Defaults to 0 (every existing single-source caller/test — where every span's own
    /// <see cref="ResolvedSpan.SourceIndex"/> is also 0 — is unaffected). <paramref name="spans"/>
    /// is still walked IN FULL to correctly accumulate output-timeline duration (spans from other
    /// sources interleaved before/between this source's own spans still consume real output time),
    /// but only a span whose own <see cref="ResolvedSpan.SourceIndex"/> matches this parameter is
    /// ever checked against <paramref name="sourceSec"/> — a span from a DIFFERENT source can never
    /// spuriously "contain" a time value that is only meaningful on this source's own clock.
    /// </param>
    internal static double? MapSourceToOutputSec(IReadOnlyList<ResolvedSpan> spans, double sourceSec, int sourceIndex = 0) =>
        OutputTimeline.Build(spans, BuildHardCutSeamsForFallback(spans.Count)).MapToOutputSec(sourceSec, sourceIndex);

    /// <summary>
    /// Intersects a source-timeline <c>[startSec, endSec)</c> window (a placement's window,
    /// possibly duration-extended) with the kept spans and maps the surviving portion to the
    /// output timeline. Returns <c>null</c> if the window is entirely cut away; otherwise the
    /// (possibly-clipped) output-timeline window. A window straddling a cut boundary is clipped
    /// to only its kept portion(s) — specifically, to the FIRST kept portion it overlaps, since a
    /// single on-screen overlay cannot span a gap in the output video.
    /// </summary>
    /// <param name="sourceIndex">Multi-source addition — see <see cref="MapSourceToOutputSec"/>'s doc comment; defaults to 0.</param>
    internal static (double Start, double End)? MapSourceWindowToOutput(
        IReadOnlyList<ResolvedSpan> spans, double startSec, double endSec, int sourceIndex = 0) =>
        OutputTimeline.Build(spans, BuildHardCutSeamsForFallback(spans.Count)).MapWindowToOutput(startSec, endSec, sourceIndex);

    /// <summary>
    /// An all-<see cref="SeamTreatment.HardCut"/>, zero-overlap seam list for <paramref name="spanCount"/>
    /// spans — what <see cref="OutputTimeline.Build"/> needs when there is no real
    /// <see cref="SeamPlan"/> list to hand it (the pre-Cut-transitions
    /// <see cref="MapSourceToOutputSec"/>/<see cref="MapSourceWindowToOutput"/> static entry points,
    /// and the "transition planning failed unexpectedly" fallback in <see cref="ExecuteAsync"/>).
    /// Produces byte-identical output-timeline math to the pre-Cut-transitions implementation these
    /// two methods used to have inline — see <see cref="OutputTimeline.Build"/>'s own doc comment.
    /// </summary>
    internal static IReadOnlyList<SeamPlan> BuildHardCutSeamsForFallback(int spanCount)
    {
        int seamCount = Math.Max(0, spanCount - 1);
        var seams = new List<SeamPlan>(seamCount);
        for (int i = 0; i < seamCount; i++)
            seams.Add(new SeamPlan(i, SeamTreatment.HardCut, 0, 0, "", 0, "fallback:hardCut"));
        return seams;
    }

    // ---------------------------------------------------------------------
    // Phase 3 (motion graphics): plan resolution — soft-failure only, exactly like a per-shot
    // Phase 2 vision-caption failure never aborting the whole analysis. A missing/bad graphics
    // plan, an unknown placement id, or a missing drawtext filter all degrade to "no graphics
    // applied", never to a failed compile — the cut is the primary deliverable.
    // ---------------------------------------------------------------------

    private sealed record DroppedOverlay(string PlacementId, string Reason);

    private async Task<(List<ResolvedOverlay> Overlays, JsonObject GraphicsNode)> ResolveGraphicsAsync(
        StepExecutionContext context,
        VideoCompileStepConfig config,
        VideoAnalysisArtifact artifact,
        IReadOnlyList<ResolvedSpan> resolvedSpans,
        OutputTimeline timeline,
        VideoScratchSpace scratch,
        VideoAnalysisMedia canonicalMedia,
        CancellationToken ct)
    {
        var graphics = new JsonObject { ["enabled"] = true, ["applied"] = false, ["appliedOverlayCount"] = 0, ["unavailable"] = false };
        List<DroppedOverlay> dropped = new();

        if (config.GraphicsPlan is null)
        {
            graphics["reason"] = "No GraphicsPlan configured.";
            graphics["droppedOverlays"] = new JsonArray();
            return ([], graphics);
        }

        (string? planJson, string? planError) = ResolveDecisionJson(context, config.GraphicsPlan, "GraphicsPlan");
        if (planJson is null)
        {
            _logger.LogWarning("VideoCompile step {StepOrder}: GraphicsPlan unresolved: {Error}", context.Step.StepOrder, planError);
            graphics["reason"] = planError ?? "GraphicsPlan input could not be resolved.";
            graphics["droppedOverlays"] = new JsonArray();
            return ([], graphics);
        }

        MotionGraphicsPlanOutput? plan;
        try
        {
            plan = DeserializeWithQuoteFallback<MotionGraphicsPlanOutput>(planJson, DecisionJsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "VideoCompile step {StepOrder}: GraphicsPlan is not valid JSON.", context.Step.StepOrder);
            graphics["reason"] = $"GraphicsPlan content is not valid JSON: {ex.Message}";
            graphics["droppedOverlays"] = new JsonArray();
            return ([], graphics);
        }

        // An explicit `"overlays": null` in the JSON overwrites the `= new()` default with a real
        // null (System.Text.Json does not run property initializers for an explicit JSON null),
        // which would otherwise NRE below instead of degrading to "no overlays, graphics not
        // applied" the same way an empty overlays array already does.
        if (plan is not null)
            plan.Overlays ??= [];

        if (plan is null || plan.Overlays.Count == 0)
        {
            graphics["droppedOverlays"] = new JsonArray();
            return ([], graphics);
        }

        // The drawtext-availability gate only matters for TEXT overlays — a plan made up
        // entirely of rendered-asset overlays (Phase 3's OverlayAssetFilterBuilder path) never
        // touches the drawtext filter at all, so it must not be blocked by drawtext being
        // unavailable in this ffmpeg build.
        bool planHasTextOverlay = plan.Overlays.Any(o => string.IsNullOrWhiteSpace(o.RenderedAssetStorageKey));
        if (planHasTextOverlay && !await IsDrawtextAvailableAsync(ct))
        {
            _logger.LogWarning(
                "VideoCompile step {StepOrder}: drawtext filter unavailable in this ffmpeg build; skipping all overlays.",
                context.Step.StepOrder);
            graphics["unavailable"] = true;
            graphics["reason"] = "The ffmpeg build in this container does not expose the drawtext filter (missing font/libfreetype support).";
            graphics["droppedOverlays"] = new JsonArray();
            return ([], graphics);
        }

        Dictionary<string, VideoAnalysisPlacement> placementById =
            (artifact.Placements ?? []).ToDictionary(p => p.Id, StringComparer.Ordinal);
        Dictionary<string, VideoAnalysisShot> shotById =
            artifact.Shots.ToDictionary(s => s.Id, StringComparer.Ordinal);
        HashSet<string> offeredPlacementIds = new(artifact.OfferedPlacementIds ?? [], StringComparer.Ordinal);

        List<(MotionGraphicsOverlay Overlay, VideoAnalysisPlacement Placement)> known = new();
        foreach (MotionGraphicsOverlay overlay in plan.Overlays)
        {
            // Validated against OfferedPlacementIds specifically (not merely "present in
            // artifact.Placements") — the same "offered is a stricter check than exists"
            // discipline VideoEditKeepSpan ids already use (see docs/video-editing.md).
            if (!offeredPlacementIds.Contains(overlay.PlacementId) ||
                !placementById.TryGetValue(overlay.PlacementId, out VideoAnalysisPlacement? placement))
            {
                dropped.Add(new DroppedOverlay(overlay.PlacementId, "unknown_placement_id"));
                continue;
            }

            known.Add((overlay, placement));
        }

        int maxOverlays = Math.Max(0, config.MaxOverlays);
        if (known.Count > maxOverlays)
        {
            foreach ((MotionGraphicsOverlay overlay, _) in known.Skip(maxOverlays))
                dropped.Add(new DroppedOverlay(overlay.PlacementId, "max_overlays_exceeded"));
            known = known.Take(maxOverlays).ToList();
        }

        // Phase 3 rendered-asset overlays validate against THIS execution's own storage prefix —
        // the exact prefix RenderVideoAndUploadToStorage itself constructs
        // (projects/{projectId}/outputFiles/{executionId}/...), never trusting the model-supplied
        // string blindly even though the agent produced it via a real render+upload (see
        // MotionGraphicsOverlay.RenderedAssetStorageKey's doc comment). Mirrors the prefix-check
        // pattern StepResultArtifactsController already applies to ArtifactStorageKey.
        string expectedAssetKeyPrefix = $"projects/{context.Execution.ProjectId}/outputFiles/{context.Execution.Id:D}/";

        List<ResolvedOverlay> resolved = new(known.Count);
        foreach ((MotionGraphicsOverlay overlay, VideoAnalysisPlacement placement) in known)
        {
            bool hasAsset = !string.IsNullOrWhiteSpace(overlay.RenderedAssetStorageKey);

            // Text/Subtext are ignored entirely for an asset overlay (see
            // MotionGraphicsOverlay.RenderedAssetStorageKey's doc comment) — an overlay is one or
            // the other, never both.
            string text = "";
            string subtext = "";
            if (!hasAsset)
            {
                text = OverlayTextSanitizer.Sanitize(overlay.Text, config.MaxOverlayTextChars);
                if (text.Length == 0)
                {
                    dropped.Add(new DroppedOverlay(overlay.PlacementId, "empty_text_after_sanitization"));
                    continue;
                }

                subtext = OverlayTextSanitizer.Sanitize(overlay.Subtext, config.MaxOverlaySubtextChars);
            }

            int durationMs = overlay.Duration switch
            {
                "Short" => config.OverlayShortMs,
                "Medium" => config.OverlayMediumMs,
                "Hold" => config.OverlayHoldMs,
                _ => config.OverlayMediumMs
            };
            if (overlay.Duration is not ("Short" or "Medium" or "Hold"))
            {
                _logger.LogInformation(
                    "VideoCompile step {StepOrder}: overlay for placement {PlacementId} has unrecognized Duration '{Duration}'; defaulting to Medium.",
                    context.Step.StepOrder, overlay.PlacementId, overlay.Duration);
            }

            // The placement's own window already encodes WHERE (spatially/temporally) is a good
            // moment; Duration (Short/Medium/Hold) controls HOW LONG the overlay stays up, and the
            // owning shot's own end bounds the whole candidate window — never beyond what Phase 1
            // actually analyzed for that shot.
            double shotEnd = shotById.TryGetValue(placement.ShotId, out VideoAnalysisShot? shot)
                ? shot.EndSec
                : placement.EndSec;

            // ORDER MATTERS (bug fix): the FULL candidate window [placement.StartSec, shotEnd] is
            // intersected with the kept spans FIRST, and durationMs is applied only to whatever
            // survived. Truncating to durationMs BEFORE the intersection tested only the window's
            // first few seconds, which can sit entirely inside a cut region even when the full
            // window overlaps a kept span by many seconds — the overlay was then wrongly dropped as
            // "cut_away". (Observed: placement p6, real window [0, 29.83]s, kept span [4.8, 51.4]s
            // — 25s of genuine overlap, but only [0, 3.0]s was tested.)
            double sourceStart = placement.StartSec;
            double sourceEnd = shotEnd;

            // Multi-source addition: a placement's window is only meaningful on ITS OWN source
            // clip's clock (placement.SourceIndex, resolved server-side from the artifact's own
            // id-space when placements were built — never trusted from the model). Passing it
            // through here is what stops a placement from one clip spuriously mapping against a
            // DIFFERENT clip's kept spans that merely happen to share overlapping numeric ranges.
            (double Start, double End)? keptWindow = timeline.MapWindowToOutput(sourceStart, sourceEnd, placement.SourceIndex);
            if (keptWindow is null)
            {
                dropped.Add(new DroppedOverlay(overlay.PlacementId, "cut_away"));
                continue;
            }

            // The overlay goes up where the surviving intersection actually BEGINS on the output
            // timeline and stays up for durationMs, clamped to that intersection's own end (which
            // can itself be shorter than durationMs). Source and output time advance 1:1 inside a
            // single kept span — MapSourceWindowToOutput only ever returns a window within ONE
            // span — so applying the duration on the output clock is exact, not an approximation.
            (double Start, double End) outputWindow = (
                keptWindow.Value.Start,
                Math.Min(keptWindow.Value.Start + durationMs / 1000.0, keptWindow.Value.End));

            string emphasis = overlay.Emphasis is "Subtle" or "Normal" or "Strong" ? overlay.Emphasis : "Normal";

            string? renderedAssetLocalPath = null;
            if (hasAsset)
            {
                if (!overlay.RenderedAssetStorageKey.StartsWith(expectedAssetKeyPrefix, StringComparison.Ordinal))
                {
                    _logger.LogWarning(
                        "VideoCompile step {StepOrder}: overlay for placement {PlacementId} named a RenderedAssetStorageKey outside this execution's own outputFiles prefix; dropping.",
                        context.Step.StepOrder, overlay.PlacementId);
                    dropped.Add(new DroppedOverlay(overlay.PlacementId, "invalid_asset_storage_key"));
                    continue;
                }

                string rawAssetExtension = Path.GetExtension(overlay.RenderedAssetStorageKey);
                string assetExtension = AllowedRenderedAssetExtensions.Contains(rawAssetExtension) ? rawAssetExtension : ".webm";
                string localAssetPath = scratch.GetPath($"gfx-asset-{Guid.NewGuid():N}{assetExtension}");
                MediaProbeResult assetProbe;
                try
                {
                    await _workspace.DownloadStorageKeyToFileAsync(
                        context.Execution.ProjectId, overlay.RenderedAssetStorageKey, localAssetPath, ct);

                    // Defensive: probe before ever trusting this as an extra ffmpeg -i input. A
                    // corrupt/unreadable asset would otherwise fail the WHOLE ffmpeg invocation
                    // (all overlays and the cut itself), which is exactly the "graphics must never
                    // hold the cut hostage" guarantee this method exists to uphold — so a bad
                    // asset must be caught and dropped HERE, one overlay at a time, never let
                    // through to the encoder.
                    assetProbe = await _mediaProbe.ProbeAsync(localAssetPath, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "VideoCompile step {StepOrder}: rendered asset for placement {PlacementId} could not be downloaded/probed; dropping this overlay.",
                        context.Step.StepOrder, overlay.PlacementId);
                    dropped.Add(new DroppedOverlay(overlay.PlacementId, "asset_download_or_probe_failed"));
                    continue;
                }

                // The one invariant every rendered-asset overlay promises everywhere in this
                // feature's prompts/docs — "a transparent-background clip" — is never trusted from
                // the model. A render that ignored the documented --pixel-format=yuva420p
                // --codec=vp9 recipe (or otherwise lost its alpha plane) would otherwise composite
                // as a solid, opaque rectangle over the edited video — exactly the "black square
                // with giant shadows" failure mode this check exists to catch, one overlay at a
                // time, same degrade-not-fail discipline as the probe failure above.
                if (!AlphaPixelFormats.HasAlpha(assetProbe.PixFmt))
                {
                    _logger.LogWarning(
                        "VideoCompile step {StepOrder}: rendered asset for placement {PlacementId} has no alpha channel (pix_fmt={PixFmt}); dropping this overlay rather than compositing it opaque.",
                        context.Step.StepOrder, overlay.PlacementId, assetProbe.PixFmt ?? "(unknown)");
                    dropped.Add(new DroppedOverlay(overlay.PlacementId, "asset_missing_alpha_channel"));
                    continue;
                }

                renderedAssetLocalPath = localAssetPath;
            }

            resolved.Add(new ResolvedOverlay(
                overlay.PlacementId, overlay.Kind, text, subtext, durationMs, emphasis,
                outputWindow.Start, outputWindow.End, placement.Rect, placement.TextColor,
                renderedAssetLocalPath, PlacementRegion: placement.Region));
        }

        graphics["applied"] = resolved.Count > 0;
        graphics["appliedOverlayCount"] = resolved.Count;
        graphics["droppedOverlays"] = new JsonArray(dropped.Select(d => (JsonNode)new JsonObject
        {
            ["placementId"] = d.PlacementId,
            ["reason"] = d.Reason
        }).ToArray());

        // Exact, deterministic frame-coverage for each applied overlay's drawn box — the same
        // geometry DrawtextFilterBuilder/OverlayAssetFilterBuilder will actually render (never
        // re-derived approximately), so VideoReviewAgent can judge "is this overlay oversized"
        // from a hard number instead of eyeballing pixels. See docs/video-editing.md "Motion
        // graphics (Phase 3)" / "review loop".
        double frameArea = Math.Max(1, canonicalMedia.Width) * (double)Math.Max(1, canonicalMedia.Height);
        graphics["appliedOverlays"] = new JsonArray(resolved.Select(o =>
        {
            (int _, int _, int boxW, int boxH) = DrawtextFilterBuilder.ComputeAccentBoxPixels(
                o.Rect, o.PlacementRegion, canonicalMedia.Width, canonicalMedia.Height,
                config.OverlayBoxHeightPct, config.OverlayBoxWidthPct);
            double coveragePct = Math.Round(boxW * (double)boxH / frameArea * 100, 1);
            return (JsonNode)new JsonObject
            {
                ["placementId"] = o.PlacementId,
                ["coveragePct"] = coveragePct
            };
        }).ToArray());

        return (resolved, graphics);
    }

    // ---------------------------------------------------------------------
    // Tracked screen inserts (see docs/video-editing.md "Tracked screen inserts (Phase 5)"):
    // plan resolution — soft-failure only, exactly like graphics/music. The model contributed
    // only an offered r{n} id and a render it performed itself; every number below (corner
    // keyframes, output windows, frame indices) comes from the artifact's deterministic tracking
    // data mapped through OutputTimeline — never from the model.
    // ---------------------------------------------------------------------

    private sealed record DroppedInsert(string RegionId, string Reason);

    private async Task<(List<ResolvedScreenInsert> Inserts, JsonObject InsertsNode)> ResolveInsertsAsync(
        StepExecutionContext context,
        VideoCompileStepConfig config,
        VideoAnalysisArtifact artifact,
        OutputTimeline timeline,
        VideoScratchSpace scratch,
        VideoAnalysisMedia canonicalMedia,
        bool segmentedEncode,
        CancellationToken ct)
    {
        var node = new JsonObject { ["enabled"] = true, ["applied"] = false, ["appliedInsertCount"] = 0, ["unavailable"] = false };
        List<DroppedInsert> dropped = new();

        JsonObject Finish(List<ResolvedScreenInsert> resolved)
        {
            node["applied"] = resolved.Count > 0;
            node["appliedInsertCount"] = resolved.Count;
            node["droppedInserts"] = new JsonArray(dropped.Select(d => (JsonNode)new JsonObject
            {
                ["regionId"] = d.RegionId,
                ["reason"] = d.Reason
            }).ToArray());
            return node;
        }

        if (segmentedEncode)
        {
            // v1 limitation (docs/video-editing.md "Tracked screen inserts (Phase 5)"): the
            // animated corner-pin path is only wired into the single-source select-path encode.
            node["reason"] = "Screen inserts are not applied on a multi-source compile or one with overlapping seam transitions in v1.";
            return ([], Finish([]));
        }

        // Inserts ride the SAME plan reference as graphics — the planner emits overlays and
        // inserts in one MotionGraphicsPlanOutput.
        if (config.GraphicsPlan is null)
        {
            node["reason"] = "No GraphicsPlan configured (screen inserts are read from the same MotionGraphicsPlanOutput as overlays).";
            return ([], Finish([]));
        }

        (string? planJson, string? planError) = ResolveDecisionJson(context, config.GraphicsPlan, "GraphicsPlan");
        if (planJson is null)
        {
            node["reason"] = planError ?? "GraphicsPlan input could not be resolved.";
            return ([], Finish([]));
        }

        MotionGraphicsPlanOutput? plan;
        try
        {
            plan = DeserializeWithQuoteFallback<MotionGraphicsPlanOutput>(planJson, DecisionJsonOptions);
        }
        catch (JsonException ex)
        {
            node["reason"] = $"GraphicsPlan content is not valid JSON: {ex.Message}";
            return ([], Finish([]));
        }

        // Same explicit-JSON-null hardening as ResolveGraphicsAsync's plan.Overlays ??= [].
        if (plan is not null)
            plan.Inserts ??= [];

        if (plan is null || plan.Inserts.Count == 0)
            return ([], Finish([]));

        if (!await IsPerspectiveAvailableAsync(ct))
        {
            _logger.LogWarning(
                "VideoCompile step {StepOrder}: perspective/alphamerge filters unavailable in this ffmpeg build; skipping all screen inserts.",
                context.Step.StepOrder);
            node["unavailable"] = true;
            node["reason"] = "The ffmpeg build in this container does not expose the perspective and alphamerge filters.";
            return ([], Finish([]));
        }

        Dictionary<string, VideoInsertRegionTrack> trackById =
            (artifact.InsertRegions ?? []).ToDictionary(r => r.Id, StringComparer.Ordinal);
        HashSet<string> offeredInsertRegionIds = new(artifact.OfferedInsertRegionIds ?? [], StringComparer.Ordinal);

        double minConfidence = Math.Clamp(config.MinInsertConfidence, 0, 1);
        int maxInserts = Math.Max(0, config.MaxInserts);
        int maxExprKeyframes = Math.Clamp(config.MaxInsertExprKeyframes, 2, 500);
        double overscan = Math.Clamp(config.InsertOverscan, 0, 0.1);
        string expectedAssetKeyPrefix = $"projects/{context.Execution.ProjectId}/outputFiles/{context.Execution.Id:D}/";

        var seenRegionIds = new HashSet<string>(StringComparer.Ordinal);
        List<ResolvedScreenInsert> resolved = new();
        foreach (ScreenInsert insert in plan.Inserts)
        {
            // "Offered is a stricter check than exists" — same discipline as every other id family.
            if (!offeredInsertRegionIds.Contains(insert.RegionId) ||
                !trackById.TryGetValue(insert.RegionId, out VideoInsertRegionTrack? track))
            {
                dropped.Add(new DroppedInsert(insert.RegionId, "unknown_region_id"));
                continue;
            }

            if (!seenRegionIds.Add(insert.RegionId))
            {
                dropped.Add(new DroppedInsert(insert.RegionId, "duplicate_region_id"));
                continue;
            }

            if (resolved.Count >= maxInserts)
            {
                dropped.Add(new DroppedInsert(insert.RegionId, "max_inserts_exceeded"));
                continue;
            }

            if (track.Confidence < minConfidence)
            {
                dropped.Add(new DroppedInsert(insert.RegionId, "confidence_below_threshold"));
                continue;
            }

            if (track.Keyframes is not { Count: > 0 })
            {
                dropped.Add(new DroppedInsert(insert.RegionId, "no_tracking_keyframes"));
                continue;
            }

            if (string.IsNullOrWhiteSpace(insert.RenderedAssetStorageKey) ||
                !insert.RenderedAssetStorageKey.StartsWith(expectedAssetKeyPrefix, StringComparison.Ordinal))
            {
                dropped.Add(new DroppedInsert(insert.RegionId, "invalid_asset_storage_key"));
                continue;
            }

            // Map the track's window through the cut to the output timeline. A window straddling a
            // cut is clipped to its FIRST kept portion, exactly like an overlay (a single insert
            // cannot span a gap in the output video).
            (double Start, double End)? outputWindow = timeline.MapWindowToOutput(track.StartSec, track.EndSec, track.SourceIndex);
            if (outputWindow is null)
            {
                dropped.Add(new DroppedInsert(insert.RegionId, "cut_away"));
                continue;
            }

            IReadOnlyList<InsertQuadFrame> keyframes = BuildInsertKeyframes(
                track, timeline, canonicalMedia, outputWindow.Value, maxExprKeyframes, overscan);
            if (keyframes.Count == 0)
            {
                dropped.Add(new DroppedInsert(insert.RegionId, "no_keyframes_survive_cut"));
                continue;
            }

            string rawExtension = Path.GetExtension(insert.RenderedAssetStorageKey);
            string extension = AllowedRenderedAssetExtensions.Contains(rawExtension) ? rawExtension : ".mp4";
            string localAssetPath = scratch.GetPath($"insert-asset-{Guid.NewGuid():N}{extension}");
            try
            {
                await _workspace.DownloadStorageKeyToFileAsync(
                    context.Execution.ProjectId, insert.RenderedAssetStorageKey, localAssetPath, ct);

                // Same defensive probe as rendered-asset overlays: a corrupt asset must be caught
                // and dropped HERE, one insert at a time, never handed to the encoder where it
                // would fail the whole compile.
                await _mediaProbe.ProbeAsync(localAssetPath, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "VideoCompile step {StepOrder}: rendered asset for insert region {RegionId} could not be downloaded/probed; dropping this insert.",
                    context.Step.StepOrder, insert.RegionId);
                dropped.Add(new DroppedInsert(insert.RegionId, "asset_download_or_probe_failed"));
                continue;
            }

            resolved.Add(new ResolvedScreenInsert(
                insert.RegionId, localAssetPath, outputWindow.Value.Start, outputWindow.Value.End,
                keyframes, canonicalMedia.FpsNum > 0 ? canonicalMedia.FpsNum : 30, canonicalMedia.FpsDen > 0 ? canonicalMedia.FpsDen : 1));
        }

        return (resolved, Finish(resolved));
    }

    /// <summary>
    /// Converts a track's normalized, source-timeline corner keyframes into pixel-space,
    /// insert-branch-frame-indexed keyframes for <see cref="ScreenInsertFilterBuilder"/>: each
    /// keyframe's source time is mapped through the cut (<see cref="OutputTimeline.MapToOutputSec"/>
    /// — keyframes falling in a cut gap are skipped), re-based onto the insert branch's own clock
    /// (frame 0 = the insert's on-screen start), uniformly downsampled to at most
    /// <paramref name="maxKeyframes"/>, and expanded outward about the quad centroid by
    /// <paramref name="overscan"/> so the plate's edge fringe hides under the inserted content.
    /// Internal + pure so tests can drive it directly.
    /// </summary>
    internal static IReadOnlyList<InsertQuadFrame> BuildInsertKeyframes(
        VideoInsertRegionTrack track,
        OutputTimeline timeline,
        VideoAnalysisMedia canonicalMedia,
        (double Start, double End) outputWindow,
        int maxKeyframes,
        double overscan)
    {
        int fpsNum = canonicalMedia.FpsNum > 0 ? canonicalMedia.FpsNum : 30;
        int fpsDen = canonicalMedia.FpsDen > 0 ? canonicalMedia.FpsDen : 1;
        double fps = fpsNum / (double)fpsDen;
        int width = Math.Max(2, canonicalMedia.Width);
        int height = Math.Max(2, canonicalMedia.Height);
        const double Epsilon = 1e-6;

        var surviving = new List<(long Frame, VideoInsertQuadKeyframe K)>();
        foreach (VideoInsertQuadKeyframe k in track.Keyframes)
        {
            double? outSec = timeline.MapToOutputSec(k.TimeSec, track.SourceIndex);
            if (outSec is null || outSec.Value < outputWindow.Start - Epsilon || outSec.Value > outputWindow.End + Epsilon)
                continue;

            long frame = (long)Math.Round(Math.Max(0, outSec.Value - outputWindow.Start) * fps);
            surviving.Add((frame, k));
        }

        // A mapped window with no surviving keyframes (very sparse tracking, all samples in a
        // trimmed sliver) still gets ONE constant quad: the keyframe nearest the window's own
        // source moment — a static composite beats a dropped one when the track itself was good.
        if (surviving.Count == 0)
        {
            VideoInsertQuadKeyframe nearest = track.Keyframes
                .OrderBy(k => Math.Abs(k.TimeSec - (track.StartSec + track.EndSec) / 2))
                .First();
            surviving.Add((0, nearest));
        }

        // Uniform downsample to the expression-size cap, always keeping first and last.
        List<(long Frame, VideoInsertQuadKeyframe K)> sampled;
        if (surviving.Count <= maxKeyframes)
        {
            sampled = surviving;
        }
        else
        {
            sampled = new List<(long, VideoInsertQuadKeyframe)>(maxKeyframes);
            for (int i = 0; i < maxKeyframes; i++)
            {
                int idx = (int)Math.Round(i * (surviving.Count - 1) / (double)(maxKeyframes - 1));
                sampled.Add(surviving[idx]);
            }
        }

        var result = new List<InsertQuadFrame>(sampled.Count);
        long lastFrame = -1;
        foreach ((long frame, VideoInsertQuadKeyframe k) in sampled)
        {
            if (frame <= lastFrame && result.Count > 0)
                continue;
            lastFrame = frame;

            double cx = (k.X0 + k.X1 + k.X2 + k.X3) / 4.0;
            double cy = (k.Y0 + k.Y1 + k.Y2 + k.Y3) / 4.0;
            double Ex(double x) => (cx + (x - cx) * (1 + overscan)) * width;
            double Ey(double y) => (cy + (y - cy) * (1 + overscan)) * height;

            result.Add(new InsertQuadFrame(
                frame,
                Ex(k.X0), Ey(k.Y0), Ex(k.X1), Ey(k.Y1), Ex(k.X2), Ey(k.Y2), Ex(k.X3), Ey(k.Y3)));
        }

        return result;
    }

    /// <summary>
    /// Probes whether the ffmpeg build exposes both the <c>perspective</c> and <c>alphamerge</c>
    /// filters (tracked screen inserts), caching for the process lifetime — mirrors
    /// <see cref="IsDrawtextAvailableAsync"/> exactly, never throws.
    /// </summary>
    private async Task<bool> IsPerspectiveAvailableAsync(CancellationToken ct)
    {
        if (_perspectiveAvailableCache.HasValue)
            return _perspectiveAvailableCache.Value;

        await PerspectiveProbeLock.WaitAsync(ct);
        try
        {
            if (_perspectiveAvailableCache.HasValue)
                return _perspectiveAvailableCache.Value;

            try
            {
                VideoToolResult result = await _videoToolRunner.RunFfmpegAsync(
                    new[] { "-hide_banner", "-filters" }, TimeSpan.FromSeconds(15), ct);
                _perspectiveAvailableCache = result.Succeeded &&
                    result.StdOut.Contains("perspective", StringComparison.Ordinal) &&
                    result.StdOut.Contains("alphamerge", StringComparison.Ordinal);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "VideoCompile: perspective/alphamerge availability probe failed; treating as unavailable.");
                _perspectiveAvailableCache = false;
            }

            return _perspectiveAvailableCache.Value;
        }
        finally
        {
            PerspectiveProbeLock.Release();
        }
    }

    // ---------------------------------------------------------------------
    // Color grading (see docs/video-editing.md "Color grading")
    // ---------------------------------------------------------------------

    /// <summary>
    /// Resolves the step's <see cref="VideoCompileStepConfig.ColorGradePlan"/> to a concrete,
    /// first-party ffmpeg filter chain (or null when no grade applies) plus the EDL/output
    /// <c>colorGrade</c> report node. Purely soft-failure — every degrade path returns a null
    /// chain with a machine-readable <c>reason</c>, never a failed compile. The plan contributes
    /// only WORDS (validated against <see cref="ColorGradeFilterBuilder"/>'s allowlists — an
    /// unknown <c>Look</c> is dropped as <c>unknown_look_word</c> rather than guessed at;
    /// unknown strength/tone words normalize per the builder's documented rules); every number
    /// in the chain comes from the builder's own tables.
    /// </summary>
    private async Task<(string? FilterChain, JsonObject ColorGradeNode)> ResolveColorGradeAsync(
        StepExecutionContext context,
        VideoCompileStepConfig config,
        CancellationToken ct)
    {
        var node = new JsonObject { ["enabled"] = true, ["applied"] = false };

        if (config.ColorGradePlan is null)
        {
            node["reason"] = "no_plan_configured";
            return (null, node);
        }

        (string? planJson, string? planError) = ResolveDecisionJson(context, config.ColorGradePlan, "ColorGradePlan");
        if (planJson is null)
        {
            _logger.LogInformation(
                "VideoCompile step {StepOrder}: ColorGradePlan unresolved: {Error}", context.Step.StepOrder, planError);
            node["reason"] = "plan_unresolved";
            return (null, node);
        }

        ColorGradePlanOutput? plan = null;
        try
        {
            plan = DeserializeWithQuoteFallback<ColorGradePlanOutput>(planJson, DecisionJsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogInformation(
                ex, "VideoCompile step {StepOrder}: ColorGradePlan is not valid JSON.", context.Step.StepOrder);
        }

        if (plan is null)
        {
            node["reason"] = "plan_invalid_json";
            return (null, node);
        }

        string look = plan.Look ?? string.Empty;
        node["look"] = look;

        if (string.IsNullOrWhiteSpace(look) || !ColorGradeFilterBuilder.AllowedLooks.Contains(look))
        {
            // Never guess a substitute for an unrecognized look — the words-only contract's
            // value is that every applied number traces to a recognized word.
            node["reason"] = "unknown_look_word";
            return (null, node);
        }

        // Normalized words are reported (not the raw ones) so the EDL states exactly what the
        // deterministic resolution actually used.
        string strength = ColorGradeFilterBuilder.NormalizeStrength(plan.Strength);
        string shadowTone = ColorGradeFilterBuilder.NormalizeShadowTone(plan.ShadowTone);
        string highlightTone = ColorGradeFilterBuilder.NormalizeHighlightTone(plan.HighlightTone);
        node["strength"] = strength;
        node["shadowTone"] = shadowTone;
        node["highlightTone"] = highlightTone;

        if (look == "None")
        {
            // The plan's own first-class "no grade" decision — reported distinctly from every
            // failure reason, since nothing degraded: the colorist chose this.
            node["reason"] = "look_none";
            return (null, node);
        }

        if (!await IsColorGradeFiltersAvailableAsync(ct))
        {
            node["reason"] = "grade_filters_unavailable";
            return (null, node);
        }

        string? chain = ColorGradeFilterBuilder.BuildFilterChain(look, strength, shadowTone, highlightTone);
        if (chain is null)
        {
            // Defensive only: every recognized non-None look currently produces a non-empty
            // chain, but a future table edit must degrade here rather than emit a dangling label.
            node["reason"] = "empty_filter_chain";
            return (null, node);
        }

        node["applied"] = true;
        node["filterChain"] = chain;
        node["reason"] = null;
        return (chain, node);
    }

    private async Task<bool> IsColorGradeFiltersAvailableAsync(CancellationToken ct)
    {
        if (_colorGradeFiltersAvailableCache.HasValue)
            return _colorGradeFiltersAvailableCache.Value;

        await ColorGradeProbeLock.WaitAsync(ct);
        try
        {
            if (_colorGradeFiltersAvailableCache.HasValue)
                return _colorGradeFiltersAvailableCache.Value;

            try
            {
                VideoToolResult result = await _videoToolRunner.RunFfmpegAsync(
                    new[] { "-hide_banner", "-filters" }, TimeSpan.FromSeconds(15), ct);
                // "eq" is matched space-padded — as ffmpeg's -filters table renders every filter
                // name — since a bare Contains("eq") would match unrelated names (e.g. "areq"-
                // style false positives on future builds); the longer names are unambiguous.
                _colorGradeFiltersAvailableCache = result.Succeeded &&
                    result.StdOut.Contains("colorbalance", StringComparison.Ordinal) &&
                    result.StdOut.Contains("colorlevels", StringComparison.Ordinal) &&
                    result.StdOut.Contains("hue", StringComparison.Ordinal) &&
                    result.StdOut.Contains(" eq ", StringComparison.Ordinal);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "VideoCompile: color-grade filter availability probe failed; treating as unavailable.");
                _colorGradeFiltersAvailableCache = false;
            }

            return _colorGradeFiltersAvailableCache.Value;
        }
        finally
        {
            ColorGradeProbeLock.Release();
        }
    }

    // ---------------------------------------------------------------------
    // Background music (see docs/video-editing.md "Background music"): plan resolution — soft-
    // failure only, exactly like Phase 3's graphics resolution above. A missing/bad music plan, an
    // unoffered track id, a non-audio project file, a download/probe failure, or an unavailable
    // amix build all degrade to "no music applied", never to a failed compile — the cut is the
    // primary deliverable.
    // ---------------------------------------------------------------------

    private static JsonObject DroppedMusicNode(string reason, string? trackId) => new()
    {
        ["reason"] = reason,
        ["trackId"] = trackId ?? ""
    };

    private static JsonArray ToJsonArray(List<JsonObject> nodes) =>
        new(nodes.Select(n => (JsonNode)n).ToArray());

    private async Task<(ResolvedMusic? Music, JsonObject MusicNode)> ResolveMusicAsync(
        StepExecutionContext context,
        VideoCompileStepConfig config,
        VideoAnalysisArtifact artifact,
        IReadOnlyList<ResolvedSpan> resolvedSpans,
        OutputTimeline timeline,
        VideoScratchSpace scratch,
        bool hasDialogueAudio,
        CancellationToken ct)
    {
        var music = new JsonObject { ["enabled"] = true, ["applied"] = false, ["unavailable"] = false };
        List<JsonObject> dropped = new();

        // The compiled OUTPUT timeline's own total length — Cut-transitions addition: this used to
        // be the frame-quantized sum of span durations directly (resolvedSpans.Sum(...)), which is
        // still exactly what OutputTimeline.TotalSec computes when every seam is a HardCut (0
        // overlap) — see OutputTimeline.Build. With real crossfade overlaps this is now shorter
        // than that raw sum by the total overlap, which is exactly what a fade-out/duck window
        // placed against "the edit's own length" must use, or it would land inside the crossfade.
        double outputTimelineSeconds = timeline.TotalSec;

        JsonObject Finish()
        {
            music["dropped"] = ToJsonArray(dropped);
            return music;
        }

        // ---- 1. Resolve which track (if any), and from where ----
        string? trackIdFromPlan = null;
        Guid? projectFileId = null;
        string source = "none";
        string? intensityWord = null, duckingWord = null, fitWord = null;

        if (config.MusicPlan is not null)
        {
            (string? planJson, string? planError) = ResolveDecisionJson(context, config.MusicPlan, "MusicPlan");
            if (planJson is null)
            {
                _logger.LogInformation(
                    "VideoCompile step {StepOrder}: MusicPlan unresolved: {Error}", context.Step.StepOrder, planError);
                dropped.Add(DroppedMusicNode("plan_unresolved", null));
            }
            else
            {
                MusicPlanOutput? plan = null;
                try
                {
                    plan = DeserializeWithQuoteFallback<MusicPlanOutput>(planJson, DecisionJsonOptions);
                }
                catch (JsonException ex)
                {
                    _logger.LogInformation(
                        ex, "VideoCompile step {StepOrder}: MusicPlan is not valid JSON.", context.Step.StepOrder);
                    dropped.Add(DroppedMusicNode("plan_invalid_json", null));
                }

                if (plan is not null && !string.IsNullOrWhiteSpace(plan.TrackId))
                {
                    HashSet<string> offeredMusicIds = new(artifact.OfferedMusicIds ?? [], StringComparer.Ordinal);
                    VideoAnalysisMusicCandidate? candidate =
                        artifact.MusicCandidates?.FirstOrDefault(m => m.Id == plan.TrackId);

                    if (!offeredMusicIds.Contains(plan.TrackId) || candidate is null)
                    {
                        dropped.Add(DroppedMusicNode("unknown_track_id", plan.TrackId));
                    }
                    else
                    {
                        trackIdFromPlan = plan.TrackId;
                        projectFileId = candidate.ProjectFileId;
                        source = "plan";
                        intensityWord = plan.Intensity;
                        duckingWord = plan.Ducking;
                        fitWord = plan.Fit;
                    }
                }
            }
        }

        if (projectFileId is null && config.MusicTrackProjectFileId.HasValue)
        {
            projectFileId = config.MusicTrackProjectFileId.Value;
            source = "config";
        }

        if (projectFileId is null)
        {
            music["source"] = "none";
            return (null, Finish());
        }

        // ---- 2. Validate project scope + audio mime (allowlist — a video/text file must never
        // reach the "-i" input added below) ----
        IReadOnlyList<ProjectWorkspaceFile> files =
            await _workspace.ListFilesAsync(context.Execution.ProjectId, ct);
        ProjectWorkspaceFile? file = files.FirstOrDefault(f => f.Id == projectFileId.Value);
        if (file is null)
        {
            dropped.Add(DroppedMusicNode("track_not_in_project", trackIdFromPlan));
            music["source"] = "none";
            return (null, Finish());
        }

        if (!file.MimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            dropped.Add(DroppedMusicNode("track_not_audio", trackIdFromPlan));
            music["source"] = "none";
            return (null, Finish());
        }

        // ---- 3. Download + probe. A corrupt/unreadable track would otherwise fail the ENTIRE
        // ffmpeg invocation as an extra -i input, so it must be caught here, not in the encoder. ----
        string ext = Path.GetExtension(file.StorageKey) switch { "" => ".mp3", var e => e };
        string localPath = scratch.GetPath($"music{ext}");
        MediaProbeResult probe;
        try
        {
            await _workspace.DownloadStorageKeyToFileAsync(context.Execution.ProjectId, file.StorageKey, localPath, ct);
            probe = await _mediaProbe.ProbeAsync(localPath, ct);
            if (probe.DurationSec <= 0 || probe.AudioCodec is null)
                throw new InvalidOperationException("Music track has no audio stream or zero duration.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "VideoCompile step {StepOrder}: music track download/probe failed; dropping music.", context.Step.StepOrder);
            dropped.Add(DroppedMusicNode("track_download_or_probe_failed", trackIdFromPlan));
            music["source"] = "none";
            return (null, Finish());
        }

        // ---- 4. amix normalize-option probe (cached, process-lifetime — mirrors IsDrawtextAvailableAsync) ----
        if (!await IsAmixNormalizeAvailableAsync(ct))
        {
            music["unavailable"] = true;
            music["reason"] = "The ffmpeg build in this container's amix filter does not expose a normalize option.";
            music["source"] = "none";
            return (null, Finish());
        }

        // ---- 5. Resolve enum words -> concrete dB levels/booleans, entirely server-side ----
        string intensity = intensityWord is "Quiet" or "Balanced" or "Feature" ? intensityWord : "Balanced";
        string ducking = duckingWord is "Off" or "Light" or "Normal" or "Heavy" ? duckingWord : "Normal";
        string fit = fitWord is "LoopToFit" or "PlayOnce" ? fitWord : config.MusicFitPolicy.ToString();

        bool configDuckingOff = config.MusicDucking == MusicDuckingMode.Off || ducking == "Off";

        // Bug group C.2: when the final output has no dialogue audio at all (single-source
        // audio-less, or multi-source where NOT ONE referenced clip has audio — see
        // hasDialogueAudio, threaded in from the caller's sourceHasAudio/anySourceHasAudio
        // flags), there is nothing to duck against. Fold that into the same duckingOff switch
        // that already collapses lift-window planning to a flat, undocked bed level, rather than
        // planning ducking windows from dialogue silence/segments that will not exist in the
        // output.
        bool duckingOff = configDuckingOff || !hasDialogueAudio;

        int bedDb = Math.Clamp(intensity switch
        {
            "Quiet" => config.MusicBedQuietDb,
            "Feature" => config.MusicBedFeatureDb,
            _ => config.MusicBedBalancedDb
        }, -40, -6);

        int duckAttenDb = duckingOff
            ? 0
            : Math.Clamp(ducking switch
            {
                "Light" => config.MusicDuckLightDb,
                "Heavy" => config.MusicDuckHeavyDb,
                _ => config.MusicDuckNormalDb
            }, -30, 0);

        double bedGainLinear = Math.Round(Math.Pow(10, bedDb / 20.0), 5);
        double duckGainLinear = Math.Round(Math.Pow(10, (bedDb + duckAttenDb) / 20.0), 5);

        // ---- 6. Lift windows: Off collapses to an empty list (a constant ducked bed — see
        // MusicMixFilterBuilder.BuildVolumeExpression). ----
        MusicLiftPlan liftPlan = duckingOff
            ? new MusicLiftPlan([], "none", 0, 0)
            : MusicMixPlanner.PlanLiftWindows(
                artifact.SilenceSpans, artifact.Segments,
                (start, end, sourceIndex) => timeline.MapWindowToOutput(start, end, sourceIndex),
                outputTimelineSeconds,
                rampSec: Math.Max(0, config.MusicDuckRampMs) / 1000.0,
                minWindowSec: Math.Max(0, config.MinMusicLiftWindowMs) / 1000.0,
                mergeSec: Math.Max(0, config.MusicLiftMergeMs) / 1000.0,
                maxWindows: Math.Max(0, config.MaxMusicLiftWindows));

        // ---- 7. Fit / fade / loop (see docs/video-editing.md "Background music") ----
        bool loopToFit = fit != "PlayOnce";
        double playEndSec;
        bool loopInput;
        if (loopToFit)
        {
            playEndSec = outputTimelineSeconds;
            loopInput = probe.DurationSec < outputTimelineSeconds;
        }
        else
        {
            playEndSec = outputTimelineSeconds > 0 ? Math.Min(probe.DurationSec, outputTimelineSeconds) : probe.DurationSec;
            loopInput = false;
        }

        double fadeInSec = Math.Max(0, config.MusicFadeInMs) / 1000.0;
        double fadeOutSec = Math.Max(0, config.MusicFadeOutMs) / 1000.0;
        if (playEndSec > 0 && fadeInSec + fadeOutSec > playEndSec)
        {
            double collapsed = playEndSec / 3.0;
            fadeInSec = collapsed;
            fadeOutSec = collapsed;
        }

        int loops = loopInput && probe.DurationSec > 0
            ? (int)Math.Ceiling(playEndSec / probe.DurationSec)
            : 1;

        var resolved = new ResolvedMusic(
            TrackId: trackIdFromPlan ?? "",
            ProjectFileId: projectFileId.Value,
            TrackName: file.OriginalFileName,
            LocalPath: localPath,
            TrackDurationSec: probe.DurationSec,
            OutputDurationSec: outputTimelineSeconds,
            PlayEndSec: playEndSec,
            LoopInput: loopInput,
            BedGainLinear: bedGainLinear,
            DuckGainLinear: duckGainLinear,
            RampSec: Math.Max(0, config.MusicDuckRampMs) / 1000.0,
            FadeInSec: fadeInSec,
            FadeOutSec: fadeOutSec,
            LiftWindows: liftPlan.Windows);

        // Bug group C.2: a headroom number describes dialogue that isn't there when the output has
        // no dialogue audio — report {"applicable": false} with the actual reason instead of
        // computing one against Phase 1 loudness data from audio that got dropped.
        JsonObject headroom = hasDialogueAudio
            ? BuildDialogueHeadroom(artifact, resolvedSpans, bedDb, duckAttenDb, duckingOff)
            : new JsonObject { ["applicable"] = false, ["reason"] = "no_dialogue_audio_in_output" };

        music["applied"] = true;
        music["source"] = source;
        music["trackId"] = trackIdFromPlan ?? "";
        music["trackName"] = file.OriginalFileName;
        music["intensity"] = intensity;
        music["ducking"] = duckingOff ? "Off" : ducking;
        music["fit"] = loopToFit ? "LoopToFit" : "PlayOnce";
        music["bedDbfs"] = bedDb;
        music["duckedDbfs"] = bedDb + duckAttenDb;
        music["trackDurationSec"] = Math.Round(probe.DurationSec, 2);
        music["outputDurationSec"] = Math.Round(outputTimelineSeconds, 2);
        music["loops"] = loops;
        music["playEndSec"] = Math.Round(playEndSec, 2);
        music["fadeInSec"] = Math.Round(fadeInSec, 2);
        music["fadeOutSec"] = Math.Round(fadeOutSec, 2);
        music["duckBasis"] = !hasDialogueAudio ? "no_dialogue_audio" : (duckingOff ? "none" : liftPlan.Basis);
        music["liftWindows"] = liftPlan.Windows.Count;
        music["liftCoveragePct"] = liftPlan.LiftCoveragePct;
        music["speechCoveragePct"] = liftPlan.SpeechCoveragePct;
        music["dialogueHeadroom"] = headroom;

        return (resolved, Finish());
    }

    /// <summary>
    /// Deterministic evidence for <c>AgentType.VideoReviewAgent</c> (see docs/video-editing.md
    /// "Background music"/"review loop"): the duration-weighted mean dialogue RMS across the KEPT
    /// spans only (from Phase 1's per-shot <see cref="VideoAnalysisShotAudio.RmsDbfs"/>) against
    /// the resolved ducked-music level, so a reviewer sees a hard, server-computed headroom number
    /// rather than something a model estimates from audio it cannot hear. <c>applicable: false</c>
    /// when no kept shot carries a Phase 1 audio descriptor (AnalyzeAudioLevels was off/degraded).
    /// </summary>
    private static JsonObject BuildDialogueHeadroom(
        VideoAnalysisArtifact artifact, IReadOnlyList<ResolvedSpan> resolvedSpans, int bedDb, int duckAttenDb, bool duckingOff)
    {
        double totalWeight = 0;
        double weightedSum = 0;

        foreach (VideoAnalysisShot shot in artifact.Shots)
        {
            if (shot.Audio is null)
                continue;

            foreach (ResolvedSpan span in resolvedSpans)
            {
                if (span.SourceIndex != shot.SourceIndex)
                    continue;

                double overlap = Math.Min(span.SnappedEnd, shot.EndSec) - Math.Max(span.SnappedStart, shot.StartSec);
                if (overlap <= 0)
                    continue;

                totalWeight += overlap;
                weightedSum += overlap * shot.Audio.RmsDbfs;
            }
        }

        if (totalWeight <= 0)
            return new JsonObject { ["applicable"] = false };

        double meanDialogueRmsDbfs = Math.Round(weightedSum / totalWeight, 1);
        double duckedMusicDbfs = Math.Round((double)(duckingOff ? bedDb : bedDb + duckAttenDb), 1);

        return new JsonObject
        {
            ["applicable"] = true,
            ["meanDialogueRmsDbfs"] = meanDialogueRmsDbfs,
            ["duckedMusicDbfs"] = duckedMusicDbfs,
            ["headroomDb"] = Math.Round(meanDialogueRmsDbfs - duckedMusicDbfs, 1)
        };
    }

    // ---------------------------------------------------------------------
    // Sound effects (see docs/video-editing.md "Sound effects"): plan resolution — soft-failure
    // only, exactly like graphics/music/inserts/grade above. A missing/bad SFX plan degrades to
    // "no SFX applied"; a single bad cue (unoffered clip/anchor id, cut-away anchor, bad file)
    // drops THAT cue and keeps the rest — the cut is always the primary deliverable.
    // ---------------------------------------------------------------------

    private static JsonObject DroppedSfxNode(string reason, string? sfxId, string? anchorId = null) => new()
    {
        ["reason"] = reason,
        ["sfxId"] = sfxId ?? "",
        ["anchorId"] = anchorId ?? ""
    };

    private async Task<(List<ResolvedSfxCue> Cues, JsonObject SfxNode)> ResolveSfxAsync(
        StepExecutionContext context,
        VideoCompileStepConfig config,
        VideoAnalysisArtifact artifact,
        OutputTimeline timeline,
        VideoScratchSpace scratch,
        bool hasBaseAudio,
        CancellationToken ct)
    {
        var sfx = new JsonObject { ["enabled"] = true, ["applied"] = false, ["unavailable"] = false };
        List<JsonObject> dropped = new();
        var cueNodes = new JsonArray();
        List<ResolvedSfxCue> resolved = [];

        (List<ResolvedSfxCue>, JsonObject) Finish()
        {
            sfx["appliedCueCount"] = resolved.Count;
            sfx["applied"] = resolved.Count > 0;
            sfx["cues"] = cueNodes;
            sfx["dropped"] = ToJsonArray(dropped);
            return (resolved, sfx);
        }

        // ---- 1. Resolve + parse the plan. Unlike music, there is no deterministic
        // config-supplied fallback to fall through to — no plan simply means no SFX. ----
        if (config.SfxPlan is null)
        {
            sfx["reason"] = "no_plan_configured";
            return Finish();
        }

        (string? planJson, string? planError) = ResolveDecisionJson(context, config.SfxPlan, "SfxPlan");
        if (planJson is null)
        {
            _logger.LogInformation(
                "VideoCompile step {StepOrder}: SfxPlan unresolved: {Error}", context.Step.StepOrder, planError);
            sfx["reason"] = "plan_unresolved";
            return Finish();
        }

        SfxPlanOutput? plan = null;
        try
        {
            plan = DeserializeWithQuoteFallback<SfxPlanOutput>(planJson, DecisionJsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogInformation(
                ex, "VideoCompile step {StepOrder}: SfxPlan is not valid JSON.", context.Step.StepOrder);
        }

        if (plan is null)
        {
            sfx["reason"] = "plan_invalid_json";
            return Finish();
        }

        // An empty cue list is a fully VALID plan (the sound designer judged no effect is
        // needed) — reported distinctly from every failure reason, mirroring the color grade
        // path's own first-class look_none decision.
        if (plan.Cues.Count == 0)
        {
            sfx["reason"] = "empty_plan";
            return Finish();
        }

        // ---- 2. A cue is layered over the base audio mix, so it needs one to exist. SFX-only
        // audio (synthesizing a track for an otherwise-audio-less output) is deliberately not
        // built — see docs/video-editing.md "Sound effects". ----
        if (!hasBaseAudio)
        {
            sfx["reason"] = "no_base_audio";
            foreach (SfxCue cue in plan.Cues)
                dropped.Add(DroppedSfxNode("no_base_audio", cue.SfxId, cue.AnchorId));
            return Finish();
        }

        // ---- 3. amix normalize-option probe — the same hard requirement (and the same cached
        // probe) music has: without normalize=0, amix would quietly reduce the dialogue level. ----
        if (!await IsAmixNormalizeAvailableAsync(ct))
        {
            sfx["unavailable"] = true;
            sfx["reason"] = "The ffmpeg build in this container's amix filter does not expose a normalize option.";
            return Finish();
        }

        // ---- 4. Per-cue resolution. Every id check is against the OFFERED sets, not merely
        // "exists in the artifact" — the same stricter-than-exists discipline Keep spans get. ----
        HashSet<string> offeredSfxIds = new(artifact.OfferedSfxIds ?? [], StringComparer.Ordinal);
        HashSet<string> offeredAnchorIds = new(artifact.OfferedIds ?? [], StringComparer.Ordinal);
        Dictionary<string, (double Start, double End, int SourceIndex)> idTimes = BuildIdTimeIndex(artifact);

        IReadOnlyList<ProjectWorkspaceFile>? files = null;
        // One local file per DISTINCT clip — several cues may replay the same clip, which must
        // not cost several downloads (each still becomes its own ffmpeg input below, since each
        // cue's branch has its own trim/gain/delay).
        Dictionary<Guid, (string LocalPath, MediaProbeResult Probe)> clipCache = new();

        double maxCueSeconds = Math.Max(0.25, config.MaxSfxCueSeconds);
        double leadSec = Math.Max(0, config.SfxLeadMs) / 1000.0;
        double lagSec = Math.Max(0, config.SfxLagMs) / 1000.0;
        double fadeOutSec = Math.Max(0, config.SfxFadeOutMs) / 1000.0;
        int maxCues = Math.Max(0, config.MaxSfxCues);

        foreach (SfxCue cue in plan.Cues)
        {
            if (resolved.Count >= maxCues)
            {
                dropped.Add(DroppedSfxNode("max_cues_exceeded", cue.SfxId, cue.AnchorId));
                continue;
            }

            VideoAnalysisSfxCandidate? candidate = artifact.SfxCandidates?.FirstOrDefault(c => c.Id == cue.SfxId);
            if (string.IsNullOrWhiteSpace(cue.SfxId) || !offeredSfxIds.Contains(cue.SfxId) || candidate is null)
            {
                dropped.Add(DroppedSfxNode("unknown_sfx_id", cue.SfxId, cue.AnchorId));
                continue;
            }

            if (string.IsNullOrWhiteSpace(cue.AnchorId) || !offeredAnchorIds.Contains(cue.AnchorId) ||
                !idTimes.TryGetValue(cue.AnchorId, out (double Start, double End, int SourceIndex) anchor))
            {
                dropped.Add(DroppedSfxNode("unknown_anchor_id", cue.SfxId, cue.AnchorId));
                continue;
            }

            // The anchor's START moment, mapped through the cut to the output timeline — the
            // same MapToOutputSec machinery graphics overlays and music lift windows already
            // trust. Null means the moment was cut away by the edit decision: the cue is
            // dropped, never relocated to some other moment the model did not choose.
            double? mappedStart = timeline.MapToOutputSec(anchor.Start, anchor.SourceIndex);
            if (mappedStart is null)
            {
                dropped.Add(DroppedSfxNode("anchor_cut_away", cue.SfxId, cue.AnchorId));
                continue;
            }

            // Timing/Volume words normalize exactly like music's Intensity/Ducking words —
            // unknown words fall back to the neutral value rather than dropping the cue. The
            // Lead/Lag shift is applied ON THE OUTPUT TIMELINE (after mapping), so a shifted cue
            // can never land inside a cut region the anchor's own moment survived.
            string timing = cue.Timing is "OnCut" or "Lead" or "Lag" ? cue.Timing : "OnCut";
            string volume = cue.Volume is "Subtle" or "Normal" or "Strong" ? cue.Volume : "Normal";

            double outputStart = timing switch
            {
                "Lead" => mappedStart.Value - leadSec,
                "Lag" => mappedStart.Value + lagSec,
                _ => mappedStart.Value
            };
            outputStart = Math.Clamp(outputStart, 0, Math.Max(0, timeline.TotalSec));

            // ---- Clip file: project-scope + audio-mime allowlist + download + probe, exactly
            // the ResolveMusicAsync discipline (a corrupt clip would otherwise fail the ENTIRE
            // ffmpeg invocation as an extra -i input). ----
            if (!clipCache.TryGetValue(candidate.ProjectFileId, out (string LocalPath, MediaProbeResult Probe) clip))
            {
                files ??= await _workspace.ListFilesAsync(context.Execution.ProjectId, ct);
                ProjectWorkspaceFile? file = files.FirstOrDefault(f => f.Id == candidate.ProjectFileId);
                if (file is null)
                {
                    dropped.Add(DroppedSfxNode("sfx_not_in_project", cue.SfxId, cue.AnchorId));
                    continue;
                }

                if (!file.MimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
                {
                    dropped.Add(DroppedSfxNode("sfx_not_audio", cue.SfxId, cue.AnchorId));
                    continue;
                }

                string ext = Path.GetExtension(file.StorageKey) switch { "" => ".wav", var e => e };
                string localPath = scratch.GetPath($"sfx-{candidate.Id}{ext}");
                try
                {
                    await _workspace.DownloadStorageKeyToFileAsync(context.Execution.ProjectId, file.StorageKey, localPath, ct);
                    MediaProbeResult probe = await _mediaProbe.ProbeAsync(localPath, ct);
                    if (probe.DurationSec <= 0 || probe.AudioCodec is null)
                        throw new InvalidOperationException("SFX clip has no audio stream or zero duration.");
                    clip = (localPath, probe);
                    clipCache[candidate.ProjectFileId] = clip;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex, "VideoCompile step {StepOrder}: SFX clip download/probe failed; dropping cue {SfxId}.",
                        context.Step.StepOrder, cue.SfxId);
                    dropped.Add(DroppedSfxNode("sfx_download_or_probe_failed", cue.SfxId, cue.AnchorId));
                    continue;
                }
            }

            // Play window: the clip's own length, capped by MaxSfxCueSeconds (a long file
            // misused as a cue must never become a de-facto bed) and by the remaining output.
            double playDuration = Math.Min(clip.Probe.DurationSec, maxCueSeconds);
            playDuration = Math.Min(playDuration, Math.Max(0, timeline.TotalSec - outputStart));
            if (playDuration < 0.05)
            {
                dropped.Add(DroppedSfxNode("cue_window_empty", cue.SfxId, cue.AnchorId));
                continue;
            }

            int gainDb = Math.Clamp(volume switch
            {
                "Subtle" => config.SfxSubtleDb,
                "Strong" => config.SfxStrongDb,
                _ => config.SfxNormalDb
            }, -40, 0);
            double gainLinear = Math.Round(Math.Pow(10, gainDb / 20.0), 5);
            double cueFadeOut = Math.Min(fadeOutSec, playDuration / 2.0);

            resolved.Add(new ResolvedSfxCue(
                SfxId: cue.SfxId,
                AnchorId: cue.AnchorId,
                ProjectFileId: candidate.ProjectFileId,
                ClipName: candidate.FileName,
                LocalPath: clip.LocalPath,
                OutputStartSec: outputStart,
                PlayDurationSec: playDuration,
                GainLinear: gainLinear,
                FadeOutSec: cueFadeOut));

            cueNodes.Add(new JsonObject
            {
                ["sfxId"] = cue.SfxId,
                ["anchorId"] = cue.AnchorId,
                ["clipName"] = candidate.FileName,
                ["timing"] = timing,
                ["volume"] = volume,
                ["gainDb"] = gainDb,
                ["outputStartSec"] = Math.Round(outputStart, 3),
                ["playDurationSec"] = Math.Round(playDuration, 3)
            });
        }

        return Finish();
    }

    /// <summary>
    /// Probes whether the ffmpeg build's <c>amix</c> filter exposes a <c>normalize</c> option,
    /// caching the result for the process lifetime — mirrors <see cref="IsDrawtextAvailableAsync"/>
    /// exactly. Without <c>normalize=0</c>, <c>amix</c> silently halves every input's level
    /// (including the dialogue track), so a missing option must degrade ALL music rather than
    /// risk quietly reducing dialogue loudness.
    /// </summary>
    private async Task<bool> IsAmixNormalizeAvailableAsync(CancellationToken ct)
    {
        if (_amixNormalizeAvailableCache.HasValue)
            return _amixNormalizeAvailableCache.Value;

        await AmixProbeLock.WaitAsync(ct);
        try
        {
            if (_amixNormalizeAvailableCache.HasValue)
                return _amixNormalizeAvailableCache.Value;

            try
            {
                VideoToolResult result = await _videoToolRunner.RunFfmpegAsync(
                    new[] { "-hide_banner", "-h", "filter=amix" }, TimeSpan.FromSeconds(15), ct);
                _amixNormalizeAvailableCache = result.Succeeded &&
                    result.StdOut.Contains("normalize", StringComparison.Ordinal);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "VideoCompile: amix normalize-option availability probe failed; treating as unavailable.");
                _amixNormalizeAvailableCache = false;
            }

            return _amixNormalizeAvailableCache.Value;
        }
        finally
        {
            AmixProbeLock.Release();
        }
    }

    /// <summary>
    /// Probes whether the ffmpeg build on <see cref="VideoEditingOptions.FfmpegPath"/> exposes
    /// BOTH the <c>xfade</c> and <c>acrossfade</c> filters — every overlapping seam treatment
    /// (<see cref="SeamTreatment.SoftCut"/>/<see cref="SeamTreatment.Dissolve"/>/
    /// <see cref="SeamTreatment.DipToBlack"/>/<see cref="SeamTreatment.WhipBlur"/>) needs both.
    /// Caches the result for the process lifetime, mirrors <see cref="IsDrawtextAvailableAsync"/>
    /// exactly. Never throws — any failure to probe is treated as "unavailable", which
    /// <see cref="SeamTransitionPlanner.Plan"/> handles by demoting every such seam to
    /// <see cref="SeamTreatment.DipCut"/> instead.
    /// </summary>
    private async Task<bool> IsXfadeAvailableAsync(CancellationToken ct)
    {
        if (_xfadeAvailableCache.HasValue)
            return _xfadeAvailableCache.Value;

        await XfadeProbeLock.WaitAsync(ct);
        try
        {
            if (_xfadeAvailableCache.HasValue)
                return _xfadeAvailableCache.Value;

            try
            {
                VideoToolResult result = await _videoToolRunner.RunFfmpegAsync(
                    new[] { "-hide_banner", "-filters" }, TimeSpan.FromSeconds(15), ct);
                _xfadeAvailableCache = result.Succeeded &&
                    result.StdOut.Contains("xfade", StringComparison.Ordinal) &&
                    result.StdOut.Contains("acrossfade", StringComparison.Ordinal);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "VideoCompile: xfade/acrossfade availability probe failed; treating as unavailable.");
                _xfadeAvailableCache = false;
            }

            return _xfadeAvailableCache.Value;
        }
        finally
        {
            XfadeProbeLock.Release();
        }
    }

    /// <summary>
    /// Probes whether the ffmpeg build on <see cref="VideoEditingOptions.FfmpegPath"/> exposes the
    /// drawtext filter (needs libfreetype/a font package — see the WorkflowEngine Dockerfile),
    /// caching the result for the process lifetime so this never runs more than once. Never throws
    /// — any failure to probe is treated as "unavailable", the same graceful-degradation outcome
    /// as drawtext genuinely being absent.
    /// </summary>
    private async Task<bool> IsDrawtextAvailableAsync(CancellationToken ct)
    {
        if (_drawtextAvailableCache.HasValue)
            return _drawtextAvailableCache.Value;

        await DrawtextProbeLock.WaitAsync(ct);
        try
        {
            if (_drawtextAvailableCache.HasValue)
                return _drawtextAvailableCache.Value;

            try
            {
                VideoToolResult result = await _videoToolRunner.RunFfmpegAsync(
                    new[] { "-hide_banner", "-filters" }, TimeSpan.FromSeconds(15), ct);
                _drawtextAvailableCache = result.Succeeded &&
                    result.StdOut.Contains("drawtext", StringComparison.Ordinal);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "VideoCompile: drawtext availability probe failed; treating as unavailable.");
                _drawtextAvailableCache = false;
            }

            return _drawtextAvailableCache.Value;
        }
        finally
        {
            DrawtextProbeLock.Release();
        }
    }

    // ---------------------------------------------------------------------
    // Encoding
    // ---------------------------------------------------------------------

    private async Task<VideoToolResult> EncodeReencodeAsync(
        VideoScratchSpace scratch,
        string localVideoPath,
        string outputPath,
        IReadOnlyList<ResolvedSpan> spans,
        string videoCodec,
        string audioCodec,
        string preset,
        int crf,
        TimeSpan timeout,
        CancellationToken ct,
        IReadOnlyList<ResolvedOverlay>? overlays = null,
        int probedWidth = 0,
        int probedHeight = 0,
        VideoCompileStepConfig? graphicsConfig = null,
        StepExecutionContext? progressContext = null,
        double totalOutputSeconds = 0,
        ResolvedMusic? music = null,
        bool sourceHasAudio = true,
        // Cut transitions (see docs/video-editing.md "Cut transitions"): this path only ever
        // carries NON-overlapping seam treatments (HardCut/AudioOnly/DipCut) — the executor routes
        // any seam with OverlapSec > 0 to EncodeReencodeSegmentedAsync instead, since select/
        // aselect cannot express a crossfade. seamPlans/timeline are null/empty on the byte-
        // identical pre-transitions call path (TransitionPolicy=Off), in which case none of the
        // tail stages below are added at all.
        IReadOnlyList<SeamPlan>? seamPlans = null,
        OutputTimeline? timeline = null,
        VideoCompileStepConfig? transitionConfig = null,
        ProgramFadeResolved? programFade = null,
        // Tracked screen inserts (see docs/video-editing.md "Tracked screen inserts (Phase 5)"):
        // null/empty on every pre-inserts call path — the label plumbing below is then untouched.
        IReadOnlyList<ResolvedScreenInsert>? inserts = null,
        // Color grading (see docs/video-editing.md "Color grading"): a fully-built, first-party
        // filter chain from ColorGradeFilterBuilder, or null on every pre-grade call path — the
        // cut stage below is then byte-identical to before this addition.
        string? colorGradeFilter = null,
        // Sound effects (see docs/video-editing.md "Sound effects"): null/empty on every pre-SFX
        // call path — the audio label plumbing below is then untouched.
        IReadOnlyList<ResolvedSfxCue>? sfx = null)
    {
        // Half-open [SnappedStart, SnappedEnd) per span, matching ToStartFrame(floor)/ToEndFrame
        // (ceiling)'s own semantics (EndFrame is the first EXCLUDED frame — see MapSourceToOutputSec's
        // doc comment and the span-duration accumulation in MapSourceToOutputSec/
        // MapSourceWindowToOutput, both of which already assume EndFrame-StartFrame frames per
        // span, not EndFrame-StartFrame+1). ffmpeg's between(x,min,max) is INCLUSIVE on both ends
        // (x >= min && x <= max), so using it here would additionally select the frame whose PTS
        // is exactly SnappedEnd — frame index EndFrame, one frame past what was actually kept —
        // for every span, accumulating drift across the whole cut. gte(t,start)*lt(t,end) (product
        // as logical AND) makes the actual encoder output match the frame-quantization math.
        string BetweenTerms() => string.Join("+", spans.Select(s =>
            $"gte(t,{FfmpegArgvFormat.Number(s.SnappedStart)})*lt(t,{FfmpegArgvFormat.Number(s.SnappedEnd)})"));

        string videoFilter = $"select='{BetweenTerms()}',setpts=N/FRAME_RATE/TB";
        string audioFilter = $"aselect='{BetweenTerms()}',asetpts=N/SR/TB";

        // Color grading: appended to the cut stage itself, BEFORE any insert/overlay stage below,
        // so motion graphics always paint clean on top of graded footage (the stage-ordering rule
        // in docs/video-editing.md "Color grading"). Null (the default, and every pre-grade call
        // path) leaves the cut stage byte-identical to before this addition.
        if (colorGradeFilter is not null)
            videoFilter += "," + colorGradeFilter;

        // Phase 3: partition into the two overlay flavors up front. Text overlays go through the
        // existing DrawtextFilterBuilder path unchanged; asset overlays (a rendered,
        // transparent-background Remotion clip) go through the new OverlayAssetFilterBuilder path.
        // A single compile can contain both flavors at once — a mix is not treated specially,
        // each flavor just runs its own filter-chain stage, chained one after the other.
        List<ResolvedOverlay> textOverlays = overlays?.Where(o => !o.IsAssetOverlay).ToList() ?? [];
        List<ResolvedOverlay> assetOverlays = overlays?.Where(o => o.IsAssetOverlay).ToList() ?? [];

        // Tracked screen inserts: each insert's asset is its own extra ffmpeg input, added AFTER
        // every asset-overlay input (so OverlayAssetFilterBuilder's `i => i + 1` mapping stays
        // untouched) and BEFORE the music input (so music stays last).
        List<ResolvedScreenInsert> screenInserts = inserts?.ToList() ?? [];
        bool hasInserts = screenInserts.Count > 0;

        // Background music (see docs/video-editing.md "Background music"): the music input is
        // deliberately the LAST ffmpeg input, after every asset-overlay input (and now after
        // every screen-insert input too — zero of those on any pre-inserts call path, keeping
        // this expression byte-identical then) — this is what keeps OverlayAssetFilterBuilder's
        // existing `inputIndexForIndex: i => i + 1` mapping (and every existing filter-string
        // test asserting it) completely untouched by this addition.
        int musicInputIndex = 1 + assetOverlays.Count + screenInserts.Count;

        // Sound effects (see docs/video-editing.md "Sound effects"): each cue's clip is its own
        // extra ffmpeg input, added AFTER the music input (so musicInputIndex — and every
        // existing filter-string test asserting it — stays completely untouched by this
        // addition). A cue needs a base audio chain to be layered over; ResolveSfxAsync already
        // guarantees zero cues when neither dialogue nor music exists, so the guard here is
        // defensive symmetry, not a second decision point.
        List<ResolvedSfxCue> sfxCues = sfx?.ToList() ?? [];
        bool hasSfx = sfxCues.Count > 0 && (sourceHasAudio || music is not null);
        int sfxInputBase = musicInputIndex + (music is not null ? 1 : 0);

        // ---- Cut transitions (see docs/video-editing.md "Cut transitions"): this select-path
        // encoder only ever sees non-overlapping seam treatments (HardCut/AudioOnly/DipCut — any
        // overlapping seam routes the whole compile to EncodeReencodeSegmentedAsync instead), so
        // only two stages can ever apply here: a DipCut video fade-pair / a global AudioOnly ramp,
        // and the whole-piece program fade — both LAST, after overlays/the music mix (see the
        // stage-ordering rule in docs/video-editing.md). When seamPlans/timeline/programFade are
        // all null/empty (TransitionPolicy=Off and no program fade configured — the default), every
        // computed suffix below is null/empty and the filtergraph is byte-identical to before this
        // addition. ----
        string? dipCutVideoSuffix = seamPlans is not null && timeline is not null
            ? TransitionFilterBuilder.BuildDipCutVideoFilterSuffix(seamPlans, timeline)
            : null;
        string? audioRampExpr = seamPlans is not null && timeline is not null && transitionConfig is not null
            ? TransitionFilterBuilder.BuildAudioSeamRampExpression(
                seamPlans, timeline, Math.Max(0, transitionConfig.AudioSeamRampMs) / 1000.0)
            : null;
        double programFadeTotalSec = timeline?.TotalSec ?? totalOutputSeconds;
        string videoProgramSuffix = programFade is not null
            ? ProgramEnvelopeFilterBuilder.BuildVideoFadeSuffix(programFade, programFadeTotalSec)
            : "";
        string audioProgramSuffix = programFade is not null
            ? ProgramEnvelopeFilterBuilder.BuildAudioFadeSuffix(programFade, programFadeTotalSec)
            : "";

        bool hasVideoTailStage = dipCutVideoSuffix is not null || videoProgramSuffix.Length > 0;
        bool audioOutputPossible = sourceHasAudio || music is not null;
        bool hasAudioTailStage = audioOutputPossible && (audioRampExpr is not null || audioProgramSuffix.Length > 0);

        string videoFinalLabel = hasVideoTailStage ? "[vxfd]" : "[vout]";
        string audioFinalLabelInner = hasAudioTailStage ? "[axfd]" : "[aout]";

        // The audio cut stage's own output label flips from [aout]/[axfd] straight to [adial] only
        // when music is present, mirroring the [vcut]/[vtxt]/[vout] video-label-chaining convention
        // Phase 3 already established. When music is null this whole audio branch is
        // byte-identical to the pre-music compile path.
        //
        // sourceHasAudio=false (the source clip has no audio stream at all — real, not
        // hypothetical: free stock B-roll routinely ships video-only) means "[0:a]" would fail
        // ffmpeg outright ("Stream specifier ':a' ... matches no streams"), so that whole branch
        // is skipped. With no music either, audioPart is null and the output has no audio track
        // at all (map/-c:a below become conditional on this). With music, there is nothing to
        // duck against, so the music branch's own output becomes the audio final label directly —
        // it is the entire output audio, not mixed with anything.
        // Sound effects: when cues are present, the pre-SFX audio chain (the plain dialogue cut,
        // the dialogue+music mix, or the music-only branch) ends at an internal [abase] label
        // instead of the audio final label directly, and the SFX mix stage appended below becomes
        // the new audio final label — the exact [aout]->[adial] label-chaining convention music
        // itself established. When hasSfx is false, audioBaseLabel IS audioFinalLabelInner and
        // every string below is byte-identical to the pre-SFX path.
        string audioBaseLabel = hasSfx ? "[abase]" : audioFinalLabelInner;
        string audioCutLabel = music is not null ? "[adial]" : audioBaseLabel;
        // aformat on the dialogue cut is needed by BOTH mixes (music's and SFX's) — amix
        // requires matching sample rate/channel layout across its inputs.
        string musicAwareAudioFilter = music is not null || hasSfx
            ? $"{audioFilter},aformat=sample_rates=48000:channel_layouts=stereo"
            : audioFilter;
        string? audioPart = !sourceHasAudio
            ? (music is null ? null : MusicMixFilterBuilder.BuildMusicBranch(musicInputIndex, music, outLabel: audioBaseLabel))
            : (music is null
                ? $"[0:a]{musicAwareAudioFilter}{audioBaseLabel}"
                : $"[0:a]{musicAwareAudioFilter}{audioCutLabel};" +
                  MusicMixFilterBuilder.BuildMusicBranch(musicInputIndex, music) + ";" +
                  MusicMixFilterBuilder.BuildMixStage(audioCutLabel, finalLabel: audioBaseLabel));

        if (hasSfx && audioPart is not null)
        {
            var sfxParts = new List<string> { audioPart };
            for (int k = 0; k < sfxCues.Count; k++)
                sfxParts.Add(SfxMixFilterBuilder.BuildCueBranch(sfxInputBase + k, k, sfxCues[k]));
            sfxParts.Add(SfxMixFilterBuilder.BuildMixStage(audioBaseLabel, sfxCues.Count, finalLabel: audioFinalLabelInner));
            audioPart = string.Join(";", sfxParts);
        }

        bool hasAudioOutput = audioPart is not null;

        // Tail stage(s) — appended after the existing cut/overlay/music filterComplex below.
        List<string> videoTailFilters = new();
        if (dipCutVideoSuffix is not null)
            videoTailFilters.Add(dipCutVideoSuffix);
        if (videoProgramSuffix.Length > 0)
            videoTailFilters.Add(videoProgramSuffix.TrimStart(','));
        string? videoTailStage = hasVideoTailStage ? $"[vxfd]{string.Join(",", videoTailFilters)}[vout]" : null;

        List<string> audioTailFilters = new();
        if (audioRampExpr is not null)
            audioTailFilters.Add($"volume=eval=frame:volume='{audioRampExpr}'");
        if (audioProgramSuffix.Length > 0)
            audioTailFilters.Add(audioProgramSuffix.TrimStart(','));
        string? audioTailStage = hasAudioTailStage ? $"[axfd]{string.Join(",", audioTailFilters)}[aout]" : null;

        string filterComplex;
        bool hasAnyOverlay = textOverlays.Count > 0 || assetOverlays.Count > 0;
        if ((hasAnyOverlay && graphicsConfig is not null) || hasInserts)
        {
            // Phase 3: the cut stage now outputs to an internal label ([vcut]) instead of
            // [vout] directly — the LAST overlay stage becomes the new [vout] that -map
            // continues to reference. Text never appears in this string: each text overlay's
            // sanitized text was already written to its own scratch file before this call, and
            // DrawtextFilterBuilder only ever interpolates the FILE PATH here, never the text.
            for (int i = 0; i < textOverlays.Count; i++)
            {
                ResolvedOverlay overlay = textOverlays[i];
                await WriteOverlayTextFileAsync(scratch, DrawtextFilterBuilder.MainTextSlot(i), overlay.SanitizedText, ct);
                if (overlay.SanitizedSubtext.Length > 0)
                    await WriteOverlayTextFileAsync(scratch, DrawtextFilterBuilder.SubtextSlot(i), overlay.SanitizedSubtext, ct);
            }

            List<string> chainParts = new();
            string currentLabel = "[vcut]";

            // Tracked screen inserts render FIRST — an insert is scene content (inside a phone's
            // screen), so lower-thirds/titles drawn by the overlay stages below must paint on top
            // of it, not under it.
            if (hasInserts)
            {
                string insertStageFinalLabel = hasAnyOverlay && graphicsConfig is not null ? "[vins]" : videoFinalLabel;
                chainParts.Add(ScreenInsertFilterBuilder.BuildFilterChain(
                    currentLabel, screenInserts, probedWidth, probedHeight,
                    inputIndexForIndex: i => 1 + assetOverlays.Count + i,
                    finalLabel: insertStageFinalLabel));
                currentLabel = insertStageFinalLabel;
            }

            if (textOverlays.Count > 0)
            {
                // When asset overlays also follow, this stage ends at an internal [vtxt] label
                // instead of the video final label directly, so the asset stage below can chain
                // after it and become that final label itself.
                string textStageFinalLabel = assetOverlays.Count > 0 ? "[vtxt]" : videoFinalLabel;
                chainParts.Add(DrawtextFilterBuilder.BuildFilterChain(
                    currentLabel, textOverlays, probedWidth, probedHeight,
                    graphicsConfig.OverlayFontSizePct, graphicsConfig.OverlayFadeMs,
                    graphicsConfig.OverlayFontColor, graphicsConfig.OverlayBoxColor, _options.FontFilePath,
                    slot => scratch.GetPath($"ov-{slot}.txt"),
                    finalLabel: textStageFinalLabel,
                    boxHeightPct: graphicsConfig.OverlayBoxHeightPct,
                    boxWidthPct: graphicsConfig.OverlayBoxWidthPct));
                currentLabel = textStageFinalLabel;
            }

            if (assetOverlays.Count > 0)
            {
                // Each asset overlay is added as its own extra ffmpeg -i input below, in this
                // same assetOverlays order — input 0 is always the main source video, so asset
                // overlay index i is ffmpeg input index i + 1.
                chainParts.Add(OverlayAssetFilterBuilder.BuildFilterChain(
                    currentLabel, assetOverlays, probedWidth, probedHeight,
                    inputIndexForIndex: i => i + 1,
                    finalLabel: videoFinalLabel,
                    boxHeightPct: graphicsConfig.OverlayBoxHeightPct,
                    boxWidthPct: graphicsConfig.OverlayBoxWidthPct));
            }

            filterComplex = audioPart is not null
                ? $"[0:v]{videoFilter}[vcut];{audioPart};{string.Join(";", chainParts)}"
                : $"[0:v]{videoFilter}[vcut];{string.Join(";", chainParts)}";
        }
        else
        {
            filterComplex = audioPart is not null
                ? $"[0:v]{videoFilter}{videoFinalLabel};{audioPart}"
                : $"[0:v]{videoFilter}{videoFinalLabel}";
        }

        // Cut transitions: append the tail stage(s) — computed above — after the existing
        // cut/overlay/music filtergraph. Both are null on the byte-identical pre-transitions path.
        if (videoTailStage is not null)
            filterComplex += ";" + videoTailStage;
        if (audioTailStage is not null)
            filterComplex += ";" + audioTailStage;

        // Real encode progress (best-effort — see BuildFfmpegProgressLineHandler): ffmpeg's
        // "-progress pipe:1" emits periodic out_time_us/out_time_ms key=value lines to stdout,
        // which the runner already reads line-by-line — so a real percentage against the known
        // output duration costs only this one extra flag plus a stdout-line parser, no new process
        // or polling. Only wired when we actually know the target duration; otherwise the "Encoding"
        // stage-start event from the caller is all the caller ever gets, exactly as if compile had
        // no percentage support at all.
        Action<string>? progressLineHandler = progressContext is not null && totalOutputSeconds > 0
            ? BuildFfmpegProgressLineHandler(progressContext, totalOutputSeconds)
            : null;

        List<string> args = new() { "-nostdin", "-hide_banner", "-y", "-loglevel", "error" };
        if (progressLineHandler is not null)
        {
            args.Add("-progress");
            args.Add("pipe:1");
        }
        args.Add("-protocol_whitelist"); args.Add("file"); args.Add("-i"); args.Add(localVideoPath);

        // One extra -i per asset overlay, in the same order OverlayAssetFilterBuilder assumed
        // above (input index i + 1) — every one of these paths was already downloaded to local
        // scratch space AND ffprobe-validated by ResolveGraphicsAsync before this function ever
        // saw it, so there is no untrusted/unvalidated file reaching ffmpeg's argv here.
        foreach (ResolvedOverlay assetOverlay in assetOverlays)
        {
            args.Add("-i");
            args.Add(assetOverlay.RenderedAssetLocalPath!);
        }

        // Tracked screen inserts: one extra -i per insert, in the same order the
        // ScreenInsertFilterBuilder chain above assumed (input index 1 + assetOverlays.Count + i)
        // — every one of these paths was downloaded to local scratch AND ffprobe-validated by
        // ResolveInsertsAsync before this function ever saw it.
        foreach (ResolvedScreenInsert screenInsert in screenInserts)
        {
            args.Add("-i");
            args.Add(screenInsert.LocalAssetPath);
        }

        // Background music: the LAST input, after every asset-overlay input (see musicInputIndex
        // above). -stream_loop -1 only when the track is shorter than the edit under LoopToFit —
        // the amix stage's duration=first pins the mixed output to the dialogue length regardless,
        // so an infinite looped input can never run away.
        if (music is not null)
        {
            if (music.LoopInput)
            {
                args.Add("-stream_loop");
                args.Add("-1");
            }

            args.Add("-i");
            args.Add(music.LocalPath);
        }

        // Sound effects: one extra -i per cue, AFTER the music input, in the same order the
        // BuildCueBranch calls above assumed (input index sfxInputBase + k). Several cues
        // replaying the same clip repeat the same local path — each cue's branch still needs
        // its own input stream to trim/gain/delay independently. Every path was downloaded to
        // local scratch AND ffprobe-validated by ResolveSfxAsync before this method ever saw it.
        if (hasSfx)
        {
            foreach (ResolvedSfxCue cue in sfxCues)
            {
                args.Add("-i");
                args.Add(cue.LocalPath);
            }
        }

        // R20, extended for Phase 3: overlays can make one long filter string even with very few
        // spans (many drawbox/drawtext/overlay filters chained), so the script-file threshold now
        // also accounts for filter STRING LENGTH, not just segment count — but ONLY when overlays
        // are actually present. Item F cleanup: the length clause used to apply unconditionally,
        // so a plain cut-only compile with ~45+ segments (no overlays at all) already produced a
        // >4000-char filter string from the between(t,...) terms alone and silently switched to
        // -filter_complex_script — functionally equivalent, but not the byte-identical
        // pre-Phase-3 behavior this feature's backward-compatibility claim promises. Gating on
        // overlays being present keeps a no-overlay compile on exactly its original count-only
        // threshold, while an overlay-heavy (or music-enabled) compile still gets the length-based
        // safety net.
        bool hasOverlays = textOverlays.Count > 0 || assetOverlays.Count > 0;
        bool hasMusic = music is not null;
        if (spans.Count > FilterComplexScriptThreshold || ((hasOverlays || hasMusic || hasInserts || hasSfx) && filterComplex.Length > 4000))
        {
            string scriptPath = scratch.GetPath("filter_complex.txt");
            await File.WriteAllTextAsync(scriptPath, filterComplex, ct);
            args.Add("-filter_complex_script");
            args.Add(scriptPath);
        }
        else
        {
            args.Add("-filter_complex");
            args.Add(filterComplex);
        }

        args.Add("-map"); args.Add("[vout]");
        if (hasAudioOutput)
        {
            args.Add("-map"); args.Add("[aout]");
        }
        args.Add("-c:v"); args.Add(videoCodec);
        args.Add("-crf"); args.Add(FfmpegArgvFormat.Number(crf));
        args.Add("-preset"); args.Add(preset);
        if (hasAudioOutput)
        {
            args.Add("-c:a"); args.Add(audioCodec);
        }
        args.Add(outputPath);

        return await _videoToolRunner.RunFfmpegAsync(args, timeout, ct, progressLineHandler);
    }

    /// <summary>
    /// Multi-source addition. Encodes a cut list whose spans reference MORE THAN ONE distinct
    /// downloaded source file — the ORIGINAL <see cref="EncodeReencodeAsync"/> above stays
    /// completely untouched and is still used for every single-source compile (see
    /// docs/video-editing.md "Multiple source clips"). Deliberately a separate method rather than
    /// a generalization of <see cref="EncodeReencodeAsync"/>: ffmpeg's <c>select</c> filter always
    /// emits one input's own matched ranges in THAT INPUT'S OWN chronological order, so it cannot
    /// express a Keep-span order that jumps between clips arbitrarily — only the <c>concat</c>
    /// filter, fed one small pre-trimmed clip PER SPAN in the exact order they should play, can.
    /// Each span becomes its own <c>trim</c>/<c>atrim</c> branch off the CORRECT ffmpeg input index
    /// for that span's own source clip, normalized to one canonical frame size/rate/audio format
    /// (<paramref name="canonicalMedia"/> — the first kept span's own clip, see
    /// <c>ExecuteAsync</c>) since <c>concat</c> requires every concatenated stream to share
    /// identical parameters, then concatenated in Keep order. Per-segment audio: a span whose own
    /// source clip lacks an audio stream gets a synthesized matching-duration silence branch
    /// (<c>anullsrc</c>) instead of dropping audio for the whole output — see
    /// <paramref name="sourceHasAudioByIndex"/>.
    /// </summary>
    /// <summary>
    /// Multi-source addition, extended for Cut transitions (see docs/video-editing.md "Cut
    /// transitions"): renamed from <c>EncodeReencodeMultiSourceAsync</c> — this is now also the
    /// encode path for a SINGLE source with a real overlapping transition (a crossfade cannot be
    /// expressed by the select-path's own <c>select</c>/<c>aselect</c> filters), not only for
    /// multiple distinct source clips. <paramref name="seamPlans"/>/<paramref name="timeline"/>
    /// are null on any call site that predates this addition; every seam-aware stage below
    /// (per-span DipCut/AudioOnly local fades, block/xfade grouping) degrades to plain,
    /// all-hard-cut concatenation in that case — byte-identical to the pre-transitions behavior.
    /// </summary>
    private async Task<VideoToolResult> EncodeReencodeSegmentedAsync(
        VideoScratchSpace scratch,
        IReadOnlyDictionary<int, string> localPathBySource,
        string outputPath,
        IReadOnlyList<ResolvedSpan> spans,
        string videoCodec,
        string audioCodec,
        string preset,
        int crf,
        VideoAnalysisMedia canonicalMedia,
        TimeSpan timeout,
        CancellationToken ct,
        IReadOnlyList<ResolvedOverlay>? overlays = null,
        VideoCompileStepConfig? graphicsConfig = null,
        StepExecutionContext? progressContext = null,
        double totalOutputSeconds = 0,
        ResolvedMusic? music = null,
        IReadOnlyDictionary<int, bool>? sourceHasAudioByIndex = null,
        IReadOnlyList<SeamPlan>? seamPlans = null,
        OutputTimeline? timeline = null,
        VideoCompileStepConfig? transitionConfig = null,
        ProgramFadeResolved? programFade = null,
        // Color grading (see docs/video-editing.md "Color grading"): a fully-built, first-party
        // filter chain from ColorGradeFilterBuilder, applied as its own stage directly after the
        // concat/transition stage (before overlays), or null on every pre-grade call path — the
        // label plumbing below is then untouched.
        string? colorGradeFilter = null,
        // Sound effects (see docs/video-editing.md "Sound effects"): null/empty on every pre-SFX
        // call path — the audio label plumbing below is then untouched. Cue timing is pure
        // output-timeline adelay, so cues work identically on this segmented path (multi-source
        // and crossfade-overlap compiles included) — unlike screen inserts, there is no per-span
        // timeline bookkeeping for a cue to disagree with.
        IReadOnlyList<ResolvedSfxCue>? sfx = null)
    {
        // Deterministic ffmpeg -i order: sorted distinct source indices actually referenced. Input
        // 0 is not necessarily "the" primary source here (that's canonicalMedia's own index,
        // separately tracked) — it is simply whichever referenced source sorts first.
        List<int> orderedSourceIndices = localPathBySource.Keys.OrderBy(i => i).ToList();
        Dictionary<int, int> ffmpegInputIndexBySource = orderedSourceIndices
            .Select((sourceIdx, inputIdx) => (sourceIdx, inputIdx))
            .ToDictionary(t => t.sourceIdx, t => t.inputIdx);

        // A missing entry (only possible when the caller omits the map entirely) is treated as
        // "has audio" — the pre-existing, safer default this parameter itself used to be.
        bool SourceHasAudio(int sourceIndex) =>
            sourceHasAudioByIndex is null || !sourceHasAudioByIndex.TryGetValue(sourceIndex, out bool has) || has;

        // Degenerate case only: NOT ONE referenced source has an audio stream at all, so there is
        // no real dialogue anywhere to preserve — dropping audio for the whole output (exactly the
        // pre-existing behavior) is simpler and just as correct as concatenating an all-silent
        // track would be. Any OTHER mix (at least one source with audio, at least one without)
        // instead synthesizes silence per audio-less span below, keeping every audio-having span's
        // real dialogue intact.
        bool anySourceHasAudio = orderedSourceIndices.Any(SourceHasAudio);

        // Cut transitions: the program fade (see docs/video-editing.md "Cut transitions") is
        // always the LAST stage — after overlays, after the music mix — so both suffixes are
        // computed once up front and applied as a final filter-chain entry below. Per-seam DipCut/
        // AudioOnly treatments are handled differently on THIS path — folded directly into each
        // span's own trim/atrim branch (see the loop below) rather than as a separate tail stage,
        // since there is no single global timeline filter that can apply to per-span-independent
        // decoded clips the way the select-path's global volume-envelope trick can.
        double programFadeTotalSec = timeline?.TotalSec ?? totalOutputSeconds;
        string videoProgramSuffix = programFade is not null
            ? ProgramEnvelopeFilterBuilder.BuildVideoFadeSuffix(programFade, programFadeTotalSec)
            : "";
        string audioProgramSuffix = programFade is not null
            ? ProgramEnvelopeFilterBuilder.BuildAudioFadeSuffix(programFade, programFadeTotalSec)
            : "";
        bool hasVideoProgramFade = videoProgramSuffix.Length > 0;
        bool hasAudioProgramFade = audioProgramSuffix.Length > 0 && (anySourceHasAudio || music is not null);
        string videoFinalLabel = hasVideoProgramFade ? "[vpre]" : "[vout]";
        string audioFinalLabelInner = hasAudioProgramFade ? "[apre]" : "[aout]";

        // libx264/libx265 require even dimensions; clamp the canonical target defensively even
        // though a real ffprobe'd width/height is virtually always already even.
        int cw = canonicalMedia.Width > 0 ? canonicalMedia.Width : 1920;
        int ch = canonicalMedia.Height > 0 ? canonicalMedia.Height : 1080;
        cw -= cw % 2;
        ch -= ch % 2;
        int cfpsNum = canonicalMedia.FpsNum > 0 ? canonicalMedia.FpsNum : 30;
        int cfpsDen = canonicalMedia.FpsDen > 0 ? canonicalMedia.FpsDen : 1;

        var filterParts = new List<string>();
        for (int i = 0; i < spans.Count; i++)
        {
            ResolvedSpan span = spans[i];
            int ffInputIdx = ffmpegInputIndexBySource[span.SourceIndex];
            string ss = FfmpegArgvFormat.Number(span.SnappedStart);
            string ee = FfmpegArgvFormat.Number(span.SnappedEnd);
            double spanDurationSec = Math.Max(0.0, span.SnappedEnd - span.SnappedStart);

            // Cut transitions: DipCut/AudioOnly are non-overlapping treatments applied per-span,
            // inside this span's own trim/atrim branch — see docs/video-editing.md "Cut
            // transitions". seamPlans is null on any pre-transitions call site, in which case both
            // suffixes are always "".
            SeamPlan? seamBefore = seamPlans is not null && i > 0 ? seamPlans[i - 1] : null;
            SeamPlan? seamAfter = seamPlans is not null && i < spans.Count - 1 ? seamPlans[i] : null;
            string videoFadeSuffix = TransitionFilterBuilder.BuildSpanVideoFadeSuffix(spanDurationSec, seamBefore, seamAfter);
            string audioFadeSuffix = TransitionFilterBuilder.BuildSpanAudioFadeSuffix(spanDurationSec, seamBefore, seamAfter);

            // Cut transitions: the scale/pad/setsar/fps normalization below exists only to make
            // DIFFERENT source clips concat-compatible — a provable no-op when every span already
            // shares the one and only source clip's own dimensions/rate (a single-source compile
            // that landed on this segmented path purely because of a real crossfade, not because
            // of multiple physical source clips). Skipping it there keeps that case's encode
            // simpler/cheaper without changing the output at all.
            string normalizeSuffix = orderedSourceIndices.Count == 1
                ? ""
                : $",scale={cw}:{ch}:force_original_aspect_ratio=decrease,pad={cw}:{ch}:(ow-iw)/2:(oh-ih)/2:color=black,setsar=1,fps={cfpsNum}/{cfpsDen}";

            // Time-based (not frame-based) trim, deliberately mirroring the single-source path's
            // own time-based `gte(t,...)*lt(t,...)` select terms exactly — SnappedStart/SnappedEnd
            // are already frame-quantized seconds, so this reproduces the same frame-accurate
            // boundary without depending on StartFrame/EndFrame staying meaningful when a source's
            // fps could not be determined (ResolvedSpan reports 0/0 for both in that case).
            filterParts.Add(
                $"[{ffInputIdx}:v]trim=start={ss}:end={ee},setpts=PTS-STARTPTS" +
                $"{normalizeSuffix}{videoFadeSuffix}[v{i}]");

            // concat's own "a=" stream count must be uniform across every concatenated segment, so
            // an audio-less span (real B-roll routinely ships video-only, which would otherwise
            // make "[N:a]" fail ffmpeg outright for that input) cannot simply omit its own audio
            // branch while its neighbors keep theirs. Per-segment fix: a span whose OWN source has
            // no audio stream instead gets a synthesized, matching-duration silence branch —
            // anullsrc is a SOURCE filter (needs no `-i`/input stream of its own, so none of the
            // asset-overlay/music input-index bookkeeping elsewhere in this method is affected),
            // its `d=` option makes it self-terminating (no atrim needed on top), and it already
            // natively produces 48kHz/stereo output — the same format every real atrim branch below
            // is normalized to — so no extra aformat is needed either way. This only ever runs when
            // at least one referenced source DOES have audio (anySourceHasAudio); when none does,
            // the whole output drops audio instead (see anySourceHasAudio above), exactly mirroring
            // the single-source path's own no-audio-at-all fallback.
            if (anySourceHasAudio)
            {
                if (SourceHasAudio(span.SourceIndex))
                {
                    filterParts.Add(
                        $"[{ffInputIdx}:a]atrim=start={ss}:end={ee},asetpts=PTS-STARTPTS," +
                        "aformat=sample_rates=48000:channel_layouts=stereo" +
                        $"{audioFadeSuffix}[a{i}]");
                }
                else
                {
                    filterParts.Add($"anullsrc=r=48000:cl=stereo:d={FfmpegArgvFormat.Number(spanDurationSec)}[a{i}]");
                }
            }
        }

        List<ResolvedOverlay> textOverlays = overlays?.Where(o => !o.IsAssetOverlay).ToList() ?? [];
        List<ResolvedOverlay> assetOverlays = overlays?.Where(o => o.IsAssetOverlay).ToList() ?? [];
        bool hasOverlays = textOverlays.Count > 0 || assetOverlays.Count > 0;

        // Same [vcut]-vs-[vout] labeling convention EncodeReencodeAsync uses: the concat/transition
        // stage outputs straight to the video final label when there is nothing further to draw,
        // or to an internal [vcat] label that the grade/overlay chain below continues from when
        // there is. Color grading adds one stage directly after the concat (before overlays, so
        // graphics paint clean on top of graded footage — the same stage-ordering rule the
        // single-source path applies by folding the grade into its cut stage).
        bool hasGrade = colorGradeFilter is not null;
        string videoConcatLabel = hasOverlays || hasGrade ? "[vcat]" : videoFinalLabel;

        // Sound effects (see docs/video-editing.md "Sound effects"): mirrors the single-source
        // path's own [abase] convention exactly — when cues are present, the pre-SFX audio chain
        // ends at [abase] and the SFX mix stage appended below becomes the audio final label.
        List<ResolvedSfxCue> sfxCues = sfx?.ToList() ?? [];
        bool hasSfx = sfxCues.Count > 0 && (anySourceHasAudio || music is not null);
        string audioBaseLabel = hasSfx ? "[abase]" : audioFinalLabelInner;

        // Background music (see docs/video-editing.md "Background music"): mirrors
        // EncodeReencodeAsync's own [aout]-vs-[adial] flip. No extra aformat is needed here on the
        // dialogue side — every per-span atrim branch above already ends in
        // aformat=sample_rates=48000:channel_layouts=stereo, so the concat output already matches
        // the music branch's own format by construction.
        string? audioConcatLabel = anySourceHasAudio ? (music is not null ? "[adial]" : audioBaseLabel) : null;

        // Cut transitions: groups spans into maximal non-overlapping "blocks" (each one plain
        // concat), chained pairwise via xfade/acrossfade at every seam with OverlapSec > 0 — see
        // TransitionFilterBuilder.BuildSegmentedTransitions. When seamPlans/timeline are null (any
        // pre-transitions call site), an all-hard-cut fallback plan collapses this to exactly one
        // block and one plain concat covering every span, byte-identical to the single
        // `concat=n=...` line this replaces.
        IReadOnlyList<SeamPlan> effectiveSeamPlans = seamPlans ?? BuildHardCutSeamsForFallback(spans.Count);
        OutputTimeline effectiveTimeline = timeline ?? OutputTimeline.Build(spans, effectiveSeamPlans);
        TransitionFilterBuilder.SegmentedTransitionResult segmented = TransitionFilterBuilder.BuildSegmentedTransitions(
            effectiveSeamPlans, effectiveTimeline, spans.Count, anySourceHasAudio,
            i => $"[v{i}]", i => $"[a{i}]",
            videoFinalLabel: videoConcatLabel, audioFinalLabel: audioConcatLabel ?? "[aout]");
        filterParts.AddRange(segmented.FilterParts);

        // Color grading: its own stage between the concat and the overlay chain. When overlays
        // follow, it ends at an internal [vgrd] label the overlay chain continues from; otherwise
        // it becomes the video final label itself.
        string overlayChainStartLabel = videoConcatLabel;
        if (hasGrade)
        {
            string gradeStageFinalLabel = hasOverlays && graphicsConfig is not null ? "[vgrd]" : videoFinalLabel;
            filterParts.Add($"[vcat]{colorGradeFilter}{gradeStageFinalLabel}");
            overlayChainStartLabel = gradeStageFinalLabel;
        }

        if (hasOverlays && graphicsConfig is not null)
        {
            // Deliberately duplicated (not shared) with EncodeReencodeAsync's own inline overlay-
            // chain construction just below it in this file: that method is exercised by exact
            // filter-string tests, and reusing it here would mean changing its signature/shape
            // purely to serve this new, structurally different multi-input caller — not worth
            // risking the single-source path's tested behavior over.
            for (int i = 0; i < textOverlays.Count; i++)
            {
                ResolvedOverlay overlay = textOverlays[i];
                await WriteOverlayTextFileAsync(scratch, DrawtextFilterBuilder.MainTextSlot(i), overlay.SanitizedText, ct);
                if (overlay.SanitizedSubtext.Length > 0)
                    await WriteOverlayTextFileAsync(scratch, DrawtextFilterBuilder.SubtextSlot(i), overlay.SanitizedSubtext, ct);
            }

            string currentLabel = overlayChainStartLabel;
            if (textOverlays.Count > 0)
            {
                string textStageFinalLabel = assetOverlays.Count > 0 ? "[vtxt]" : videoFinalLabel;
                filterParts.Add(DrawtextFilterBuilder.BuildFilterChain(
                    currentLabel, textOverlays, cw, ch,
                    graphicsConfig.OverlayFontSizePct, graphicsConfig.OverlayFadeMs,
                    graphicsConfig.OverlayFontColor, graphicsConfig.OverlayBoxColor, _options.FontFilePath,
                    slot => scratch.GetPath($"ov-{slot}.txt"),
                    finalLabel: textStageFinalLabel,
                    boxHeightPct: graphicsConfig.OverlayBoxHeightPct,
                    boxWidthPct: graphicsConfig.OverlayBoxWidthPct));
                currentLabel = textStageFinalLabel;
            }

            if (assetOverlays.Count > 0)
            {
                // Asset overlay inputs are appended after every SOURCE input below, so their
                // ffmpeg input index is offset by however many distinct source clips were added
                // (orderedSourceIndices.Count), not the single-source path's fixed "+1".
                int assetInputBase = orderedSourceIndices.Count;
                filterParts.Add(OverlayAssetFilterBuilder.BuildFilterChain(
                    currentLabel, assetOverlays, cw, ch,
                    inputIndexForIndex: i => assetInputBase + i,
                    finalLabel: videoFinalLabel,
                    boxHeightPct: graphicsConfig.OverlayBoxHeightPct,
                    boxWidthPct: graphicsConfig.OverlayBoxWidthPct));
            }
        }

        if (music is not null)
        {
            // Music input index: after every SOURCE input AND every asset-overlay input — mirrors
            // the single-source path's own "music is the last input" rule, with the asset-overlay
            // offset here being orderedSourceIndices.Count (not the single-source path's fixed
            // "+1", since multiple sources may be present).
            int musicInputIndex = orderedSourceIndices.Count + assetOverlays.Count;
            if (anySourceHasAudio)
            {
                filterParts.Add(MusicMixFilterBuilder.BuildMusicBranch(musicInputIndex, music));
                filterParts.Add(MusicMixFilterBuilder.BuildMixStage(audioConcatLabel!, finalLabel: audioBaseLabel));
            }
            else
            {
                // No dialogue anywhere in this compile to duck against — the music branch's own
                // output becomes the audio base/final label directly, exactly as the single-source
                // path does.
                filterParts.Add(MusicMixFilterBuilder.BuildMusicBranch(musicInputIndex, music, outLabel: audioBaseLabel));
            }
        }

        // Sound effects: cue branches + the SFX mix stage, layered over whatever base audio the
        // stages above produced ([abase] — the dialogue concat, the dialogue+music mix, or the
        // music-only branch). Cue input index: after every SOURCE input, every asset-overlay
        // input, AND the music input, mirroring the single-source path's own "cues are the last
        // inputs" rule with this path's own multi-source offsets.
        if (hasSfx)
        {
            int sfxInputBase = orderedSourceIndices.Count + assetOverlays.Count + (music is not null ? 1 : 0);
            for (int k = 0; k < sfxCues.Count; k++)
                filterParts.Add(SfxMixFilterBuilder.BuildCueBranch(sfxInputBase + k, k, sfxCues[k]));
            filterParts.Add(SfxMixFilterBuilder.BuildMixStage(audioBaseLabel, sfxCues.Count, finalLabel: audioFinalLabelInner));
        }

        bool hasAudioOutput = anySourceHasAudio || music is not null;

        // Cut transitions: the whole-piece program fade — always the LAST stage, after
        // overlays/the music mix. Both are no-ops (empty strings) on the byte-identical
        // pre-transitions path (programFade == null).
        if (hasVideoProgramFade)
            filterParts.Add($"[vpre]{videoProgramSuffix.TrimStart(',')}[vout]");
        if (hasAudioProgramFade)
            filterParts.Add($"[apre]{audioProgramSuffix.TrimStart(',')}[aout]");

        string filterComplex = string.Join(";", filterParts);

        Action<string>? progressLineHandler = progressContext is not null && totalOutputSeconds > 0
            ? BuildFfmpegProgressLineHandler(progressContext, totalOutputSeconds)
            : null;

        List<string> args = new() { "-nostdin", "-hide_banner", "-y", "-loglevel", "error" };
        if (progressLineHandler is not null)
        {
            args.Add("-progress");
            args.Add("pipe:1");
        }

        args.Add("-protocol_whitelist"); args.Add("file");
        foreach (int sourceIdx in orderedSourceIndices)
        {
            args.Add("-i");
            args.Add(localPathBySource[sourceIdx]);
        }

        // One extra -i per asset overlay, after every source input — each path was already
        // downloaded to local scratch space AND ffprobe-validated by ResolveGraphicsAsync before
        // this method ever saw it.
        foreach (ResolvedOverlay assetOverlay in assetOverlays)
        {
            args.Add("-i");
            args.Add(assetOverlay.RenderedAssetLocalPath!);
        }

        if (music is not null)
        {
            if (music.LoopInput)
            {
                args.Add("-stream_loop");
                args.Add("-1");
            }

            args.Add("-i");
            args.Add(music.LocalPath);
        }

        // Sound effects: one extra -i per cue, AFTER the music input, in the same order the
        // BuildCueBranch calls above assumed (input index sfxInputBase + k) — every path was
        // downloaded to local scratch AND ffprobe-validated by ResolveSfxAsync.
        if (hasSfx)
        {
            foreach (ResolvedSfxCue cue in sfxCues)
            {
                args.Add("-i");
                args.Add(cue.LocalPath);
            }
        }

        // Always scripted to a file rather than passed inline via -filter_complex: a multi-source
        // filtergraph (a trim/scale/pad/fps/aformat pair PER SPAN, plus the concat stage, plus any
        // overlays) is virtually guaranteed to exceed a safe inline argv length well before
        // spans.Count reaches the single-source path's own FilterComplexScriptThreshold, so
        // correctness here is worth more than the minor overhead of always writing the script file.
        string scriptPath = scratch.GetPath("filter_complex_multisource.txt");
        await File.WriteAllTextAsync(scriptPath, filterComplex, ct);
        args.Add("-filter_complex_script");
        args.Add(scriptPath);

        args.Add("-map"); args.Add("[vout]");
        if (hasAudioOutput)
        {
            args.Add("-map"); args.Add("[aout]");
        }
        args.Add("-c:v"); args.Add(videoCodec);
        args.Add("-crf"); args.Add(FfmpegArgvFormat.Number(crf));
        args.Add("-preset"); args.Add(preset);
        if (hasAudioOutput)
        {
            args.Add("-c:a"); args.Add(audioCodec);
        }
        args.Add(outputPath);

        return await _videoToolRunner.RunFfmpegAsync(args, timeout, ct, progressLineHandler);
    }

    /// <summary>
    /// Builds a per-stdout-line handler that parses ffmpeg's "-progress pipe:1" key=value output
    /// into a 0-99 encode percentage (reserving 100 for the caller's own post-encode "Uploading
    /// compiled video" checkpoint) against the known <paramref name="totalOutputSeconds"/>, and
    /// reports it via <see cref="StepExecutionContext.ReportProgressAsync"/>. ffmpeg emits both
    /// "out_time_ms=" (a long-standing ffmpeg naming quirk — despite the name, its value is
    /// MICROSECONDS, kept for backward compatibility) and, on newer builds, the unambiguous
    /// "out_time_us="; both are treated identically here. Throttled to at most once per whole
    /// percentage point and at least 2 seconds apart, since ffmpeg's default progress cadence
    /// (multiple lines/second) would otherwise flood RabbitMQ with an ephemeral UI signal nobody
    /// needs that granular. Runs on the process's async stdout-reader thread (see
    /// IVideoToolRunner's doc comment) — never throws, and only ever fires the publish
    /// fire-and-forget so a slow/failed publish can never stall ffmpeg's own stdout pump.
    /// </summary>
    private static Action<string> BuildFfmpegProgressLineHandler(StepExecutionContext context, double totalOutputSeconds)
    {
        object gate = new();
        int lastReportedPercent = -1;
        DateTime lastReportedAt = DateTime.MinValue;

        return line =>
        {
            int eq = line.IndexOf('=');
            if (eq < 0)
                return;

            string key = line[..eq];
            if (key != "out_time_ms" && key != "out_time_us")
                return;

            if (!long.TryParse(line[(eq + 1)..], out long microseconds))
                return;

            double elapsedSec = microseconds / 1_000_000.0;
            int percent = (int)Math.Clamp(Math.Round(elapsedSec / totalOutputSeconds * 100), 0, 99);

            lock (gate)
            {
                DateTime now = DateTime.UtcNow;
                if (percent == lastReportedPercent || now - lastReportedAt < TimeSpan.FromSeconds(2))
                    return;
                lastReportedPercent = percent;
                lastReportedAt = now;
            }

            _ = context.ReportProgressAsync("Encoding", percent);
        };
    }

    /// <summary>
    /// Writes one overlay text/subtext's ALREADY-SANITIZED content to its own scratch file
    /// (<c>{scratch}/ov-{slot}.txt</c>), referenced by <see cref="DrawtextFilterBuilder"/> via
    /// drawtext's <c>textfile=</c> option — never inline <c>text=</c> — so no drawtext
    /// metacharacter in the text can ever terminate or inject into the filter string, because the
    /// text never appears in the filter string at all.
    /// </summary>
    private static Task WriteOverlayTextFileAsync(VideoScratchSpace scratch, int slot, string sanitizedText, CancellationToken ct) =>
        File.WriteAllTextAsync(scratch.GetPath($"ov-{slot}.txt"), sanitizedText, ct);

    private async Task<VideoToolResult> EncodeStreamCopyAsync(
        VideoScratchSpace scratch,
        string localVideoPath,
        string outputPath,
        IReadOnlyList<ResolvedSpan> spans,
        TimeSpan timeout,
        CancellationToken ct)
    {
        List<string> segmentPaths = new(spans.Count);
        for (int i = 0; i < spans.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            string segPath = scratch.GetPath($"seg-{i}.mp4");
            List<string> segArgs = new()
            {
                "-nostdin", "-hide_banner", "-y", "-loglevel", "error",
                "-protocol_whitelist", "file",
                "-ss", FfmpegArgvFormat.Number(spans[i].SnappedStart),
                "-to", FfmpegArgvFormat.Number(spans[i].SnappedEnd),
                "-i", localVideoPath,
                "-c", "copy",
                segPath
            };

            VideoToolResult segResult = await _videoToolRunner.RunFfmpegAsync(segArgs, timeout, ct);
            if (!segResult.Succeeded)
                return segResult;

            // Audit only: probe the produced segment so the EDL/logs can record the actual
            // (keyframe-snapped) duration alongside the requested one.
            try
            {
                MediaProbeResult segProbe = await _mediaProbe.ProbeAsync(segPath, ct);
                _logger.LogInformation(
                    "VideoCompile StreamCopy segment {Index}: requested [{Start:F3},{End:F3}), actual duration {Actual:F3}s",
                    i, spans[i].SnappedStart, spans[i].SnappedEnd, segProbe.DurationSec);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "VideoCompile StreamCopy segment {Index}: post-cut probe failed (non-fatal)", i);
            }

            segmentPaths.Add(segPath);
        }

        string concatListPath = scratch.GetPath("concat.txt");
        StringBuilder sb = new();
        foreach (string segPath in segmentPaths)
            sb.AppendLine($"file '{segPath.Replace("'", "'\\''")}'");
        await File.WriteAllTextAsync(concatListPath, sb.ToString(), ct);

        List<string> concatArgs = new()
        {
            "-nostdin", "-hide_banner", "-y", "-loglevel", "error",
            "-protocol_whitelist", "file,concat",
            "-f", "concat", "-safe", "0",
            "-i", concatListPath,
            "-c", "copy",
            outputPath
        };

        return await _videoToolRunner.RunFfmpegAsync(concatArgs, timeout, ct);
    }

    // ---------------------------------------------------------------------
    // Validation / sanitization helpers
    // ---------------------------------------------------------------------

    private static string SanitizeOutputFileName(string? raw)
    {
        string candidate = string.IsNullOrWhiteSpace(raw) ? "edited" : raw!;
        string cleaned = Regex.Replace(candidate, "[^A-Za-z0-9._-]", "_").Trim('.', '_', '-');
        if (string.IsNullOrWhiteSpace(cleaned))
            cleaned = "edited";

        if (cleaned.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            return cleaned;

        string withoutExt = Path.GetFileNameWithoutExtension(cleaned);
        if (string.IsNullOrWhiteSpace(withoutExt))
            withoutExt = "edited";

        return withoutExt + ".mp4";
    }

    private static bool IsValidOverlayFontColor(string color) =>
        NamedOverlayFontColors.Contains(color) || HexColorPattern.IsMatch(color);

    private static string? EvaluateExpect(VideoCompileExpectation? expect, double totalOutputSeconds, double retainedRatio)
    {
        if (expect is null)
            return null;

        if (expect.MinOutputSeconds.HasValue && totalOutputSeconds < expect.MinOutputSeconds.Value)
            return $"expect.minOutputSeconds={expect.MinOutputSeconds.Value:F2} but the resolved edit is {totalOutputSeconds:F2}s.";

        if (expect.MaxOutputSeconds.HasValue && totalOutputSeconds > expect.MaxOutputSeconds.Value)
            return $"expect.maxOutputSeconds={expect.MaxOutputSeconds.Value:F2} but the resolved edit is {totalOutputSeconds:F2}s.";

        if (expect.MinRetainedRatio.HasValue && retainedRatio < expect.MinRetainedRatio.Value)
        {
            return $"expect.minRetainedRatio={expect.MinRetainedRatio.Value:F2} but the edit retains only " +
                   $"{retainedRatio:F2} of the source — refusing an edit that discards nearly everything.";
        }

        if (expect.MaxRetainedRatio.HasValue && retainedRatio > expect.MaxRetainedRatio.Value)
            return $"expect.maxRetainedRatio={expect.MaxRetainedRatio.Value:F2} but the edit retains {retainedRatio:F2} of the source.";

        return null;
    }

    private static string Truncate(string value, int max = 2000) => value.Length <= max ? value : value[^max..];

    /// <summary>
    /// Builds the <c>transitions</c> output node (see docs/video-editing.md "Cut transitions").
    /// Only ever called when <see cref="VideoCompileStepConfig.TransitionPolicy"/> is not
    /// <see cref="VideoTransitionPolicy.Off"/>. <c>downgradedCount</c> is a heuristic over
    /// <see cref="SeamPlan.Rule"/>: a seam that ENDED UP <see cref="SeamTreatment.AudioOnly"/> but
    /// whose matched rule was NOT one that naturally produces <see cref="SeamTreatment.AudioOnly"/>
    /// on its own (R1/R7, or the whole-plan policy/cap forcing) must have gotten there via the
    /// planner's own neighbour-sum/density-cap downgrade path.
    /// </summary>
    private static JsonObject BuildTransitionsNode(
        VideoCompileStepConfig config, IReadOnlyList<SeamPlan> seamPlans, bool xfadeAvailable, bool segmentCountOverCap)
    {
        var treatments = new JsonObject();
        foreach (SeamTreatment t in Enum.GetValues<SeamTreatment>())
            treatments[t.ToString()] = seamPlans.Count(p => p.Treatment == t);

        int appliedCount = seamPlans.Count(p => p.OverlapSec > 0);
        double overlapSec = Math.Round(seamPlans.Sum(p => p.OverlapSec), 3);
        int downgradedCount = seamPlans.Count(p =>
            p.Treatment == SeamTreatment.AudioOnly &&
            !p.Rule.StartsWith("R1", StringComparison.Ordinal) &&
            !p.Rule.StartsWith("R7", StringComparison.Ordinal) &&
            !p.Rule.StartsWith("policy:", StringComparison.Ordinal) &&
            !p.Rule.StartsWith("capped:", StringComparison.Ordinal) &&
            !p.Rule.StartsWith("off:", StringComparison.Ordinal));

        int audioOnlyCount = seamPlans.Count(p => p.Treatment == SeamTreatment.AudioOnly);
        bool audioRampSkipped = audioOnlyCount > TransitionFilterBuilder.MaxAudioRampTerms;

        return new JsonObject
        {
            ["policy"] = config.TransitionPolicy.ToString(),
            ["xfadeAvailable"] = xfadeAvailable,
            ["appliedCount"] = appliedCount,
            ["overlapSec"] = overlapSec,
            ["downgradedCount"] = downgradedCount,
            ["audioRampSkipped"] = audioRampSkipped,
            ["treatments"] = treatments,
            ["reason"] = segmentCountOverCap ? "segment_count_over_cap" : null
        };
    }

    /// <summary>Builds the <c>programFade</c> output node. Only ever called when <see cref="ProgramEnvelopeFilterBuilder.IsEnabled"/> is true.</summary>
    private static JsonObject BuildProgramFadeNode(ProgramFadeResolved fade) => new()
    {
        ["videoInSec"] = Math.Round(fade.VideoInSec, 3),
        ["videoOutSec"] = Math.Round(fade.VideoOutSec, 3),
        ["audioInSec"] = Math.Round(fade.AudioInSec, 3),
        ["audioOutSec"] = Math.Round(fade.AudioOutSec, 3),
        ["color"] = fade.Color
    };

    private static JsonObject BuildEdl(
        VideoCompileStepConfig config,
        string analysisArtifactKey,
        IReadOnlyList<ResolvedSpan> spans,
        double retainedRatio,
        double totalOutputSeconds,
        int droppedOverCap,
        int crf,
        JsonObject? graphics = null,
        JsonObject? music = null,
        JsonObject? audio = null,
        JsonObject? transitions = null,
        JsonObject? programFade = null,
        double? transitionOverlapSec = null,
        JsonObject? inserts = null,
        JsonObject? colorGrade = null,
        JsonObject? sfx = null)
    {
        var segmentsArray = new JsonArray();
        for (int i = 0; i < spans.Count; i++)
        {
            ResolvedSpan s = spans[i];
            segmentsArray.Add(new JsonObject
            {
                ["index"] = i,
                ["requestedStartSec"] = s.RequestedStart,
                ["requestedEndSec"] = s.RequestedEnd,
                ["snappedStartSec"] = s.SnappedStart,
                ["snappedEndSec"] = s.SnappedEnd,
                ["startFrame"] = s.StartFrame,
                ["endFrame"] = s.EndFrame,
                // Multi-source addition — which source clip this segment cuts from. Always 0 for a
                // single-source compile, so this key's presence/value is not new information there,
                // just made explicit.
                ["sourceIndex"] = s.SourceIndex
            });
        }

        var edl = new JsonObject
        {
            ["version"] = 1,
            ["analysisArtifactStorageKey"] = analysisArtifactKey,
            ["analysisStepOrder"] = config.AnalysisStepOrder,
            ["analysisStepResultId"] = config.AnalysisStepResultId?.ToString(),
            ["mode"] = config.Mode.ToString(),
            ["segments"] = segmentsArray,
            ["retainedRatio"] = retainedRatio,
            ["totalOutputSeconds"] = totalOutputSeconds,
            ["droppedSegmentsOverCap"] = droppedOverCap,
            ["codec"] = new JsonObject
            {
                ["video"] = config.VideoCodec,
                ["audio"] = config.AudioCodec,
                ["preset"] = config.Preset,
                ["crf"] = crf
            }
        };

        // Phase 3 (motion graphics): only present at all when EnableGraphics=true — when false
        // (the default), the EDL shape is byte-identical to the pre-Phase-3 compile path.
        if (graphics is not null)
            edl["graphics"] = graphics;

        // Background music: same discipline — only present when EnableMusic=true.
        if (music is not null)
            edl["music"] = music;

        // Tracked screen inserts: same discipline — only present when EnableInserts=true.
        if (inserts is not null)
            edl["inserts"] = inserts;

        // Color grading: same discipline — only present when EnableColorGrade=true.
        if (colorGrade is not null)
            edl["colorGrade"] = colorGrade;

        // Sound effects: same discipline — only present when EnableSfx=true.
        if (sfx is not null)
            edl["sfx"] = sfx;

        // Bug group C: unlike graphics/music, always present — audio isn't opt-in the way those
        // phases are, so a dropped audio stream is never silently unreported.
        if (audio is not null)
            edl["audio"] = audio;

        // Cut transitions: same discipline as graphics/music — only present when TransitionPolicy
        // != Off / a program fade is actually configured.
        if (transitions is not null)
            edl["transitions"] = transitions;

        if (programFade is not null)
            edl["programFade"] = programFade;

        if (transitionOverlapSec.HasValue)
            edl["transitionOverlapSec"] = Math.Round(transitionOverlapSec.Value, 3);

        return edl;
    }

    private static string BuildResolvedInputDescriptor(VideoCompileStepConfig config, VideoEditDecisionOutput decision)
    {
        var descriptor = new JsonObject
        {
            ["analysisStepOrder"] = config.AnalysisStepOrder,
            ["analysisStepResultId"] = config.AnalysisStepResultId?.ToString(),
            ["decisionFrom"] = config.Decision.From.ToString(),
            ["keepSpanCount"] = decision.Keep.Count
        };
        return descriptor.ToJsonString(EnvelopeJsonOptions);
    }

    private StepExecutionResult Failure(
        StepExecutionContext context, Stopwatch sw, string code, string message, string? artifactStorageKey = null)
    {
        _logger.LogWarning("VideoCompile step {StepOrder} failed: [{Code}] {Message}", context.Step.StepOrder, code, message);

        var envelope = new JsonObject
        {
            ["status"] = "failed",
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
        };

        return new StepExecutionResult
        {
            Output = envelope.ToJsonString(EnvelopeJsonOptions),
            NextStepIndex = context.CurrentStepIndex + 1,
            NewIterationCount = context.IterationCount,
            DurationMs = sw.ElapsedMilliseconds,
            TokensUsed = 0,
            Status = StepStatus.Failed,
            ErrorDetails = message,
            ArtifactStorageKey = artifactStorageKey
        };
    }
}
