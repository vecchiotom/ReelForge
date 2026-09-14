using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Inference;
using ReelForge.Shared.Workflows;
using ReelForge.WorkflowEngine.Services.Storage;
using ReelForge.WorkflowEngine.Services.Video;

namespace ReelForge.WorkflowEngine.Execution.StepExecutors;

/// <summary>
/// Executes <see cref="StepType.VideoAnalyze"/> steps: deterministic, non-LLM derushing of a
/// source video into shots/silence gaps/(optional) transcript, plus (Phase 1) deterministic
/// visual/audio scene descriptors, persisted as a full analysis artifact plus a bounded,
/// id-anchored "{view, meta}" prompt envelope for the downstream VideoStoryEditor agent step.
/// See plan §4.1/§3 and docs/video-editing.md "Scene/visual analysis (Phase 1)".
///
/// Purity/safety discipline mirrors <see cref="ExtractStepExecutor"/> exactly: this executor
/// never throws — every path, including an unexpected exception, returns a
/// <see cref="StepExecutionResult"/> whose Output is valid JSON (WorkflowStepResult.OutputJson is
/// a jsonb column). The one piece of network I/O (ASR) gets its own small bounded retry
/// internally; the whole step is never retried by the outer executor-retry mechanism
/// (see WorkflowExecutorService.ResolveMaxRetries) since re-running it just reproduces the same
/// deterministic failure at the cost of minutes of decode. Phase 1's visual/audio-level analysis
/// stages follow the exact same "degrade, never fail the step" discipline as transcription: each
/// gets its own try/catch that can never let an exception escape, and on failure the step
/// continues with shots/silences/transcript exactly as if that stage were off.
/// </summary>
public class VideoAnalyzeStepExecutor : IStepExecutor
{
    // Same convention as ExtractStepExecutor: enum JSON values are exact PascalCase C# member
    // names; property names follow JsonSerializerDefaults.Web (camelCase).
    private static readonly JsonSerializerOptions ConfigJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions ArtifactJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions EnvelopeJsonOptions = new(JsonSerializerDefaults.Web);

    private const int AsrMaxAttempts = 3;
    private const int AudioWindowMs = 250;

    /// <summary>Per-shot captioning retry count — mirrors <see cref="AsrMaxAttempts"/>'s "own small bounded retry" shape but shallower (2, not 3): a captioning failure only costs one shot, not the whole step.</summary>
    private const int VisionMaxAttempts = 2;

    private readonly IMediaProbe _mediaProbe;
    private readonly ISilenceDetector _silenceDetector;
    private readonly IShotDetector _shotDetector;
    private readonly IAudioExtractor _audioExtractor;
    private readonly IFrameGridSampler _frameGridSampler;
    private readonly IKeyframeExtractor _keyframeExtractor;
    private readonly IShotCaptioner _shotCaptioner;
    private readonly IProjectFileWorkspace _workspace;
    private readonly ITranscriptionClientFactory _transcriptionClientFactory;
    private readonly IInferenceProviderResolver _providerResolver;
    private readonly VideoEditingOptions _options;
    private readonly ILogger<VideoAnalyzeStepExecutor> _logger;

    public VideoAnalyzeStepExecutor(
        IMediaProbe mediaProbe,
        ISilenceDetector silenceDetector,
        IShotDetector shotDetector,
        IAudioExtractor audioExtractor,
        IFrameGridSampler frameGridSampler,
        IKeyframeExtractor keyframeExtractor,
        IShotCaptioner shotCaptioner,
        IProjectFileWorkspace workspace,
        ITranscriptionClientFactory transcriptionClientFactory,
        IInferenceProviderResolver providerResolver,
        IOptions<VideoEditingOptions> options,
        ILogger<VideoAnalyzeStepExecutor> logger)
    {
        _mediaProbe = mediaProbe;
        _silenceDetector = silenceDetector;
        _shotDetector = shotDetector;
        _audioExtractor = audioExtractor;
        _frameGridSampler = frameGridSampler;
        _keyframeExtractor = keyframeExtractor;
        _shotCaptioner = shotCaptioner;
        _workspace = workspace;
        _transcriptionClientFactory = transcriptionClientFactory;
        _providerResolver = providerResolver;
        _options = options.Value;
        _logger = logger;
    }

    public StepType StepType => StepType.VideoAnalyze;

    public async Task<StepExecutionResult> ExecuteAsync(StepExecutionContext context)
    {
        Stopwatch sw = Stopwatch.StartNew();
        WorkflowStep step = context.Step;
        VideoScratchSpace? scratch = null;

        try
        {
            VideoAnalyzeStepConfig? config;
            try
            {
                if (string.IsNullOrWhiteSpace(step.VideoAnalyzeConfigJson))
                    return Failure(context, sw, "CONFIG_INVALID", "VideoAnalyze step has no VideoAnalyzeConfigJson configured.");

                config = JsonSerializer.Deserialize<VideoAnalyzeStepConfig>(step.VideoAnalyzeConfigJson, ConfigJsonOptions);
            }
            catch (JsonException ex)
            {
                return Failure(context, sw, "CONFIG_INVALID", $"VideoAnalyzeConfigJson is not valid JSON: {ex.Message}");
            }

            if (config is null)
                return Failure(context, sw, "CONFIG_INVALID", "VideoAnalyzeConfigJson deserialized to null.");

            (string? storageKey, string? resolveError) = await ResolveSourceStorageKeyAsync(context, config.Source);
            if (storageKey is null)
                return Failure(context, sw, "SOURCE_UNRESOLVED", resolveError ?? "Could not resolve the video source.");

            context.RecordResolvedInput(BuildResolvedInputDescriptor(config, storageKey));

            scratch = VideoScratchSpace.Create(_options, context.Execution.Id, step.Id, _logger);
            string localVideoPath = scratch.GetPath("source" + GuessExtension(storageKey));

            await _workspace.DownloadStorageKeyToFileAsync(
                context.Execution.ProjectId, storageKey, localVideoPath, context.CancellationToken);

            // Cheapest guardrail we can actually apply given IProjectFileWorkspace's surface (no
            // HEAD/size-without-download primitive): check the downloaded size against
            // MaxInputBytes before any decode (probe/silence/shot/ASR) runs.
            // TODO: Ideal fix would check size BEFORE download via a lightweight HEAD-style API
            // (e.g., GetObjectMetadataAsync), but IProjectFileWorkspace does not expose such a method.
            // A pre-download guard would require adding that API to the storage abstraction layer.
            long fileSizeBytes = new FileInfo(localVideoPath).Length;
            if (fileSizeBytes > config.MaxInputBytes)
            {
                return Failure(
                    context, sw, "INPUT_TOO_LARGE",
                    $"Source video is {fileSizeBytes} bytes, exceeding MaxInputBytes={config.MaxInputBytes}.");
            }

            MediaProbeResult probe = await _mediaProbe.ProbeAsync(localVideoPath, context.CancellationToken);

            if (config.MaxDurationSeconds > 0 && probe.DurationSec > config.MaxDurationSeconds)
            {
                return Failure(
                    context, sw, "DURATION_EXCEEDED",
                    $"Source video duration {probe.DurationSec:F2}s exceeds MaxDurationSeconds={config.MaxDurationSeconds}. " +
                    "Bailing out before audio extraction/ASR.");
            }

            IReadOnlyList<(double StartSec, double EndSec)> silenceSpans = Array.Empty<(double, double)>();
            if (config.DetectSilence)
            {
                silenceSpans = await _silenceDetector.DetectAsync(
                    localVideoPath, config.SilenceThresholdDb, config.MinSilenceMs / 1000.0, probe.DurationSec, context.CancellationToken);
            }

            IReadOnlyList<(double StartSec, double EndSec)> shotSpans = Array.Empty<(double, double)>();
            if (config.DetectShots)
            {
                shotSpans = await _shotDetector.DetectShotsAsync(
                    localVideoPath, config.SceneThreshold, probe.DurationSec, context.CancellationToken);
            }

            TranscriptResult? transcript = null;
            bool transcriptionApplied = false;
            bool transcriptionDegraded = false;
            string? transcriptionProviderName = null;

            if (config.Transcription != VideoTranscriptionMode.Off)
            {
                try
                {
                    ResolvedTranscriptionProvider? provider =
                        await _providerResolver.ResolveTranscriptionAsync(config.TranscriptionProviderId, context.CancellationToken);

                    if (provider is null)
                    {
                        if (config.Transcription == VideoTranscriptionMode.Required)
                        {
                            return Failure(
                                context, sw, "TRANSCRIPTION_UNAVAILABLE",
                                "Transcription is Required but no transcription-capable inference provider is configured.");
                        }

                        _logger.LogInformation(
                            "VideoAnalyze step {StepOrder}: no transcription provider resolved; degrading (Transcription=Optional).",
                            step.StepOrder);
                        transcriptionDegraded = true;
                    }
                    else
                    {
                        transcript = await TranscribeWithChunkingAsync(
                            scratch, localVideoPath, probe, silenceSpans, config, provider, context.CancellationToken);
                        transcriptionApplied = true;
                        transcriptionProviderName = provider.Name;
                    }
                }
                catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "VideoAnalyze step {StepOrder}: transcription failed", step.StepOrder);
                    if (config.Transcription == VideoTranscriptionMode.Required)
                        return Failure(context, sw, "TRANSCRIPTION_FAILED", $"Transcription failed: {ex.Message}");

                    transcriptionDegraded = true;
                }
            }

