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

            // ---- Resolve the analyze step's FULL artifact (never the bounded view) ----

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

            (string? decisionJson, string? decisionError) = ResolveDecisionJson(context, config.Decision);
            if (decisionJson is null)
                return Failure(context, sw, "DECISION_UNRESOLVED", decisionError ?? "Could not resolve the editorial decision input.");

            VideoEditDecisionOutput? decision;
            try
            {
                decision = JsonSerializer.Deserialize<VideoEditDecisionOutput>(decisionJson, DecisionJsonOptions);
            }
            catch (JsonException ex)
            {
                return Failure(context, sw, "DECISION_INVALID", $"Decision input is not valid JSON: {ex.Message}");
            }

            if (decision is null || decision.Keep.Count == 0)
                return Failure(context, sw, "EMPTY_KEEP", "Decision has no Keep spans; nothing to compile.");

            context.RecordResolvedInput(BuildResolvedInputDescriptor(config, decision));

            // ---- Reject unknown ids (must be in offeredIds — actually offered, not merely present) ----

            HashSet<string> offeredIdSet = new(artifact.OfferedIds, StringComparer.Ordinal);
            Dictionary<string, (double Start, double End)> idTimes = BuildIdTimeIndex(artifact);

            List<(double Start, double End)> rawSpans = new(decision.Keep.Count);
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

                (double fromStart, _) = idTimes[span.FromId];
                (_, double toEnd) = idTimes[span.ToId];

                if (toEnd <= fromStart)
                {
                    return Failure(
                        context, sw, "INVALID_SPAN",
                        $"Keep span from '{span.FromId}' to '{span.ToId}' resolves to a non-positive duration " +
                        $"([{fromStart:F3}, {toEnd:F3})). ToId must not precede FromId.");
                }

                rawSpans.Add((fromStart, toEnd));
            }

            // ---- Normalize: reject reordering/overlap, coalesce, pad, clamp, drop, cap ----

            for (int i = 1; i < rawSpans.Count; i++)
            {
                if (rawSpans[i].Start < rawSpans[i - 1].Start)
                {
                    return Failure(
                        context, sw, "SPANS_OUT_OF_ORDER",
                        $"Keep spans must be listed in chronological order; span {i} starts at {rawSpans[i].Start:F3}s, " +
                        $"before span {i - 1} which starts at {rawSpans[i - 1].Start:F3}s. Reordering kept spans is not " +
                        "supported — list them in the order they should play.");
                }
            }

            List<(double Start, double End)> nonOverlapping = new();
            foreach ((double start, double end) in rawSpans)
            {
                if (nonOverlapping.Count > 0 && start < nonOverlapping[^1].End - AdjacencyEpsilonSec)
                {
                    return Failure(
                        context, sw, "SPANS_OVERLAP",
                        $"Keep spans overlap: [{nonOverlapping[^1].Start:F3}, {nonOverlapping[^1].End:F3}) and " +
                        $"[{start:F3}, {end:F3}).");
                }

                nonOverlapping.Add((start, end));
            }

            List<(double Start, double End)> coalesced = CoalesceAdjacent(nonOverlapping);

            double prePadSec = Math.Max(0, config.PrePaddingMs) / 1000.0;
            double postPadSec = Math.Max(0, config.PostPaddingMs) / 1000.0;
            double durationSec = artifact.Media.DurationSec;

            List<(double Start, double End)> padded = coalesced
                .Select(s => (
                    Start: Math.Clamp(s.Start - prePadSec, 0, durationSec),
                    End: Math.Clamp(s.End + postPadSec, 0, durationSec)))
                .Where(s => s.End > s.Start)
                .ToList();

            // Padding can push neighboring spans into each other; coalesce again post-padding.
            padded = CoalesceAdjacent(padded);

            double minSegmentSec = Math.Max(0, config.MinSegmentMs) / 1000.0;
            List<(double Start, double End)> aboveMinLength = padded.Where(s => s.End - s.Start >= minSegmentSec).ToList();

            int droppedOverCap = 0;
            List<(double Start, double End)> finalSpans = aboveMinLength;
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
            double retainedRatio = durationSec > 0 ? totalOutputSeconds / durationSec : 0;

            string? expectError = EvaluateExpect(config.Expect, totalOutputSeconds, retainedRatio);
            if (expectError is not null)
                return Failure(context, sw, "EXPECT_FAILED", expectError);

            // ---- Frame-quantize on the exact rational fps (never a collapsed double) ----

            int fpsNum = artifact.Media.FpsNum;
            int fpsDen = artifact.Media.FpsDen;
            bool hasValidFps = fpsNum > 0 && fpsDen > 0;

            List<ResolvedSpan> resolvedSpans = finalSpans.Select(s =>
            {
                if (!hasValidFps)
                    return new ResolvedSpan(s.Start, s.End, s.Start, s.End, 0, 0);

                long startFrame = ToStartFrame(s.Start, fpsNum, fpsDen);
                long endFrame = ToEndFrame(s.End, fpsNum, fpsDen);
                double snappedStart = FrameToSec(startFrame, fpsNum, fpsDen);
                double snappedEnd = FrameToSec(endFrame, fpsNum, fpsDen);
                return new ResolvedSpan(s.Start, s.End, snappedStart, snappedEnd, startFrame, endFrame);
            }).ToList();

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

            // ---- Write the EDL audit artifact ----

            string edlLocalPath = scratch.GetPath("edl.json");
            JsonObject edl = BuildEdl(
                config, analysisArtifactKey, resolvedSpans, retainedRatio, totalOutputSeconds, droppedOverCap, crf);
            await File.WriteAllTextAsync(edlLocalPath, edl.ToJsonString(EnvelopeJsonOptions), context.CancellationToken);

            string edlFileName = $"video-analysis/{context.Execution.Id:D}/step-{step.StepOrder}-edl.json";
            string edlStorageKey = await _workspace.UploadArtifactAsync(
                context.Execution.ProjectId, edlLocalPath, edlFileName, "application/json", context.CancellationToken);

            // ---- Resolve the source video (same artifact.Media info implies we need the bytes too) ----

            (string? sourceStorageKey, string? sourceError) = await ResolveSourceStorageKeyAsync(context, config);
            if (sourceStorageKey is null)
                return Failure(context, sw, "SOURCE_UNRESOLVED", sourceError ?? "Could not resolve the source video to cut.", edlStorageKey);

            string localVideoPath = scratch.GetPath("source" + Path.GetExtension(sourceStorageKey) switch { "" => ".mp4", var e => e });
            await _workspace.DownloadStorageKeyToFileAsync(
                context.Execution.ProjectId, sourceStorageKey, localVideoPath, context.CancellationToken);

            string encodedLocalPath = scratch.GetPath(outputFileName);
            TimeSpan timeout = TimeSpan.FromSeconds(_options.CompileTimeoutSeconds);

            VideoToolResult encodeResult = config.Mode == VideoCompileMode.Reencode
                ? await EncodeReencodeAsync(scratch, localVideoPath, encodedLocalPath, resolvedSpans, config.VideoCodec, config.AudioCodec, config.Preset, crf, timeout, context.CancellationToken)
                : await EncodeStreamCopyAsync(scratch, localVideoPath, encodedLocalPath, resolvedSpans, timeout, context.CancellationToken);

            if (!encodeResult.Succeeded)
            {
                return Failure(
                    context, sw, "ENCODE_FAILED",
                    $"ffmpeg encode failed (exitCode={encodeResult.ExitCode}, timedOut={encodeResult.TimedOut}): {Truncate(encodeResult.StdErr)}",
                    edlStorageKey);
            }

            // ---- Upload the compiled video ----

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
                ["outputDurationSec"] = totalOutputSeconds,
                ["retainedRatio"] = retainedRatio,
                ["droppedSegmentsOverCap"] = droppedOverCap
            };

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

        StepOutputHistoryEntry? entry = context.StepOutputHistory
            .FirstOrDefault(h => h.StepOrder == config.AnalysisStepOrder);

        return entry is null || string.IsNullOrWhiteSpace(entry.ArtifactStorageKey)
            ? (null, $"Step {config.AnalysisStepOrder} in this execution did not produce an ArtifactStorageKey.")
            : (entry.ArtifactStorageKey, null);
    }

    private static (string? Json, string? Error) ResolveDecisionJson(StepExecutionContext context, ExtractInputRef decisionRef)
    {
        string? content = decisionRef.From switch
        {
            ExtractInputSource.Previous => context.StepOutputHistory
                .LastOrDefault(h => !string.IsNullOrWhiteSpace(h.Output))?.Output,
            ExtractInputSource.Step => decisionRef.StepOrder.HasValue
                ? context.StepOutputHistory.FirstOrDefault(h => h.StepOrder == decisionRef.StepOrder.Value)?.Output
                : null,
            _ => null
        };

        return string.IsNullOrWhiteSpace(content) ? (null, "Decision input resolved to empty content.") : (content, null);
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

    private static Dictionary<string, (double Start, double End)> BuildIdTimeIndex(VideoAnalysisArtifact artifact)
    {
        var index = new Dictionary<string, (double Start, double End)>(StringComparer.Ordinal);
        foreach (VideoAnalysisShot s in artifact.Shots)
            index[s.Id] = (s.StartSec, s.EndSec);
        foreach (VideoAnalysisSilenceSpan s in artifact.SilenceSpans)
            index[s.Id] = (s.StartSec, s.EndSec);
        foreach (VideoAnalysisSegment s in artifact.Segments)
            index[s.Id] = (s.StartSec, s.EndSec);
        // Words are deliberately excluded — never offered to the model in v1, so never resolvable here.
        return index;
    }

    private static List<(double Start, double End)> CoalesceAdjacent(IReadOnlyList<(double Start, double End)> spans)
    {
        List<(double Start, double End)> result = new();
        foreach ((double start, double end) in spans)
        {
            if (result.Count > 0 && start <= result[^1].End + AdjacencyEpsilonSec)
            {
                (double prevStart, double prevEnd) = result[^1];
                result[^1] = (prevStart, Math.Max(prevEnd, end));
            }
            else
            {
                result.Add((start, end));
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

    private sealed record ResolvedSpan(
        double RequestedStart, double RequestedEnd, double SnappedStart, double SnappedEnd, long StartFrame, long EndFrame);

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
        CancellationToken ct)
    {
        string BetweenTerms() => string.Join("+", spans.Select(s =>
            $"between(t,{FfmpegArgvFormat.Number(s.SnappedStart)},{FfmpegArgvFormat.Number(s.SnappedEnd)})"));

        string videoFilter = $"select='{BetweenTerms()}',setpts=N/FRAME_RATE/TB";
        string audioFilter = $"aselect='{BetweenTerms()}',asetpts=N/SR/TB";
        string filterComplex = $"[0:v]{videoFilter}[vout];[0:a]{audioFilter}[aout]";

        List<string> args = new() { "-nostdin", "-hide_banner", "-y", "-loglevel", "error", "-protocol_whitelist", "file", "-i", localVideoPath };

        if (spans.Count > FilterComplexScriptThreshold)
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
        args.Add("-map"); args.Add("[aout]");
        args.Add("-c:v"); args.Add(videoCodec);
        args.Add("-crf"); args.Add(FfmpegArgvFormat.Number(crf));
        args.Add("-preset"); args.Add(preset);
        args.Add("-c:a"); args.Add(audioCodec);
        args.Add(outputPath);

        return await _videoToolRunner.RunFfmpegAsync(args, timeout, ct);
    }

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

    private static JsonObject BuildEdl(
        VideoCompileStepConfig config,
        string analysisArtifactKey,
        IReadOnlyList<ResolvedSpan> spans,
        double retainedRatio,
        double totalOutputSeconds,
        int droppedOverCap,
        int crf)
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
                ["endFrame"] = s.EndFrame
            });
        }

        return new JsonObject
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
