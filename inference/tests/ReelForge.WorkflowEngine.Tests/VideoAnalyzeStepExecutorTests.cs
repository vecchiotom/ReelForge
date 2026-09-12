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
        return CreateExecutor(workspace, probe, silence, shotDetector, transcriptionFactory, providerResolver, null);
    }

    private static VideoAnalyzeStepExecutor CreateExecutor(
        Mock<IProjectFileWorkspace> workspace,
        Mock<IMediaProbe> probe,
        Mock<ISilenceDetector> silence,
        Mock<IShotDetector> shotDetector,
        Mock<ITranscriptionClientFactory> transcriptionFactory,
        Mock<IInferenceProviderResolver> providerResolver,
        Mock<IAudioExtractor>? audioExtractor = null)
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
}
