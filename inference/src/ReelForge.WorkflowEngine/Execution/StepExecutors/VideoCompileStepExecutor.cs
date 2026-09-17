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
                decision = JsonSerializer.Deserialize<VideoEditDecisionOutput>(decisionJson, DecisionJsonOptions);
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
            // audio only when that one clip lacks it; multi-source currently drops ALL audio the
            // moment ANY referenced clip lacks it (see EncodeReencodeMultiSourceAsync's
            // allSourcesHaveAudio gating — a documented, lower-priority follow-up would synthesize
            // silence per-segment instead). Threaded into ResolveMusicAsync (skip ducking/lift-
            // window computation against dialogue that will not exist in the output) and the new
            // "audio" EDL/outputSummary block below (report the degrade instead of leaving it
            // silent).
            bool allSourcesHaveAudio = usedSourceIndices.All(i => sourceHasAudioByIndex[i]);
            bool sourceHasAudio = sourceHasAudioByIndex[usedSourceIndices[0]];
            bool hasDialogueAudioInOutput = isMultiSource ? allSourcesHaveAudio : sourceHasAudio;

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
                    context, config, artifact, resolvedSpans, scratch, canonicalMedia, context.CancellationToken);
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
                    context, config, artifact, resolvedSpans, scratch, hasDialogueAudioInOutput, context.CancellationToken);
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

            // ---- Write the EDL audit artifact ----

            await context.ReportProgressAsync("Writing edit decision list");
            string edlLocalPath = scratch.GetPath("edl.json");
            JsonObject edl = BuildEdl(
                config, analysisArtifactKey, resolvedSpans, retainedRatio, totalOutputSeconds, droppedOverCap, crf, graphicsNode, musicNode, audioNode);
            await File.WriteAllTextAsync(edlLocalPath, edl.ToJsonString(EnvelopeJsonOptions), context.CancellationToken);

            string edlFileName = $"video-analysis/{context.Execution.Id:D}/step-{step.StepOrder}-edl.json";
            string edlStorageKey = await _workspace.UploadArtifactAsync(
                context.Execution.ProjectId, edlLocalPath, edlFileName, "application/json", context.CancellationToken);

            string encodedLocalPath = scratch.GetPath(outputFileName);
            TimeSpan timeout = TimeSpan.FromSeconds(_options.CompileTimeoutSeconds);

            await context.ReportProgressAsync("Encoding", 0);
            VideoToolResult encodeResult;
            if (isMultiSource)
            {
                // Guaranteed Mode=Reencode by the MULTI_SOURCE_REQUIRES_REENCODE check above.
                encodeResult = await EncodeReencodeMultiSourceAsync(
                    scratch, localPathBySource, encodedLocalPath, resolvedSpans, config.VideoCodec, config.AudioCodec, config.Preset, crf,
                    canonicalMedia, timeout, context.CancellationToken,
                    overlays: resolvedOverlays, graphicsConfig: resolvedOverlays.Count > 0 ? config : null,
                    progressContext: context, totalOutputSeconds: totalOutputSeconds,
                    music: resolvedMusic, allSourcesHaveAudio: allSourcesHaveAudio);
            }
            else
            {
                // Exactly one distinct source referenced — the ORIGINAL single-input code path,
                // completely unchanged when the source has audio, so a single-source (or
                // single-clip-in-practice) compile's ffmpeg argv/behavior stays byte-identical to
                // before this addition.
                string localVideoPath = localPathBySource[usedSourceIndices[0]];
                encodeResult = config.Mode == VideoCompileMode.Reencode
                    ? await EncodeReencodeAsync(
                        scratch, localVideoPath, encodedLocalPath, resolvedSpans, config.VideoCodec, config.AudioCodec, config.Preset, crf,
                        timeout, context.CancellationToken,
                        overlays: resolvedOverlays, probedWidth: canonicalMedia.Width, probedHeight: canonicalMedia.Height,
                        graphicsConfig: resolvedOverlays.Count > 0 ? config : null,
                        progressContext: context, totalOutputSeconds: totalOutputSeconds,
                        music: resolvedMusic, sourceHasAudio: sourceHasAudio)
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
                ["outputDurationSec"] = totalOutputSeconds,
                ["retainedRatio"] = retainedRatio,
                ["droppedSegmentsOverCap"] = droppedOverCap,
                ["sentenceCheck"] = BuildSentenceCheck(artifact, decision)
            };

            if (graphicsNode is not null)
                outputSummary["graphics"] = JsonNode.Parse(graphicsNode.ToJsonString(EnvelopeJsonOptions));

            if (musicNode is not null)
                outputSummary["music"] = JsonNode.Parse(musicNode.ToJsonString(EnvelopeJsonOptions));

            outputSummary["audio"] = JsonNode.Parse(audioNode.ToJsonString(EnvelopeJsonOptions));

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
        // Hoisted to RobustJsonExtractor so ReviewLoopStepExecutor can apply the same hardening
        // to AgentType.VideoReviewAgent's output — see that class's doc comment for the full
        // rationale. Kept as an internal alias here so this call site (and any external test
        // referencing VideoCompileStepExecutor.ExtractJsonObject) is unaffected.
        string? extracted = ExtractJsonObject(content);
        return extracted is null
            ? (null, $"{label} input did not contain a recognizable JSON object.")
            : (extracted, null);
    }

    /// <inheritdoc cref="RobustJsonExtractor.ExtractJsonObject"/>
    internal static string? ExtractJsonObject(string raw) => RobustJsonExtractor.ExtractJsonObject(raw);

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
    private static Dictionary<string, (double Start, double End, int SourceIndex)> BuildIdTimeIndex(VideoAnalysisArtifact artifact)
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

    /// <summary>Trailing characters (after stripping closing quotes/parens) that count as a sentence ending.</summary>
    private static readonly char[] SentenceTerminalChars = ['.', '!', '?', '…'];

    /// <summary>Trailing closing-quote/paren characters stripped before checking for terminal punctuation, so `He said "stop."` still counts.</summary>
    private static readonly char[] TrailingWrapperChars = ['"', '\'', '”', '’', ')', ']'];

    internal static bool EndsWithSentenceTerminalPunctuation(string text)
    {
        string trimmed = text.TrimEnd().TrimEnd(TrailingWrapperChars);
        return trimmed.Length > 0 && SentenceTerminalChars.Contains(trimmed[^1]);
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
        // is a strong signal Whisper's own segmentation split what was really one sentence in two
        // — evidence for the review agent, not itself a correction (the compile step never trusts
        // an id the story editor was not actually offered/did not choose).
        bool nextSegmentContinues = false;
        if (!endsAtSentenceBoundary && idx + 1 < segments.Count)
        {
            VideoAnalysisSegment next = segments[idx + 1];
            nextSegmentContinues = next.StartSec - lastSegment.EndSec < 1.0;
        }

        result["applicable"] = true;
        result["lastKeptId"] = lastToId;
        result["lastSegmentText"] = lastSegment.Text;
        result["endsAtSentenceBoundary"] = endsAtSentenceBoundary;
        result["nextSegmentContinues"] = nextSegmentContinues;
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
    internal static double? MapSourceToOutputSec(IReadOnlyList<ResolvedSpan> spans, double sourceSec, int sourceIndex = 0)
    {
        double accumulated = 0;
        foreach (ResolvedSpan span in spans)
        {
            if (span.SourceIndex != sourceIndex)
            {
                accumulated += span.SnappedEnd - span.SnappedStart;
                continue;
            }

            if (sourceSec < span.SnappedStart)
                return null; // Falls in this source's own cut gap before this span (or before its first span).

            if (sourceSec < span.SnappedEnd)
                return accumulated + (sourceSec - span.SnappedStart);

            accumulated += span.SnappedEnd - span.SnappedStart;
        }

        return null; // Past the end of this source's last kept span (or this source is never kept at all).
    }

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
        IReadOnlyList<ResolvedSpan> spans, double startSec, double endSec, int sourceIndex = 0)
    {
        if (endSec <= startSec)
            return null;

        double accumulated = 0;
        foreach (ResolvedSpan span in spans)
        {
            if (span.SourceIndex != sourceIndex)
            {
                accumulated += span.SnappedEnd - span.SnappedStart;
                continue;
            }

            double overlapStart = Math.Max(startSec, span.SnappedStart);
            double overlapEnd = Math.Min(endSec, span.SnappedEnd);

            if (overlapEnd > overlapStart)
            {
                double outStart = accumulated + (overlapStart - span.SnappedStart);
                double outEnd = accumulated + (overlapEnd - span.SnappedStart);
                return (outStart, outEnd);
            }

            accumulated += span.SnappedEnd - span.SnappedStart;
        }

        return null; // The window never overlapped any kept span of this source — entirely cut away.
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
            plan = JsonSerializer.Deserialize<MotionGraphicsPlanOutput>(planJson, DecisionJsonOptions);
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
            // moment; Duration (Short/Medium/Hold) controls HOW LONG the overlay stays up,
            // extended from the placement's start and clamped to the owning shot's own bounds —
            // never beyond what Phase 1 actually analyzed for that shot.
            double shotEnd = shotById.TryGetValue(placement.ShotId, out VideoAnalysisShot? shot)
                ? shot.EndSec
                : placement.EndSec;
            double sourceStart = placement.StartSec;
            double sourceEnd = Math.Min(placement.StartSec + durationMs / 1000.0, shotEnd);

            // Multi-source addition: a placement's window is only meaningful on ITS OWN source
            // clip's clock (placement.SourceIndex, resolved server-side from the artifact's own
            // id-space when placements were built — never trusted from the model). Passing it
            // through here is what stops a placement from one clip spuriously mapping against a
            // DIFFERENT clip's kept spans that merely happen to share overlapping numeric ranges.
            (double Start, double End)? outputWindow = MapSourceWindowToOutput(resolvedSpans, sourceStart, sourceEnd, placement.SourceIndex);
            if (outputWindow is null)
            {
                dropped.Add(new DroppedOverlay(overlay.PlacementId, "cut_away"));
                continue;
            }

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

                string assetExtension = Path.GetExtension(overlay.RenderedAssetStorageKey) switch { "" => ".webm", var e => e };
                string localAssetPath = scratch.GetPath($"gfx-asset-{Guid.NewGuid():N}{assetExtension}");
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
                    await _mediaProbe.ProbeAsync(localAssetPath, ct);
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

                renderedAssetLocalPath = localAssetPath;
            }

            resolved.Add(new ResolvedOverlay(
                overlay.PlacementId, overlay.Kind, text, subtext, durationMs, emphasis,
                outputWindow.Value.Start, outputWindow.Value.End, placement.Rect, placement.TextColor,
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
        VideoScratchSpace scratch,
        bool hasDialogueAudio,
        CancellationToken ct)
    {
        var music = new JsonObject { ["enabled"] = true, ["applied"] = false, ["unavailable"] = false };
        List<JsonObject> dropped = new();

        // The frame-quantized edit length — deliberately NOT totalOutputSeconds (the
        // pre-frame-quantization sum, off by up to a frame per span), since a fade-out placed
        // against it could land a frame after the file actually ends.
        double outputTimelineSeconds = resolvedSpans.Sum(s => s.SnappedEnd - s.SnappedStart);

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
                    plan = JsonSerializer.Deserialize<MusicPlanOutput>(planJson, DecisionJsonOptions);
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
        // audio-less, or multi-source with audio dropped across the whole output — see
        // hasDialogueAudio, threaded in from the caller's sourceHasAudio/allSourcesHaveAudio
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
                (start, end, sourceIndex) => MapSourceWindowToOutput(resolvedSpans, start, end, sourceIndex),
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
        bool sourceHasAudio = true)
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

        // Phase 3: partition into the two overlay flavors up front. Text overlays go through the
        // existing DrawtextFilterBuilder path unchanged; asset overlays (a rendered,
        // transparent-background Remotion clip) go through the new OverlayAssetFilterBuilder path.
        // A single compile can contain both flavors at once — a mix is not treated specially,
        // each flavor just runs its own filter-chain stage, chained one after the other.
        List<ResolvedOverlay> textOverlays = overlays?.Where(o => !o.IsAssetOverlay).ToList() ?? [];
        List<ResolvedOverlay> assetOverlays = overlays?.Where(o => o.IsAssetOverlay).ToList() ?? [];

        // Background music (see docs/video-editing.md "Background music"): the music input is
        // deliberately the LAST ffmpeg input, after every asset-overlay input — this is what keeps
        // OverlayAssetFilterBuilder's existing `inputIndexForIndex: i => i + 1` mapping (and every
        // existing filter-string test asserting it) completely untouched by this addition.
        int musicInputIndex = 1 + assetOverlays.Count;

        // The audio cut stage's own output label flips from [aout] straight to [adial] only when
        // music is present, mirroring the [vcut]/[vtxt]/[vout] video-label-chaining convention
        // Phase 3 already established. When music is null this whole audio branch is
        // byte-identical to the pre-music compile path.
        //
        // sourceHasAudio=false (the source clip has no audio stream at all — real, not
        // hypothetical: free stock B-roll routinely ships video-only) means "[0:a]" would fail
        // ffmpeg outright ("Stream specifier ':a' ... matches no streams"), so that whole branch
        // is skipped. With no music either, audioPart is null and the output has no audio track
        // at all (map/-c:a below become conditional on this). With music, there is nothing to
        // duck against, so the music branch's own output becomes [aout] directly — it is the
        // entire output audio, not mixed with anything.
        string audioCutLabel = music is not null ? "[adial]" : "[aout]";
        string musicAwareAudioFilter = music is not null
            ? $"{audioFilter},aformat=sample_rates=48000:channel_layouts=stereo"
            : audioFilter;
        string? audioPart = !sourceHasAudio
            ? (music is null ? null : MusicMixFilterBuilder.BuildMusicBranch(musicInputIndex, music, outLabel: "[aout]"))
            : (music is null
                ? $"[0:a]{audioFilter}[aout]"
                : $"[0:a]{musicAwareAudioFilter}{audioCutLabel};" +
                  MusicMixFilterBuilder.BuildMusicBranch(musicInputIndex, music) + ";" +
                  MusicMixFilterBuilder.BuildMixStage(audioCutLabel));
        bool hasAudioOutput = audioPart is not null;

        string filterComplex;
        if ((textOverlays.Count > 0 || assetOverlays.Count > 0) && graphicsConfig is not null)
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

            if (textOverlays.Count > 0)
            {
                // When asset overlays also follow, this stage ends at an internal [vtxt] label
                // instead of [vout] directly, so the asset stage below can chain after it and
                // become the actual [vout] itself.
                string textStageFinalLabel = assetOverlays.Count > 0 ? "[vtxt]" : "[vout]";
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
                    finalLabel: "[vout]",
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
                ? $"[0:v]{videoFilter}[vout];{audioPart}"
                : $"[0:v]{videoFilter}[vout]";
        }

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
        if (spans.Count > FilterComplexScriptThreshold || ((hasOverlays || hasMusic) && filterComplex.Length > 4000))
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
    /// identical parameters, then concatenated in Keep order.
    /// </summary>
    private async Task<VideoToolResult> EncodeReencodeMultiSourceAsync(
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
        bool allSourcesHaveAudio = true)
    {
        // Deterministic ffmpeg -i order: sorted distinct source indices actually referenced. Input
        // 0 is not necessarily "the" primary source here (that's canonicalMedia's own index,
        // separately tracked) — it is simply whichever referenced source sorts first.
        List<int> orderedSourceIndices = localPathBySource.Keys.OrderBy(i => i).ToList();
        Dictionary<int, int> ffmpegInputIndexBySource = orderedSourceIndices
            .Select((sourceIdx, inputIdx) => (sourceIdx, inputIdx))
            .ToDictionary(t => t.sourceIdx, t => t.inputIdx);

        // libx264/libx265 require even dimensions; clamp the canonical target defensively even
        // though a real ffprobe'd width/height is virtually always already even.
        int cw = canonicalMedia.Width > 0 ? canonicalMedia.Width : 1920;
        int ch = canonicalMedia.Height > 0 ? canonicalMedia.Height : 1080;
        cw -= cw % 2;
        ch -= ch % 2;
        int cfpsNum = canonicalMedia.FpsNum > 0 ? canonicalMedia.FpsNum : 30;
        int cfpsDen = canonicalMedia.FpsDen > 0 ? canonicalMedia.FpsDen : 1;

        var filterParts = new List<string>();
        var concatInputLabels = new StringBuilder();
        for (int i = 0; i < spans.Count; i++)
        {
            ResolvedSpan span = spans[i];
            int ffInputIdx = ffmpegInputIndexBySource[span.SourceIndex];
            string ss = FfmpegArgvFormat.Number(span.SnappedStart);
            string ee = FfmpegArgvFormat.Number(span.SnappedEnd);

            // Time-based (not frame-based) trim, deliberately mirroring the single-source path's
            // own time-based `gte(t,...)*lt(t,...)` select terms exactly — SnappedStart/SnappedEnd
            // are already frame-quantized seconds, so this reproduces the same frame-accurate
            // boundary without depending on StartFrame/EndFrame staying meaningful when a source's
            // fps could not be determined (ResolvedSpan reports 0/0 for both in that case).
            filterParts.Add(
                $"[{ffInputIdx}:v]trim=start={ss}:end={ee},setpts=PTS-STARTPTS," +
                $"scale={cw}:{ch}:force_original_aspect_ratio=decrease,pad={cw}:{ch}:(ow-iw)/2:(oh-ih)/2:color=black," +
                $"setsar=1,fps={cfpsNum}/{cfpsDen}[v{i}]");

            // allSourcesHaveAudio=false: at least one referenced clip has no audio stream at all
            // (real B-roll routinely ships video-only), which would make "[N:a]" fail ffmpeg
            // outright for that input. concat's own "a=" stream count must be uniform across every
            // concatenated segment, so mixing audio-having and audio-less segments in one concat
            // isn't an option here — the simplest correct behavior is to drop audio for the WHOLE
            // compiled output whenever any one source lacks it, exactly mirroring the single-source
            // path's per-clip fallback rather than attempting to synthesize matching silence.
            if (allSourcesHaveAudio)
            {
                filterParts.Add(
                    $"[{ffInputIdx}:a]atrim=start={ss}:end={ee},asetpts=PTS-STARTPTS," +
                    "aformat=sample_rates=48000:channel_layouts=stereo" +
                    $"[a{i}]");

                concatInputLabels.Append($"[v{i}][a{i}]");
            }
            else
            {
                concatInputLabels.Append($"[v{i}]");
            }
        }

        List<ResolvedOverlay> textOverlays = overlays?.Where(o => !o.IsAssetOverlay).ToList() ?? [];
        List<ResolvedOverlay> assetOverlays = overlays?.Where(o => o.IsAssetOverlay).ToList() ?? [];
        bool hasOverlays = textOverlays.Count > 0 || assetOverlays.Count > 0;

        // Same [vcut]-vs-[vout] labeling convention EncodeReencodeAsync uses: the concat stage
        // outputs straight to [vout] when there is nothing further to draw, or to an internal
        // [vcat] label that the overlay chain below continues from when there is.
        string videoConcatLabel = hasOverlays ? "[vcat]" : "[vout]";

        // Background music (see docs/video-editing.md "Background music"): mirrors
        // EncodeReencodeAsync's own [aout]-vs-[adial] flip. No extra aformat is needed here on the
        // dialogue side — every per-span atrim branch above already ends in
        // aformat=sample_rates=48000:channel_layouts=stereo, so the concat output already matches
        // the music branch's own format by construction.
        string? audioConcatLabel = allSourcesHaveAudio ? (music is not null ? "[adial]" : "[aout]") : null;
        filterParts.Add(allSourcesHaveAudio
            ? $"{concatInputLabels}concat=n={spans.Count}:v=1:a=1{videoConcatLabel}{audioConcatLabel}"
            : $"{concatInputLabels}concat=n={spans.Count}:v=1:a=0{videoConcatLabel}");

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

            string currentLabel = videoConcatLabel;
            if (textOverlays.Count > 0)
            {
                string textStageFinalLabel = assetOverlays.Count > 0 ? "[vtxt]" : "[vout]";
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
                    finalLabel: "[vout]",
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
            if (allSourcesHaveAudio)
            {
                filterParts.Add(MusicMixFilterBuilder.BuildMusicBranch(musicInputIndex, music));
                filterParts.Add(MusicMixFilterBuilder.BuildMixStage(audioConcatLabel!));
            }
            else
            {
                // No dialogue anywhere in this compile to duck against — the music branch's own
                // output becomes [aout] directly, exactly as the single-source path does.
                filterParts.Add(MusicMixFilterBuilder.BuildMusicBranch(musicInputIndex, music, outLabel: "[aout]"));
            }
        }

        bool hasAudioOutput = allSourcesHaveAudio || music is not null;
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
        JsonObject? audio = null)
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

        // Bug group C: unlike graphics/music, always present — audio isn't opt-in the way those
        // phases are, so a dropped audio stream is never silently unreported.
        if (audio is not null)
            edl["audio"] = audio;

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