            // ---- Assign deterministic ids by index and build the shot list ----

            List<VideoAnalysisShot> shots = shotSpans
                .Select((s, i) => new VideoAnalysisShot($"s{i}", s.StartSec, s.EndSec))
                .ToList();

            // ---- Phase 1: audio-level sampling (reuses the WAV already extracted for
            // transcription, or extracts it fresh) — never lets an exception escape the step. ----
            bool audioLevelsApplied = false;
            if (config.AnalyzeAudioLevels)
            {
                try
                {
                    string fullWavPath = scratch.GetPath("audio-full.wav");
                    if (!File.Exists(fullWavPath))
                    {
                        await _audioExtractor.ExtractWavAsync(localVideoPath, fullWavPath, context.CancellationToken);
                    }

                    byte[] wavBytes = await File.ReadAllBytesAsync(fullWavPath, context.CancellationToken);
                    IReadOnlyList<WavRmsSampler.RmsWindow> windows = WavRmsSampler.Sample(wavBytes, AudioWindowMs);

                    for (int i = 0; i < shots.Count; i++)
                    {
                        VideoAnalysisShotAudio? audio = ComputeShotAudio(shots[i], windows, silenceSpans);
                        if (audio is not null)
                            shots[i] = shots[i] with { Audio = audio };
                    }

                    audioLevelsApplied = true;
                }
                catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "VideoAnalyze step {StepOrder}: audio-level sampling failed; degrading.", step.StepOrder);
                    audioLevelsApplied = false;
                }
            }

            // ---- Phase 1: visual scene analysis — one grid ffmpeg pass + pure C# analyzer.
            // The ENTIRE block (grid sampling + FrameGridAnalyzer calls) is wrapped so an
            // exception anywhere in here can never fail the step; shots/silences/transcript are
            // used exactly as if this stage were off. ----
            bool visualApplied = false;
            bool visualDegraded = false;
            List<VideoAnalysisDuplicateGroup> duplicateGroups = [];

            // Clamped here, at the point config is consumed — same convention as
            // VideoCompileStepExecutor's Crf/MaxSegments clamps — so a workflow-author-supplied
            // config (e.g. 2048x2048) can't allocate an enormous grid.rgb scratch file or force a
            // huge single in-memory byte[] read of it (Item D cleanup). Computed unconditionally
            // (not just when AnalyzeVisuals) so the "visual" meta block below always reports the
            // grid dimensions that would actually be used.
            int gridWidth = Math.Clamp(config.VisualGridWidth, 8, 256);
            int gridHeight = Math.Clamp(config.VisualGridHeight, 8, 256);

            if (config.AnalyzeVisuals)
            {
                try
                {
                    FrameGridResult grid = await _frameGridSampler.SampleAsync(
                        localVideoPath, scratch, config.VisualSampleFps, gridWidth, gridHeight,
                        probe.DurationSec, config.MaxVisualSampleFrames, context.CancellationToken);

                    if (grid.FrameCount <= 0)
                        throw new InvalidOperationException("Grid sampler produced zero sampled frames.");

                    var analyzerOptions = new FrameGridAnalyzer.Options(
                        config.StillMotionThreshold, config.MinStillWindowMs, config.MaxStillWindowsPerShot);

                    var signatures = new List<FrameGridAnalyzer.ShotSignature>();
                    var signatureShotIds = new List<string>();
                    var takeQualities = new List<double>();

                    for (int i = 0; i < shots.Count; i++)
                    {
                        VideoAnalysisShot shot = shots[i];
                        List<byte[]> frames = FrameGridAnalyzer.SliceShotFrames(
                            grid.PixelData, grid.FrameCount, grid.GridWidth, grid.GridHeight, grid.EffectiveFps,
                            shot.StartSec, shot.EndSec, out int startFrameIndex);

                        // Absolute source-timeline time of frames[0] — NOT necessarily shot.StartSec,
                        // since grid sample times are quantized to i/fps (see SliceShotFrames). Needed
                        // so AnalyzeShot reports StillWindows in absolute seconds, not shot-relative.
                        double shotStartOffsetSec = grid.EffectiveFps > 0
                            ? startFrameIndex / grid.EffectiveFps
                            : shot.StartSec;

                        VideoAnalysisShotVisual visual = FrameGridAnalyzer.AnalyzeShot(
                            frames, grid.GridWidth, grid.GridHeight, grid.EffectiveFps, analyzerOptions,
                            shotStartOffsetSec);

                        shots[i] = shot with { Visual = visual };

                        if (config.DetectNearDuplicates)
                        {
                            signatures.Add(FrameGridAnalyzer.ComputeSignature(frames, grid.GridWidth, grid.GridHeight));
                            signatureShotIds.Add(shot.Id);
                            takeQualities.Add(FrameGridAnalyzer.ComputeTakeQuality(
                                visual.MotionStdDev, shots[i].Audio?.RmsDbfs, visual.BrightnessMean, shot.EndSec - shot.StartSec));
                        }
                    }

                    if (config.DetectNearDuplicates && signatures.Count > 0)
                    {
                        IReadOnlyList<VideoAnalysisDuplicateGroup> groups = FrameGridAnalyzer.GroupDuplicates(
                            signatureShotIds, signatures, takeQualities, config.DuplicateSimilarityThreshold,
                            config.DuplicateWindowShots,
                            out IReadOnlyDictionary<string, (string GroupId, int GroupRank, bool IsBestTake)> assignments);

                        for (int i = 0; i < shots.Count; i++)
                        {
                            if (shots[i].Visual is null || !assignments.TryGetValue(shots[i].Id, out (string GroupId, int GroupRank, bool IsBestTake) a))
                                continue;

                            shots[i] = shots[i] with
                            {
                                Visual = shots[i].Visual! with
                                {
                                    DuplicateGroupId = a.GroupId,
                                    GroupRank = a.GroupRank,
                                    IsBestTake = a.IsBestTake
                                }
                            };
                        }

                        duplicateGroups = groups.ToList();
                    }

                    visualApplied = true;
                }
                catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "VideoAnalyze step {StepOrder}: visual analysis failed; degrading.", step.StepOrder);
                    visualDegraded = true;
                }
            }

            // ---- Phase 2: vision-LLM shot captioning — runs LAST among analysis stages, after
            // every deterministic stage above, so a vision failure can never put anything
            // deterministic at risk. Off by default (VideoVisionMode.Off); see
            // VideoVisionMode's doc comment for why this deviates from Transcription's
            // Optional-by-default. ----
            bool visionApplied = false;
            bool visionDegraded = false;
            bool visionPartial = false;
            string? visionProviderName = null;
            int captionedShotCount = 0;
            int failedShotCount = 0;

            if (config.Vision != VideoVisionMode.Off)
            {
                try
                {
                    ResolvedInferenceProvider? visionProvider =
                        await _providerResolver.ResolveVisionAsync(config.VisionProviderId, context.CancellationToken);

                    if (visionProvider is null)
                    {
                        if (config.Vision == VideoVisionMode.Required)
                        {
                            return Failure(
                                context, sw, "VISION_UNAVAILABLE",
                                "Vision is Required but no vision-capable inference provider is configured.");
                        }

                        _logger.LogInformation(
                            "VideoAnalyze step {StepOrder}: no vision provider resolved; degrading (Vision=Optional).",
                            step.StepOrder);
                        visionDegraded = true;
                    }
                    else
                    {
                        visionProviderName = visionProvider.Name;

                        IReadOnlyList<string> selectedShotIds = KeyframeSelector.SelectShotsToCaption(
                            shots, duplicateGroups.Count > 0 ? duplicateGroups : null,
                            config.CaptionSelection, config.MaxCaptionedShots, config.MinCaptionShotSeconds);

                        if (selectedShotIds.Count > 0)
                        {
                            Dictionary<string, int> shotIndexById = shots
                                .Select((s, i) => (s.Id, i))
                                .ToDictionary(t => t.Id, t => t.i);

                            using CancellationTokenSource visionCts =
                                CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
                            visionCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, config.VisionTimeoutSeconds)));
                            CancellationToken visionCt = visionCts.Token;

                            foreach (string shotId in selectedShotIds)
                            {
                                if (visionCts.IsCancellationRequested)
                                {
                                    if (!context.CancellationToken.IsCancellationRequested)
                                        visionPartial = true;
                                    break;
                                }

                                if (!shotIndexById.TryGetValue(shotId, out int shotIdx))
                                    continue;

                                try
                                {
                                    VideoAnalysisShot shot = shots[shotIdx];
                                    double atSec = KeyframeSelector.ChooseKeyframeSec(shot);
                                    string keyframePath = scratch.GetPath($"keyframe-{shot.Id}.jpg");

                                    await _keyframeExtractor.ExtractKeyframeAsync(
                                        localVideoPath, keyframePath, atSec, config.KeyframeMaxWidth, visionCt);

                                    ShotCaptionRequest request = new(shot.Id, keyframePath, atSec);
                                    VideoShotCaption caption = await CaptionWithRetryAsync(
                                        visionProvider, request, config.MaxCaptionChars, visionCt);

                                    // SAFETY (plan §3): the shot-id <-> caption binding is never
                                    // model-controlled. Overwrite ShotId with the id we actually
                                    // requested — never trust whatever the model echoed back — before
                                    // attaching the caption to this shot.
                                    shots[shotIdx] = shot with { Caption = caption with { ShotId = shot.Id } };
                                    captionedShotCount++;
                                }
                                catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
                                {
                                    throw;
                                }
                                catch (OperationCanceledException)
                                {
                                    // The aggregate VisionTimeoutSeconds budget (not the outer
                                    // execution's own CancellationToken) fired mid-call: stop
                                    // captioning further shots but keep what already succeeded.
                                    visionPartial = true;
                                    break;
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(
                                        ex, "VideoAnalyze step {StepOrder}: captioning shot {ShotId} failed after retries; skipping.",
                                        step.StepOrder, shotId);
                                    failedShotCount++;
                                }
                            }
                        }

                        visionApplied = captionedShotCount > 0;
                        if (!visionApplied)
                        {
                            if (config.Vision == VideoVisionMode.Required)
                            {
                                return Failure(
                                    context, sw, "VISION_FAILED",
                                    "Vision is Required but captioning did not produce any shot captions.");
                            }

                            visionDegraded = true;
                        }
                    }
                }
                catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "VideoAnalyze step {StepOrder}: vision captioning setup failed; degrading.", step.StepOrder);
                    if (config.Vision == VideoVisionMode.Required)
                        return Failure(context, sw, "VISION_FAILED", $"Vision captioning failed: {ex.Message}");

                    visionDegraded = true;
                }
            }

            // ---- Phase 3: deterministic overlay-placement candidates (see
            // docs/video-editing.md "Motion graphics (Phase 3)") — derived entirely from Phase
            // 1's per-shot Visual/Regions data, so only computed when visual analysis actually
            // succeeded (never when it degraded/was off). Purely additive: off by default. ----
            List<VideoAnalysisPlacement> placements = config.EmitOverlayPlacements && visualApplied
                ? OverlayPlacementBuilder.BuildPlacements(shots, config.MaxPlacementsPerShot, config.MaxPlacements).ToList()
                : [];

            VideoAnalysisPacing pacing = FrameGridAnalyzer.ComputePacing(shots, probe.DurationSec);

            List<VideoAnalysisSilenceSpan> silences = silenceSpans
                .Select((s, i) => new VideoAnalysisSilenceSpan($"g{i}", s.StartSec, s.EndSec, FindAfterShot(shots, s.StartSec)))
                .ToList();

            List<VideoAnalysisSegment> segments = (transcript?.Segments ?? (IReadOnlyList<TranscriptSegment>)Array.Empty<TranscriptSegment>())
                .Select((seg, i) => new VideoAnalysisSegment($"t{i}", FindShotFor(shots, seg.StartSec), seg.StartSec, seg.EndSec, seg.Text))
                .ToList();

            List<VideoAnalysisWord> words = (transcript?.Words ?? (IReadOnlyList<TranscriptWord>)Array.Empty<TranscriptWord>())
                .Select((w, i) => new VideoAnalysisWord($"w{i}", w.StartSec, w.EndSec, w.Text))
                .ToList();

            // A preliminary artifact to hand to BuildBoundedView below (it reads Shots/SilenceSpans/
            // Media/counts/Pacing/DuplicateGroups only, never OfferedIds, so a placeholder here is
            // safe — see the corrected artifact constructed after the view is built).
            VideoAnalysisArtifact draftArtifact = new(
                Version: 2,
                Media: new VideoAnalysisMedia(probe.DurationSec, probe.FpsNum, probe.FpsDen, probe.Width, probe.Height),
                Shots: shots,
                SilenceSpans: silences,
                Segments: segments,
                Words: words,
                OfferedIds: [],
                Provenance: new VideoAnalysisProvenance(
                    TranscriptionMode: config.Transcription,
                    TranscriptionApplied: transcriptionApplied,
                    TranscriptionDegraded: transcriptionDegraded,
                    TranscriptionProvider: transcriptionProviderName,
                    TranscriptionLanguage: config.Language,
                    AnalyzedAt: DateTime.UtcNow,
                    VisualAnalysisApplied: visualApplied,
                    VisualAnalysisDegraded: visualDegraded,
                    AudioLevelsApplied: audioLevelsApplied,
                    SharpnessAvailable: false,
                    VisionMode: config.Vision,
                    VisionApplied: visionApplied,
                    VisionDegraded: visionDegraded,
                    VisionProvider: visionProviderName,
                    CaptionedShotCount: captionedShotCount,
                    VisionPartial: visionPartial),
                DuplicateGroups: duplicateGroups.Count > 0 ? duplicateGroups : null,
                Pacing: pacing,
                Placements: placements.Count > 0 ? placements : null,
                OfferedPlacementIds: []);

            // ---- Build the bounded, id-anchored prompt view FIRST, so we know exactly which ids
            // were actually shown before persisting the artifact's OfferedIds. ----

            int maxSegmentTextChars = Math.Max(1, config.MaxSegmentTextChars);
            List<VideoAnalysisSegment> viewSegments = segments
                .Select(s => s.Text.Length > maxSegmentTextChars
                    ? s with { Text = s.Text[..maxSegmentTextChars] }
                    : s)
                .ToList();

            // artifactStorageKey isn't known yet (the artifact hasn't been uploaded) — meta's
            // copy is patched in below once it is.
            (JsonObject view, JsonObject meta, List<string> viewOfferedIds, List<string> viewOfferedPlacementIds) = BuildBoundedView(
                draftArtifact, viewSegments, config, artifactStorageKey: string.Empty,
                transcriptionApplied, transcriptionDegraded, transcriptionProviderName,
                visualApplied, visualDegraded, gridWidth, gridHeight, audioLevelsApplied,
                visionApplied, visionDegraded, visionPartial, visionProviderName, captionedShotCount, failedShotCount);

            // The persisted artifact's OfferedIds must be exactly the ids that survived view
            // truncation — VideoCompileStepExecutor validates the model's Keep spans against this
            // list, so persisting the full pre-truncation id set here would let it accept ids the
            // model was never actually shown, defeating the id-anchored contract entirely (found
            // by Copilot review). OfferedPlacementIds gets the exact same discipline, applied to
            // the SEPARATE Phase 3 placement-id namespace (never merged with OfferedIds).
            VideoAnalysisArtifact artifact = draftArtifact with
            {
                OfferedIds = viewOfferedIds,
                OfferedPlacementIds = viewOfferedPlacementIds
            };

            string artifactLocalPath = scratch.GetPath("analysis.json");
            await File.WriteAllTextAsync(
                artifactLocalPath, JsonSerializer.Serialize(artifact, ArtifactJsonOptions), context.CancellationToken);

            string artifactFileName = $"video-analysis/{context.Execution.Id:D}/step-{step.StepOrder}-analysis.json";
            string artifactStorageKey = await _workspace.UploadArtifactAsync(
                context.Execution.ProjectId, artifactLocalPath, artifactFileName, "application/json", context.CancellationToken);

            meta["artifactStorageKey"] = artifactStorageKey;

            string? expectError = EvaluateExpect(config.Expect, artifact);
            if (expectError is not null)
            {
                _logger.LogWarning(
                    "VideoAnalyze step {StepOrder} failed expect check: {Message}", step.StepOrder, expectError);
                return Failure(context, sw, "EXPECT_FAILED", expectError, artifactStorageKey);
            }

            context.RecordResolvedInput(BuildResolvedInputDescriptor(config, storageKey, viewOfferedIds.Count));

            var envelope = new JsonObject { ["view"] = view, ["meta"] = meta };

            return new StepExecutionResult
            {
                Output = envelope.ToJsonString(EnvelopeJsonOptions),
                NextStepIndex = context.CurrentStepIndex + 1,
                NewIterationCount = context.IterationCount,
                DurationMs = sw.ElapsedMilliseconds,
                TokensUsed = 0,
                Status = StepStatus.Completed,
                ArtifactStorageKey = artifactStorageKey
            };
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VideoAnalyze step {StepOrder} failed unexpectedly", step.StepOrder);
            return Failure(context, sw, "UNEXPECTED_ERROR", $"Unexpected error: {ex.Message}");
        }
        finally
        {
            scratch?.Dispose();
        }
    }

    // ---------------------------------------------------------------------
    // Source resolution
    // ---------------------------------------------------------------------

    private async Task<(string? Key, string? Error)> ResolveSourceStorageKeyAsync(
        StepExecutionContext context, VideoSourceRef source)
    {
        switch (source.Kind)
        {
            case VideoSourceKind.PreviousStepOutput:
            {
                StepOutputHistoryEntry? entry = context.StepOutputHistory
                    .LastOrDefault(h => !string.IsNullOrWhiteSpace(h.OutputStorageKey));
                return entry is null
                    ? (null, "Source=PreviousStepOutput but no prior step in this execution produced a video/media OutputStorageKey.")
                    : (entry.OutputStorageKey, null);
            }

            case VideoSourceKind.StepOutput:
            {
                if (!source.StepOrder.HasValue)
                    return (null, "Source=StepOutput requires StepOrder.");

                StepOutputHistoryEntry? entry = context.StepOutputHistory
                    .FirstOrDefault(h => h.StepOrder == source.StepOrder.Value);
                return entry is null || string.IsNullOrWhiteSpace(entry.OutputStorageKey)
                    ? (null, $"Step {source.StepOrder.Value} did not produce a video/media OutputStorageKey.")
                    : (entry.OutputStorageKey, null);
            }

            case VideoSourceKind.ProjectFile:
            {
                if (!source.ProjectFileId.HasValue)
                    return (null, "Source=ProjectFile requires ProjectFileId.");

                IReadOnlyList<ProjectWorkspaceFile> files =
                    await _workspace.ListFilesAsync(context.Execution.ProjectId, context.CancellationToken);
                ProjectWorkspaceFile? file = files.FirstOrDefault(f => f.Id == source.ProjectFileId.Value);
                return file is null
                    ? (null, $"ProjectFile '{source.ProjectFileId.Value}' was not found in this project.")
                    : (file.StorageKey, null);
            }

            default:
                return (null, $"Unknown VideoSourceKind '{source.Kind}'.");
        }
    }

    private static string GuessExtension(string storageKey)
    {
        string ext = Path.GetExtension(storageKey);
        return string.IsNullOrWhiteSpace(ext) ? ".bin" : ext;
    }

    // ---------------------------------------------------------------------
    // Transcription (chunking + absolute-time offsetting)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Extracts the full audio track, plans ASR chunks via <see cref="TranscriptChunkPlanner"/>
    /// when the track exceeds <see cref="VideoAnalyzeStepConfig.MaxAsrChunkBytes"/>, transcribes
    /// each chunk, and offsets every returned word/segment timestamp by that chunk's absolute
    /// start time before concatenating — this offset step is the single most likely correctness
    /// bug in this feature (plan §3/§7 R10) and is covered by
    /// <c>VideoAnalyzeStepExecutorTests.Transcription_offsets_chunk_relative_timestamps_to_absolute</c>.
    /// </summary>
    private async Task<TranscriptResult> TranscribeWithChunkingAsync(
        VideoScratchSpace scratch,
        string localVideoPath,
        MediaProbeResult probe,
        IReadOnlyList<(double StartSec, double EndSec)> silenceSpans,
        VideoAnalyzeStepConfig config,
        ResolvedTranscriptionProvider provider,
        CancellationToken ct)
    {
        string fullWavPath = scratch.GetPath("audio-full.wav");
        await _audioExtractor.ExtractWavAsync(localVideoPath, fullWavPath, ct);
        long wavBytes = new FileInfo(fullWavPath).Length;

        IReadOnlyList<(double StartSec, double EndSec)> chunks = TranscriptChunkPlanner.PlanChunks(
            probe.DurationSec, wavBytes, silenceSpans, config.MaxAsrChunkBytes);

        ITranscriptionClient client = _transcriptionClientFactory.Get(provider);

        List<TranscriptSegment> allSegments = new();
        List<TranscriptWord> allWords = new();
        StringBuilder allText = new();

        if (chunks.Count <= 1)
        {
            await using FileStream stream = new(fullWavPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            TranscriptResult result = await TranscribeWithRetryAsync(
                client, stream, "audio.wav", config.Language, config.WordTimestamps, ct);

            // Single chunk covers [0, duration) — its "chunk-relative" timestamps already ARE
            // absolute, so no offset is applied (offset would be +0 anyway).
            allSegments.AddRange(result.Segments);
            allWords.AddRange(result.Words);
            allText.Append(result.Text);
        }
        else
        {
            int chunkIndex = 0;
            foreach ((double chunkStartSec, double chunkEndSec) in chunks)
            {
                ct.ThrowIfCancellationRequested();

                string chunkPath = scratch.GetPath($"audio-chunk-{chunkIndex}.wav");
                await _audioExtractor.ExtractWavRangeAsync(localVideoPath, chunkPath, chunkStartSec, chunkEndSec, ct);

                await using FileStream chunkStream = new(chunkPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                TranscriptResult chunkResult = await TranscribeWithRetryAsync(
                    client, chunkStream, $"chunk-{chunkIndex}.wav", config.Language, config.WordTimestamps, ct);

                // THE offset step (plan §3/R10): every timestamp the ASR backend returned is
                // relative to the start of THIS chunk's audio bytes. Absolute time in the
                // original track = chunkStartSec + chunkRelativeSec.
                foreach (TranscriptSegment seg in chunkResult.Segments)
                    allSegments.Add(new TranscriptSegment(seg.Text, seg.StartSec + chunkStartSec, seg.EndSec + chunkStartSec));
                foreach (TranscriptWord w in chunkResult.Words)
                    allWords.Add(new TranscriptWord(w.Text, w.StartSec + chunkStartSec, w.EndSec + chunkStartSec));

                if (allText.Length > 0 && chunkResult.Text.Length > 0)
                    allText.Append(' ');
                allText.Append(chunkResult.Text);

                chunkIndex++;
            }
        }

        return new TranscriptResult(allText.ToString(), allSegments, allWords, config.Language);
    }

    /// <summary>
    /// Small bounded retry around only the ASR network call (plan: "ASR's own network call ...
    /// should have its own small bounded retry internally" — distinct from, and instead of, the
    /// outer whole-step executor retry, which is intentionally disabled for this step type).
    /// </summary>
    private static async Task<TranscriptResult> TranscribeWithRetryAsync(
        ITranscriptionClient client, FileStream wav, string fileName, string? language, bool wordTimestamps, CancellationToken ct)
    {
        Exception? last = null;
        for (int attempt = 1; attempt <= AsrMaxAttempts; attempt++)
        {
            try
            {
                if (attempt > 1)
                    wav.Position = 0;

                return await client.TranscribeAsync(wav, fileName, language, wordTimestamps, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < AsrMaxAttempts)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromSeconds(attempt), ct);
            }
        }

        throw last ?? new InvalidOperationException("Transcription failed after retries.");
    }

    // ---------------------------------------------------------------------
    // Phase 2: vision captioning retry (mirrors TranscribeWithRetryAsync's shape, adapted —
    // shallower retry count since a captioning failure only costs one shot, never the whole step;
    // the caller's own catch block is what keeps one shot's exhausted-retry failure from aborting
    // captioning of the rest)
    // ---------------------------------------------------------------------

    private async Task<VideoShotCaption> CaptionWithRetryAsync(
        ResolvedInferenceProvider provider, ShotCaptionRequest request, int maxCaptionChars, CancellationToken ct)
    {
        Exception? last = null;
        for (int attempt = 1; attempt <= VisionMaxAttempts; attempt++)
        {
            try
            {
                return await _shotCaptioner.CaptionAsync(provider, request, maxCaptionChars, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < VisionMaxAttempts)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromSeconds(attempt), ct);
            }
        }

        throw last ?? new InvalidOperationException($"Captioning shot '{request.ShotId}' failed after retries.");
    }

    // ---------------------------------------------------------------------
    // Id linking helpers
    // ---------------------------------------------------------------------

    private static string? FindAfterShot(IReadOnlyList<VideoAnalysisShot> shots, double silenceStartSec) =>
        shots.LastOrDefault(s => s.StartSec <= silenceStartSec)?.Id;

    private static string? FindShotFor(IReadOnlyList<VideoAnalysisShot> shots, double timeSec) =>
        shots.LastOrDefault(s => s.StartSec <= timeSec)?.Id;

    // ---------------------------------------------------------------------
    // Phase 1: per-shot audio levels (energy-weighted mean over overlapping WavRmsSampler windows)
    // ---------------------------------------------------------------------

    private static VideoAnalysisShotAudio? ComputeShotAudio(
        VideoAnalysisShot shot,
        IReadOnlyList<WavRmsSampler.RmsWindow> windows,
        IReadOnlyList<(double StartSec, double EndSec)> silenceSpans)
    {
        double duration = shot.EndSec - shot.StartSec;
        if (duration <= 0)
            return null;

        double energySum = 0, weightSum = 0, peakDbfs = -96.0;
        foreach (WavRmsSampler.RmsWindow w in windows)
        {
            double overlap = Overlap(w.StartSec, w.EndSec, shot.StartSec, shot.EndSec);
            if (overlap <= 0)
                continue;

            double linearEnergy = Math.Pow(10, w.RmsDbfs / 10.0);
            energySum += linearEnergy * overlap;
            weightSum += overlap;
            if (w.PeakDbfs > peakDbfs)
                peakDbfs = w.PeakDbfs;
        }

        if (weightSum <= 0)
            return null;

        double avgEnergy = energySum / weightSum;
        double rmsDbfs = avgEnergy > 0 ? 10 * Math.Log10(avgEnergy) : -96.0;

        double silenceSec = silenceSpans.Sum(s => Overlap(s.StartSec, s.EndSec, shot.StartSec, shot.EndSec));
        double speechRatio = Math.Clamp(1 - silenceSec / duration, 0, 1);

        string loudnessClass = rmsDbfs switch
        {
            > -15 => "Loud",
            > -30 => "Normal",
            _ => "Quiet"
        };

        return new VideoAnalysisShotAudio(rmsDbfs, peakDbfs, speechRatio, loudnessClass);
    }

    private static double Overlap(double aStart, double aEnd, double bStart, double bEnd) =>
        Math.Max(0, Math.Min(aEnd, bEnd) - Math.Max(aStart, bStart));

    // ---------------------------------------------------------------------
    // Bounded view construction (Extract's exact truncation discipline, plus Phase 1's
    // detail-degrades-before-items-drop discipline)
    // ---------------------------------------------------------------------

    private sealed record OfferedItem(
        string Kind, string Id, VideoAnalysisShot? Shot, VideoAnalysisSilenceSpan? Silence, VideoAnalysisSegment? Segment);

    private static List<OfferedItem> BuildOfferedItems(VideoAnalysisArtifact artifact, List<VideoAnalysisSegment> viewSegments)
    {
        var list = new List<OfferedItem>();
        foreach (VideoAnalysisShot s in artifact.Shots)
            list.Add(new OfferedItem("shot", s.Id, s, null, null));
        foreach (VideoAnalysisSilenceSpan s in artifact.SilenceSpans)
            list.Add(new OfferedItem("silence", s.Id, null, s, null));
        foreach (VideoAnalysisSegment s in viewSegments)
            list.Add(new OfferedItem("segment", s.Id, null, null, s));
        return list;
    }

    private static (JsonObject View, JsonObject Meta, List<string> OfferedIds, List<string> OfferedPlacementIds) BuildBoundedView(
        VideoAnalysisArtifact artifact,
        List<VideoAnalysisSegment> viewSegments,
        VideoAnalyzeStepConfig config,
        string artifactStorageKey,
        bool transcriptionApplied,
        bool transcriptionDegraded,
        string? transcriptionProviderName,
        bool visualApplied,
        bool visualDegraded,
        int visualGridWidth,
        int visualGridHeight,
        bool audioLevelsApplied,
        bool visionApplied,
        bool visionDegraded,
        bool visionPartial,
        string? visionProviderName,
        int captionedShotCount,
        int failedShotCount)
    {
        List<OfferedItem> offered = BuildOfferedItems(artifact, viewSegments);
        int totalItemCount = offered.Count;

        int maxViewSegments = Math.Max(0, config.MaxViewSegments);
        if (offered.Count > maxViewSegments)
            offered = offered.Take(maxViewSegments).ToList();

        int maxOutputChars = Math.Clamp(config.MaxOutputChars, 256, 200_000);

        // Detail degrades BEFORE items drop (the most important correctness property of Phase 1):
        // try the configured level, then each lower level in turn, re-serializing the SAME item
        // set at each level — only once at None does the item-dropping loop below ever run. This
        // guarantees offeredIdCount can never be smaller than a plain AnalyzeVisuals=false run at
        // the same MaxOutputChars would produce, since None's shot node is byte-identical to the
        // pre-Phase-1 shape.
        bool haveVisualOrAudioData = artifact.Shots.Any(s => s.Visual is not null || s.Audio is not null || s.Caption is not null);
        VideoVisualDetail startDetail = haveVisualOrAudioData ? config.VisualDetail : VideoVisualDetail.None;
        List<VideoVisualDetail> levelsToTry = startDetail switch
        {
            VideoVisualDetail.Full => [VideoVisualDetail.Full, VideoVisualDetail.Compact, VideoVisualDetail.None],
            VideoVisualDetail.Compact => [VideoVisualDetail.Compact, VideoVisualDetail.None],
            _ => [VideoVisualDetail.None]
        };

        JsonObject view = new();
        string serialized = string.Empty;
        VideoVisualDetail detailApplied = levelsToTry[^1];

        foreach (VideoVisualDetail detail in levelsToTry)
        {
            view = BuildView(artifact, offered, detail, config);
            serialized = view.ToJsonString(EnvelopeJsonOptions);
            detailApplied = detail;
            if (serialized.Length <= maxOutputChars)
                break;
        }

        // Never truncate mid-JSON: drop whole trailing items (from the end of the combined,
        // priority-ordered list) and re-serialize until the view fits the char budget. Only
        // reached once the lowest-tried detail level still doesn't fit.
        while (serialized.Length > maxOutputChars && offered.Count > 0)
        {
            offered.RemoveAt(offered.Count - 1);
            view = BuildView(artifact, offered, detailApplied, config);
            serialized = view.ToJsonString(EnvelopeJsonOptions);
        }

        List<string> offeredIds = offered.Select(i => i.Id).ToList();
        int droppedItems = Math.Max(0, totalItemCount - offeredIds.Count);
        bool truncated = droppedItems > 0;

        // Phase 3: placements are a SEPARATE budget concern from OfferedIds/offered items above
        // (see VideoAnalysisArtifact.OfferedPlacementIds's doc comment) — they are shown as one
        // atomic array gated on detail != None (same gate as pacing/duplicateGroups), never
        // individually dropped by the item-truncation loop above. So "offered" here means
        // exactly "the whole placements array survived to the detail level actually applied" —
        // empty whenever detailApplied == None, which is also the same point Compact/Full's
        // per-shot v/a/c detail gets dropped, satisfying "dropped as a whole array before shot
        // items start getting dropped" (item-dropping only ever runs once detail has already
        // reached None).
        List<string> offeredPlacementIds = detailApplied != VideoVisualDetail.None
            ? (artifact.Placements?.Select(p => p.Id).ToList() ?? [])
            : [];

        var meta = new JsonObject
        {
            ["operation"] = "videoAnalyze",
            ["artifactStorageKey"] = artifactStorageKey,
            ["offeredIdCount"] = offeredIds.Count,
            ["truncated"] = truncated,
            ["droppedItems"] = droppedItems,
            ["transcription"] = new JsonObject
            {
                ["mode"] = config.Transcription.ToString(),
                ["applied"] = transcriptionApplied,
                ["provider"] = transcriptionProviderName,
                ["degraded"] = transcriptionDegraded
            },
            ["visual"] = new JsonObject
            {
                ["applied"] = visualApplied,
                ["degraded"] = visualDegraded,
                ["sampleFps"] = config.VisualSampleFps,
                ["gridWidth"] = visualGridWidth,
                ["gridHeight"] = visualGridHeight,
                ["detail"] = detailApplied.ToString()
            },
            ["visualDetailApplied"] = detailApplied.ToString(),
            ["audioLevels"] = new JsonObject { ["applied"] = audioLevelsApplied },
            ["vision"] = new JsonObject
            {
                ["mode"] = config.Vision.ToString(),
                ["applied"] = visionApplied,
                ["provider"] = visionProviderName,
                ["degraded"] = visionDegraded,
                ["partial"] = visionPartial,
                ["captionedShots"] = captionedShotCount,
                ["failedShots"] = failedShotCount
            },
            ["sourceChars"] = artifact.Shots.Count + artifact.SilenceSpans.Count + artifact.Segments.Count + artifact.Words.Count,
            ["outputChars"] = serialized.Length,
            ["offeredPlacementIdCount"] = offeredPlacementIds.Count
        };

        return (view, meta, offeredIds, offeredPlacementIds);
    }

    private static JsonObject BuildView(
        VideoAnalysisArtifact artifact, List<OfferedItem> items, VideoVisualDetail detail, VideoAnalyzeStepConfig config)
    {
        var view = new JsonObject
        {
            ["media"] = MediaNode(artifact.Media),
            ["shots"] = ToArray(items.Where(i => i.Kind == "shot").Select(i => ShotNode(i.Shot!, detail, config.StillMotionThreshold))),
            ["silences"] = ToArray(items.Where(i => i.Kind == "silence").Select(i => SilenceNode(i.Silence!))),
            ["segments"] = ToArray(items.Where(i => i.Kind == "segment").Select(i => SegmentNode(i.Segment!)))
        };

        // Gated on detail != None (rather than unconditionally): at None-detail, this view must
        // be byte-identical regardless of whether visual data exists internally but was merely
        // suppressed by budget-driven degradation vs. never computed at all (AnalyzeVisuals=false)
        // — otherwise a non-empty Pacing.MotionTimeline (which only exists when visual analysis
        // ran) would silently reintroduce a byte-size difference the detail-degrades-before-drop
        // guarantee is supposed to eliminate.
        if (detail != VideoVisualDetail.None)
        {
            if (artifact.Pacing is not null)
                view["pacing"] = PacingNode(artifact.Pacing);

            if (artifact.DuplicateGroups is { Count: > 0 })
                view["duplicateGroups"] = DuplicateGroupsNode(artifact.DuplicateGroups, config.MaxViewDuplicateGroups);

            if (artifact.Placements is { Count: > 0 })
                view["placements"] = PlacementsNode(artifact.Placements);
        }

        return view;
    }

    private static JsonArray ToArray(IEnumerable<JsonObject> items)
    {
        var array = new JsonArray();
        foreach (JsonObject node in items)
            array.Add(node);
        return array;
    }

    private static JsonObject MediaNode(VideoAnalysisMedia media) => new()
    {
        ["durationSec"] = media.DurationSec,
        ["fpsNum"] = media.FpsNum,
        ["fpsDen"] = media.FpsDen,
        ["width"] = media.Width,
        ["height"] = media.Height
    };

    /// <summary>
    /// <c>detail == None</c> produces exactly today's (pre-Phase-1) shot shape — no <c>v</c>/<c>a</c>
    /// key at all — so existing golden-output-shaped tests never need updating for the
    /// "no visual data" case. The four base fields are deliberately left unrounded at every detail
    /// level (only the new visual/audio numbers inside <c>v</c>/<c>a</c> get rounded), so this node
    /// is byte-identical to the original across the board.
    /// </summary>
    private static JsonObject ShotNode(VideoAnalysisShot s, VideoVisualDetail detail, double stillMotionThreshold)
    {
        var node = new JsonObject
        {
            ["id"] = s.Id,
            ["startSec"] = s.StartSec,
            ["endSec"] = s.EndSec,
            ["durationSec"] = s.EndSec - s.StartSec
        };

        if (detail == VideoVisualDetail.None)
            return node;

        if (s.Visual is not null)
            node["v"] = VisualNode(s.Visual, detail, stillMotionThreshold);

        if (s.Audio is not null)
            node["a"] = AudioNode(s.Audio);

        if (s.Caption is not null)
            node["c"] = CaptionNode(s.Caption, detail);

        return node;
    }

    /// <summary>
    /// Phase 2's <c>"c"</c> (caption) key — present only at <c>Compact</c>/<c>Full</c> detail (the
    /// caller never calls this at <c>None</c>), same degrade-before-drop discipline Phase 1
    /// established for <c>"v"</c>/<c>"a"</c>. <c>Compact</c> keeps the fields most useful for a
    /// quick keep/cut judgment; <c>Full</c> adds the rest.
    /// </summary>
    private static JsonObject CaptionNode(VideoShotCaption c, VideoVisualDetail detail)
    {
        var node = new JsonObject
        {
            ["summary"] = c.Summary,
            ["scale"] = c.ShotScale,
            ["mood"] = c.Mood
        };

        if (c.Tags.Count > 0)
            node["tags"] = new JsonArray(c.Tags.Select(t => (JsonNode)t).ToArray());

        if (detail == VideoVisualDetail.Full)
        {
            if (c.Subjects.Count > 0)
                node["subjects"] = new JsonArray(c.Subjects.Select(t => (JsonNode)t).ToArray());

            node["action"] = c.Action;
            node["setting"] = c.Setting;
            node["cameraAngle"] = c.CameraAngle;

            if (c.OnScreenText.Count > 0)
                node["onScreenText"] = new JsonArray(c.OnScreenText.Select(t => (JsonNode)t).ToArray());
        }

        return node;
    }

    private static JsonObject VisualNode(VideoAnalysisShotVisual v, VideoVisualDetail detail, double stillMotionThreshold)
    {
        var node = new JsonObject
        {
            ["motion"] = Score(v.MotionMean),
            ["move"] = v.CameraMove,
            ["cutIn"] = v.HeadMotion < stillMotionThreshold ? "still" : "moving",
            ["cutOut"] = v.TailMotion < stillMotionThreshold ? "still" : "moving"
        };

        List<VideoAnalysisStillWindow> stillWindowsToShow = detail == VideoVisualDetail.Full
            ? v.StillWindows.ToList()
            : v.StillWindows.OrderByDescending(w => w.EndSec - w.StartSec).Take(1).ToList();
        if (stillWindowsToShow.Count > 0)
        {
            node["still"] = new JsonArray(stillWindowsToShow
                .Select(w => (JsonNode)new JsonObject { ["startSec"] = Round2(w.StartSec), ["endSec"] = Round2(w.EndSec) })
                .ToArray());
        }

        node["bright"] = Score(v.BrightnessMean);
        node["contrast"] = Score(v.ContrastRms);

        List<VideoAnalysisColor> colorsToShow = detail == VideoVisualDetail.Full
            ? v.DominantColors.ToList()
            : v.DominantColors.Take(2).ToList();
        if (colorsToShow.Count > 0)
            node["colors"] = new JsonArray(colorsToShow.Select(c => (JsonNode)c.Hex).ToArray());

        if (v.BestOverlayRegion is not null)
        {
            VideoAnalysisRegion? region = v.Regions.FirstOrDefault(r => r.Name == v.BestOverlayRegion);
            if (region is not null)
            {
                node["safe"] = new JsonObject
                {
                    ["region"] = region.Name,
                    ["fit"] = Score(region.Suitability),
                    ["text"] = region.TextColor
                };
            }
        }

        if (v.DuplicateGroupId is not null)
        {
            node["dup"] = v.DuplicateGroupId;
            node["best"] = v.IsBestTake;
        }

        if (v.KenBurnsCandidate)
            node["kenBurns"] = true;

        if (detail == VideoVisualDetail.Full)
        {
            node["motionStdDev"] = Score(v.MotionStdDev);
            node["motionPeak"] = Score(v.MotionPeak);
            node["cameraConfidence"] = Score(v.CameraMoveConfidence);

            if (v.Regions.Count > 0)
            {
                node["regions"] = new JsonArray(v.Regions.Select(r => (JsonNode)new JsonObject
                {
                    ["name"] = r.Name,
                    ["luma"] = Score(r.LumaMean),
                    ["clutter"] = Score(r.LumaStdDev),
                    ["motion"] = Score(r.TemporalMotion),
                    ["fit"] = Score(r.Suitability),
                    ["text"] = r.TextColor
                }).ToArray());
            }
        }

        return node;
    }

    private static JsonObject AudioNode(VideoAnalysisShotAudio a) => new()
    {
        ["rms"] = (int)Math.Round(a.RmsDbfs),
        ["speech"] = Score(a.SpeechRatio)
    };

    private static JsonObject PacingNode(VideoAnalysisPacing p) => new()
    {
        ["meanShotSec"] = Round2(p.MeanShotSeconds),
        ["medianShotSec"] = Round2(p.MedianShotSeconds),
        ["cutsPerMinute"] = Round2(p.CutsPerMinute),
        ["motionTimeline"] = new JsonArray(p.MotionTimeline.Select(m => (JsonNode)Score(m)).ToArray()),
        ["timelineBinSec"] = Round2(p.TimelineBinSeconds)
    };

    private static JsonArray DuplicateGroupsNode(IReadOnlyList<VideoAnalysisDuplicateGroup> groups, int max) =>
        new(groups.Take(Math.Max(0, max)).Select(g => (JsonNode)new JsonObject
        {
            ["id"] = g.Id,
            ["shotIds"] = new JsonArray(g.ShotIds.Select(id => (JsonNode)id).ToArray()),
            ["bestShotId"] = g.BestShotId,
            ["similarity"] = Score(g.MeanSimilarity)
        }).ToArray());

    /// <summary>
    /// Phase 3's <c>view.placements</c> — deliberately small/budget-friendly (no rect, no time
    /// window, no anchor kind): a downstream motion-graphics planner only needs "which candidate
    /// ids exist, how good is each, is it a light- or dark-text region", never the geometry or
    /// timing that <c>VideoCompileStepExecutor</c> alone resolves from the full artifact.
    /// </summary>
    private static JsonArray PlacementsNode(IReadOnlyList<VideoAnalysisPlacement> placements) =>
        new(placements.Select(p => (JsonNode)new JsonObject
        {
            ["id"] = p.Id,
            ["shotId"] = p.ShotId,
            ["region"] = p.Region,
            ["fit"] = Score(p.Suitability),
            ["text"] = p.TextColor
        }).ToArray());

    private static JsonObject SilenceNode(VideoAnalysisSilenceSpan s) => new()
    {
        ["id"] = s.Id,
        ["startSec"] = s.StartSec,
        ["endSec"] = s.EndSec,
        ["durationSec"] = s.EndSec - s.StartSec,
        ["afterShot"] = s.AfterShot
    };

    private static JsonObject SegmentNode(VideoAnalysisSegment s) => new()
    {
        ["id"] = s.Id,
        ["shot"] = s.Shot,
        ["startSec"] = s.StartSec,
        ["endSec"] = s.EndSec,
        ["text"] = s.Text
    };

    /// <summary>Seconds rounded to 2dp — used only for the new Phase 1 visual/pacing numbers.</summary>
    private static double Round2(double seconds) => Math.Round(seconds, 2);

    /// <summary>A 0..1 score rounded to a 0..100 integer — used only for the new Phase 1 visual/pacing numbers.</summary>
    private static int Score(double value01) => (int)Math.Round(Math.Clamp(value01, 0, 1) * 100);

    // ---------------------------------------------------------------------
    // Expectation / failure / descriptor helpers
    // ---------------------------------------------------------------------

    private static string? EvaluateExpect(VideoAnalyzeExpectation? expect, VideoAnalysisArtifact artifact)
    {
        if (expect is null)
            return null;

        if (expect.MinShots.HasValue && artifact.Shots.Count < expect.MinShots.Value)
            return $"expect.minShots={expect.MinShots.Value} but only {artifact.Shots.Count} shots were detected.";

        if (expect.MinTranscriptSegments.HasValue && artifact.Segments.Count < expect.MinTranscriptSegments.Value)
        {
            return $"expect.minTranscriptSegments={expect.MinTranscriptSegments.Value} but only " +
                   $"{artifact.Segments.Count} transcript segments were produced.";
        }

        if (expect.MaxSilenceRatio.HasValue && artifact.Media.DurationSec > 0)
        {
            double silenceSeconds = artifact.SilenceSpans.Sum(s => Math.Max(0, s.EndSec - s.StartSec));
            double ratio = silenceSeconds / artifact.Media.DurationSec;
            if (ratio > expect.MaxSilenceRatio.Value)
            {
                return $"expect.maxSilenceRatio={expect.MaxSilenceRatio.Value:F2} but measured silence ratio was {ratio:F2}.";
            }
        }

        if (expect.MinShotsWithVisuals.HasValue)
        {
            int withVisuals = artifact.Shots.Count(s => s.Visual is not null);
            if (withVisuals < expect.MinShotsWithVisuals.Value)
            {
                return $"expect.minShotsWithVisuals={expect.MinShotsWithVisuals.Value} but only " +
                       $"{withVisuals} shots have visual descriptors.";
            }
        }

        return null;
    }

    private static string BuildResolvedInputDescriptor(VideoAnalyzeStepConfig config, string storageKey, int? offeredIdCount = null)
    {
        var descriptor = new JsonObject
        {
            ["source"] = new JsonObject
            {
                ["kind"] = config.Source.Kind.ToString(),
                ["storageKey"] = storageKey
            },
            ["detectSilence"] = config.DetectSilence,
            ["detectShots"] = config.DetectShots,
            ["transcription"] = config.Transcription.ToString(),
            ["analyzeVisuals"] = config.AnalyzeVisuals,
            ["vision"] = config.Vision.ToString()
        };

        if (offeredIdCount.HasValue)
            descriptor["offeredIdCount"] = offeredIdCount.Value;

        return descriptor.ToJsonString(EnvelopeJsonOptions);
    }

    private StepExecutionResult Failure(
        StepExecutionContext context, Stopwatch sw, string code, string message, string? artifactStorageKey = null)
    {
        _logger.LogWarning("VideoAnalyze step {StepOrder} failed: [{Code}] {Message}", context.Step.StepOrder, code, message);

        var envelope = new JsonObject
        {
            ["view"] = null,
            ["meta"] = new JsonObject { ["operation"] = "videoAnalyze" },
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
