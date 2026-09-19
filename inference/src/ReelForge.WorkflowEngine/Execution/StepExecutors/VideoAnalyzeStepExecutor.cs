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
/// Executes <see cref="StepType.VideoAnalyze"/> steps: deterministic, non-LLM derushing of one or
/// more source videos into shots/silence gaps/(optional) transcript, plus (Phase 1) deterministic
/// visual/audio scene descriptors, persisted as a full analysis artifact plus a bounded,
/// id-anchored "{view, meta}" prompt envelope for the downstream VideoStoryEditor agent step.
/// See plan §4.1/§3, docs/video-editing.md "Scene/visual analysis (Phase 1)", and
/// docs/video-editing.md "Multiple source clips" for the multi-source addition.
///
/// <para>
/// <b>Multi-source addition:</b> <see cref="VideoAnalyzeStepConfig.Sources"/> (plural) lets one
/// step analyze several source clips (e.g. multiple takes/angles/B-roll) into ONE merged artifact.
/// Each clip is analyzed independently via <see cref="AnalyzeSourceAsync"/> (the exact same
/// deterministic per-source pipeline this executor always ran — silence/shot detection,
/// transcription, Phase 1/2/3 analysis — is entirely unchanged, just scoped to one clip's local
/// file at a time, processed sequentially never in parallel), then every clip's LOCAL ids
/// (each restarting at "s0"/"g0"/"t0"/"w0"/"p0"/"d0") are remapped into ONE globally-unique id
/// space via a running per-id-kind offset, and every item is tagged with the
/// <c>SourceIndex</c> of the clip it came from. A single-source config (the default —
/// <see cref="VideoAnalyzeStepConfig.Sources"/> null/empty, so only <see cref="VideoAnalyzeStepConfig.Source"/>
/// is set) is treated as a one-element source list, so ids/artifact shape/behavior are byte-identical
/// to before this addition.
/// </para>
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
    private readonly ISharpnessSampler _sharpnessSampler;
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
        ISharpnessSampler sharpnessSampler,
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
        _sharpnessSampler = sharpnessSampler;
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

            // ---- Multi-source addition: Sources (plural) is the authoritative list when set;
            // otherwise fall back to the single legacy Source field as a one-element list, which
            // is exactly today's behavior (see VideoAnalyzeStepConfig.Sources's doc comment). ----
            IReadOnlyList<VideoSourceRef> sources = config.Sources is { Count: > 0 } ? config.Sources : [config.Source];

            scratch = VideoScratchSpace.Create(_options, context.Execution.Id, step.Id, _logger);

            // Clamped here, at the point config is consumed — same convention as
            // VideoCompileStepExecutor's Crf/MaxSegments clamps — so a workflow-author-supplied
            // config (e.g. 2048x2048) can't allocate an enormous grid.rgb scratch file. Computed
            // once (identical for every source, since it's a single config), reused per source.
            int gridWidth = Math.Clamp(config.VisualGridWidth, 8, 256);
            int gridHeight = Math.Clamp(config.VisualGridHeight, 8, 256);

            // ---- Phase 4 (§3): weighted step-progress plan. Declaration order in `enabled` doesn't
            // matter (VideoAnalyzeProgressPlan.Stage's declaration order is what drives sequencing);
            // this list only decides WHICH stages get non-zero weight. ----
            var enabledStages = new List<VideoAnalyzeProgressPlan.Stage>
            {
                VideoAnalyzeProgressPlan.Stage.DownloadSource, VideoAnalyzeProgressPlan.Stage.ProbeSource,
                VideoAnalyzeProgressPlan.Stage.BuildView, VideoAnalyzeProgressPlan.Stage.UploadArtifact
            };
            if (config.DetectSilence)
                enabledStages.Add(VideoAnalyzeProgressPlan.Stage.DetectSilence);
            if (config.DetectShots)
                enabledStages.Add(VideoAnalyzeProgressPlan.Stage.DetectShots);
            if (config.Transcription != VideoTranscriptionMode.Off)
            {
                enabledStages.Add(VideoAnalyzeProgressPlan.Stage.ResolveTranscription);
                enabledStages.Add(VideoAnalyzeProgressPlan.Stage.Transcribe);
            }
            if (config.AnalyzeAudioLevels)
                enabledStages.Add(VideoAnalyzeProgressPlan.Stage.SampleAudioLevels);
            if (config.AnalyzeVisuals)
            {
                enabledStages.Add(VideoAnalyzeProgressPlan.Stage.SampleFrameGrid);
                enabledStages.Add(VideoAnalyzeProgressPlan.Stage.AnalyzeShots);
            }
            if (config.AnalyzeVisuals && config.DetectNearDuplicates)
                enabledStages.Add(VideoAnalyzeProgressPlan.Stage.GroupDuplicates);
            if (config.DetectInsertRegions)
                enabledStages.Add(VideoAnalyzeProgressPlan.Stage.TrackInsertRegions);
            if (config.DetectSharpness)
                enabledStages.Add(VideoAnalyzeProgressPlan.Stage.SampleSharpness);
            if (config.AnalyzeVisuals && config.AnalyzeColorGrading && config.DetectLookGroups)
                enabledStages.Add(VideoAnalyzeProgressPlan.Stage.MatchLooks);
            // Sound effects (see docs/video-editing.md "Sound effects") deliberately reuse the
            // music-candidate listing stage rather than adding a new one: both enumerate the same
            // project file list in the same single ListFilesAsync call below, so there is no
            // independent duration for a separate stage to weight.
            if (config.OfferMusicTracks || config.OfferSfxClips)
                enabledStages.Add(VideoAnalyzeProgressPlan.Stage.ListMusicCandidates);
            if (config.Vision != VideoVisionMode.Off)
            {
                enabledStages.Add(VideoAnalyzeProgressPlan.Stage.ExtractKeyframes);
                enabledStages.Add(VideoAnalyzeProgressPlan.Stage.CaptionShots);
            }

            var progress = new VideoAnalyzeProgressPlan(enabledStages, sources.Count);

            // Recorded early (before any per-source download/analysis) so a step that fails partway
            // through still leaves a useful trace of what it was resolving to — mirrors the
            // pre-multi-source behavior of recording resolved input right after source resolution,
            // before any heavier work that could fail.
            context.RecordResolvedInput(BuildResolvedInputDescriptor(config, sources, null));

            // Phase 4 (§5.5): a cap that is genuinely STEP-wide rather than silently per-source —
            // the exact bug §0.3 found in MaxCaptionedShots pre-hoist. Sharpness cannot simply be
            // hoisted to step level like captioning was, because it must run before EACH source's
            // own duplicate grouping in order to feed ComputeTakeQuality — so instead every source
            // shares one mutable budget object.
            var sharpnessBudget = new StepBudget { Remaining = Math.Max(0, config.MaxSharpnessShots) };

            var sourceResults = new List<SourceAnalysisResult>(sources.Count);
            for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                SourceAnalysisOutcome outcome = await AnalyzeSourceAsync(
                    context, scratch, config, sources[sourceIndex], sourceIndex, sources.Count, gridWidth, gridHeight,
                    progress, sharpnessBudget, context.CancellationToken);

                if (outcome.Result is null)
                {
                    string message = sources.Count > 1
                        ? $"Source {sourceIndex} ({sources[sourceIndex].Kind}): {outcome.ErrorMessage}"
                        : outcome.ErrorMessage ?? "Unknown error.";
                    return Failure(context, sw, outcome.ErrorCode ?? "UNEXPECTED_ERROR", message);
                }

                sourceResults.Add(outcome.Result);
            }

            context.RecordResolvedInput(BuildResolvedInputDescriptor(config, sources, sourceResults));

            // ---- Merge every source's LOCAL ids into ONE globally-unique id space (plan: "ids
            // must stay globally unique across ALL sources in one artifact, assigned contiguously
            // across every source file in analysis order, not restarting per-file"). Each id KIND
            // (s/g/t/w/p/d) gets its own running offset counter — the same scheme each kind
            // already used within a single source, just carried across sources now. A single
            // source produces exactly one merge pass at offset 0, so this is byte-identical to the
            // pre-multi-source output for that case. ----

            var shots = new List<VideoAnalysisShot>();
            var silences = new List<VideoAnalysisSilenceSpan>();
            var segments = new List<VideoAnalysisSegment>();
            var words = new List<VideoAnalysisWord>();
            var placements = new List<VideoAnalysisPlacement>();
            var duplicateGroups = new List<VideoAnalysisDuplicateGroup>();
            var insertRegionTracks = new List<VideoInsertRegionTrack>();
            var sourceInfos = new List<VideoAnalysisSourceInfo>();

            int shotOffset = 0, silenceOffset = 0, segmentOffset = 0, wordOffset = 0, placementOffset = 0, duplicateGroupOffset = 0, insertRegionOffset = 0;

            for (int sourceIndex = 0; sourceIndex < sourceResults.Count; sourceIndex++)
            {
                SourceAnalysisResult r = sourceResults[sourceIndex];
                sourceInfos.Add(new VideoAnalysisSourceInfo(sourceIndex, r.StorageKey, r.Media));

                // Local shot id -> global shot id, needed to remap every back-reference below
                // (SilenceSpan.AfterShot, Segment.Shot, Placement.ShotId, DuplicateGroup member ids)
                // from this source's own local id space into the merged global one.
                var shotIdMap = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (VideoAnalysisShot s in r.Shots)
                {
                    string globalId = OffsetId(s.Id, shotOffset);
                    shotIdMap[s.Id] = globalId;
                    shots.Add(s with { Id = globalId, SourceIndex = sourceIndex });
                }

                foreach (VideoAnalysisSilenceSpan s in r.Silences)
                {
                    silences.Add(s with
                    {
                        Id = OffsetId(s.Id, silenceOffset),
                        SourceIndex = sourceIndex,
                        AfterShot = MapLocalId(s.AfterShot, shotIdMap)
                    });
                }

                foreach (VideoAnalysisSegment s in r.Segments)
                {
                    segments.Add(s with
                    {
                        Id = OffsetId(s.Id, segmentOffset),
                        SourceIndex = sourceIndex,
                        Shot = MapLocalId(s.Shot, shotIdMap)
                    });
                }

                foreach (VideoAnalysisWord w in r.Words)
                    words.Add(w with { Id = OffsetId(w.Id, wordOffset), SourceIndex = sourceIndex });

                foreach (VideoAnalysisPlacement p in r.Placements)
                {
                    placements.Add(p with
                    {
                        Id = OffsetId(p.Id, placementOffset),
                        SourceIndex = sourceIndex,
                        ShotId = MapLocalId(p.ShotId, shotIdMap) ?? p.ShotId
                    });
                }

                foreach (VideoAnalysisDuplicateGroup g in r.DuplicateGroups)
                {
                    duplicateGroups.Add(g with
                    {
                        Id = OffsetId(g.Id, duplicateGroupOffset),
                        ShotIds = g.ShotIds.Select(id => MapLocalId(id, shotIdMap) ?? id).ToList(),
                        BestShotId = MapLocalId(g.BestShotId, shotIdMap) ?? g.BestShotId
                    });
                }

                foreach (VideoInsertRegionTrack t in r.InsertRegions)
                {
                    insertRegionTracks.Add(t with
                    {
                        Id = OffsetId(t.Id, insertRegionOffset),
                        SourceIndex = sourceIndex,
                        ShotId = MapLocalId(t.ShotId, shotIdMap)
                    });
                }

                shotOffset += r.Shots.Count;
                silenceOffset += r.Silences.Count;
                segmentOffset += r.Segments.Count;
                wordOffset += r.Words.Count;
                placementOffset += r.Placements.Count;
                duplicateGroupOffset += r.DuplicateGroups.Count;
                insertRegionOffset += r.InsertRegions.Count;
            }

            // ---- Aggregate per-source provenance/counters into ONE artifact-level record and a
            // couple of summed counters. For a single source this is a pass-through (Count == 1
            // short-circuits to that source's own Provenance/counts unchanged) — full per-source
            // provenance breakdown is intentionally out of scope for this addition (see
            // docs/video-editing.md "Multiple source clips"). ----
            VideoAnalysisProvenance provenance = AggregateProvenance(sourceResults.Select(r => r.Provenance).ToList());
            double totalDurationSec = sourceInfos.Sum(s => s.Media.DurationSec);

            VideoAnalysisPacing pacing = FrameGridAnalyzer.ComputePacing(shots, totalDurationSec);

            // ---- Background music (see docs/video-editing.md "Background music"): PROJECT-level,
            // not per-source, unlike everything above — one candidate list regardless of how many
            // source clips this step analyzed. Deliberately no ffprobe of the candidates (the fit
            // policy is resolved server-side at compile time regardless of exact duration, so
            // probing every candidate here would only cost N downloads for a list the agent picks
            // one item from). Never fails the step — a ListFilesAsync failure just degrades to zero
            // candidates. ----
            // Sound-effects candidates (docs/video-editing.md "Sound effects") share this same
            // block, and the same single ListFilesAsync call, since both features enumerate the
            // project's audio/* files — under independent id namespaces (m{n} vs x{n}) and
            // independent caps. Same degrade rule: a listing failure produces zero candidates
            // for BOTH lists, never a failed step.
            List<VideoAnalysisMusicCandidate> musicCandidates = [];
            List<VideoAnalysisSfxCandidate> sfxCandidates = [];
            if (config.OfferMusicTracks || config.OfferSfxClips)
            {
                await ReportAsync(context, progress, VideoAnalyzeProgressPlan.Stage.ListMusicCandidates, "Listing audio candidates");
                try
                {
                    IReadOnlyList<ProjectWorkspaceFile> files =
                        await _workspace.ListFilesAsync(context.Execution.ProjectId, context.CancellationToken);
                    List<ProjectWorkspaceFile> audioFiles = files
                        .Where(f => f.MimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(f => f.OriginalFileName, StringComparer.Ordinal)
                        .ThenBy(f => f.Id)
                        .ToList();

                    if (config.OfferMusicTracks)
                    {
                        musicCandidates = audioFiles
                            .Take(Math.Clamp(config.MaxMusicTracks, 0, 100))
                            .Select((f, i) => new VideoAnalysisMusicCandidate($"m{i}", f.Id, f.OriginalFileName, f.MimeType, f.SizeBytes))
                            .ToList();
                    }

                    if (config.OfferSfxClips)
                    {
                        sfxCandidates = audioFiles
                            .Take(Math.Clamp(config.MaxSfxClips, 0, 100))
                            .Select((f, i) => new VideoAnalysisSfxCandidate($"x{i}", f.Id, f.OriginalFileName, f.MimeType, f.SizeBytes))
                            .ToList();
                    }
                }
                catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "VideoAnalyze step {StepOrder}: listing project files for music/SFX candidates failed; degrading to zero candidates.", step.StepOrder);
                }
            }

            // ---- Phase 4 (D4): cross-source look/grade grouping — step-level by design: with
            // multi-source, a step routinely merges clips shot on different cameras/days/white
            // balances, and nothing in the view previously told the story editor those clips look
            // different. Must run BEFORE the vision block below, since a future prompt-priming step
            // feeds look-group membership into the vision prompt. ----
            var lookGroups = new List<VideoAnalysisLookGroup>();
            bool lookGroupingApplied = false, lookUniform = false;

            if (config.AnalyzeVisuals && config.AnalyzeColorGrading && config.DetectLookGroups)
            {
                await ReportAsync(context, progress, VideoAnalyzeProgressPlan.Stage.MatchLooks, "Matching shot looks");
                try
                {
                    var lookIds = new List<string>();
                    var lookSigs = new List<FrameGridAnalyzer.LookSignature>();
                    foreach (VideoAnalysisShot s in shots)
                    {
                        if (s.Visual is null || s.Visual.ColorTemperatureClass is null)
                            continue;

                        lookIds.Add(s.Id);
                        lookSigs.Add(new FrameGridAnalyzer.LookSignature(
                            s.Visual.Warmth, s.Visual.Tint, s.Visual.BrightnessMean,
                            s.Visual.BlackPoint, s.Visual.WhitePoint, s.Visual.SaturationMean));
                    }

                    if (lookIds.Count >= 2)
                    {
                        IReadOnlyList<VideoAnalysisLookGroup> groups = FrameGridAnalyzer.GroupLooks(
                            lookIds, lookSigs, config.LookSimilarityThreshold,
                            out IReadOnlyDictionary<string, (string GroupId, int LookRank)> assign);

                        for (int i = 0; i < shots.Count; i++)
                        {
                            if (shots[i].Visual is null || !assign.TryGetValue(shots[i].Id, out (string GroupId, int LookRank) a))
                                continue;

                            shots[i] = shots[i] with
                            {
                                Visual = shots[i].Visual! with { LookGroupId = a.GroupId, LookRank = a.LookRank }
                            };
                        }

                        // Group descriptors come from the representative shot's own already-computed
                        // classes, never a re-derivation from the centroid — so the words always
                        // describe a real frame.
                        lookGroups = groups.Select(g =>
                        {
                            VideoAnalysisShotVisual? rep = shots.FirstOrDefault(s => s.Id == g.RepresentativeShotId)?.Visual;
                            return g with
                            {
                                ColorTemperatureClass = rep?.ColorTemperatureClass,
                                ToneClass = rep?.ToneClass,
                                SaturationClass = rep?.SaturationClass
                            };
                        }).ToList();

                        // The single-camera talking-head case: one group covering every analyzed
                        // shot carries no discriminating information, so §6 suppresses every
                        // per-shot "look" key for it.
                        lookUniform = lookGroups.Count == 1 && lookGroups[0].ShotIds.Count == lookIds.Count;
                        lookGroupingApplied = true;
                    }
                }
                catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "VideoAnalyze step {StepOrder}: look grouping failed; degrading.", step.StepOrder);
                    lookGroups.Clear();
                    lookGroupingApplied = false;
                    lookUniform = false;
                }
            }

            bool colorGradingApplied = config.AnalyzeVisuals && config.AnalyzeColorGrading && provenance.VisualAnalysisApplied;

            // ---- Phase 2: vision-LLM shot captioning — HOISTED to step level (§2.1). Runs ONCE,
            // after every source clip has finished the deterministic per-source pipeline above, so
            // MaxCaptionedShots/VisionTimeoutSeconds are genuine STEP-WIDE budgets across every
            // clip instead of being silently multiplied by source count (the bug §0.3 of the plan
            // documents). Still the strictly-last analysis stage. Off by default (VideoVisionMode.Off).
            // ----
            bool visionApplied = false, visionDegraded = false, visionPartial = false;
            string? visionProviderName = null;
            int captionedShotCountTotal = 0, failedShotCountTotal = 0, persistedKeyframeCount = 0, keyframesExtractedTotal = 0;

            if (config.Vision != VideoVisionMode.Off)
            {
                try
                {
                    ResolvedInferenceProvider? visionProvider =
                        await _providerResolver.ResolveVisionAsync(config.VisionProviderId, context.CancellationToken);

                    if (visionProvider is null)
                    {
                        if (config.Vision == VideoVisionMode.Required)
                            return Failure(context, sw, "VISION_UNAVAILABLE",
                                "Vision is Required but no vision-capable inference provider is configured.");

                        _logger.LogInformation(
                            "VideoAnalyze step {StepOrder}: no vision provider resolved; degrading (Vision=Optional).",
                            step.StepOrder);
                        visionDegraded = true;
                    }
                    else
                    {
                        visionProviderName = visionProvider.Name;

                        // Each caption's keyframe must be extracted from the clip that shot
                        // actually came from.
                        string[] localPathBySource = sourceResults.Select(r => r.LocalVideoPath).ToArray();

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

                            await ReportAsync(context, progress, VideoAnalyzeProgressPlan.Stage.CaptionShots, $"Captioning shots (0/{selectedShotIds.Count})");

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
                                    string sourceVideoPath = localPathBySource[shot.SourceIndex];

                                    // Phase 4 (§7.4): KeyframesPerShot==1 keeps calling
                                    // ExtractKeyframeAsync exactly as before — byte-identical.
                                    int keyframesPerShot = Math.Clamp(config.KeyframesPerShot, 1, 3);
                                    bool isContactSheet = keyframesPerShot > 1;
                                    if (isContactSheet)
                                    {
                                        IReadOnlyList<double> atSecs = KeyframeSelector.ChooseKeyframeSecs(shot, keyframesPerShot);
                                        await _keyframeExtractor.ExtractContactSheetAsync(
                                            sourceVideoPath, keyframePath, atSecs, config.KeyframeMaxWidth, visionCt);
                                    }
                                    else
                                    {
                                        await _keyframeExtractor.ExtractKeyframeAsync(
                                            sourceVideoPath, keyframePath, atSec, config.KeyframeMaxWidth, visionCt);
                                    }

                                    keyframesExtractedTotal++;
                                    if (VideoAnalyzeProgressPlan.ShouldReportItem(keyframesExtractedTotal - 1, selectedShotIds.Count))
                                        await ReportAsync(
                                            context, progress, VideoAnalyzeProgressPlan.Stage.ExtractKeyframes,
                                            $"Extracting keyframes ({keyframesExtractedTotal}/{selectedShotIds.Count})",
                                            fraction: keyframesExtractedTotal / (double)selectedShotIds.Count);

                                    // Phase 4 (decision #1): prime the prompt with Phase 1's own
                                    // measured words for this shot — null when visual analysis
                                    // was off/degraded for it.
                                    string? measuredContext = BuildMeasuredContext(shot.Visual);

                                    ShotCaptionRequest request = new(shot.Id, keyframePath, atSec, measuredContext, isContactSheet);
                                    VideoShotCaption caption = await CaptionWithRetryAsync(
                                        visionProvider, request, config.MaxCaptionChars, visionCt);

                                    // SAFETY: the shot-id <-> caption binding is never
                                    // model-controlled. Overwrite ShotId with the id we actually
                                    // requested — never trust whatever the model echoed back —
                                    // before attaching the caption.
                                    shots[shotIdx] = shot with { Caption = caption with { ShotId = shot.Id } };
                                    captionedShotCountTotal++;

                                    // Phase 4 (§7.5): only on caption success, so a failed shot
                                    // never leaves an orphan keyframe in storage.
                                    if (config.PersistKeyframes)
                                    {
                                        try
                                        {
                                            await _workspace.UploadArtifactAsync(
                                                context.Execution.ProjectId, keyframePath,
                                                $"video-analysis/{context.Execution.Id:D}/step-{step.StepOrder}-keyframes/{shot.Id}.jpg",
                                                "image/jpeg", visionCt);
                                            persistedKeyframeCount++;
                                        }
                                        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
                                        {
                                            throw;
                                        }
                                        catch (Exception ex)
                                        {
                                            // Observability is never allowed to cost a caption.
                                            _logger.LogWarning(ex, "VideoAnalyze step {StepOrder}: persisting keyframe for {ShotId} failed; continuing.",
                                                step.StepOrder, shot.Id);
                                        }
                                    }
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
                                    failedShotCountTotal++;
                                }

                                int shotsAttempted = captionedShotCountTotal + failedShotCountTotal;
                                if (VideoAnalyzeProgressPlan.ShouldReportItem(shotsAttempted - 1, selectedShotIds.Count))
                                    await ReportAsync(
                                        context, progress, VideoAnalyzeProgressPlan.Stage.CaptionShots,
                                        $"Captioning shots ({shotsAttempted}/{selectedShotIds.Count})",
                                        fraction: shotsAttempted / (double)selectedShotIds.Count);
                            }
                        }

                        visionApplied = captionedShotCountTotal > 0;
                        if (!visionApplied)
                        {
                            if (config.Vision == VideoVisionMode.Required)
                                return Failure(context, sw, "VISION_FAILED",
                                    "Vision is Required but captioning did not produce any shot captions.");

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

            // AggregateProvenance ORed all-false vision flags from every source (harmless — vision
            // no longer runs per-source); the step-level pass above is the sole source of truth now.
            provenance = provenance with
            {
                VisionApplied = visionApplied,
                VisionDegraded = visionDegraded,
                VisionProvider = visionProviderName,
                CaptionedShotCount = captionedShotCountTotal,
                VisionPartial = visionPartial,
                ColorGradingApplied = colorGradingApplied,
                LookGroupingApplied = lookGroupingApplied,
                LookGroupCount = lookGroups.Count,
                LookUniform = lookUniform,
                LetterboxDetectionApplied = config.DetectLetterbox && provenance.VisualAnalysisApplied,
                SharpnessAvailable = sharpnessBudget.Measured > 0,
                SharpnessShotCount = sharpnessBudget.Measured
            };

            // Top-level Media mirrors source 0 — the only source in the single-source case, or the
            // first/"primary" clip in the multi-source case. Every per-clip computation downstream
            // (VideoCompileStepExecutor's frame quantization, padding clamps, graphics geometry)
            // must read Sources[i].Media instead of this field once more than one source exists.
            VideoAnalysisMedia primaryMedia = sourceInfos[0].Media;

            // A preliminary artifact to hand to BuildBoundedView below (it reads Shots/SilenceSpans/
            // Media/counts/Pacing/DuplicateGroups only, never OfferedIds, so a placeholder here is
            // safe — see the corrected artifact constructed after the view is built).
            VideoAnalysisArtifact draftArtifact = new(
                Version: 2,
                Media: primaryMedia,
                Shots: shots,
                SilenceSpans: silences,
                Segments: segments,
                Words: words,
                OfferedIds: [],
                Provenance: provenance,
                DuplicateGroups: duplicateGroups.Count > 0 ? duplicateGroups : null,
                Pacing: pacing,
                Placements: placements.Count > 0 ? placements : null,
                OfferedPlacementIds: [],
                Sources: sourceInfos,
                MusicCandidates: musicCandidates.Count > 0 ? musicCandidates : null,
                OfferedMusicIds: [],
                LookGroups: lookGroups.Count > 0 ? lookGroups : null,
                InsertRegions: insertRegionTracks.Count > 0 ? insertRegionTracks : null,
                OfferedInsertRegionIds: [],
                SfxCandidates: sfxCandidates.Count > 0 ? sfxCandidates : null,
                OfferedSfxIds: []);

            // ---- Build the bounded, id-anchored prompt view FIRST, so we know exactly which ids
            // were actually shown before persisting the artifact's OfferedIds. ----

            int maxSegmentTextChars = Math.Max(1, config.MaxSegmentTextChars);
            List<VideoAnalysisSegment> viewSegments = segments
                .Select(s => s.Text.Length > maxSegmentTextChars
                    ? s with { Text = s.Text[..maxSegmentTextChars] }
                    : s)
                .ToList();

            bool isMultiSource = sourceInfos.Count > 1;

            await ReportAsync(context, progress, VideoAnalyzeProgressPlan.Stage.BuildView, "Building bounded view");

            // artifactStorageKey isn't known yet (the artifact hasn't been uploaded) — meta's
            // copy is patched in below once it is.
            (JsonObject view, JsonObject meta, List<string> viewOfferedIds, List<string> viewOfferedPlacementIds, List<string> viewOfferedMusicIds, List<string> viewOfferedInsertRegionIds, List<string> viewOfferedSfxIds) = BuildBoundedView(
                draftArtifact, viewSegments, config, artifactStorageKey: string.Empty,
                provenance.TranscriptionApplied, provenance.TranscriptionDegraded, provenance.TranscriptionProvider,
                provenance.VisualAnalysisApplied, provenance.VisualAnalysisDegraded, gridWidth, gridHeight, provenance.AudioLevelsApplied,
                provenance.VisionApplied, provenance.VisionDegraded, provenance.VisionPartial, provenance.VisionProvider,
                captionedShotCountTotal, failedShotCountTotal, isMultiSource, persistedKeyframeCount);

            // The persisted artifact's OfferedIds must be exactly the ids that survived view
            // truncation — VideoCompileStepExecutor validates the model's Keep spans against this
            // list, so persisting the full pre-truncation id set here would let it accept ids the
            // model was never actually shown, defeating the id-anchored contract entirely (found
            // by Copilot review). OfferedPlacementIds/OfferedMusicIds get the exact same
            // discipline, each applied to its own SEPARATE id namespace (never merged with
            // OfferedIds or with each other).
            VideoAnalysisArtifact artifact = draftArtifact with
            {
                OfferedIds = viewOfferedIds,
                OfferedPlacementIds = viewOfferedPlacementIds,
                OfferedMusicIds = viewOfferedMusicIds,
                OfferedInsertRegionIds = viewOfferedInsertRegionIds,
                OfferedSfxIds = viewOfferedSfxIds
            };

            string artifactLocalPath = scratch.GetPath("analysis.json");
            await File.WriteAllTextAsync(
                artifactLocalPath, JsonSerializer.Serialize(artifact, ArtifactJsonOptions), context.CancellationToken);

            // fraction: 1 — UploadArtifact is always the LAST declared stage (see
            // VideoAnalyzeProgressPlan.Stage), so this is also the step's final progress report; it
            // must land at exactly 100%, not just "about to start the last stage".
            await ReportAsync(context, progress, VideoAnalyzeProgressPlan.Stage.UploadArtifact, "Uploading analysis artifact", fraction: 1);
            string artifactFileName = $"video-analysis/{context.Execution.Id:D}/step-{step.StepOrder}-analysis.json";
            string artifactStorageKey = await _workspace.UploadArtifactAsync(
                context.Execution.ProjectId, artifactLocalPath, artifactFileName, "application/json", context.CancellationToken);

            meta["artifactStorageKey"] = artifactStorageKey;

            string? expectError = EvaluateExpect(config.Expect, artifact, totalDurationSec);
            if (expectError is not null)
            {
                _logger.LogWarning(
                    "VideoAnalyze step {StepOrder} failed expect check: {Message}", step.StepOrder, expectError);
                return Failure(context, sw, "EXPECT_FAILED", expectError, artifactStorageKey);
            }

            context.RecordResolvedInput(BuildResolvedInputDescriptor(config, sources, sourceResults, viewOfferedIds.Count));

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
    // Multi-source id merge helpers
    // ---------------------------------------------------------------------

    /// <summary>
    /// Every id in this feature is "{prefix-char}{n}" (e.g. "s4", "g12") — offsets it by adding
    /// <paramref name="offset"/> to its numeric suffix. The single mechanism behind "ids stay
    /// globally unique across all sources in one artifact, assigned contiguously across every
    /// source file in analysis order" (see class doc comment).
    /// </summary>
    internal static string OffsetId(string localId, int offset)
    {
        if (offset == 0)
            return localId;

        char prefixChar = localId[0];
        int n = int.Parse(localId.AsSpan(1));
        return $"{prefixChar}{n + offset}";
    }

    /// <summary>Maps a local (per-source) id back-reference through <paramref name="shotIdMap"/> into the merged global id space; passes null through unchanged.</summary>
    private static string? MapLocalId(string? localId, IReadOnlyDictionary<string, string> shotIdMap) =>
        localId is not null && shotIdMap.TryGetValue(localId, out string? mapped) ? mapped : localId;

    /// <summary>
    /// Merges N sources' independently-computed <see cref="VideoAnalysisProvenance"/> into one
    /// artifact-level record: "applied"/"degraded" become true if ANY source applied/degraded that
    /// stage (an artifact-wide OR, not a per-source breakdown — see docs/video-editing.md
    /// "Multiple source clips" for why full per-source provenance detail is out of scope for this
    /// addition). A single source passes through completely unchanged.
    /// </summary>
    private static VideoAnalysisProvenance AggregateProvenance(IReadOnlyList<VideoAnalysisProvenance> perSource)
    {
        if (perSource.Count == 1)
            return perSource[0];

        return new VideoAnalysisProvenance(
            TranscriptionMode: perSource[0].TranscriptionMode,
            TranscriptionApplied: perSource.Any(p => p.TranscriptionApplied),
            TranscriptionDegraded: perSource.Any(p => p.TranscriptionDegraded),
            TranscriptionProvider: perSource.Select(p => p.TranscriptionProvider).FirstOrDefault(p => p is not null),
            TranscriptionLanguage: perSource[0].TranscriptionLanguage,
            AnalyzedAt: DateTime.UtcNow,
            VisualAnalysisApplied: perSource.Any(p => p.VisualAnalysisApplied),
            VisualAnalysisDegraded: perSource.Any(p => p.VisualAnalysisDegraded),
            AudioLevelsApplied: perSource.Any(p => p.AudioLevelsApplied),
            SharpnessAvailable: false,
            VisionMode: perSource[0].VisionMode,
            VisionApplied: perSource.Any(p => p.VisionApplied),
            VisionDegraded: perSource.Any(p => p.VisionDegraded),
            VisionProvider: perSource.Select(p => p.VisionProvider).FirstOrDefault(p => p is not null),
            CaptionedShotCount: perSource.Sum(p => p.CaptionedShotCount),
            VisionPartial: perSource.Any(p => p.VisionPartial),
            InsertTrackingApplied: perSource.Any(p => p.InsertTrackingApplied),
            InsertTrackingDegraded: perSource.Any(p => p.InsertTrackingDegraded),
            InsertRegionCount: perSource.Sum(p => p.InsertRegionCount));
    }

    // ---------------------------------------------------------------------
    // Per-source analysis — the exact deterministic pipeline this executor always ran (silence/
    // shot detection, transcription, Phase 1/2/3 analysis), now scoped to ONE source clip's local
    // file so ExecuteAsync can run it once per entry in the resolved source list. Every id
    // assigned in here is LOCAL to this one source (each kind restarting at 0) — ExecuteAsync's
    // merge step re-offsets them into the artifact's global id space afterward. Guardrails
    // (MaxDurationSeconds/MaxInputBytes) are enforced PER SOURCE here — each bounds the cost of
    // that one source's own ffmpeg/ASR calls, which is what they actually protect regardless of
    // how many sibling sources are also being analyzed in the same step.
    // ---------------------------------------------------------------------

    /// <summary>
    /// A cap that is genuinely STEP-wide rather than silently per-source — the exact bug §0.3
    /// found in MaxCaptionedShots. Sharpness cannot simply be hoisted to step level like
    /// captioning was, because it must run before this source's own duplicate grouping in order to
    /// feed ComputeTakeQuality. <see cref="Measured"/> is the step-level sharpness-shot-count for
    /// <see cref="VideoAnalysisProvenance.SharpnessShotCount"/>.
    /// </summary>
    private sealed class StepBudget
    {
        public int Remaining;
        public int Measured;
    }

    private sealed record SourceAnalysisResult(
        string StorageKey,
        /// <summary>
        /// Alive until <see cref="ExecuteAsync"/>'s outer <c>finally</c> disposes <c>scratch</c> —
        /// needed by the step-level vision pass (§2.1) so each shot's keyframe can still be
        /// extracted from the clip it actually came from, after every source has finished analysis.
        /// </summary>
        string LocalVideoPath,
        VideoAnalysisMedia Media,
        List<VideoAnalysisShot> Shots,
        List<VideoAnalysisSilenceSpan> Silences,
        List<VideoAnalysisSegment> Segments,
        List<VideoAnalysisWord> Words,
        List<VideoAnalysisPlacement> Placements,
        List<VideoAnalysisDuplicateGroup> DuplicateGroups,
        List<VideoInsertRegionTrack> InsertRegions,
        VideoAnalysisProvenance Provenance);

    private sealed record SourceAnalysisOutcome(SourceAnalysisResult? Result, string? ErrorCode, string? ErrorMessage)
    {
        public static SourceAnalysisOutcome Ok(SourceAnalysisResult result) => new(result, null, null);
        public static SourceAnalysisOutcome Fail(string code, string message) => new(null, code, message);
    }

    /// <summary>Every progress call site goes through this — see plan §3.3's exact call-site mapping.</summary>
    private static Task ReportAsync(
        StepExecutionContext context, VideoAnalyzeProgressPlan plan, VideoAnalyzeProgressPlan.Stage stage,
        string label, int sourceIndex = 0, double fraction = 0)
        => context.ReportProgressAsync(label, plan.Percent(stage, sourceIndex, fraction));

    /// <summary>
    /// Phase 4 (decision #1) — formats the deterministic Phase 1 measurements for one shot into a
    /// short sentence of WORDS for the vision prompt, never a number the model could restate. Null
    /// when visual analysis was off/degraded for this shot (no ColorTemperatureClass derived).
    /// </summary>
    private static string? BuildMeasuredContext(VideoAnalysisShotVisual? v)
    {
        if (v?.ColorTemperatureClass is null)
            return null;

        var parts = new List<string>
        {
            $"color temperature {v.ColorTemperatureClass}",
            $"tone curve {v.ToneClass}",
            $"saturation {v.SaturationClass}",
            $"camera {v.CameraMove}"
        };

        if (v.ActiveCrop is not null)
            parts.Add("letterboxed/pillarboxed");
        if (v.BacklitCandidate)
            parts.Add("possibly backlit (measurement is a coarse 3x3 heuristic — confirm or reject from the image)");

        return string.Join(", ", parts);
    }

    private async Task<SourceAnalysisOutcome> AnalyzeSourceAsync(
        StepExecutionContext context,
        VideoScratchSpace scratch,
        VideoAnalyzeStepConfig config,
        VideoSourceRef sourceRef,
        int sourceIndex,
        int totalSources,
        int gridWidth,
        int gridHeight,
        VideoAnalyzeProgressPlan progress,
        StepBudget sharpnessBudget,
        CancellationToken ct)
    {
        string progressPrefix = totalSources > 1 ? $"[{sourceIndex + 1}/{totalSources}] " : "";
        string filePrefix = totalSources > 1 ? $"src{sourceIndex}-" : "";

        (string? storageKey, string? resolveError) = await ResolveSourceStorageKeyAsync(context, sourceRef);
        if (storageKey is null)
            return SourceAnalysisOutcome.Fail("SOURCE_UNRESOLVED", resolveError ?? "Could not resolve the video source.");

        string localVideoPath = scratch.GetPath(filePrefix + "source" + GuessExtension(storageKey));

        await ReportAsync(context, progress, VideoAnalyzeProgressPlan.Stage.DownloadSource, progressPrefix + "Downloading source", sourceIndex);
        await _workspace.DownloadStorageKeyToFileAsync(context.Execution.ProjectId, storageKey, localVideoPath, ct);

        // Cheapest guardrail we can actually apply given IProjectFileWorkspace's surface (no
        // HEAD/size-without-download primitive): check the downloaded size against
        // MaxInputBytes before any decode (probe/silence/shot/ASR) runs. Enforced PER SOURCE —
        // each source's own ffmpeg/ASR cost is bounded by ITS OWN size, independent of how many
        // sibling sources are being analyzed in the same step.
        long fileSizeBytes = new FileInfo(localVideoPath).Length;
        if (fileSizeBytes > config.MaxInputBytes)
        {
            return SourceAnalysisOutcome.Fail(
                "INPUT_TOO_LARGE",
                $"Source video is {fileSizeBytes} bytes, exceeding MaxInputBytes={config.MaxInputBytes}.");
        }

        await ReportAsync(context, progress, VideoAnalyzeProgressPlan.Stage.ProbeSource, progressPrefix + "Probing source media", sourceIndex);
        MediaProbeResult probe = await _mediaProbe.ProbeAsync(localVideoPath, ct);

        if (config.MaxDurationSeconds > 0 && probe.DurationSec > config.MaxDurationSeconds)
        {
            return SourceAnalysisOutcome.Fail(
                "DURATION_EXCEEDED",
                $"Source video duration {probe.DurationSec:F2}s exceeds MaxDurationSeconds={config.MaxDurationSeconds}. " +
                "Bailing out before audio extraction/ASR.");
        }

        IReadOnlyList<(double StartSec, double EndSec)> silenceSpans = Array.Empty<(double, double)>();
        // Real B-roll/stock footage routinely ships with no audio stream at all (e.g. silent
        // typing/keyboard close-ups). ffmpeg's silencedetect filter operates on an audio stream
        // that doesn't exist here, and fails outright ("Output file does not contain any
        // stream") rather than degrading — the same failure mode
        // VideoCompileStepExecutor.EncodeReencodeAsync already guards against for its own no-
        // audio sources (see "Source_with_no_audio_stream_..." there). Probe already ran above,
        // so this is a free check, not an extra ffmpeg invocation.
        if (config.DetectSilence && probe.AudioCodec is not null)
        {
            await ReportAsync(context, progress, VideoAnalyzeProgressPlan.Stage.DetectSilence, progressPrefix + "Detecting silence", sourceIndex);
            silenceSpans = await _silenceDetector.DetectAsync(
                localVideoPath, config.SilenceThresholdDb, config.MinSilenceMs / 1000.0, probe.DurationSec, ct);
        }

        IReadOnlyList<(double StartSec, double EndSec)> shotSpans = Array.Empty<(double, double)>();
        if (config.DetectShots)
        {
            await ReportAsync(context, progress, VideoAnalyzeProgressPlan.Stage.DetectShots, progressPrefix + "Detecting shots", sourceIndex);
            shotSpans = await _shotDetector.DetectShotsAsync(localVideoPath, config.SceneThreshold, probe.DurationSec, ct);
        }

        TranscriptResult? transcript = null;
        bool transcriptionApplied = false;
        bool transcriptionDegraded = false;
        string? transcriptionProviderName = null;

        if (config.Transcription != VideoTranscriptionMode.Off)
        {
            try
            {
                await ReportAsync(context, progress, VideoAnalyzeProgressPlan.Stage.ResolveTranscription, progressPrefix + "Resolving transcription provider", sourceIndex);
                ResolvedTranscriptionProvider? provider =
                    await _providerResolver.ResolveTranscriptionAsync(config.TranscriptionProviderId, ct);

                if (provider is null)
                {
                    if (config.Transcription == VideoTranscriptionMode.Required)
                    {
                        return SourceAnalysisOutcome.Fail(
                            "TRANSCRIPTION_UNAVAILABLE",
                            "Transcription is Required but no transcription-capable inference provider is configured.");
                    }

                    _logger.LogInformation(
                        "VideoAnalyze step {StepOrder} source {SourceIndex}: no transcription provider resolved; degrading (Transcription=Optional).",
                        context.Step.StepOrder, sourceIndex);
                    transcriptionDegraded = true;
                }
                else
                {
                    await ReportAsync(context, progress, VideoAnalyzeProgressPlan.Stage.Transcribe, progressPrefix + "Transcribing audio", sourceIndex);
                    transcript = await TranscribeWithChunkingAsync(
                        context, scratch, filePrefix, localVideoPath, probe, silenceSpans, config, provider,
                        progress, sourceIndex, ct);
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
                _logger.LogWarning(ex, "VideoAnalyze step {StepOrder} source {SourceIndex}: transcription failed", context.Step.StepOrder, sourceIndex);
                if (config.Transcription == VideoTranscriptionMode.Required)
                    return SourceAnalysisOutcome.Fail("TRANSCRIPTION_FAILED", $"Transcription failed: {ex.Message}");

                transcriptionDegraded = true;
            }
        }

        // ---- Assign deterministic LOCAL ids by index and build the shot list ----

        List<VideoAnalysisShot> shots = shotSpans
            .Select((s, i) => new VideoAnalysisShot($"s{i}", s.StartSec, s.EndSec))
            .ToList();

        // ---- Phase 1: audio-level sampling (reuses the WAV already extracted for
        // transcription, or extracts it fresh) — never lets an exception escape this method. ----
        bool audioLevelsApplied = false;
        if (config.AnalyzeAudioLevels)
        {
            try
            {
                await ReportAsync(context, progress, VideoAnalyzeProgressPlan.Stage.SampleAudioLevels, progressPrefix + "Sampling audio levels", sourceIndex);
                string fullWavPath = scratch.GetPath($"{filePrefix}audio-full.wav");
                if (!File.Exists(fullWavPath))
                {
                    await _audioExtractor.ExtractWavAsync(localVideoPath, fullWavPath, ct);
                }

                byte[] wavBytes = await File.ReadAllBytesAsync(fullWavPath, ct);
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
                _logger.LogWarning(ex, "VideoAnalyze step {StepOrder} source {SourceIndex}: audio-level sampling failed; degrading.", context.Step.StepOrder, sourceIndex);
                audioLevelsApplied = false;
            }
        }

        // ---- Phase 1: visual scene analysis — one grid ffmpeg pass + pure C# analyzer.
        // The ENTIRE block (grid sampling + FrameGridAnalyzer calls) is wrapped so an
        // exception anywhere in here can never fail this source's analysis; shots/silences/
        // transcript are used exactly as if this stage were off. ----
        bool visualApplied = false;
        bool visualDegraded = false;
        List<VideoAnalysisDuplicateGroup> duplicateGroups = [];

        if (config.AnalyzeVisuals)
        {
            try
            {
                await ReportAsync(context, progress, VideoAnalyzeProgressPlan.Stage.SampleFrameGrid, progressPrefix + "Sampling visual analysis", sourceIndex);
                FrameGridResult grid = await _frameGridSampler.SampleAsync(
                    localVideoPath, scratch, config.VisualSampleFps, gridWidth, gridHeight,
                    probe.DurationSec, config.MaxVisualSampleFrames, ct);

                if (grid.FrameCount <= 0)
                    throw new InvalidOperationException("Grid sampler produced zero sampled frames.");

                var analyzerOptions = new FrameGridAnalyzer.Options(
                    config.StillMotionThreshold, config.MinStillWindowMs, config.MaxStillWindowsPerShot,
                    config.AnalyzeColorGrading, config.DetectLetterbox);

                var signatures = new List<FrameGridAnalyzer.ShotSignature>();
                var signatureShotIds = new List<string>();

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
                    }

                    if (VideoAnalyzeProgressPlan.ShouldReportItem(i, shots.Count))
                        await ReportAsync(
                            context, progress, VideoAnalyzeProgressPlan.Stage.AnalyzeShots,
                            progressPrefix + $"Analyzing shots ({i + 1}/{shots.Count})", sourceIndex,
                            (i + 1) / (double)shots.Count);
                }

                // Phase 4 (§5.5): sharpness — AFTER the per-shot analyze loop, BEFORE this source's
                // own duplicate grouping, so real Sharpness (when measured) reaches
                // ComputeTakeQuality below instead of only the neutral placeholder.
                if (config.DetectSharpness && sharpnessBudget.Remaining > 0)
                {
                    int patch = Math.Max(0, Math.Min(256, Math.Min(probe.Width, probe.Height)) & ~1);
                    if (patch >= 32)
                    {
                        List<VideoAnalysisShot> sharpnessTargets = shots
                            .Where(s => s.Visual is not null)
                            .OrderByDescending(s => s.EndSec - s.StartSec)
                            .Take(sharpnessBudget.Remaining)
                            .OrderBy(s => s.Id, StringComparer.Ordinal)
                            .ToList();

                        for (int k = 0; k < sharpnessTargets.Count; k++)
                        {
                            if (VideoAnalyzeProgressPlan.ShouldReportItem(k, sharpnessTargets.Count))
                                await ReportAsync(
                                    context, progress, VideoAnalyzeProgressPlan.Stage.SampleSharpness,
                                    progressPrefix + $"Measuring focus ({k + 1}/{sharpnessTargets.Count})", sourceIndex,
                                    (k + 1) / (double)sharpnessTargets.Count);

                            try
                            {
                                double? sharp = await _sharpnessSampler.MeasureAsync(
                                    localVideoPath, scratch, $"{filePrefix}sharp-{sharpnessTargets[k].Id}.gray",
                                    KeyframeSelector.ChooseKeyframeSec(sharpnessTargets[k]), patch, ct);

                                if (sharp is null)
                                    continue;

                                int idx = shots.FindIndex(s => s.Id == sharpnessTargets[k].Id);
                                if (idx < 0 || shots[idx].Visual is null)
                                    continue;

                                shots[idx] = shots[idx] with { Visual = shots[idx].Visual! with { Sharpness = sharp } };
                                sharpnessBudget.Measured++;
                            }
                            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "VideoAnalyze step {StepOrder} source {SourceIndex}: sharpness measurement failed for shot {ShotId}; leaving Sharpness null.",
                                    context.Step.StepOrder, sourceIndex, sharpnessTargets[k].Id);
                            }
                            finally
                            {
                                // Decrement on every ATTEMPT, not only on success — MeasureAsync
                                // returns null on any ffmpeg failure/missing output/short read, and
                                // leaving Remaining untouched in that case let one source burn
                                // MaxSharpnessShots attempts with zero successful measurements while
                                // the next source started with the budget still fully intact (the
                                // same per-source-instead-of-step-wide bug MaxCaptionedShots had).
                                sharpnessBudget.Remaining--;
                            }
                        }
                    }
                }

                // Built AFTER sharpness so a shot's real Sharpness (when measured) is what
                // ComputeTakeQuality actually scores, not the neutral 0.5 placeholder.
                List<double> takeQualities = signatureShotIds
                    .Select(id =>
                    {
                        VideoAnalysisShot s = shots.First(sh => sh.Id == id);
                        VideoAnalysisShotVisual v = s.Visual!;
                        return FrameGridAnalyzer.ComputeTakeQuality(
                            v.MotionStdDev, s.Audio?.RmsDbfs, v.BrightnessMean, s.EndSec - s.StartSec, v.Sharpness);
                    })
                    .ToList();

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

                if (config.DetectNearDuplicates)
                    await ReportAsync(
                        context, progress, VideoAnalyzeProgressPlan.Stage.GroupDuplicates,
                        progressPrefix + "Grouping similar takes", sourceIndex);

                visualApplied = true;
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "VideoAnalyze step {StepOrder} source {SourceIndex}: visual analysis failed; degrading.", context.Step.StepOrder, sourceIndex);
                visualDegraded = true;
            }
        }

        // ---- Phase 2: vision-LLM shot captioning was HOISTED to step level (§2.1) — it now runs
        // once in ExecuteAsync, after every source has finished this per-source pipeline, so
        // MaxCaptionedShots/VisionTimeoutSeconds are genuine STEP-wide budgets instead of being
        // silently multiplied by source count. Recorded here as neutral/false — AggregateProvenance
        // then ORs all-false vision flags across sources (harmless), and the step-level pass
        // overwrites them via `with` once captioning actually runs. ----
        const bool visionApplied = false, visionDegraded = false, visionPartial = false;
        const string? visionProviderName = null;
        const int captionedShotCount = 0;

        // ---- Tracked screen inserts (docs/video-editing.md "Tracked screen inserts (Phase 5)"):
        // deterministic chroma-plate quad tracking over a dedicated, higher-resolution grid pass.
        // The WHOLE block degrades on any exception exactly like the visual-analysis block above —
        // insert tracking must never fail an otherwise-good analysis. Ids here are LOCAL
        // ("r0"...) — remapped by ExecuteAsync's merge step. ----
        List<VideoInsertRegionTrack> insertRegions = [];
        bool insertTrackingApplied = false;
        bool insertTrackingDegraded = false;

        if (config.DetectInsertRegions)
        {
            try
            {
                await ReportAsync(context, progress, VideoAnalyzeProgressPlan.Stage.TrackInsertRegions, progressPrefix + "Tracking insert regions", sourceIndex);

                // Clamped at the point config is consumed, same convention as the Phase 1 grid
                // clamps in ExecuteAsync — an author-supplied 4096x4096 must not allocate a huge
                // raw grid buffer.
                int insertGridWidth = Math.Clamp(config.InsertGridWidth, 64, 640);
                int insertGridHeight = Math.Clamp(config.InsertGridHeight, 36, 360);

                FrameGridResult insertGrid = await _frameGridSampler.SampleAsync(
                    localVideoPath, scratch, Math.Clamp(config.InsertSampleFps, 0.5, 30.0),
                    insertGridWidth, insertGridHeight, probe.DurationSec,
                    Math.Clamp(config.MaxInsertSampleFrames, 10, 20_000), ct);

                if (insertGrid.FrameCount > 0)
                {
                    IReadOnlyList<VideoInsertRegionTrack> tracks = ChromaQuadTracker.Track(
                        insertGrid.PixelData, insertGrid.FrameCount, insertGrid.GridWidth, insertGrid.GridHeight,
                        insertGrid.EffectiveFps,
                        new ChromaQuadTracker.Options(
                            ColorName: config.InsertRegionColor,
                            MinAreaRatio: Math.Clamp(config.MinInsertRegionAreaRatio, 0.0001, 0.9),
                            MinTrackSeconds: Math.Max(0.1, config.MinInsertRegionSeconds),
                            MaxRegions: Math.Clamp(config.MaxInsertRegions, 0, 32)));

                    // ShotId is view-legibility only (mirrors SilenceSpan.AfterShot) — the track's
                    // midpoint decides which shot "owns" it.
                    insertRegions = tracks
                        .Select(t => t with { ShotId = FindShotFor(shots, (t.StartSec + t.EndSec) / 2) })
                        .ToList();
                }

                insertTrackingApplied = true;
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "VideoAnalyze step {StepOrder} source {SourceIndex}: insert-region tracking failed; degrading.", context.Step.StepOrder, sourceIndex);
                insertRegions = [];
                insertTrackingDegraded = true;
            }
        }

        // ---- Phase 3: deterministic overlay-placement candidates — derived entirely from Phase
        // 1's per-shot Visual/Regions data, so only computed when visual analysis actually
        // succeeded (never when it degraded/was off). Ids here are LOCAL ("p0"...) — remapped by
        // ExecuteAsync's merge step. ----
        List<VideoAnalysisPlacement> placements = config.EmitOverlayPlacements && visualApplied
            ? OverlayPlacementBuilder.BuildPlacements(shots, config.MaxPlacementsPerShot, config.MaxPlacements, config.MaxTimeSlicesPerRegion).ToList()
            : [];

        List<VideoAnalysisSilenceSpan> silences = silenceSpans
            .Select((s, i) => new VideoAnalysisSilenceSpan($"g{i}", s.StartSec, s.EndSec, FindAfterShot(shots, s.StartSec)))
            .ToList();

        List<VideoAnalysisSegment> segments = (transcript?.Segments ?? (IReadOnlyList<TranscriptSegment>)Array.Empty<TranscriptSegment>())
            .Select((seg, i) => new VideoAnalysisSegment($"t{i}", FindShotFor(shots, seg.StartSec), seg.StartSec, seg.EndSec, seg.Text))
            .ToList();

        List<VideoAnalysisWord> words = (transcript?.Words ?? (IReadOnlyList<TranscriptWord>)Array.Empty<TranscriptWord>())
            .Select((w, i) => new VideoAnalysisWord($"w{i}", w.StartSec, w.EndSec, w.Text))
            .ToList();

        VideoAnalysisMedia media = new(probe.DurationSec, probe.FpsNum, probe.FpsDen, probe.Width, probe.Height);

        VideoAnalysisProvenance provenance = new(
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
            VisionPartial: visionPartial,
            InsertTrackingApplied: insertTrackingApplied,
            InsertTrackingDegraded: insertTrackingDegraded,
            InsertRegionCount: insertRegions.Count);

        return SourceAnalysisOutcome.Ok(new SourceAnalysisResult(
            storageKey, localVideoPath, media, shots, silences, segments, words, placements, duplicateGroups, insertRegions, provenance));
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
    /// <paramref name="scratchFilePrefix"/> namespaces the extracted wav/chunk scratch files by
    /// source (e.g. "src1-") so concurrent... rather, SEQUENTIAL per-source runs never collide on
    /// the same scratch filename.
    /// </summary>
    private async Task<TranscriptResult> TranscribeWithChunkingAsync(
        StepExecutionContext context,
        VideoScratchSpace scratch,
        string scratchFilePrefix,
        string localVideoPath,
        MediaProbeResult probe,
        IReadOnlyList<(double StartSec, double EndSec)> silenceSpans,
        VideoAnalyzeStepConfig config,
        ResolvedTranscriptionProvider provider,
        VideoAnalyzeProgressPlan progress,
        int sourceIndex,
        CancellationToken ct)
    {
        string fullWavPath = scratch.GetPath($"{scratchFilePrefix}audio-full.wav");
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
            TranscriptResult result = await TranscribeWithRetryAsync(
                client, fullWavPath, "audio.wav", config.Language, config.WordTimestamps, ct);

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

                await ReportAsync(
                    context, progress, VideoAnalyzeProgressPlan.Stage.Transcribe,
                    $"Transcribing audio (chunk {chunkIndex + 1}/{chunks.Count})",
                    sourceIndex, chunkIndex / (double)chunks.Count);

                string chunkPath = scratch.GetPath($"{scratchFilePrefix}audio-chunk-{chunkIndex}.wav");
                await _audioExtractor.ExtractWavRangeAsync(localVideoPath, chunkPath, chunkStartSec, chunkEndSec, ct);

                TranscriptResult chunkResult = await TranscribeWithRetryAsync(
                    client, chunkPath, $"chunk-{chunkIndex}.wav", config.Language, config.WordTimestamps, ct);

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
    ///
    /// Takes a file PATH, not an open stream: the transcription SDK's HTTP call owns and disposes
    /// whatever stream it's handed once that call completes (success or failure), so a stream
    /// reused across attempts throws ObjectDisposedException ("Cannot access a closed file") on
    /// any retry after attempt 1 — masking the original transient error entirely. Opening a fresh
    /// stream per attempt avoids that.
    /// </summary>
    private static async Task<TranscriptResult> TranscribeWithRetryAsync(
        ITranscriptionClient client, string wavPath, string fileName, string? language, bool wordTimestamps, CancellationToken ct)
    {
        Exception? last = null;
        for (int attempt = 1; attempt <= AsrMaxAttempts; attempt++)
        {
            try
            {
                await using FileStream wav = new(wavPath, FileMode.Open, FileAccess.Read, FileShare.Read);
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
        double zcrSum = 0;
        var overlappingRms = new List<double>();
        foreach (WavRmsSampler.RmsWindow w in windows)
        {
            double overlap = Overlap(w.StartSec, w.EndSec, shot.StartSec, shot.EndSec);
            if (overlap <= 0)
                continue;

            double linearEnergy = Math.Pow(10, w.RmsDbfs / 10.0);
            energySum += linearEnergy * overlap;
            weightSum += overlap;
            zcrSum += w.ZeroCrossingRate * overlap;
            overlappingRms.Add(w.RmsDbfs);
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

        ShotAudioAnalyzer.Result ch = ShotAudioAnalyzer.Analyze(
            rmsDbfs, peakDbfs, speechRatio, overlappingRms, weightSum > 0 ? zcrSum / weightSum : 0);

        return new VideoAnalysisShotAudio(rmsDbfs, peakDbfs, speechRatio, loudnessClass,
            ch.CrestFactorDb, ch.NoiseFloorDbfs, ch.LevelStability, ch.ZeroCrossingRate,
            ch.CharacterClass, ch.CharacterConfidence);
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

    private static (JsonObject View, JsonObject Meta, List<string> OfferedIds, List<string> OfferedPlacementIds, List<string> OfferedMusicIds, List<string> OfferedInsertRegionIds, List<string> OfferedSfxIds) BuildBoundedView(
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
        int failedShotCount,
        bool isMultiSource,
        int persistedKeyframeCount = 0)
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

        bool lookUniform = artifact.Provenance.LookUniform;

        // Sentence-boundary facts are derived from the artifact's FULL segment texts, never from
        // the (possibly MaxSegmentTextChars-truncated) copies in `viewSegments` — see
        // TranscriptPunctuation. Computed once here and threaded through every BuildView attempt
        // of the degrade/drop loop below rather than recomputed per attempt.
        HashSet<string> endsSentenceIds = artifact.Segments
            .Where(s => TranscriptPunctuation.EndsSentence(s.Text))
            .Select(s => s.Id)
            .ToHashSet(StringComparer.Ordinal);

        IReadOnlyDictionary<int, TranscriptPunctuation.Stats> punctuationBySource =
            TranscriptPunctuation.SummarizeBySource(artifact.Segments);

        foreach (VideoVisualDetail detail in levelsToTry)
        {
            view = BuildView(artifact, offered, detail, config, isMultiSource, lookUniform, endsSentenceIds);
            serialized = view.ToJsonString(EnvelopeJsonOptions);
            detailApplied = detail;
            if (serialized.Length <= maxOutputChars)
                break;
        }

        // Background music (see docs/video-editing.md "Background music"): view.musicTracks is
        // shown as ONE ATOMIC ARRAY, deliberately NOT gated on VisualDetail (music tracks have
        // nothing to do with visual detail). It is dropped as a whole array — after detail has
        // already degraded all the way to None, before the per-item drop loop below ever runs —
        // if the view still does not fit. This is a strictly separate budget concern from
        // OfferedIds/detail, mirroring the discipline placements already established.
        bool musicTracksSuppressed = false;
        if (serialized.Length > maxOutputChars && artifact.MusicCandidates is { Count: > 0 })
        {
            JsonObject suppressedView = BuildView(artifact, offered, detailApplied, config, isMultiSource, lookUniform, endsSentenceIds, suppressMusicTracks: true);
            string suppressedSerialized = suppressedView.ToJsonString(EnvelopeJsonOptions);
            if (suppressedSerialized.Length < serialized.Length)
            {
                view = suppressedView;
                serialized = suppressedSerialized;
                musicTracksSuppressed = true;
            }
        }

        // Sound-effect clips: same atomic-array budget discipline as musicTracks just above —
        // suppressed as a whole only after detail has fully degraded and musicTracks was already
        // tried, and always before any offered item is dropped. Suppressed AFTER musicTracks
        // (SFX is the newer, more optional layer) and BEFORE insertRegions.
        bool sfxClipsSuppressed = false;
        if (serialized.Length > maxOutputChars && artifact.SfxCandidates is { Count: > 0 })
        {
            JsonObject suppressedView = BuildView(
                artifact, offered, detailApplied, config, isMultiSource, lookUniform, endsSentenceIds,
                suppressMusicTracks: musicTracksSuppressed, suppressSfxClips: true);
            string suppressedSerialized = suppressedView.ToJsonString(EnvelopeJsonOptions);
            if (suppressedSerialized.Length < serialized.Length)
            {
                view = suppressedView;
                serialized = suppressedSerialized;
                sfxClipsSuppressed = true;
            }
        }

        // Tracked screen inserts: same atomic-array budget discipline as musicTracks just above —
        // suppressed as a whole only after detail has fully degraded and music was already
        // suppressed, and always before any offered item is dropped.
        bool insertRegionsSuppressed = false;
        if (serialized.Length > maxOutputChars && artifact.InsertRegions is { Count: > 0 })
        {
            JsonObject suppressedView = BuildView(
                artifact, offered, detailApplied, config, isMultiSource, lookUniform, endsSentenceIds,
                suppressMusicTracks: musicTracksSuppressed, suppressInsertRegions: true, suppressSfxClips: sfxClipsSuppressed);
            string suppressedSerialized = suppressedView.ToJsonString(EnvelopeJsonOptions);
            if (suppressedSerialized.Length < serialized.Length)
            {
                view = suppressedView;
                serialized = suppressedSerialized;
                insertRegionsSuppressed = true;
            }
        }

        // Never truncate mid-JSON: drop whole trailing items (from the end of the combined,
        // priority-ordered list) and re-serialize until the view fits the char budget. Only
        // reached once the lowest-tried detail level (and, if applicable, music-track suppression)
        // still doesn't fit.
        while (serialized.Length > maxOutputChars && offered.Count > 0)
        {
            offered.RemoveAt(offered.Count - 1);
            view = BuildView(artifact, offered, detailApplied, config, isMultiSource, lookUniform, endsSentenceIds, suppressMusicTracks: musicTracksSuppressed, suppressInsertRegions: insertRegionsSuppressed, suppressSfxClips: sfxClipsSuppressed);
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

        // Background music: "offered" means exactly "the whole musicTracks array survived to the
        // final view" — empty whenever it was suppressed for budget, regardless of VisualDetail.
        List<string> offeredMusicIds = !musicTracksSuppressed
            ? (artifact.MusicCandidates?.Select(m => m.Id).ToList() ?? [])
            : [];

        // Tracked screen inserts: "offered" means exactly "the whole insertRegions array survived
        // to the final view" — empty whenever it was suppressed for budget, regardless of
        // VisualDetail (the musicTracks discipline, since insert regions come from their own grid
        // pass and are independent of Phase 1 visual analysis).
        List<string> offeredInsertRegionIds = !insertRegionsSuppressed
            ? (artifact.InsertRegions?.Select(r => r.Id).ToList() ?? [])
            : [];

        // Sound-effect clips: "offered" means exactly "the whole sfxClips array survived to the
        // final view" — empty whenever it was suppressed for budget, regardless of VisualDetail
        // (the exact musicTracks discipline).
        List<string> offeredSfxIds = !sfxClipsSuppressed
            ? (artifact.SfxCandidates?.Select(c => c.Id).ToList() ?? [])
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
                ["degraded"] = transcriptionDegraded,
                // How much the per-segment "endsSentence" flag can be TRUSTED, per source clip —
                // one entry per source that produced transcript segments, empty when there is no
                // transcript at all. See TranscriptPunctuation: some ASR deployments punctuate
                // only a small minority of segments, and on such a source a missing full stop
                // says nothing about whether a thought is complete.
                ["punctuated"] = PunctuationNode(punctuationBySource)
            },
            ["visual"] = new JsonObject
            {
                ["applied"] = visualApplied,
                ["degraded"] = visualDegraded,
                ["sampleFps"] = config.VisualSampleFps,
                ["gridWidth"] = visualGridWidth,
                ["gridHeight"] = visualGridHeight,
                ["detail"] = detailApplied.ToString(),
                ["colorGrading"] = artifact.Provenance.ColorGradingApplied,
                ["letterbox"] = artifact.Provenance.LetterboxDetectionApplied
            },
            ["visualDetailApplied"] = detailApplied.ToString(),
            ["audioLevels"] = new JsonObject { ["applied"] = audioLevelsApplied, ["character"] = audioLevelsApplied },
            ["look"] = new JsonObject
            {
                ["applied"] = artifact.Provenance.LookGroupingApplied,
                ["groups"] = artifact.Provenance.LookGroupCount,
                ["uniform"] = artifact.Provenance.LookUniform
            },
            ["vision"] = new JsonObject
            {
                ["mode"] = config.Vision.ToString(),
                ["applied"] = visionApplied,
                ["provider"] = visionProviderName,
                ["degraded"] = visionDegraded,
                ["partial"] = visionPartial,
                ["captionedShots"] = captionedShotCount,
                ["failedShots"] = failedShotCount,
                ["persistedKeyframes"] = persistedKeyframeCount
            },
            ["sourceChars"] = artifact.Shots.Count + artifact.SilenceSpans.Count + artifact.Segments.Count + artifact.Words.Count,
            ["outputChars"] = serialized.Length,
            ["offeredPlacementIdCount"] = offeredPlacementIds.Count,
            ["offeredMusicTrackCount"] = offeredMusicIds.Count
        };

        // Sound effects — gated on the step actually configuring SFX candidates (the newer
        // insertTracking discipline rather than offeredMusicTrackCount's unconditional one), so
        // an OfferSfxClips=false run's meta shape is byte-identical to before this addition.
        if (config.OfferSfxClips)
            meta["offeredSfxClipCount"] = offeredSfxIds.Count;

        // Tracked screen inserts — only ever present when the step actually configured tracking,
        // so a DetectInsertRegions=false run's meta shape is byte-identical to before this
        // addition (the same gating discipline isMultiSource uses just below).
        if (config.DetectInsertRegions)
        {
            meta["insertTracking"] = new JsonObject
            {
                ["applied"] = artifact.Provenance.InsertTrackingApplied,
                ["degraded"] = artifact.Provenance.InsertTrackingDegraded,
                ["regions"] = artifact.Provenance.InsertRegionCount
            };
            meta["offeredInsertRegionIdCount"] = offeredInsertRegionIds.Count;
        }

        // Multi-source addition — only ever present when more than one source was analyzed, so a
        // single-source view's meta shape is byte-identical to before this addition.
        if (isMultiSource)
            meta["sourceCount"] = artifact.Sources?.Count ?? 1;

        return (view, meta, offeredIds, offeredPlacementIds, offeredMusicIds, offeredInsertRegionIds, offeredSfxIds);
    }

    private static JsonObject BuildView(
        VideoAnalysisArtifact artifact, List<OfferedItem> items, VideoVisualDetail detail, VideoAnalyzeStepConfig config, bool isMultiSource,
        bool lookUniform, IReadOnlySet<string> endsSentenceIds, bool suppressMusicTracks = false, bool suppressInsertRegions = false,
        bool suppressSfxClips = false)
    {
        var view = new JsonObject
        {
            ["media"] = MediaNode(artifact.Media),
            ["shots"] = ToArray(items.Where(i => i.Kind == "shot").Select(i => ShotNode(i.Shot!, detail, config.StillMotionThreshold, isMultiSource, lookUniform))),
            ["silences"] = ToArray(items.Where(i => i.Kind == "silence").Select(i => SilenceNode(i.Silence!, isMultiSource))),
            ["segments"] = ToArray(items.Where(i => i.Kind == "segment").Select(i => SegmentNode(i.Segment!, isMultiSource, endsSentenceIds)))
        };

        // Multi-source addition — one entry per analyzed clip, so the story-editor agent knows how
        // many "src" values it may see and (when it matters) each clip's own duration/dimensions.
        // Gated on isMultiSource for the exact same byte-identical-for-one-source reason every
        // other new key here is gated.
        if (isMultiSource && artifact.Sources is { Count: > 0 } sources)
        {
            view["sources"] = new JsonArray(sources.Select(s => (JsonNode)new JsonObject
            {
                ["src"] = s.SourceIndex,
                ["durationSec"] = s.Media.DurationSec
            }).ToArray());
        }

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
                view["placements"] = PlacementsNode(artifact.Placements, isMultiSource);

            if (artifact.LookGroups is { Count: > 0 })
                view["lookGroups"] = LookGroupsNode(artifact.LookGroups, config.MaxViewLookGroups);
        }

        // Background music (see docs/video-editing.md "Background music"): deliberately
        // UNCONDITIONAL (not inside the `detail != None` block above) — music tracks have nothing
        // to do with visual detail, so this key's presence must never depend on VisualDetail. It IS
        // dropped as a whole array when the caller (BuildBoundedView) determines it must suppress
        // it for budget, via suppressMusicTracks.
        if (!suppressMusicTracks && artifact.MusicCandidates is { Count: > 0 })
            view["musicTracks"] = MusicTracksNode(artifact.MusicCandidates);

        // Sound effects (see docs/video-editing.md "Sound effects"): the exact same UNCONDITIONAL
        // discipline as musicTracks directly above — SFX clip candidates have nothing to do with
        // visual detail, so this key's presence must never depend on VisualDetail. Dropped as one
        // atomic array when the caller determines it must suppress it for budget, via
        // suppressSfxClips.
        if (!suppressSfxClips && artifact.SfxCandidates is { Count: > 0 })
            view["sfxClips"] = SfxClipsNode(artifact.SfxCandidates);

        // Tracked screen inserts: same UNCONDITIONAL discipline as musicTracks (deliberately NOT
        // the detail-gated placements one) — insert regions come from their own dedicated grid
        // pass and exist regardless of whether Phase 1 visual analysis ran at all, so their
        // presence must never depend on VisualDetail. Dropped as one atomic array when the caller
        // determines it must suppress them for budget, via suppressInsertRegions.
        if (!suppressInsertRegions && artifact.InsertRegions is { Count: > 0 })
            view["insertRegions"] = InsertRegionsNode(artifact.InsertRegions, isMultiSource);

        return view;
    }

    /// <summary>
    /// Deliberately small: no <c>ProjectFileId</c>, no storage key, no size — the model needs the
    /// name to choose and the id to reference; anything else is unnecessary surface area.
    /// </summary>
    private static JsonArray MusicTracksNode(IReadOnlyList<VideoAnalysisMusicCandidate> candidates) =>
        new(candidates.Select(m => (JsonNode)new JsonObject
        {
            ["id"] = m.Id,
            ["name"] = m.FileName
        }).ToArray());

    /// <summary>
    /// Deliberately as small as <see cref="MusicTracksNode"/>: the model needs the name to choose
    /// and the id to reference; anything else is unnecessary surface area.
    /// </summary>
    private static JsonArray SfxClipsNode(IReadOnlyList<VideoAnalysisSfxCandidate> candidates) =>
        new(candidates.Select(c => (JsonNode)new JsonObject
        {
            ["id"] = c.Id,
            ["name"] = c.FileName
        }).ToArray());

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
    /// is byte-identical to the original across the board. <paramref name="isMultiSource"/> gates
    /// the new <c>"src"</c> key the exact same way every other multi-source addition in this view
    /// is gated — a single-source view never gains this key.
    /// </summary>
    private static JsonObject ShotNode(VideoAnalysisShot s, VideoVisualDetail detail, double stillMotionThreshold, bool isMultiSource, bool lookUniform)
    {
        var node = new JsonObject
        {
            ["id"] = s.Id,
            ["startSec"] = s.StartSec,
            ["endSec"] = s.EndSec,
            ["durationSec"] = s.EndSec - s.StartSec
        };

        if (isMultiSource)
            node["src"] = s.SourceIndex;

        if (detail == VideoVisualDetail.None)
            return node;

        if (s.Visual is not null)
            node["v"] = VisualNode(s.Visual, detail, stillMotionThreshold, lookUniform);

        if (s.Audio is not null)
            node["a"] = AudioNode(s.Audio, detail);

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
            ["mood"] = c.Mood,
            // Phase 4: how the shot is graded/finished — a usability signal the story editor
            // should weigh on every take comparison, so it belongs at Compact.
            ["style"] = c.VisualStyle
        };

        if (c.Tags.Count > 0)
            node["tags"] = new JsonArray(c.Tags.Select(t => (JsonNode)t).ToArray());

        // Phase 4: visible technical defects — only present when non-empty, same "absence is
        // never a signal" convention the rest of this view follows.
        if (c.TechnicalIssues.Count > 0)
            node["issues"] = new JsonArray(c.TechnicalIssues.Select(t => (JsonNode)t).ToArray());

        if (detail == VideoVisualDetail.Full)
        {
            if (c.Subjects.Count > 0)
                node["subjects"] = new JsonArray(c.Subjects.Select(t => (JsonNode)t).ToArray());

            node["action"] = c.Action;
            node["setting"] = c.Setting;
            node["cameraAngle"] = c.CameraAngle;

            if (c.OnScreenText.Count > 0)
                node["onScreenText"] = new JsonArray(c.OnScreenText.Select(t => (JsonNode)t).ToArray());

            node["timeOfDay"] = c.TimeOfDay;
            node["lighting"] = c.Lighting;
            node["framing"] = c.Framing;
        }

        return node;
    }

    private static JsonObject VisualNode(VideoAnalysisShotVisual v, VideoVisualDetail detail, double stillMotionThreshold, bool lookUniform)
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

        // Phase 4 (D4): singleton (LookGroupId null) and uniform (one group covers every analyzed
        // shot — the single-camera-talking-head case) both suppress this key. Exact precedent:
        // DuplicateGroupId == null above.
        if (v.LookGroupId is not null && !lookUniform)
            node["look"] = v.LookGroupId;

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

            // Phase 4 (D1-D3): only present once a class was actually derived (AnalyzeColorGrading on).
            if (v.ColorTemperatureClass is not null)
            {
                node["temp"] = v.ColorTemperatureClass;
                node["tone"] = v.ToneClass;
                node["sat"] = v.SaturationClass;
                node["black"] = Score(v.BlackPoint);
                node["white"] = Score(v.WhitePoint);
            }

            // Phase 4 (D6): only present when a real letterbox/pillarbox bar was detected.
            if (v.ActiveCrop is not null)
            {
                node["crop"] = new JsonObject
                {
                    ["x"] = Score(v.ActiveCrop.X),
                    ["y"] = Score(v.ActiveCrop.Y),
                    ["w"] = Score(v.ActiveCrop.W),
                    ["h"] = Score(v.ActiveCrop.H)
                };
            }

            // Phase 4 (D7): only present when true — same "-Candidate" precedent as kenBurns above.
            if (v.BacklitCandidate)
                node["backlit"] = true;

            // Phase 4 (§5.4): only present when a real sharpness measurement exists.
            if (v.Sharpness is not null)
                node["sharp"] = Score(v.Sharpness.Value);

            // Phase 4 (D4): rank within the look group, present exactly when "look" is (i.e. never
            // shown for a singleton or under a uniform-look suppression).
            if (v.LookGroupId is not null && !lookUniform && v.LookRank is not null)
                node["lookRank"] = v.LookRank.Value;
        }

        return node;
    }

    private static JsonObject AudioNode(VideoAnalysisShotAudio a, VideoVisualDetail detail)
    {
        var node = new JsonObject
        {
            ["rms"] = (int)Math.Round(a.RmsDbfs),
            ["speech"] = Score(a.SpeechRatio),
            // ALWAYS emitted, including the common "Dialogue" value: the absence of a key must
            // never be a signal in this view (decision #2; see docs/video-editing.md's
            // degrade-before-drop invariant).
            ["char"] = a.AudioCharacterClass ?? "Dialogue"
        };

        if (detail == VideoVisualDetail.Full)
        {
            node["crest"] = (int)Math.Round(a.CrestFactorDb);
            node["floor"] = (int)Math.Round(a.NoiseFloorDbfs);
            node["steady"] = Score(a.LevelStability);
            node["charFit"] = Score(a.AudioCharacterConfidence);
        }

        return node;
    }

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
    /// Phase 4 (D4) — dereferences each shot's opaque <c>"look"</c> id to a few descriptive words.
    /// Placement here (inside the same <c>detail != None</c> block as <c>duplicateGroups</c>) is
    /// the whole degrade-before-drop compliance story for this addition and must not move: it
    /// vanishes at <c>None</c> exactly like <c>pacing</c>/<c>duplicateGroups</c>/<c>placements</c>.
    /// </summary>
    private static JsonArray LookGroupsNode(IReadOnlyList<VideoAnalysisLookGroup> groups, int max) =>
        new(groups.Take(Math.Max(0, max)).Select(g =>
        {
            var node = new JsonObject
            {
                ["id"] = g.Id,
                ["shotIds"] = new JsonArray(g.ShotIds.Select(id => (JsonNode)id).ToArray()),
                ["repShotId"] = g.RepresentativeShotId,
                ["cohesion"] = Score(g.Cohesion)
            };
            if (g.ColorTemperatureClass is not null)
                node["temp"] = g.ColorTemperatureClass;
            if (g.ToneClass is not null)
                node["tone"] = g.ToneClass;
            if (g.SaturationClass is not null)
                node["sat"] = g.SaturationClass;
            return (JsonNode)node;
        }).ToArray());

    /// <summary>
    /// Phase 3's <c>view.placements</c> — deliberately small/budget-friendly (no rect, no anchor
    /// kind): a downstream motion-graphics planner only needs "which candidate ids exist, WHEN
    /// each one sits on the source timeline, how good is each, is it a light- or dark-text
    /// region", never the geometry that <c>VideoCompileStepExecutor</c> alone resolves from the
    /// full artifact.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>startSec</c>/<c>endSec</c> (named to match <see cref="SegmentNode"/>'s existing
    /// transcript-segment convention, and rounded like every other Phase 1+ number in this view)
    /// are NOT a loosening of the no-timestamp rule: that rule governs model OUTPUT only —
    /// <c>MotionGraphicsPlanOutput</c> still has no time-bearing property at all, and
    /// <c>VideoCompileStepExecutor</c> still resolves an overlay's real window from the full
    /// artifact's unrounded <see cref="VideoAnalysisPlacement.StartSec"/>/<c>EndSec</c>, never
    /// from anything shown here. Transcript segments already expose the same two fields as input
    /// to <c>VideoStoryEditor</c>.
    /// </para>
    /// <para>
    /// Without them the view was genuinely undecidable: <c>MaxTimeSlicesPerRegion</c> splits one
    /// long shot's region into several candidate sub-windows, so several placements on a single
    /// static shot (a talking head, say) serialized byte-identically except for their id — the
    /// planner had literally no information on which to pick one over another and defaulted to
    /// the first of each identical run. The time window is the only thing that distinguishes
    /// them, and it is also what lets the planner match an overlay to the transcript segment
    /// (same <c>startSec</c>/<c>endSec</c> units, same source timeline) whose content it is
    /// supposed to support.
    /// </para>
    /// </remarks>
    private static JsonArray PlacementsNode(IReadOnlyList<VideoAnalysisPlacement> placements, bool isMultiSource) =>
        new(placements.Select(p =>
        {
            var node = new JsonObject
            {
                ["id"] = p.Id,
                ["shotId"] = p.ShotId,
                ["region"] = p.Region,
                ["startSec"] = Round2(p.StartSec),
                ["endSec"] = Round2(p.EndSec),
                ["fit"] = Score(p.Suitability),
                ["text"] = p.TextColor
            };
            if (isMultiSource)
                node["src"] = p.SourceIndex;
            return (JsonNode)node;
        }).ToArray());

    /// <summary>
    /// Tracked screen inserts' <c>view.insertRegions</c> — deliberately QUALITATIVE: an opaque id,
    /// the owning shot, the source-timeline window (input-only, same precedent/rounding as
    /// <see cref="PlacementsNode"/>'s <c>startSec</c>/<c>endSec</c>), bucketed confidence/size/
    /// motion words, and a rounded aspect hint for shaping the rendered content. The per-frame
    /// corner tracking data — the actual numbers — is NEVER shown here: it lives only in the full
    /// artifact, where <c>VideoCompileStepExecutor</c> alone resolves a chosen id back to it.
    /// </summary>
    private static JsonArray InsertRegionsNode(IReadOnlyList<VideoInsertRegionTrack> regions, bool isMultiSource) =>
        new(regions.Select(r =>
        {
            var node = new JsonObject
            {
                ["id"] = r.Id,
                ["shotId"] = r.ShotId,
                ["startSec"] = Round2(r.StartSec),
                ["endSec"] = Round2(r.EndSec),
                ["conf"] = r.Confidence >= 0.75 ? "high" : r.Confidence >= 0.5 ? "medium" : "low",
                ["size"] = r.MeanAreaRatio >= 0.15 ? "large" : r.MeanAreaRatio >= 0.04 ? "medium" : "small",
                ["motion"] = r.MotionClass,
                ["aspect"] = Round2(r.MeanAspectRatio),
                ["color"] = r.ColorName
            };
            if (isMultiSource)
                node["src"] = r.SourceIndex;
            return (JsonNode)node;
        }).ToArray());

    /// <summary>
    /// <c>meta.transcription.punctuated</c> — one entry per source clip that produced transcript
    /// segments, in source-index order: <c>{src, segments, punctuatedSegments, ratio, reliable}</c>.
    /// Always an array (never a scalar) regardless of source count, so a consumer never has to
    /// branch on single- vs multi-source to read it; the "src" key is present in every entry for
    /// the same reason, even though a single-source view's segment nodes carry no "src".
    /// </summary>
    private static JsonArray PunctuationNode(IReadOnlyDictionary<int, TranscriptPunctuation.Stats> bySource) =>
        new(bySource
            .OrderBy(kv => kv.Key)
            .Select(kv => (JsonNode)new JsonObject
            {
                ["src"] = kv.Key,
                ["segments"] = kv.Value.SegmentCount,
                ["punctuatedSegments"] = kv.Value.PunctuatedCount,
                ["ratio"] = kv.Value.Ratio is { } ratio ? (JsonNode)TranscriptPunctuation.Round(ratio) : null,
                ["reliable"] = kv.Value.Reliable
            })
            .ToArray());

    private static JsonObject SilenceNode(VideoAnalysisSilenceSpan s, bool isMultiSource)
    {
        var node = new JsonObject
        {
            ["id"] = s.Id,
            ["startSec"] = s.StartSec,
            ["endSec"] = s.EndSec,
            ["durationSec"] = s.EndSec - s.StartSec,
            ["afterShot"] = s.AfterShot
        };
        if (isMultiSource)
            node["src"] = s.SourceIndex;
        return node;
    }

    /// <summary>
    /// <paramref name="endsSentenceIds"/> carries the per-segment sentence-boundary flag computed
    /// from the FULL artifact text (see <see cref="TranscriptPunctuation"/>) — deliberately not
    /// recomputed from <paramref name="s"/>.Text here, because the copies reaching this method have
    /// already been truncated to <c>MaxSegmentTextChars</c> and truncation can chop off the very
    /// full stop the flag is about.
    /// </summary>
    private static JsonObject SegmentNode(VideoAnalysisSegment s, bool isMultiSource, IReadOnlySet<string> endsSentenceIds)
    {
        var node = new JsonObject
        {
            ["id"] = s.Id,
            ["shot"] = s.Shot,
            ["startSec"] = s.StartSec,
            ["endSec"] = s.EndSec,
            ["text"] = s.Text,
            ["endsSentence"] = endsSentenceIds.Contains(s.Id)
        };
        if (isMultiSource)
            node["src"] = s.SourceIndex;
        return node;
    }

    /// <summary>Seconds rounded to 2dp — used only for the new Phase 1 visual/pacing numbers.</summary>
    private static double Round2(double seconds) => Math.Round(seconds, 2);

    /// <summary>A 0..1 score rounded to a 0..100 integer — used only for the new Phase 1 visual/pacing numbers.</summary>
    private static int Score(double value01) => (int)Math.Round(Math.Clamp(value01, 0, 1) * 100);

    // ---------------------------------------------------------------------
    // Expectation / failure / descriptor helpers
    // ---------------------------------------------------------------------

    private static string? EvaluateExpect(VideoAnalyzeExpectation? expect, VideoAnalysisArtifact artifact, double totalDurationSec)
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

        if (expect.MaxSilenceRatio.HasValue && totalDurationSec > 0)
        {
            double silenceSeconds = artifact.SilenceSpans.Sum(s => Math.Max(0, s.EndSec - s.StartSec));
            double ratio = silenceSeconds / totalDurationSec;
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

    private static string BuildResolvedInputDescriptor(
        VideoAnalyzeStepConfig config, IReadOnlyList<VideoSourceRef> sources, IReadOnlyList<SourceAnalysisResult>? results, int? offeredIdCount = null)
    {
        var descriptor = new JsonObject
        {
            ["sources"] = new JsonArray(sources.Select((s, i) => (JsonNode)new JsonObject
            {
                ["kind"] = s.Kind.ToString(),
                ["storageKey"] = results is not null && i < results.Count ? results[i].StorageKey : null
            }).ToArray()),
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
