using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Inference;
using ReelForge.Shared.Workflows;
using ReelForge.WorkflowEngine.Execution;
using ReelForge.WorkflowEngine.Execution.StepExecutors;
using ReelForge.WorkflowEngine.Services.Storage;
using ReelForge.WorkflowEngine.Services.Video;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Tests for <see cref="VideoAnalyzeStepExecutor"/> (plan §4.1/§3, WS5 DoD). Covers the bounded
/// prompt-view budget discipline (never truncate mid-JSON, offeredIds matches exactly what made
/// it into the view), the three transcription modes' failure/degrade behaviour, the
/// never-throws/always-valid-JSON contract, and — the single most likely correctness bug in this
/// feature (plan R10) — that ASR chunk-relative timestamps are correctly offset back to absolute
/// time before they reach the artifact/view.
/// </summary>
public class VideoAnalyzeStepExecutorTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private const string SourceVideoKey = "projects/p/outputFiles/e/render.mp4";

    // ---------------------------------------------------------------------
    // View budget / truncation discipline
    // ---------------------------------------------------------------------

    [Fact]
    public async Task MaxOutputChars_forces_whole_item_drops_and_output_is_always_valid_json()
    {
        var shots = Enumerable.Range(0, 500)
            .Select(i => ((double)i * 2, (double)(i * 2 + 1)))
            .ToArray();

        StepExecutionContext context = CreateContext(
            out Mock<IProjectFileWorkspace> workspace,
            out Mock<IMediaProbe> probe,
            out Mock<ISilenceDetector> silence,
            out Mock<IShotDetector> shotDetector,
            out _, out _,
            configOverride: cfg => cfg with { DetectSilence = false, MaxOutputChars = 800, MaxViewSegments = 1000, Transcription = VideoTranscriptionMode.Off });

        probe.Setup(p => p.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaProbeResult(1000, 30, 1, 1920, 1080, "h264", "aac", 48000));
        shotDetector
            .Setup(s => s.DetectShotsAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(shots);

        StepExecutionResult result = await CreateExecutor(workspace, probe, silence, shotDetector, out _, out _).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed);

        Action parse = () => JsonDocument.Parse(result.Output);
        parse.Should().NotThrow("the executor must never emit invalid/truncated-mid-JSON output");

        using JsonDocument doc = JsonDocument.Parse(result.Output);
        JsonElement meta = doc.RootElement.GetProperty("meta");
        meta.GetProperty("truncated").GetBoolean().Should().BeTrue();
        meta.GetProperty("droppedItems").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Persisted_artifact_OfferedIds_matches_the_truncated_view_not_the_full_analysis()
    {
        // Regression test (found by Copilot review): the persisted artifact's OfferedIds must be
        // exactly the ids that survived MaxOutputChars/MaxViewSegments truncation, since
        // VideoCompileStepExecutor validates a Keep span's ids against this exact list. If the
        // full pre-truncation id set were persisted instead, the story-editor agent could
        // reference an id it was never actually shown, defeating the id-anchored contract.
        var shots = Enumerable.Range(0, 500)
            .Select(i => ((double)i * 2, (double)(i * 2 + 1)))
            .ToArray();

        StepExecutionContext context = CreateContext(
            out Mock<IProjectFileWorkspace> workspace,
            out Mock<IMediaProbe> probe,
            out Mock<ISilenceDetector> silence,
            out Mock<IShotDetector> shotDetector,
            out _, out _,
            configOverride: cfg => cfg with { DetectSilence = false, MaxOutputChars = 800, MaxViewSegments = 1000, Transcription = VideoTranscriptionMode.Off });

        probe.Setup(p => p.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaProbeResult(1000, 30, 1, 1920, 1080, "h264", "aac", 48000));
        shotDetector
            .Setup(s => s.DetectShotsAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(shots);

        VideoAnalyzeStepExecutor executor = CreateExecutor(workspace, probe, silence, shotDetector, out _, out _);

        // Added AFTER CreateExecutor's own default UploadArtifactAsync setup so this capturing
        // setup is the most recently defined one and wins the match.
        string? capturedArtifactJson = null;
        workspace
            .Setup(w => w.UploadArtifactAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .Callback<Guid, string, string, string, CancellationToken, string>((_, path, _, _, _, _) =>
                capturedArtifactJson = File.ReadAllText(path))
            .ReturnsAsync("projects/p/agentFiles/video-analysis/e/step-1-analysis.json");

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);

        using JsonDocument outputDoc = JsonDocument.Parse(result.Output);
        JsonElement view = outputDoc.RootElement.GetProperty("view");
        JsonElement meta = outputDoc.RootElement.GetProperty("meta");
        int viewItemCount = view.GetProperty("shots").GetArrayLength()
            + view.GetProperty("silences").GetArrayLength()
            + view.GetProperty("segments").GetArrayLength();

        meta.GetProperty("truncated").GetBoolean().Should().BeTrue("500 shots must not fit an 800-char budget");
        viewItemCount.Should().BeLessThan(500);

        capturedArtifactJson.Should().NotBeNull();
        using JsonDocument artifactDoc = JsonDocument.Parse(capturedArtifactJson!);
        int persistedOfferedIdCount = artifactDoc.RootElement.GetProperty("offeredIds").GetArrayLength();

        // The bug: this used to be 500 (every shot in the full analysis) regardless of truncation.
        persistedOfferedIdCount.Should().Be(viewItemCount,
            "the persisted OfferedIds must match exactly what the model was shown, not the full pre-truncation analysis");
        persistedOfferedIdCount.Should().BeLessThan(500);
    }

    [Fact]
    public async Task OfferedIdCount_matches_exactly_what_made_it_into_the_trimmed_view()
    {
        var shots = Enumerable.Range(0, 200)
            .Select(i => ((double)i, (double)i + 0.5))
            .ToArray();

        StepExecutionContext context = CreateContext(
            out Mock<IProjectFileWorkspace> workspace,
            out Mock<IMediaProbe> probe,
            out Mock<ISilenceDetector> silence,
            out Mock<IShotDetector> shotDetector,
            out _, out _,
            configOverride: cfg => cfg with { DetectSilence = false, MaxOutputChars = 2000, MaxViewSegments = 1000, Transcription = VideoTranscriptionMode.Off });

        probe.Setup(p => p.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaProbeResult(1000, 30, 1, 1920, 1080, "h264", "aac", 48000));
        shotDetector
            .Setup(s => s.DetectShotsAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(shots);

        StepExecutionResult result = await CreateExecutor(workspace, probe, silence, shotDetector, out _, out _).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed);

        using JsonDocument doc = JsonDocument.Parse(result.Output);
        JsonElement view = doc.RootElement.GetProperty("view");
        JsonElement meta = doc.RootElement.GetProperty("meta");

        int actualOfferedInView = view.GetProperty("shots").GetArrayLength()
            + view.GetProperty("silences").GetArrayLength()
            + view.GetProperty("segments").GetArrayLength();

        meta.GetProperty("offeredIdCount").GetInt32().Should().Be(actualOfferedInView);
    }

    // ---------------------------------------------------------------------
    // Transcription modes
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Optional_transcription_degrades_cleanly_when_no_provider_resolves()
    {
        StepExecutionContext context = CreateContext(
            out Mock<IProjectFileWorkspace> workspace,
            out Mock<IMediaProbe> probe,
            out Mock<ISilenceDetector> silence,
            out Mock<IShotDetector> shotDetector,
            out Mock<ITranscriptionClientFactory> transcriptionFactory,
            out Mock<IInferenceProviderResolver> providerResolver,
            configOverride: cfg => cfg with { Transcription = VideoTranscriptionMode.Optional, DetectSilence = false, DetectShots = false });

        SetupBasicProbeAndDetectors(probe, silence, shotDetector);
        providerResolver
            .Setup(r => r.ResolveTranscriptionAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResolvedTranscriptionProvider?)null);

        StepExecutionResult result = await CreateExecutor(workspace, probe, silence, shotDetector, transcriptionFactory, providerResolver)
            .ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed);
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        JsonElement transcription = doc.RootElement.GetProperty("meta").GetProperty("transcription");
        transcription.GetProperty("applied").GetBoolean().Should().BeFalse();
        transcription.GetProperty("degraded").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Required_transcription_fails_clearly_when_no_provider_resolves()
    {
        StepExecutionContext context = CreateContext(
            out Mock<IProjectFileWorkspace> workspace,
            out Mock<IMediaProbe> probe,
            out Mock<ISilenceDetector> silence,
            out Mock<IShotDetector> shotDetector,
            out Mock<ITranscriptionClientFactory> transcriptionFactory,
            out Mock<IInferenceProviderResolver> providerResolver,
            configOverride: cfg => cfg with { Transcription = VideoTranscriptionMode.Required, DetectSilence = false, DetectShots = false });

        SetupBasicProbeAndDetectors(probe, silence, shotDetector);
        providerResolver
            .Setup(r => r.ResolveTranscriptionAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResolvedTranscriptionProvider?)null);

        StepExecutionResult result = await CreateExecutor(workspace, probe, silence, shotDetector, transcriptionFactory, providerResolver)
            .ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("TRANSCRIPTION_UNAVAILABLE");
    }

    // ---------------------------------------------------------------------
    // Never throws / always valid JSON
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Unexpected_exception_during_probe_is_caught_and_returns_valid_json_failure()
    {
        StepExecutionContext context = CreateContext(
            out Mock<IProjectFileWorkspace> workspace,
            out Mock<IMediaProbe> probe,
            out Mock<ISilenceDetector> silence,
            out Mock<IShotDetector> shotDetector,
            out _, out _,
            configOverride: cfg => cfg with { Transcription = VideoTranscriptionMode.Off });

        probe.Setup(p => p.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ffprobe exploded"));

        StepExecutionResult result = await CreateExecutor(workspace, probe, silence, shotDetector, out _, out _).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        Action parse = () => JsonDocument.Parse(result.Output);
        parse.Should().NotThrow();
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("UNEXPECTED_ERROR");
    }

    [Fact]
    public async Task Missing_config_fails_with_valid_json_never_throws()
    {
        var step = new WorkflowStep
        {
            Id = Guid.NewGuid(),
            StepOrder = 1,
            StepType = StepType.VideoAnalyze,
            VideoAnalyzeConfigJson = null
        };

        var context = new StepExecutionContext
        {
            Execution = new WorkflowExecution { Id = Guid.NewGuid(), ProjectId = ProjectId },
            Step = step,
            AllSteps = [step],
            AccumulatedOutput = string.Empty,
            StepOutputHistory = [],
            CurrentStepIndex = 0,
            IterationCount = 0,
            CorrelationId = "test",
            CancellationToken = CancellationToken.None
        };

        StepExecutionResult result = await CreateExecutor(
            new Mock<IProjectFileWorkspace>(), new Mock<IMediaProbe>(), new Mock<ISilenceDetector>(),
            new Mock<IShotDetector>(), out _, out _).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        Action parse = () => JsonDocument.Parse(result.Output);
        parse.Should().NotThrow();
    }

    // ---------------------------------------------------------------------
    // ASR chunk-offset arithmetic (plan R10 — the single most likely correctness bug here)
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Transcription_offsets_chunk_relative_timestamps_to_absolute_time()
    {
        StepExecutionContext context = CreateContext(
            out Mock<IProjectFileWorkspace> workspace,
            out Mock<IMediaProbe> probe,
            out Mock<ISilenceDetector> silence,
            out Mock<IShotDetector> shotDetector,
            out Mock<ITranscriptionClientFactory> transcriptionFactory,
            out Mock<IInferenceProviderResolver> providerResolver,
            configOverride: cfg => cfg with
            {
                Transcription = VideoTranscriptionMode.Required,
                DetectSilence = false,
                DetectShots = false,
                MaxAsrChunkBytes = 400, // forces multiple chunks given the 1000-byte fake wav below
                MaxOutputChars = 24000,
                MaxViewSegments = 100
            });

        // Duration 20s; no silence spans, so TranscriptChunkPlanner falls back to hard cuts at the
        // ideal boundary. bytesPerSecond = 1000/20 = 50; idealChunkSec = 400/50 = 8s.
        // => chunks: [0,8), [8,16), [16,20).
        probe.Setup(p => p.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaProbeResult(20, 30, 1, 1920, 1080, "h264", "aac", 48000));
        silence.Setup(s => s.DetectAsync(
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(double, double)>());
        shotDetector
            .Setup(s => s.DetectShotsAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(double, double)>());

        var audioExtractor = new Mock<IAudioExtractor>();
        audioExtractor
            .Setup(a => a.ExtractWavAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, CancellationToken>((_, outPath, _) =>
            {
                File.WriteAllBytes(outPath, new byte[1000]);
                return Task.CompletedTask;
            });
        audioExtractor
            .Setup(a => a.ExtractWavRangeAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, double, double, CancellationToken>((_, outPath, _, _, _) =>
            {
                File.WriteAllBytes(outPath, new byte[10]);
                return Task.CompletedTask;
            });

        ResolvedTranscriptionProvider provider = new(
            ProviderId: Guid.NewGuid(), Name: "whisper-local", Kind: InferenceProviderKind.OpenAICompatible,
            Endpoint: "http://localhost:9999", ModelName: "whisper-1", ApiKey: "", TimeoutSeconds: 30);
        providerResolver
            .Setup(r => r.ResolveTranscriptionAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(provider);

        // Chunk-relative results per chunk, returned in call order. Chunk starts (per the planner
        // above): 0, 8, 16.
        var chunkResults = new Queue<TranscriptResult>(new[]
        {
            new TranscriptResult("hello", new[] { new TranscriptSegment("hello", 0.0, 2.0) },
                new[] { new TranscriptWord("hello", 0.0, 2.0) }, "en"),
            new TranscriptResult("world", new[] { new TranscriptSegment("world", 1.0, 3.0) },
                new[] { new TranscriptWord("world", 1.0, 3.0) }, "en"),
            new TranscriptResult("!", new[] { new TranscriptSegment("!", 0.0, 1.0) },
                new[] { new TranscriptWord("!", 0.0, 1.0) }, "en")
        });

        var transcriptionClient = new Mock<ITranscriptionClient>();
        transcriptionClient
            .Setup(c => c.TranscribeAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => chunkResults.Dequeue());

        transcriptionFactory
            .Setup(f => f.Get(It.IsAny<ResolvedTranscriptionProvider>()))
            .Returns(transcriptionClient.Object);

        VideoAnalyzeStepExecutor executor = CreateExecutor(
            workspace, probe, silence, shotDetector, transcriptionFactory, providerResolver, audioExtractor);

        // Added AFTER CreateExecutor's own default UploadArtifactAsync setup so this
        // capturing setup is the most recently defined one and wins the match (Moq resolves
        // overlapping setups in most-recently-defined order).
        string? capturedArtifactJson = null;
        workspace
            .Setup(w => w.UploadArtifactAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .Callback<Guid, string, string, string, CancellationToken, string>((_, path, _, _, _, _) =>
                capturedArtifactJson = File.ReadAllText(path))
            .ReturnsAsync("projects/p/agentFiles/video-analysis/e/step-1-analysis.json");

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);

        capturedArtifactJson.Should().NotBeNull();
        using JsonDocument artifactDoc = JsonDocument.Parse(capturedArtifactJson!);
        JsonElement segments = artifactDoc.RootElement.GetProperty("segments");
        JsonElement words = artifactDoc.RootElement.GetProperty("words");

        segments.GetArrayLength().Should().Be(3);
        // chunk0 starts at 0 -> +0 offset
        segments[0].GetProperty("startSec").GetDouble().Should().BeApproximately(0.0, 1e-6);
        segments[0].GetProperty("endSec").GetDouble().Should().BeApproximately(2.0, 1e-6);
        // chunk1 starts at 8 -> 1+8=9, 3+8=11
        segments[1].GetProperty("startSec").GetDouble().Should().BeApproximately(9.0, 1e-6);
        segments[1].GetProperty("endSec").GetDouble().Should().BeApproximately(11.0, 1e-6);
        // chunk2 starts at 16 -> 0+16=16, 1+16=17
        segments[2].GetProperty("startSec").GetDouble().Should().BeApproximately(16.0, 1e-6);
        segments[2].GetProperty("endSec").GetDouble().Should().BeApproximately(17.0, 1e-6);

        words.GetArrayLength().Should().Be(3);
        words[1].GetProperty("startSec").GetDouble().Should().BeApproximately(9.0, 1e-6);
        words[2].GetProperty("startSec").GetDouble().Should().BeApproximately(16.0, 1e-6);
    }

    // =======================================================================
    // Test infrastructure
    // =======================================================================

    private static void SetupBasicProbeAndDetectors(
        Mock<IMediaProbe> probe, Mock<ISilenceDetector> silence, Mock<IShotDetector> shotDetector)
    {
        probe.Setup(p => p.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaProbeResult(30, 30, 1, 1920, 1080, "h264", "aac", 48000));
        silence.Setup(s => s.DetectAsync(
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(double, double)>());
        shotDetector
            .Setup(s => s.DetectShotsAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(double, double)>());
    }

    private static VideoAnalyzeStepExecutor CreateExecutor(
        Mock<IProjectFileWorkspace> workspace,
        Mock<IMediaProbe> probe,
        Mock<ISilenceDetector> silence,
        Mock<IShotDetector> shotDetector,
        out Mock<ITranscriptionClientFactory> transcriptionFactory,
        out Mock<IInferenceProviderResolver> providerResolver)
    {
        transcriptionFactory = new Mock<ITranscriptionClientFactory>();
        providerResolver = new Mock<IInferenceProviderResolver>();
        return CreateExecutor(workspace, probe, silence, shotDetector, transcriptionFactory, providerResolver, null, null);
    }

    private static VideoAnalyzeStepExecutor CreateExecutor(
        Mock<IProjectFileWorkspace> workspace,
        Mock<IMediaProbe> probe,
        Mock<ISilenceDetector> silence,
        Mock<IShotDetector> shotDetector,
        Mock<ITranscriptionClientFactory> transcriptionFactory,
        Mock<IInferenceProviderResolver> providerResolver,
        Mock<IAudioExtractor>? audioExtractor = null,
        Mock<IFrameGridSampler>? frameGridSampler = null,
        Mock<IKeyframeExtractor>? keyframeExtractor = null,
        Mock<IShotCaptioner>? shotCaptioner = null)
    {
        // Only install the default 100-byte wav behaviour when the caller did not supply its own
        // audioExtractor mock — installing it unconditionally would add a setup AFTER a caller's
        // own ExtractWavAsync setup and win the match (Moq resolves overlapping setups in
        // most-recently-defined order), silently discarding e.g. the ASR chunk-offset test's
        // deliberately larger fake wav.
        if (audioExtractor is null)
        {
            audioExtractor = new Mock<IAudioExtractor>();
            audioExtractor
                .Setup(a => a.ExtractWavAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns<string, string, CancellationToken>((_, outPath, _) =>
                {
                    File.WriteAllBytes(outPath, new byte[100]);
                    return Task.CompletedTask;
                });
        }

        // Left unconfigured by default: Moq returns a completed Task wrapping default(FrameGridResult)
        // (null) for an unset Task<T>-returning method, which the executor's visual-analysis
        // try/catch turns into a clean degrade (visualApplied=false) — most tests don't care about
        // Phase 1 visual data and shouldn't need to configure this mock at all.
        frameGridSampler ??= new Mock<IFrameGridSampler>();

        // Phase 2: left unconfigured by default too — every test that doesn't explicitly set
        // Vision to Optional/Required (i.e. almost all of them, since the config default is
        // VideoVisionMode.Off) never calls either mock at all, so an unconfigured strict-enough
        // default is fine here.
        keyframeExtractor ??= new Mock<IKeyframeExtractor>();
        shotCaptioner ??= new Mock<IShotCaptioner>();

        string tempScratchRoot = Path.Combine(Path.GetTempPath(), "video-analyze-tests", Guid.NewGuid().ToString("N"));
        var options = Options.Create(new VideoEditingOptions
        {
            ScratchPath = tempScratchRoot,
            MaxConcurrentJobs = 1,
            AnalyzeTimeoutSeconds = 30,
            CompileTimeoutSeconds = 30
        });

        // Default artifact-upload behaviour; tests needing to inspect the uploaded artifact add
        // their own Setup on `workspace` AFTER calling CreateExecutor so theirs is the
        // most-recently-defined (and therefore winning) match.
        workspace
            .Setup(w => w.UploadArtifactAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .ReturnsAsync("projects/p/agentFiles/video-analysis/e/step-1-analysis.json");

        return new VideoAnalyzeStepExecutor(
            probe.Object,
            silence.Object,
            shotDetector.Object,
            audioExtractor.Object,
            frameGridSampler.Object,
            keyframeExtractor.Object,
            shotCaptioner.Object,
            workspace.Object,
            transcriptionFactory.Object,
            providerResolver.Object,
            options,
            NullLogger<VideoAnalyzeStepExecutor>.Instance);
    }

    private static StepExecutionContext CreateContext(
        out Mock<IProjectFileWorkspace> workspace,
        out Mock<IMediaProbe> probe,
        out Mock<ISilenceDetector> silence,
        out Mock<IShotDetector> shotDetector,
        out Mock<ITranscriptionClientFactory> transcriptionFactory,
        out Mock<IInferenceProviderResolver> providerResolver,
        Func<VideoAnalyzeStepConfig, VideoAnalyzeStepConfig>? configOverride = null)
    {
        workspace = new Mock<IProjectFileWorkspace>();
        probe = new Mock<IMediaProbe>();
        silence = new Mock<ISilenceDetector>();
        shotDetector = new Mock<IShotDetector>();
        transcriptionFactory = new Mock<ITranscriptionClientFactory>();
        providerResolver = new Mock<IInferenceProviderResolver>();

        workspace
            .Setup(w => w.DownloadStorageKeyToFileAsync(
                It.IsAny<Guid>(), SourceVideoKey, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, string, CancellationToken>((_, _, destPath, _) =>
            {
                File.WriteAllBytes(destPath, new byte[] { 0x00, 0x01, 0x02 });
                return Task.CompletedTask;
            });

        VideoAnalyzeStepConfig config = new(
            Version: 1,
            Source: new VideoSourceRef(VideoSourceKind.PreviousStepOutput));

        if (configOverride is not null)
            config = configOverride(config);

        string configJson = JsonSerializer.Serialize(config, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        });

        var step = new WorkflowStep
        {
            Id = Guid.NewGuid(),
            StepOrder = 1,
            StepType = StepType.VideoAnalyze,
            VideoAnalyzeConfigJson = configJson
        };

        List<StepOutputHistoryEntry> history =
        [
            new StepOutputHistoryEntry(0, "Render", "{}", OutputStorageKey: SourceVideoKey, ArtifactStorageKey: null)
        ];

        return new StepExecutionContext
        {
            Execution = new WorkflowExecution { Id = Guid.NewGuid(), ProjectId = ProjectId },
            Step = step,
            AllSteps = [step],
            AccumulatedOutput = string.Empty,
            StepOutputHistory = history,
            CurrentStepIndex = 0,
            IterationCount = 0,
            CorrelationId = "test",
            CancellationToken = CancellationToken.None
        };
    }

    // =======================================================================
    // Phase 1: visual/audio scene analysis
    // =======================================================================

    /// <summary>
    /// Builds a synthetic grid buffer with genuinely varying content (a bright band that drifts
    /// over time) so MotionMean/Brightness/etc. are non-degenerate — real ffmpeg output is never
    /// involved.
    /// </summary>
    private static FrameGridResult BuildSyntheticGrid(int gridWidth, int gridHeight, int frameCount, double fps)
    {
        int frameSize = gridWidth * gridHeight * 3;
        var pixels = new byte[frameCount * frameSize];
        var rnd = new Random(42);

        for (int f = 0; f < frameCount; f++)
        {
            int brightRow = f % gridHeight;
            for (int y = 0; y < gridHeight; y++)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    int idx = (f * frameSize) + (y * gridWidth + x) * 3;
                    byte baseVal = (byte)(40 + rnd.Next(0, 10));
                    byte val = y == brightRow ? (byte)220 : baseVal;
                    pixels[idx] = val;
                    pixels[idx + 1] = val;
                    pixels[idx + 2] = val;
                }
            }
        }

        return new FrameGridResult(pixels, gridWidth, gridHeight, fps, frameCount);
    }

    [Fact]
    public async Task Grid_sampler_failure_degrades_cleanly_with_no_v_key_on_any_shot()
    {
        var shots = Enumerable.Range(0, 5)
            .Select(i => ((double)i, (double)i + 1))
            .ToArray();

        StepExecutionContext context = CreateContext(
            out Mock<IProjectFileWorkspace> workspace,
            out Mock<IMediaProbe> probe,
            out Mock<ISilenceDetector> silence,
            out Mock<IShotDetector> shotDetector,
            out _, out _,
            configOverride: cfg => cfg with { DetectSilence = false, Transcription = VideoTranscriptionMode.Off });

        probe.Setup(p => p.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaProbeResult(5, 30, 1, 1920, 1080, "h264", "aac", 48000));
        shotDetector
            .Setup(s => s.DetectShotsAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(shots);

        var frameGridSampler = new Mock<IFrameGridSampler>();
        frameGridSampler
            .Setup(g => g.SampleAsync(
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ffmpeg grid sampling exploded"));

        VideoAnalyzeStepExecutor executor = CreateExecutor(
            workspace, probe, silence, shotDetector,
            new Mock<ITranscriptionClientFactory>(), new Mock<IInferenceProviderResolver>(),
            audioExtractor: null, frameGridSampler: frameGridSampler);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);

        using JsonDocument doc = JsonDocument.Parse(result.Output);
        JsonElement meta = doc.RootElement.GetProperty("meta");
        meta.GetProperty("visual").GetProperty("applied").GetBoolean().Should().BeFalse();
        meta.GetProperty("visual").GetProperty("degraded").GetBoolean().Should().BeTrue();

        JsonElement viewShots = doc.RootElement.GetProperty("view").GetProperty("shots");
        viewShots.GetArrayLength().Should().Be(5);
        foreach (JsonElement shot in viewShots.EnumerateArray())
        {
            shot.TryGetProperty("v", out _).Should().BeFalse();
            shot.TryGetProperty("a", out _).Should().BeFalse();
        }
    }

    [Fact]
    public async Task VisualDetail_degrades_before_items_are_dropped()
    {
        // The single most important correctness property of Phase 1 (per the plan): with a
        // budget too small even for None-detail shots, VisualDetail=Full must degrade down to
        // None BEFORE dropping any offered item — so offeredIdCount at the end must be identical
        // to a plain AnalyzeVisuals=false run at the same MaxOutputChars, never smaller.
        var shots = Enumerable.Range(0, 50)
            .Select(i => ((double)i, (double)i + 1))
            .ToArray();

        FrameGridResult grid = BuildSyntheticGrid(gridWidth: 4, gridHeight: 4, frameCount: 100, fps: 2.0);

        async Task<int> RunAndGetOfferedIdCountAsync(Func<VideoAnalyzeStepConfig, VideoAnalyzeStepConfig> configOverride, bool withGrid)
        {
            StepExecutionContext ctx = CreateContext(
                out Mock<IProjectFileWorkspace> workspace,
                out Mock<IMediaProbe> probe,
                out Mock<ISilenceDetector> silence,
                out Mock<IShotDetector> shotDetector,
                out _, out _,
                configOverride: configOverride);

            probe.Setup(p => p.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MediaProbeResult(50, 30, 1, 1920, 1080, "h264", "aac", 48000));
            shotDetector
                .Setup(s => s.DetectShotsAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(shots);

            var frameGridSampler = new Mock<IFrameGridSampler>();
            if (withGrid)
            {
                frameGridSampler
                    .Setup(g => g.SampleAsync(
                        It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<int>(),
                        It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(grid);
            }

            VideoAnalyzeStepExecutor executor = CreateExecutor(
                workspace, probe, silence, shotDetector,
                new Mock<ITranscriptionClientFactory>(), new Mock<IInferenceProviderResolver>(),
                audioExtractor: null, frameGridSampler: frameGridSampler);

            StepExecutionResult result = await executor.ExecuteAsync(ctx);
            result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);

            using JsonDocument doc = JsonDocument.Parse(result.Output);
            return doc.RootElement.GetProperty("meta").GetProperty("offeredIdCount").GetInt32();
        }

        int offeredWithFullDetail = await RunAndGetOfferedIdCountAsync(
            cfg => cfg with
            {
                DetectSilence = false, Transcription = VideoTranscriptionMode.Off,
                AnalyzeVisuals = true, VisualDetail = VideoVisualDetail.Full,
                AnalyzeAudioLevels = false, DetectNearDuplicates = false,
                MaxOutputChars = 700, MaxViewSegments = 1000
            },
            withGrid: true);

        int offeredWithVisualsOff = await RunAndGetOfferedIdCountAsync(
            cfg => cfg with
            {
                DetectSilence = false, Transcription = VideoTranscriptionMode.Off,
                AnalyzeVisuals = false,
                AnalyzeAudioLevels = false, DetectNearDuplicates = false,
                MaxOutputChars = 700, MaxViewSegments = 1000
            },
            withGrid: false);

        offeredWithFullDetail.Should().Be(offeredWithVisualsOff,
            "richer per-shot visual data must never reduce how many items the model is shown at " +
            "the same MaxOutputChars — detail must degrade before items drop");
    }

    [Fact]
    public async Task Visual_and_audio_numbers_in_the_view_are_rounded()
    {
        var shots = new[] { (0.0, 5.0) };

        StepExecutionContext context = CreateContext(
            out Mock<IProjectFileWorkspace> workspace,
            out Mock<IMediaProbe> probe,
            out Mock<ISilenceDetector> silence,
            out Mock<IShotDetector> shotDetector,
            out _, out _,
            configOverride: cfg => cfg with
            {
                DetectSilence = false, Transcription = VideoTranscriptionMode.Off,
                AnalyzeVisuals = true, VisualDetail = VideoVisualDetail.Full,
                AnalyzeAudioLevels = true, DetectNearDuplicates = false,
                MaxOutputChars = 24_000
            });

        probe.Setup(p => p.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaProbeResult(5, 30, 1, 1920, 1080, "h264", "aac", 48000));
        shotDetector
            .Setup(s => s.DetectShotsAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(shots);

        FrameGridResult grid = BuildSyntheticGrid(gridWidth: 4, gridHeight: 4, frameCount: 10, fps: 2.0);
        var frameGridSampler = new Mock<IFrameGridSampler>();
        frameGridSampler
            .Setup(g => g.SampleAsync(
                It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(grid);

        var audioExtractor = new Mock<IAudioExtractor>();
        audioExtractor
            .Setup(a => a.ExtractWavAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, CancellationToken>((_, outPath, _) =>
            {
                File.WriteAllBytes(outPath, BuildSyntheticWav());
                return Task.CompletedTask;
            });

        VideoAnalyzeStepExecutor executor = CreateExecutor(
            workspace, probe, silence, shotDetector,
            new Mock<ITranscriptionClientFactory>(), new Mock<IInferenceProviderResolver>(),
            audioExtractor: audioExtractor, frameGridSampler: frameGridSampler);

        StepExecutionResult result = await executor.ExecuteAsync(context);
        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);

        using JsonDocument doc = JsonDocument.Parse(result.Output);
        JsonElement shot0 = doc.RootElement.GetProperty("view").GetProperty("shots")[0];
        shot0.TryGetProperty("v", out JsonElement v).Should().BeTrue();

        // 0..1 scores must serialize as plain 0..100 integers, not 15-17 char double noise.
        v.GetProperty("motion").ValueKind.Should().Be(JsonValueKind.Number);
        double motion = v.GetProperty("motion").GetDouble();
        motion.Should().Be(Math.Floor(motion));
        motion.Should().BeInRange(0, 100);

        double bright = v.GetProperty("bright").GetDouble();
        bright.Should().Be(Math.Floor(bright));
        bright.Should().BeInRange(0, 100);

        if (v.TryGetProperty("still", out JsonElement still) && still.GetArrayLength() > 0)
        {
            double startSec = still[0].GetProperty("startSec").GetDouble();
            Math.Round(startSec, 2).Should().Be(startSec);
        }

        if (shot0.TryGetProperty("a", out JsonElement a))
        {
            a.GetProperty("rms").GetDouble().Should().Be(Math.Floor(a.GetProperty("rms").GetDouble()));
            double speech = a.GetProperty("speech").GetDouble();
            speech.Should().Be(Math.Floor(speech));
        }
    }

    [Fact]
    public async Task AnalyzeVisuals_false_preserves_todays_exact_shape()
    {
        var shots = new[] { (0.0, 2.0), (2.0, 4.0) };

        StepExecutionContext context = CreateContext(
            out Mock<IProjectFileWorkspace> workspace,
            out Mock<IMediaProbe> probe,
            out Mock<ISilenceDetector> silence,
            out Mock<IShotDetector> shotDetector,
            out _, out _,
            configOverride: cfg => cfg with
            {
                DetectSilence = false, Transcription = VideoTranscriptionMode.Off,
                AnalyzeVisuals = false, AnalyzeAudioLevels = false
            });

        probe.Setup(p => p.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaProbeResult(4, 30, 1, 1920, 1080, "h264", "aac", 48000));
        shotDetector
            .Setup(s => s.DetectShotsAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(shots);

        VideoAnalyzeStepExecutor executor = CreateExecutor(workspace, probe, silence, shotDetector, out _, out _);

        string? capturedArtifactJson = null;
        workspace
            .Setup(w => w.UploadArtifactAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .Callback<Guid, string, string, string, CancellationToken, string>((_, path, _, _, _, _) =>
                capturedArtifactJson = File.ReadAllText(path))
            .ReturnsAsync("projects/p/agentFiles/video-analysis/e/step-1-analysis.json");

        StepExecutionResult result = await executor.ExecuteAsync(context);
        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);

        using JsonDocument doc = JsonDocument.Parse(result.Output);
        JsonElement meta = doc.RootElement.GetProperty("meta");
        meta.GetProperty("visual").GetProperty("applied").GetBoolean().Should().BeFalse();

        foreach (JsonElement shot in doc.RootElement.GetProperty("view").GetProperty("shots").EnumerateArray())
        {
            shot.TryGetProperty("v", out _).Should().BeFalse();
            shot.TryGetProperty("a", out _).Should().BeFalse();
        }

        capturedArtifactJson.Should().NotBeNull();
        using JsonDocument artifactDoc = JsonDocument.Parse(capturedArtifactJson!);
        artifactDoc.RootElement.GetProperty("version").GetInt32().Should().Be(2);
        foreach (JsonElement shot in artifactDoc.RootElement.GetProperty("shots").EnumerateArray())
        {
            shot.TryGetProperty("visual", out JsonElement visual).Should().BeTrue();
            visual.ValueKind.Should().Be(JsonValueKind.Null);
        }
    }

    /// <summary>A minimal valid 16kHz mono s16 canonical WAV with a short constant tone — enough for WavRmsSampler to parse.</summary>
    private static byte[] BuildSyntheticWav()
    {
        const int sampleRate = 16000;
        const int seconds = 1;
        int sampleCount = sampleRate * seconds;
        int dataBytes = sampleCount * 2;

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1); // PCM
        w.Write((short)1); // mono
        w.Write(sampleRate);
        w.Write(sampleRate * 2); // byte rate
        w.Write((short)2); // block align
        w.Write((short)16); // bits per sample
        w.Write("data"u8.ToArray());
        w.Write(dataBytes);

        for (int i = 0; i < sampleCount; i++)
        {
            short sample = (short)(5000 * Math.Sin(2 * Math.PI * 440 * i / sampleRate));
            w.Write(sample);
        }

        return ms.ToArray();
    }

    // =======================================================================
    // Phase 2: vision shot captioning
    // =======================================================================

    [Fact]
    public async Task Vision_off_by_default_produces_no_c_key_and_meta_vision_reports_off()
    {
        // The single most important regression test in this phase (mirroring Phase 1's
        // "degrade-before-drop" test's importance): with Vision left at its default (Off),
        // behavior must be byte-identical to a pre-Phase-2 run except for the explicit,
        // always-present meta.vision block itself.
        var shots = new[] { (0.0, 2.0), (2.0, 4.0) };

        StepExecutionContext context = CreateContext(
            out Mock<IProjectFileWorkspace> workspace,
            out Mock<IMediaProbe> probe,
            out Mock<ISilenceDetector> silence,
            out Mock<IShotDetector> shotDetector,
            out _, out _,
            configOverride: cfg => cfg with { DetectSilence = false, Transcription = VideoTranscriptionMode.Off });

        probe.Setup(p => p.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaProbeResult(4, 30, 1, 1920, 1080, "h264", "aac", 48000));
        shotDetector
            .Setup(s => s.DetectShotsAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(shots);

        StepExecutionResult result = await CreateExecutor(workspace, probe, silence, shotDetector, out _, out _).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);

        using JsonDocument doc = JsonDocument.Parse(result.Output);
        JsonElement vision = doc.RootElement.GetProperty("meta").GetProperty("vision");
        vision.GetProperty("mode").GetString().Should().Be("Off");
        vision.GetProperty("applied").GetBoolean().Should().BeFalse();
        vision.GetProperty("degraded").GetBoolean().Should().BeFalse();
        vision.GetProperty("partial").GetBoolean().Should().BeFalse();
        vision.GetProperty("captionedShots").GetInt32().Should().Be(0);
        vision.GetProperty("failedShots").GetInt32().Should().Be(0);

        foreach (JsonElement shot in doc.RootElement.GetProperty("view").GetProperty("shots").EnumerateArray())
        {
            shot.TryGetProperty("c", out _).Should().BeFalse("Vision=Off must never add a 'c' key to any shot");
        }
    }

    [Fact]
    public async Task Vision_optional_degrades_cleanly_when_no_provider_resolves()
    {
        StepExecutionContext context = CreateContext(
            out Mock<IProjectFileWorkspace> workspace,
            out Mock<IMediaProbe> probe,
            out Mock<ISilenceDetector> silence,
            out Mock<IShotDetector> shotDetector,
            out Mock<ITranscriptionClientFactory> transcriptionFactory,
            out Mock<IInferenceProviderResolver> providerResolver,
            configOverride: cfg => cfg with
            {
                Vision = VideoVisionMode.Optional, DetectSilence = false, DetectShots = false,
                Transcription = VideoTranscriptionMode.Off
            });

        SetupBasicProbeAndDetectors(probe, silence, shotDetector);
        providerResolver
            .Setup(r => r.ResolveVisionAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResolvedInferenceProvider?)null);

        StepExecutionResult result = await CreateExecutor(workspace, probe, silence, shotDetector, transcriptionFactory, providerResolver)
            .ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        JsonElement vision = doc.RootElement.GetProperty("meta").GetProperty("vision");
        vision.GetProperty("applied").GetBoolean().Should().BeFalse();
        vision.GetProperty("degraded").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Vision_required_fails_clearly_when_no_provider_resolves()
    {
        StepExecutionContext context = CreateContext(
            out Mock<IProjectFileWorkspace> workspace,
            out Mock<IMediaProbe> probe,
            out Mock<ISilenceDetector> silence,
            out Mock<IShotDetector> shotDetector,
            out Mock<ITranscriptionClientFactory> transcriptionFactory,
            out Mock<IInferenceProviderResolver> providerResolver,
            configOverride: cfg => cfg with
            {
                Vision = VideoVisionMode.Required, DetectSilence = false, DetectShots = false,
                Transcription = VideoTranscriptionMode.Off
            });

        SetupBasicProbeAndDetectors(probe, silence, shotDetector);
        providerResolver
            .Setup(r => r.ResolveVisionAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResolvedInferenceProvider?)null);

        StepExecutionResult result = await CreateExecutor(workspace, probe, silence, shotDetector, transcriptionFactory, providerResolver)
            .ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        Action parse = () => JsonDocument.Parse(result.Output);
        parse.Should().NotThrow();
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VISION_UNAVAILABLE");
    }

    [Fact]
    public async Task Vision_captioning_failure_on_one_shot_does_not_abort_captioning_of_the_others()
    {
        var shots = new[] { (0.0, 2.0), (2.0, 4.0), (4.0, 6.0) }; // -> s0, s1, s2

        StepExecutionContext context = CreateContext(
            out Mock<IProjectFileWorkspace> workspace,
            out Mock<IMediaProbe> probe,
            out Mock<ISilenceDetector> silence,
            out Mock<IShotDetector> shotDetector,
            out Mock<ITranscriptionClientFactory> transcriptionFactory,
            out Mock<IInferenceProviderResolver> providerResolver,
            configOverride: cfg => cfg with
            {
                Vision = VideoVisionMode.Optional, DetectSilence = false, Transcription = VideoTranscriptionMode.Off,
                AnalyzeVisuals = false, AnalyzeAudioLevels = false, DetectNearDuplicates = false,
                MaxCaptionedShots = 10, MinCaptionShotSeconds = 0.0, VisionTimeoutSeconds = 60
            });

        probe.Setup(p => p.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaProbeResult(6, 30, 1, 1920, 1080, "h264", "aac", 48000));
        shotDetector
            .Setup(s => s.DetectShotsAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(shots);

        ResolvedInferenceProvider provider = new(
            Guid.NewGuid(), "vision-test", InferenceProviderKind.OpenAICompatible, "http://localhost:9999", "vision-1", "", 30);
        providerResolver
            .Setup(r => r.ResolveVisionAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(provider);

        var shotCaptioner = new Mock<IShotCaptioner>();
        shotCaptioner
            .Setup(c => c.CaptionAsync(
                It.IsAny<ResolvedInferenceProvider>(), It.IsAny<ShotCaptionRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResolvedInferenceProvider _, ShotCaptionRequest r, int _, CancellationToken _) =>
                new VideoShotCaption(r.ShotId, "a caption", [], "action", "setting", "mood", "Medium", "Eye level", [], []));
        // Registered AFTER the general setup above so it wins the match for s1 specifically
        // (Moq resolves overlapping setups in most-recently-defined order) — every attempt for
        // s1 fails, exhausting CaptionWithRetryAsync's retries.
        shotCaptioner
            .Setup(c => c.CaptionAsync(
                It.IsAny<ResolvedInferenceProvider>(), It.Is<ShotCaptionRequest>(r => r.ShotId == "s1"),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("vision boom"));

        VideoAnalyzeStepExecutor executor = CreateExecutor(
            workspace, probe, silence, shotDetector, transcriptionFactory, providerResolver,
            audioExtractor: null, frameGridSampler: null, keyframeExtractor: null, shotCaptioner: shotCaptioner);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);

        using JsonDocument doc = JsonDocument.Parse(result.Output);
        JsonElement vision = doc.RootElement.GetProperty("meta").GetProperty("vision");
        vision.GetProperty("failedShots").GetInt32().Should().Be(1);
        vision.GetProperty("captionedShots").GetInt32().Should().Be(2);

        foreach (JsonElement shot in doc.RootElement.GetProperty("view").GetProperty("shots").EnumerateArray())
        {
            string id = shot.GetProperty("id").GetString()!;
            bool hasCaption = shot.TryGetProperty("c", out _);
            if (id == "s1")
                hasCaption.Should().BeFalse("s1's captioning exhausted its retries and must be skipped, not fabricated");
            else
                hasCaption.Should().BeTrue($"{id} should have captioned successfully");
        }
    }

    [Fact]
    public async Task Vision_captioning_binds_the_caption_to_the_shot_it_actually_requested_never_a_model_returned_id()
    {
        // The critical safety-property test (plan §3/§8): the shot-id <-> caption binding must
        // never be model-controlled. Here IShotCaptioner returns a caption whose ShotId is
        // deliberately wrong; the executor must still attach it to the shot it actually asked
        // about (the id in the ShotCaptionRequest it sent), not whatever the mock/model echoed.
        var shots = new[] { (0.0, 2.0) }; // -> s0 only

        StepExecutionContext context = CreateContext(
            out Mock<IProjectFileWorkspace> workspace,
            out Mock<IMediaProbe> probe,
            out Mock<ISilenceDetector> silence,
            out Mock<IShotDetector> shotDetector,
            out Mock<ITranscriptionClientFactory> transcriptionFactory,
            out Mock<IInferenceProviderResolver> providerResolver,
            configOverride: cfg => cfg with
            {
                Vision = VideoVisionMode.Required, DetectSilence = false, Transcription = VideoTranscriptionMode.Off,
                AnalyzeVisuals = false, AnalyzeAudioLevels = false, DetectNearDuplicates = false,
                MaxCaptionedShots = 10, MinCaptionShotSeconds = 0.0
            });

        probe.Setup(p => p.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaProbeResult(2, 30, 1, 1920, 1080, "h264", "aac", 48000));
        shotDetector
            .Setup(s => s.DetectShotsAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(shots);

        ResolvedInferenceProvider provider = new(
            Guid.NewGuid(), "vision-test", InferenceProviderKind.OpenAICompatible, "http://localhost:9999", "vision-1", "", 30);
        providerResolver
            .Setup(r => r.ResolveVisionAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(provider);

        var shotCaptioner = new Mock<IShotCaptioner>();
        shotCaptioner
            .Setup(c => c.CaptionAsync(
                It.IsAny<ResolvedInferenceProvider>(), It.IsAny<ShotCaptionRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VideoShotCaption(
                "s999-not-the-real-shot", "a caption", [], "action", "setting", "mood", "Medium", "Eye level", [], []));

        VideoAnalyzeStepExecutor executor = CreateExecutor(
            workspace, probe, silence, shotDetector, transcriptionFactory, providerResolver,
            audioExtractor: null, frameGridSampler: null, keyframeExtractor: null, shotCaptioner: shotCaptioner);

        string? capturedArtifactJson = null;
        workspace
            .Setup(w => w.UploadArtifactAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .Callback<Guid, string, string, string, CancellationToken, string>((_, path, _, _, _, _) =>
                capturedArtifactJson = File.ReadAllText(path))
            .ReturnsAsync("projects/p/agentFiles/video-analysis/e/step-1-analysis.json");

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);

        capturedArtifactJson.Should().NotBeNull();
        using JsonDocument artifactDoc = JsonDocument.Parse(capturedArtifactJson!);
        JsonElement shot0 = artifactDoc.RootElement.GetProperty("shots")[0];
        shot0.GetProperty("id").GetString().Should().Be("s0");
        shot0.GetProperty("caption").GetProperty("shotId").GetString().Should().Be(
            "s0", "the executor must bind the caption to the shot it actually requested, never a model-returned id");
    }
}
