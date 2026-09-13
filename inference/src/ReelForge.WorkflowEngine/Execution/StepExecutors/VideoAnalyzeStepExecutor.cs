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
/// source video into shots/silence gaps/(optional) transcript, persisted as a full analysis
/// artifact plus a bounded, id-anchored "{view, meta}" prompt envelope for the downstream
/// VideoStoryEditor agent step. See plan §4.1/§3.
///
/// Purity/safety discipline mirrors <see cref="ExtractStepExecutor"/> exactly: this executor
/// never throws — every path, including an unexpected exception, returns a
/// <see cref="StepExecutionResult"/> whose Output is valid JSON (WorkflowStepResult.OutputJson is
/// a jsonb column). The one piece of network I/O (ASR) gets its own small bounded retry
/// internally; the whole step is never retried by the outer executor-retry mechanism
/// (see WorkflowExecutorService.ResolveMaxRetries) since re-running it just reproduces the same
/// deterministic failure at the cost of minutes of decode.
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

    private readonly IMediaProbe _mediaProbe;
    private readonly ISilenceDetector _silenceDetector;
    private readonly IShotDetector _shotDetector;
    private readonly IAudioExtractor _audioExtractor;
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

            // ---- Assign deterministic ids by index and build the full artifact ----

            List<VideoAnalysisShot> shots = shotSpans
                .Select((s, i) => new VideoAnalysisShot($"s{i}", s.StartSec, s.EndSec))
                .ToList();

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
            // Media/counts only, never OfferedIds, so a placeholder here is safe — see the
            // corrected artifact constructed after the view is built).
            VideoAnalysisArtifact draftArtifact = new(
                Version: 1,
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
                    AnalyzedAt: DateTime.UtcNow));

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
            (JsonObject view, JsonObject meta, List<string> viewOfferedIds) = BuildBoundedView(
                draftArtifact, viewSegments, config, artifactStorageKey: string.Empty,
                transcriptionApplied, transcriptionDegraded, transcriptionProviderName);

            // The persisted artifact's OfferedIds must be exactly the ids that survived view
            // truncation — VideoCompileStepExecutor validates the model's Keep spans against this
            // list, so persisting the full pre-truncation id set here would let it accept ids the
            // model was never actually shown, defeating the id-anchored contract entirely (found
            // by Copilot review).
            VideoAnalysisArtifact artifact = draftArtifact with { OfferedIds = viewOfferedIds };

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
    // Id linking helpers
    // ---------------------------------------------------------------------

    private static string? FindAfterShot(IReadOnlyList<VideoAnalysisShot> shots, double silenceStartSec) =>
        shots.LastOrDefault(s => s.StartSec <= silenceStartSec)?.Id;

    private static string? FindShotFor(IReadOnlyList<VideoAnalysisShot> shots, double timeSec) =>
        shots.LastOrDefault(s => s.StartSec <= timeSec)?.Id;

    // ---------------------------------------------------------------------
    // Bounded view construction (Extract's exact truncation discipline)
    // ---------------------------------------------------------------------

    private static (JsonObject View, JsonObject Meta, List<string> OfferedIds) BuildBoundedView(
        VideoAnalysisArtifact artifact,
        List<VideoAnalysisSegment> viewSegments,
        VideoAnalyzeStepConfig config,
        string artifactStorageKey,
        bool transcriptionApplied,
        bool transcriptionDegraded,
        string? transcriptionProviderName)
    {
        // Combined offer order: shots, then silences, then segments — matches the order ids are
        // assigned in the full artifact and the order the view example in the plan renders them.
        var offered = new List<(string Kind, string Id, JsonObject Node)>();
        foreach (VideoAnalysisShot s in artifact.Shots)
            offered.Add(("shot", s.Id, ShotNode(s)));
        foreach (VideoAnalysisSilenceSpan s in artifact.SilenceSpans)
            offered.Add(("silence", s.Id, SilenceNode(s)));
        foreach (VideoAnalysisSegment s in viewSegments)
            offered.Add(("segment", s.Id, SegmentNode(s)));

        int totalItemCount = offered.Count;

        int maxViewSegments = Math.Max(0, config.MaxViewSegments);
        if (offered.Count > maxViewSegments)
            offered = offered.Take(maxViewSegments).ToList();

        int maxOutputChars = Math.Clamp(config.MaxOutputChars, 256, 200_000);

        JsonObject BuildView(IReadOnlyList<(string Kind, string Id, JsonObject Node)> items) => new()
        {
            ["media"] = MediaNode(artifact.Media),
            ["shots"] = ToArray(items.Where(i => i.Kind == "shot")),
            ["silences"] = ToArray(items.Where(i => i.Kind == "silence")),
            ["segments"] = ToArray(items.Where(i => i.Kind == "segment"))
        };

        JsonObject view = BuildView(offered);
        string serialized = view.ToJsonString(EnvelopeJsonOptions);

        // Never truncate mid-JSON: drop whole trailing items (from the end of the combined,
        // priority-ordered list) and re-serialize until the view fits the char budget.
        while (serialized.Length > maxOutputChars && offered.Count > 0)
        {
            offered.RemoveAt(offered.Count - 1);
            view = BuildView(offered);
            serialized = view.ToJsonString(EnvelopeJsonOptions);
        }

        List<string> offeredIds = offered.Select(i => i.Id).ToList();
        int droppedItems = Math.Max(0, totalItemCount - offeredIds.Count);
        bool truncated = droppedItems > 0;

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
            ["sourceChars"] = artifact.Shots.Count + artifact.SilenceSpans.Count + artifact.Segments.Count + artifact.Words.Count,
            ["outputChars"] = serialized.Length
        };

        return (view, meta, offeredIds);
    }

    private static JsonArray ToArray(IEnumerable<(string Kind, string Id, JsonObject Node)> items)
    {
        var array = new JsonArray();
        foreach ((string _, string _, JsonObject node) in items)
            array.Add(node.DeepClone());
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

    private static JsonObject ShotNode(VideoAnalysisShot s) => new()
    {
        ["id"] = s.Id,
        ["startSec"] = s.StartSec,
        ["endSec"] = s.EndSec,
        ["durationSec"] = s.EndSec - s.StartSec
    };

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
            ["transcription"] = config.Transcription.ToString()
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
