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
    // Background music (see docs/video-editing.md "Background music")
    // =======================================================================

    [Fact]
    public async Task OfferMusicTracks_false_produces_no_musicTracks_key_byte_identical_to_before_this_addition()
    {
        StepExecutionContext context = CreateContext(
            out Mock<IProjectFileWorkspace> workspace,
            out Mock<IMediaProbe> probe,
            out Mock<ISilenceDetector> silence,
            out Mock<IShotDetector> shotDetector,
            out _, out _,
            configOverride: cfg => cfg with { OfferMusicTracks = false });

        SetupBasicProbeAndDetectors(probe, silence, shotDetector);

        // ListFilesAsync must never even be called when OfferMusicTracks=false.
        workspace
            .Setup(w => w.ListFilesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ListFilesAsync should not be called when OfferMusicTracks=false"));

        StepExecutionResult result = await CreateExecutor(workspace, probe, silence, shotDetector, out _, out _).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        doc.RootElement.GetProperty("view").TryGetProperty("musicTracks", out _).Should().BeFalse();
        doc.RootElement.GetProperty("meta").GetProperty("offeredMusicTrackCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task OfferMusicTracks_true_lists_audio_files_as_m_ids_in_deterministic_name_order_excluding_non_audio()
    {
        StepExecutionContext context = CreateContext(
            out Mock<IProjectFileWorkspace> workspace,
            out Mock<IMediaProbe> probe,
            out Mock<ISilenceDetector> silence,
            out Mock<IShotDetector> shotDetector,
            out _, out _,
            configOverride: cfg => cfg with { OfferMusicTracks = true, MaxMusicTracks = 20 });

        SetupBasicProbeAndDetectors(probe, silence, shotDetector);

        Guid zTrack = Guid.NewGuid(), aTrack = Guid.NewGuid(), videoFile = Guid.NewGuid();
        workspace
            .Setup(w => w.ListFilesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectWorkspaceFile>
            {
                new(zTrack, ProjectId, "zebra.mp3", null, "userFiles", "k1", "audio/mpeg", 100, DateTime.UtcNow, null),
                new(aTrack, ProjectId, "ambient.wav", null, "userFiles", "k2", "audio/wav", 200, DateTime.UtcNow, null),
                new(videoFile, ProjectId, "clip.mp4", null, "userFiles", "k3", "video/mp4", 300, DateTime.UtcNow, null)
            });

        StepExecutionResult result = await CreateExecutor(workspace, probe, silence, shotDetector, out _, out _).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        JsonElement musicTracks = doc.RootElement.GetProperty("view").GetProperty("musicTracks");
        musicTracks.GetArrayLength().Should().Be(2, "only the two audio/* files, never the video/mp4 one");
        musicTracks[0].GetProperty("id").GetString().Should().Be("m0");
        musicTracks[0].GetProperty("name").GetString().Should().Be("ambient.wav", "deterministic ordinal name order — 'ambient' sorts before 'zebra'");
        musicTracks[1].GetProperty("id").GetString().Should().Be("m1");
        musicTracks[1].GetProperty("name").GetString().Should().Be("zebra.mp3");
        doc.RootElement.GetProperty("meta").GetProperty("offeredMusicTrackCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task OfferMusicTracks_true_ListFilesAsync_throwing_degrades_to_zero_candidates_not_a_failed_step()
    {
        StepExecutionContext context = CreateContext(
            out Mock<IProjectFileWorkspace> workspace,
            out Mock<IMediaProbe> probe,
            out Mock<ISilenceDetector> silence,
            out Mock<IShotDetector> shotDetector,
            out _, out _,
            configOverride: cfg => cfg with { OfferMusicTracks = true });

        SetupBasicProbeAndDetectors(probe, silence, shotDetector);

        workspace
            .Setup(w => w.ListFilesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("S3 unavailable"));

        StepExecutionResult result = await CreateExecutor(workspace, probe, silence, shotDetector, out _, out _).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, "a music-candidate listing failure must degrade, never fail the whole step");
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        doc.RootElement.GetProperty("view").TryGetProperty("musicTracks", out _).Should().BeFalse();
        doc.RootElement.GetProperty("meta").GetProperty("offeredMusicTrackCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task OfferMusicTracks_true_caps_at_MaxMusicTracks()
    {
        StepExecutionContext context = CreateContext(
            out Mock<IProjectFileWorkspace> workspace,
            out Mock<IMediaProbe> probe,
            out Mock<ISilenceDetector> silence,
            out Mock<IShotDetector> shotDetector,
            out _, out _,
            configOverride: cfg => cfg with { OfferMusicTracks = true, MaxMusicTracks = 2 });

        SetupBasicProbeAndDetectors(probe, silence, shotDetector);

        workspace
            .Setup(w => w.ListFilesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(0, 5)
                .Select(i => new ProjectWorkspaceFile(
                    Guid.NewGuid(), ProjectId, $"track{i}.mp3", null, "userFiles", $"k{i}", "audio/mpeg", 100, DateTime.UtcNow, null))
                .ToList());

        StepExecutionResult result = await CreateExecutor(workspace, probe, silence, shotDetector, out _, out _).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        doc.RootElement.GetProperty("view").GetProperty("musicTracks").GetArrayLength().Should().Be(2);
    }

    // =======================================================================
    // Transcript punctuation reliability (meta.transcription.punctuated) and the per-segment
    // "endsSentence" flag. Both exist because trailing punctuation is the ONLY sentence-boundary
    // signal an ASR transcript carries, and a transcriber that barely punctuates (observed in
    // production at 12%) would otherwise make every segment look like a mid-sentence fragment to
    // the story editor and to the review agent's rubric.
    // =======================================================================

    [Fact]
    public async Task A_barely_punctuated_source_reports_a_low_unreliable_punctuated_ratio_in_meta()
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
                MaxOutputChars = 24000,
                MaxViewSegments = 100
            });

        SetupBasicProbeAndDetectors(probe, silence, shotDetector);

        // 1 of 5 segments ends in terminal punctuation -> ratio 0.2, below the 0.5 reliability
        // threshold: the shape of the real production transcript that motivated this signal.
        SetupSingleChunkTranscription(transcriptionFactory, providerResolver,
            ("so the thing is that we", 0.0, 2.0),
            ("and then we tried", 2.0, 4.0),
            ("it worked out.", 4.0, 6.0),
            ("which meant", 6.0, 8.0),
            ("a lot for the team", 8.0, 10.0));

        StepExecutionResult result = await CreateExecutor(
            workspace, probe, silence, shotDetector, transcriptionFactory, providerResolver).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        using JsonDocument doc = JsonDocument.Parse(result.Output);

        JsonElement punctuated = doc.RootElement
            .GetProperty("meta").GetProperty("transcription").GetProperty("punctuated");

        punctuated.GetArrayLength().Should().Be(1, "exactly one source clip produced transcript segments");
        punctuated[0].GetProperty("src").GetInt32().Should().Be(0);
        punctuated[0].GetProperty("segments").GetInt32().Should().Be(5);
        punctuated[0].GetProperty("punctuatedSegments").GetInt32().Should().Be(1);
        punctuated[0].GetProperty("ratio").GetDouble().Should().BeApproximately(0.2, 1e-9);
        punctuated[0].GetProperty("reliable").GetBoolean().Should()
            .BeFalse("0.2 is far below the threshold at which trailing punctuation means anything");
    }

    [Fact]
    public async Task A_well_punctuated_source_reports_a_reliable_punctuated_ratio_in_meta()
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
                MaxOutputChars = 24000,
                MaxViewSegments = 100
            });

        SetupBasicProbeAndDetectors(probe, silence, shotDetector);

        // 3 of 4 -> 0.75, at or above the threshold.
        SetupSingleChunkTranscription(transcriptionFactory, providerResolver,
            ("We shipped it last week.", 0.0, 2.0),
            ("The team was thrilled!", 2.0, 4.0),
            ("and then", 4.0, 6.0),
            ("everything changed?", 6.0, 8.0));

        StepExecutionResult result = await CreateExecutor(
            workspace, probe, silence, shotDetector, transcriptionFactory, providerResolver).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        using JsonDocument doc = JsonDocument.Parse(result.Output);

        JsonElement punctuated = doc.RootElement
            .GetProperty("meta").GetProperty("transcription").GetProperty("punctuated");

        punctuated[0].GetProperty("segments").GetInt32().Should().Be(4);
        punctuated[0].GetProperty("punctuatedSegments").GetInt32().Should().Be(3);
        punctuated[0].GetProperty("ratio").GetDouble().Should().BeApproximately(0.75, 1e-9);
        punctuated[0].GetProperty("reliable").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Each_view_segment_carries_an_endsSentence_flag_derived_from_its_own_text()
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
                MaxOutputChars = 24000,
                MaxViewSegments = 100
            });

        SetupBasicProbeAndDetectors(probe, silence, shotDetector);

        SetupSingleChunkTranscription(transcriptionFactory, providerResolver,
            ("A finished thought.", 0.0, 2.0),
            ("this one keeps going", 2.0, 4.0),
            ("Shouting works too!", 4.0, 6.0),
            ("He said \"stop.\"", 6.0, 8.0),
            ("and trails off", 8.0, 10.0));

        StepExecutionResult result = await CreateExecutor(
            workspace, probe, silence, shotDetector, transcriptionFactory, providerResolver).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        using JsonDocument doc = JsonDocument.Parse(result.Output);

        JsonElement viewSegments = doc.RootElement.GetProperty("view").GetProperty("segments");
        viewSegments.GetArrayLength().Should().Be(5);

        // Keyed by text rather than array position, so this asserts the flag/text pairing itself
        // and never depends on the offered-item ordering.
        Dictionary<string, bool> flagByText = viewSegments.EnumerateArray()
            .ToDictionary(
                s => s.GetProperty("text").GetString()!,
                s => s.GetProperty("endsSentence").GetBoolean());

        flagByText["A finished thought."].Should().BeTrue();
        flagByText["this one keeps going"].Should().BeFalse();
        flagByText["Shouting works too!"].Should().BeTrue();
        flagByText["He said \"stop.\""].Should().BeTrue("a closing quote after the full stop still ends the sentence");
        flagByText["and trails off"].Should().BeFalse();
    }

    [Fact]
    public async Task Transcription_off_reports_an_empty_punctuated_list_rather_than_a_zero_ratio()
    {
        StepExecutionContext context = CreateContext(
            out Mock<IProjectFileWorkspace> workspace,
            out Mock<IMediaProbe> probe,
            out Mock<ISilenceDetector> silence,
            out Mock<IShotDetector> shotDetector,
            out _, out _,
            configOverride: cfg => cfg with { Transcription = VideoTranscriptionMode.Off });

        SetupBasicProbeAndDetectors(probe, silence, shotDetector);

        StepExecutionResult result = await CreateExecutor(workspace, probe, silence, shotDetector, out _, out _)
            .ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        using JsonDocument doc = JsonDocument.Parse(result.Output);

        doc.RootElement.GetProperty("meta").GetProperty("transcription")
            .GetProperty("punctuated").GetArrayLength().Should()
            .Be(0, "no transcript means \"unknown\", never \"0% punctuated\"");
    }

    // =======================================================================
    // Test infrastructure
    // =======================================================================

    /// <summary>
    /// Wires a transcription provider + client that returns every supplied segment in ONE chunk
    /// (the default MaxAsrChunkBytes dwarfs the fake wav the default audio extractor writes).
    /// </summary>
    private static void SetupSingleChunkTranscription(
        Mock<ITranscriptionClientFactory> transcriptionFactory,
        Mock<IInferenceProviderResolver> providerResolver,
        params (string Text, double StartSec, double EndSec)[] segments)
    {
        ResolvedTranscriptionProvider provider = new(
            ProviderId: Guid.NewGuid(), Name: "whisper-local", Kind: InferenceProviderKind.OpenAICompatible,
            Endpoint: "http://localhost:9999", ModelName: "whisper-1", ApiKey: "", TimeoutSeconds: 30);
        providerResolver
            .Setup(r => r.ResolveTranscriptionAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(provider);

        var transcriptionClient = new Mock<ITranscriptionClient>();
        transcriptionClient
            .Setup(c => c.TranscribeAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranscriptResult(
                string.Join(" ", segments.Select(s => s.Text)),
                segments.Select(s => new TranscriptSegment(s.Text, s.StartSec, s.EndSec)).ToArray(),
                Array.Empty<TranscriptWord>(),
                "en"));

        transcriptionFactory
            .Setup(f => f.Get(It.IsAny<ResolvedTranscriptionProvider>()))
            .Returns(transcriptionClient.Object);
    }

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
        Mock<IShotCaptioner>? shotCaptioner = null,
        Mock<ISharpnessSampler>? sharpnessSampler = null)
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

        // Phase 4: left unconfigured by default too — DetectSharpness defaults false, so most
        // tests never call this mock at all.
        sharpnessSampler ??= new Mock<ISharpnessSampler>();

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
            sharpnessSampler.Object,
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

    /// <summary>
    /// Builds a grid buffer as a sequence of uniform-color segments (one per shot), so each shot's
    /// D1-D3 color-temperature/tone/saturation classes are controllable and deterministic — unlike
    /// <see cref="BuildSyntheticGrid"/>'s grayscale content, which can never produce a non-Neutral
    /// ColorTemperatureClass.
    /// </summary>
    private static FrameGridResult BuildColoredGrid(
        int gridWidth, int gridHeight, double fps, params (int FrameCount, byte R, byte G, byte B)[] segments)
    {
        int frameSize = gridWidth * gridHeight * 3;
        int totalFrames = segments.Sum(s => s.FrameCount);
        var pixels = new byte[totalFrames * frameSize];

        int frameIndex = 0;
        foreach ((int frameCount, byte r, byte g, byte b) in segments)
        {
            for (int f = 0; f < frameCount; f++)
            {
                for (int p = 0; p < gridWidth * gridHeight; p++)
                {
                    int idx = (frameIndex * frameSize) + p * 3;
                    pixels[idx] = r;
                    pixels[idx + 1] = g;
                    pixels[idx + 2] = b;
                }

                frameIndex++;
            }
        }

        return new FrameGridResult(pixels, gridWidth, gridHeight, fps, totalFrames);
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
                It.IsAny<string>(), It.IsAny<VideoScratchSpace>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<int>(),
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
                        It.IsAny<string>(), It.IsAny<VideoScratchSpace>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<int>(),
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
    public async Task Oversized_visual_grid_dimensions_are_clamped_to_a_sane_range()
    {
        // Item D: an oversized workflow-author-supplied VisualGridWidth/Height (e.g. 2048x2048)
        // must be clamped before it ever reaches the sampler, rather than allocating an enormous
        // grid.rgb scratch file / forcing a huge single in-memory byte[] read of it.
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
                AnalyzeVisuals = true, AnalyzeAudioLevels = false, DetectNearDuplicates = false,
                VisualGridWidth = 2048, VisualGridHeight = 4096
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
                It.IsAny<string>(), It.IsAny<VideoScratchSpace>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(grid);

        VideoAnalyzeStepExecutor executor = CreateExecutor(
            workspace, probe, silence, shotDetector,
            new Mock<ITranscriptionClientFactory>(), new Mock<IInferenceProviderResolver>(),
            audioExtractor: null, frameGridSampler: frameGridSampler);

        StepExecutionResult result = await executor.ExecuteAsync(context);
        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);

        frameGridSampler.Verify(g => g.SampleAsync(
            It.IsAny<string>(), It.IsAny<VideoScratchSpace>(), It.IsAny<double>(),
            It.Is<int>(w => w <= 256), It.Is<int>(h => h <= 256),
            It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);

        using JsonDocument doc = JsonDocument.Parse(result.Output);
        JsonElement visual = doc.RootElement.GetProperty("meta").GetProperty("visual");
        visual.GetProperty("gridWidth").GetInt32().Should().Be(256);
        visual.GetProperty("gridHeight").GetInt32().Should().Be(256);
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
                It.IsAny<string>(), It.IsAny<VideoScratchSpace>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<int>(),
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

    /// <summary>Same shape as <see cref="BuildSyntheticWav"/>, but a caller-chosen duration — for tests whose shots span more than 1 second.</summary>
    private static byte[] BuildToneWav(int seconds)
    {
        const int sampleRate = 16000;
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
    public async Task Vision_optional_degrades_cleanly_when_provider_resolution_throws()
    {
        // Regression for the audit's Item B: ResolveVisionAsync throwing (a transient DB/scope
        // exception) must degrade like every other Optional failure mode, not escape as
        // UNEXPECTED_ERROR and discard all the completed deterministic work.
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
            .ThrowsAsync(new InvalidOperationException("transient resolver failure"));

        StepExecutionResult result = await CreateExecutor(workspace, probe, silence, shotDetector, transcriptionFactory, providerResolver)
            .ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        JsonElement vision = doc.RootElement.GetProperty("meta").GetProperty("vision");
        vision.GetProperty("applied").GetBoolean().Should().BeFalse();
        vision.GetProperty("degraded").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Vision_required_fails_clearly_when_provider_resolution_throws()
    {
        // Mirror of the Optional case above: when Vision is Required, a resolver exception must
        // still surface as a clean VISION_FAILED result, not an unhandled exception/UNEXPECTED_ERROR.
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
            .ThrowsAsync(new InvalidOperationException("transient resolver failure"));

        StepExecutionResult result = await CreateExecutor(workspace, probe, silence, shotDetector, transcriptionFactory, providerResolver)
            .ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VISION_FAILED");
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
                new VideoShotCaption(r.ShotId, "a caption", [], "action", "setting", "mood", "Medium", "Eye level", [], [],
                    "Unknown", "flat", "neutral", "centered", []));
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
                "s999-not-the-real-shot", "a caption", [], "action", "setting", "mood", "Medium", "Eye level", [], [],
                "Unknown", "flat", "neutral", "centered", []));

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

    // =======================================================================
    // Phase 4 §7: vision-phase changes (prompt priming, contact sheet, PersistKeyframes)
    // =======================================================================

    private async Task<(StepExecutionResult Result, Mock<IProjectFileWorkspace> Workspace, Mock<IKeyframeExtractor> KeyframeExtractor, List<ShotCaptionRequest> Requests)>
        RunVisionCaptionTestAsync(
            Func<VideoAnalyzeStepConfig, VideoAnalyzeStepConfig> extraOverride,
            FrameGridResult? grid = null)
    {
        var shots = new[] { (0.0, 2.0) };

        StepExecutionContext context = CreateContext(
            out Mock<IProjectFileWorkspace> workspace,
            out Mock<IMediaProbe> probe,
            out Mock<ISilenceDetector> silence,
            out Mock<IShotDetector> shotDetector,
            out Mock<ITranscriptionClientFactory> transcriptionFactory,
            out Mock<IInferenceProviderResolver> providerResolver,
            configOverride: cfg => extraOverride(cfg with
            {
                Vision = VideoVisionMode.Required, DetectSilence = false, Transcription = VideoTranscriptionMode.Off,
                AnalyzeAudioLevels = false, DetectNearDuplicates = false,
                MaxCaptionedShots = 10, MinCaptionShotSeconds = 0.0
            }));

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

        var requests = new List<ShotCaptionRequest>();
        var shotCaptioner = new Mock<IShotCaptioner>();
        shotCaptioner
            .Setup(c => c.CaptionAsync(It.IsAny<ResolvedInferenceProvider>(), It.IsAny<ShotCaptionRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResolvedInferenceProvider _, ShotCaptionRequest r, int _, CancellationToken _) =>
            {
                requests.Add(r);
                return new VideoShotCaption(
                    r.ShotId, "a caption", [], "action", "setting", "mood", "Medium", "Eye level", [], ["soft focus"],
                    "Unknown", "flat", "neutral", "centered", ["soft focus"]);
            });

        var keyframeExtractor = new Mock<IKeyframeExtractor>();

        Mock<IFrameGridSampler>? frameGridSampler = null;
        if (grid is not null)
        {
            frameGridSampler = new Mock<IFrameGridSampler>();
            frameGridSampler
                .Setup(g => g.SampleAsync(It.IsAny<string>(), It.IsAny<VideoScratchSpace>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(grid);
        }

        VideoAnalyzeStepExecutor executor = CreateExecutor(
            workspace, probe, silence, shotDetector, transcriptionFactory, providerResolver,
            audioExtractor: null, frameGridSampler: frameGridSampler, keyframeExtractor: keyframeExtractor, shotCaptioner: shotCaptioner);

        StepExecutionResult result = await executor.ExecuteAsync(context);
        return (result, workspace, keyframeExtractor, requests);
    }

    [Fact]
    public async Task PersistKeyframes_true_uploads_one_jpeg_per_captioned_shot_under_the_video_analysis_prefix()
    {
        (StepExecutionResult result, Mock<IProjectFileWorkspace> workspace, _, _) = await RunVisionCaptionTestAsync(
            cfg => cfg with { AnalyzeVisuals = false, PersistKeyframes = true });

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);

        workspace.Invocations
            .Where(i => i.Method.Name == nameof(IProjectFileWorkspace.UploadArtifactAsync))
            .Select(i => (string)i.Arguments[2])
            .Should().Contain(fileName => fileName.Contains("video-analysis/") && fileName.Contains("-keyframes/") && fileName.EndsWith("s0.jpg"));
    }

    [Fact]
    public async Task PersistKeyframes_upload_failure_does_not_lose_the_caption()
    {
        (StepExecutionResult result, Mock<IProjectFileWorkspace> workspace, _, _) = await RunVisionCaptionTestAsync(
            cfg => cfg with { AnalyzeVisuals = false, PersistKeyframes = true });

        // The default `RunVisionCaptionTestAsync` workspace mock already succeeds every
        // UploadArtifactAsync call; re-run with a failing one to prove the caption still lands.
        StepExecutionContext context = CreateContext(
            out Mock<IProjectFileWorkspace> failingWorkspace,
            out Mock<IMediaProbe> probe,
            out Mock<ISilenceDetector> silence,
            out Mock<IShotDetector> shotDetector,
            out Mock<ITranscriptionClientFactory> transcriptionFactory,
            out Mock<IInferenceProviderResolver> providerResolver,
            configOverride: cfg => cfg with
            {
                Vision = VideoVisionMode.Required, DetectSilence = false, Transcription = VideoTranscriptionMode.Off,
                AnalyzeVisuals = false, AnalyzeAudioLevels = false, DetectNearDuplicates = false,
                MaxCaptionedShots = 10, MinCaptionShotSeconds = 0.0, PersistKeyframes = true
            });

        probe.Setup(p => p.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaProbeResult(2, 30, 1, 1920, 1080, "h264", "aac", 48000));
        shotDetector
            .Setup(s => s.DetectShotsAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([(0.0, 2.0)]);

        ResolvedInferenceProvider provider = new(
            Guid.NewGuid(), "vision-test", InferenceProviderKind.OpenAICompatible, "http://localhost:9999", "vision-1", "", 30);
        providerResolver
            .Setup(r => r.ResolveVisionAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(provider);

        var shotCaptioner = new Mock<IShotCaptioner>();
        shotCaptioner
            .Setup(c => c.CaptionAsync(It.IsAny<ResolvedInferenceProvider>(), It.IsAny<ShotCaptionRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResolvedInferenceProvider _, ShotCaptionRequest r, int _, CancellationToken _) => new VideoShotCaption(
                r.ShotId, "a caption", [], "action", "setting", "mood", "Medium", "Eye level", [], [],
                "Unknown", "flat", "neutral", "centered", []));

        VideoAnalyzeStepExecutor failingExecutor = CreateExecutor(
            failingWorkspace, probe, silence, shotDetector, transcriptionFactory, providerResolver,
            audioExtractor: null, frameGridSampler: null, keyframeExtractor: null, shotCaptioner: shotCaptioner);

        // Override AFTER CreateExecutor so this wins: the artifact-upload path itself must still
        // succeed (or the step fails for an unrelated reason) — only simulate the keyframe-persist
        // upload failing by throwing for any path containing "-keyframes/".
        failingWorkspace
            .Setup(w => w.UploadArtifactAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.Is<string>(f => f.Contains("-keyframes/")), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("upload boom"));

        StepExecutionResult failingResult = await failingExecutor.ExecuteAsync(context);

        failingResult.Status.Should().Be(StepStatus.Completed, because: failingResult.ErrorDetails ?? failingResult.Output);
        using JsonDocument doc = JsonDocument.Parse(failingResult.Output);
        doc.RootElement.GetProperty("view").GetProperty("shots")[0].TryGetProperty("c", out _).Should().BeTrue(
            "a keyframe-persist failure must never cost the caption itself");
    }

    [Fact]
    public async Task KeyframesPerShot_greater_than_one_uses_the_contact_sheet_extractor()
    {
        (StepExecutionResult result, _, Mock<IKeyframeExtractor> keyframeExtractor, _) = await RunVisionCaptionTestAsync(
            cfg => cfg with { AnalyzeVisuals = false, KeyframesPerShot = 3 });

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        keyframeExtractor.Invocations.Count(i => i.Method.Name == nameof(IKeyframeExtractor.ExtractContactSheetAsync)).Should().Be(1);
        keyframeExtractor.Invocations.Count(i => i.Method.Name == nameof(IKeyframeExtractor.ExtractKeyframeAsync)).Should().Be(0);
    }

    [Fact]
    public async Task KeyframesPerShot_equal_to_one_uses_the_single_frame_extractor()
    {
        (StepExecutionResult result, _, Mock<IKeyframeExtractor> keyframeExtractor, _) = await RunVisionCaptionTestAsync(
            cfg => cfg with { AnalyzeVisuals = false, KeyframesPerShot = 1 });

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        keyframeExtractor.Invocations.Count(i => i.Method.Name == nameof(IKeyframeExtractor.ExtractKeyframeAsync)).Should().Be(1);
        keyframeExtractor.Invocations.Count(i => i.Method.Name == nameof(IKeyframeExtractor.ExtractContactSheetAsync)).Should().Be(0);
    }

    [Fact]
    public async Task Vision_prompt_is_primed_with_the_measured_tone_words_when_visual_analysis_succeeded()
    {
        FrameGridResult grid = BuildColoredGrid(4, 4, fps: 2.0, (4, 200, 120, 50));

        (StepExecutionResult result, _, _, List<ShotCaptionRequest> requests) = await RunVisionCaptionTestAsync(
            cfg => cfg with { AnalyzeVisuals = true }, grid);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        requests.Should().ContainSingle();
        requests[0].MeasuredContext.Should().NotBeNull();
        requests[0].MeasuredContext.Should().Contain("color temperature Warm");
    }

    [Fact]
    public async Task MeasuredContext_is_null_when_visual_analysis_is_off_or_degraded()
    {
        (StepExecutionResult result, _, _, List<ShotCaptionRequest> requests) = await RunVisionCaptionTestAsync(
            cfg => cfg with { AnalyzeVisuals = false });

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        requests.Should().ContainSingle();
        requests[0].MeasuredContext.Should().BeNull();
    }

    [Fact]
    public async Task Caption_technicalIssues_reach_the_view_at_Compact_detail()
    {
        (StepExecutionResult result, _, _, _) = await RunVisionCaptionTestAsync(
            cfg => cfg with { AnalyzeVisuals = false, VisualDetail = VideoVisualDetail.Compact });

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        JsonElement caption = doc.RootElement.GetProperty("view").GetProperty("shots")[0].GetProperty("c");
        caption.GetProperty("issues").EnumerateArray().Select(e => e.GetString()).Should().Contain("soft focus");
    }

    // =======================================================================
    // Phase 4 §6: view — look groups, D1-D3 grading fields, char, ActiveCrop
    // =======================================================================

    private const int LookGridSize = 4;
    private static readonly (double, double)[] ThreeOneSecondShots = { (0.0, 1.0), (1.0, 2.0), (2.0, 3.0) };

    /// <summary>Shots s0/s1 share a warm look; s2 is a clearly different (cool) look and stays a singleton.</summary>
    private static FrameGridResult TwoWarmOneCoolGrid() => BuildColoredGrid(
        LookGridSize, LookGridSize, fps: 2.0,
        (2, 200, 120, 50), (2, 195, 118, 55), (2, 50, 120, 200));

    /// <summary>All three shots share one uniform warm look.</summary>
    private static FrameGridResult ThreeUniformWarmGrid() => BuildColoredGrid(
        LookGridSize, LookGridSize, fps: 2.0,
        (2, 200, 120, 50), (2, 198, 121, 52), (2, 202, 119, 48));

    private async Task<JsonDocument> RunLookTestAsync(
        FrameGridResult grid, Func<VideoAnalyzeStepConfig, VideoAnalyzeStepConfig>? extraOverride = null)
    {
        StepExecutionContext context = CreateContext(
            out Mock<IProjectFileWorkspace> workspace,
            out Mock<IMediaProbe> probe,
            out Mock<ISilenceDetector> silence,
            out Mock<IShotDetector> shotDetector,
            out _, out _,
            configOverride: cfg =>
            {
                VideoAnalyzeStepConfig c = cfg with
                {
                    DetectSilence = false, Transcription = VideoTranscriptionMode.Off,
                    AnalyzeVisuals = true, AnalyzeAudioLevels = true, DetectNearDuplicates = false,
                    VisualDetail = VideoVisualDetail.Full, MaxOutputChars = 24_000
                };
                return extraOverride is not null ? extraOverride(c) : c;
            });

        probe.Setup(p => p.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaProbeResult(3, 30, 1, 1920, 1080, "h264", "aac", 48000));
        shotDetector
            .Setup(s => s.DetectShotsAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ThreeOneSecondShots);

        var frameGridSampler = new Mock<IFrameGridSampler>();
        frameGridSampler
            .Setup(g => g.SampleAsync(
                It.IsAny<string>(), It.IsAny<VideoScratchSpace>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(grid);

        // A real, parseable WAV covering the FULL 3s shot timeline (not BuildSyntheticWav's
        // 1-second default, and not the default 100 zero-bytes) so ComputeShotAudio actually
        // produces an "a" node for every shot (AnalyzeAudioLevels is on above) instead of only
        // the first one / none at all.
        var audioExtractor = new Mock<IAudioExtractor>();
        audioExtractor
            .Setup(a => a.ExtractWavAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, CancellationToken>((_, outPath, _) =>
            {
                File.WriteAllBytes(outPath, BuildToneWav(seconds: 4));
                return Task.CompletedTask;
            });

        VideoAnalyzeStepExecutor executor = CreateExecutor(
            workspace, probe, silence, shotDetector,
            new Mock<ITranscriptionClientFactory>(), new Mock<IInferenceProviderResolver>(),
            audioExtractor: audioExtractor, frameGridSampler: frameGridSampler);

        StepExecutionResult result = await executor.ExecuteAsync(context);
        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        return JsonDocument.Parse(result.Output);
    }

    [Fact]
    public async Task Look_groups_appear_in_the_view_and_each_grouped_shot_carries_a_look_id()
    {
        using JsonDocument doc = await RunLookTestAsync(TwoWarmOneCoolGrid());

        JsonElement shots = doc.RootElement.GetProperty("view").GetProperty("shots");
        string look0 = shots[0].GetProperty("v").GetProperty("look").GetString()!;
        string look1 = shots[1].GetProperty("v").GetProperty("look").GetString()!;
        look0.Should().Be(look1);

        JsonElement lookGroups = doc.RootElement.GetProperty("view").GetProperty("lookGroups");
        lookGroups.GetArrayLength().Should().Be(1);
        List<string> memberIds = lookGroups[0].GetProperty("shotIds").EnumerateArray().Select(e => e.GetString()!).ToList();
        memberIds.Should().BeEquivalentTo(new[] { "s0", "s1" });
    }

    [Fact]
    public async Task A_shot_in_no_look_group_has_no_look_key()
    {
        using JsonDocument doc = await RunLookTestAsync(TwoWarmOneCoolGrid());

        JsonElement shots = doc.RootElement.GetProperty("view").GetProperty("shots");
        shots[2].GetProperty("v").TryGetProperty("look", out _).Should().BeFalse(
            "s2's look is unlike any other analyzed shot, so it must be a singleton with no look id");
    }

    [Fact]
    public async Task A_single_uniform_look_group_suppresses_every_per_shot_look_key_and_sets_meta_look_uniform()
    {
        using JsonDocument doc = await RunLookTestAsync(ThreeUniformWarmGrid());

        JsonElement shots = doc.RootElement.GetProperty("view").GetProperty("shots");
        foreach (JsonElement shot in shots.EnumerateArray())
            shot.GetProperty("v").TryGetProperty("look", out _).Should().BeFalse();

        doc.RootElement.GetProperty("meta").GetProperty("look").GetProperty("uniform").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task The_char_key_is_always_present_on_the_a_node_including_for_the_common_Dialogue_value()
    {
        using JsonDocument doc = await RunLookTestAsync(TwoWarmOneCoolGrid());

        foreach (JsonElement shot in doc.RootElement.GetProperty("view").GetProperty("shots").EnumerateArray())
            shot.GetProperty("a").TryGetProperty("char", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Full_detail_adds_temp_tone_sat_black_white_but_Compact_does_not()
    {
        using JsonDocument fullDoc = await RunLookTestAsync(TwoWarmOneCoolGrid());
        JsonElement fullVisual = fullDoc.RootElement.GetProperty("view").GetProperty("shots")[0].GetProperty("v");
        fullVisual.TryGetProperty("temp", out _).Should().BeTrue();
        fullVisual.TryGetProperty("tone", out _).Should().BeTrue();
        fullVisual.TryGetProperty("sat", out _).Should().BeTrue();
        fullVisual.TryGetProperty("black", out _).Should().BeTrue();
        fullVisual.TryGetProperty("white", out _).Should().BeTrue();

        using JsonDocument compactDoc = await RunLookTestAsync(
            TwoWarmOneCoolGrid(), extraOverride: c => c with { VisualDetail = VideoVisualDetail.Compact });
        JsonElement compactVisual = compactDoc.RootElement.GetProperty("view").GetProperty("shots")[0].GetProperty("v");
        compactVisual.TryGetProperty("temp", out _).Should().BeFalse();
        compactVisual.TryGetProperty("tone", out _).Should().BeFalse();
        compactVisual.TryGetProperty("sat", out _).Should().BeFalse();
    }

    [Fact]
    public async Task AnalyzeColorGrading_false_leaves_every_grading_field_null_and_disables_look_grouping()
    {
        using JsonDocument doc = await RunLookTestAsync(
            TwoWarmOneCoolGrid(), extraOverride: c => c with { AnalyzeColorGrading = false });

        JsonElement shots = doc.RootElement.GetProperty("view").GetProperty("shots");
        foreach (JsonElement shot in shots.EnumerateArray())
        {
            JsonElement v = shot.GetProperty("v");
            v.TryGetProperty("temp", out _).Should().BeFalse();
            v.TryGetProperty("look", out _).Should().BeFalse();
        }

        doc.RootElement.GetProperty("view").TryGetProperty("lookGroups", out _).Should().BeFalse();
    }

    [Fact]
    public async Task MaxViewLookGroups_caps_the_lookGroups_array()
    {
        // Two clearly-distinct pairs (warm/warm, cool/cool) -> two groups; cap it to one.
        FrameGridResult grid = BuildColoredGrid(
            LookGridSize, LookGridSize, fps: 2.0,
            (2, 200, 120, 50), (2, 198, 121, 52), (2, 50, 120, 200));
        StepExecutionContext context = CreateContext(
            out Mock<IProjectFileWorkspace> workspace,
            out Mock<IMediaProbe> probe,
            out Mock<ISilenceDetector> silence,
            out Mock<IShotDetector> shotDetector,
            out _, out _,
            configOverride: cfg => cfg with
            {
                DetectSilence = false, Transcription = VideoTranscriptionMode.Off,
                AnalyzeVisuals = true, AnalyzeAudioLevels = false, DetectNearDuplicates = false,
                VisualDetail = VideoVisualDetail.Full, MaxOutputChars = 24_000,
                LookSimilarityThreshold = 0.5, MaxViewLookGroups = 1
            });

        probe.Setup(p => p.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaProbeResult(3, 30, 1, 1920, 1080, "h264", "aac", 48000));
        shotDetector
            .Setup(s => s.DetectShotsAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ThreeOneSecondShots);

        var frameGridSampler = new Mock<IFrameGridSampler>();
        frameGridSampler
            .Setup(g => g.SampleAsync(
                It.IsAny<string>(), It.IsAny<VideoScratchSpace>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(grid);

        VideoAnalyzeStepExecutor executor = CreateExecutor(
            workspace, probe, silence, shotDetector,
            new Mock<ITranscriptionClientFactory>(), new Mock<IInferenceProviderResolver>(),
            audioExtractor: null, frameGridSampler: frameGridSampler);

        StepExecutionResult result = await executor.ExecuteAsync(context);
        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);

        using JsonDocument doc = JsonDocument.Parse(result.Output);
        doc.RootElement.GetProperty("view").GetProperty("lookGroups").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task ActiveCrop_only_appears_in_the_view_when_non_null()
    {
        // A plain uniform-color grid has no letterbox bars -> no crop.
        using JsonDocument noBarDoc = await RunLookTestAsync(TwoWarmOneCoolGrid());
        JsonElement noBarVisual = noBarDoc.RootElement.GetProperty("view").GetProperty("shots")[0].GetProperty("v");
        noBarVisual.TryGetProperty("crop", out _).Should().BeFalse();

        // A grid with real black bars top/bottom on every frame -> crop present.
        const int size = 20;
        int frameSize = size * size * 3;
        var pixels = new byte[6 * frameSize]; // 6 frames total (2 per shot, 3 shots)
        for (int f = 0; f < 6; f++)
        {
            for (int y = 0; y < size; y++)
            {
                bool bar = y < 4 || y >= size - 4;
                byte v = bar ? (byte)0 : (byte)200;
                for (int x = 0; x < size; x++)
                {
                    int idx = (f * frameSize) + (y * size + x) * 3;
                    pixels[idx] = v; pixels[idx + 1] = v; pixels[idx + 2] = v;
                }
            }
        }
        var barredGrid = new FrameGridResult(pixels, size, size, 2.0, 6);

        using JsonDocument barDoc = await RunLookTestAsync(barredGrid);
        JsonElement barVisual = barDoc.RootElement.GetProperty("view").GetProperty("shots")[0].GetProperty("v");
        barVisual.TryGetProperty("crop", out _).Should().BeTrue();
    }

    [Fact]
    public async Task None_detail_view_carries_no_look_or_char_keys_at_all()
    {
        using JsonDocument doc = await RunLookTestAsync(
            TwoWarmOneCoolGrid(), extraOverride: c => c with { VisualDetail = VideoVisualDetail.None });

        JsonElement shots = doc.RootElement.GetProperty("view").GetProperty("shots");
        foreach (JsonElement shot in shots.EnumerateArray())
        {
            shot.TryGetProperty("v", out _).Should().BeFalse();
            shot.TryGetProperty("a", out _).Should().BeFalse();
        }

        doc.RootElement.GetProperty("view").TryGetProperty("lookGroups", out _).Should().BeFalse();
    }

    // =======================================================================
    // Phase 4 §5.4-5.5: sharpness
    // =======================================================================

    private async Task<(StepExecutionResult Result, JsonDocument Artifact)> RunSharpnessTestAsync(
        Mock<ISharpnessSampler> sharpnessSampler, Func<VideoAnalyzeStepConfig, VideoAnalyzeStepConfig>? extraOverride = null)
    {
        StepExecutionContext context = CreateContext(
            out Mock<IProjectFileWorkspace> workspace,
            out Mock<IMediaProbe> probe,
            out Mock<ISilenceDetector> silence,
            out Mock<IShotDetector> shotDetector,
            out _, out _,
            configOverride: cfg =>
            {
                VideoAnalyzeStepConfig c = cfg with
                {
                    DetectSilence = false, Transcription = VideoTranscriptionMode.Off,
                    AnalyzeVisuals = true, AnalyzeAudioLevels = false, DetectNearDuplicates = false,
                    VisualDetail = VideoVisualDetail.Full, MaxOutputChars = 24_000
                };
                return extraOverride is not null ? extraOverride(c) : c;
            });

        probe.Setup(p => p.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaProbeResult(2, 30, 1, 1920, 1080, "h264", "aac", 48000));
        shotDetector
            .Setup(s => s.DetectShotsAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([(0.0, 1.0)]);

        var frameGridSampler = new Mock<IFrameGridSampler>();
        frameGridSampler
            .Setup(g => g.SampleAsync(
                It.IsAny<string>(), It.IsAny<VideoScratchSpace>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSyntheticGrid(4, 4, frameCount: 4, fps: 2.0));

        VideoAnalyzeStepExecutor executor = CreateExecutor(
            workspace, probe, silence, shotDetector,
            new Mock<ITranscriptionClientFactory>(), new Mock<IInferenceProviderResolver>(),
            audioExtractor: null, frameGridSampler: frameGridSampler, sharpnessSampler: sharpnessSampler);

        // Added AFTER CreateExecutor so this wins over its own default UploadArtifactAsync setup
        // (Moq resolves overlapping setups in most-recently-defined order).
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
        return (result, JsonDocument.Parse(capturedArtifactJson!));
    }

    [Fact]
    public async Task DetectSharpness_false_by_default_makes_no_sharpness_invocations()
    {
        var sharpnessSampler = new Mock<ISharpnessSampler>(MockBehavior.Strict);

        (StepExecutionResult _, JsonDocument artifact) = await RunSharpnessTestAsync(sharpnessSampler);

        sharpnessSampler.Invocations.Should().BeEmpty();
        JsonElement shot0 = artifact.RootElement.GetProperty("shots")[0];
        shot0.GetProperty("visual").GetProperty("sharpness").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task DetectSharpness_true_populates_Sharpness_and_sets_provenance_SharpnessAvailable()
    {
        var sharpnessSampler = new Mock<ISharpnessSampler>();
        sharpnessSampler
            .Setup(s => s.MeasureAsync(It.IsAny<string>(), It.IsAny<VideoScratchSpace>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0.75);

        (StepExecutionResult _, JsonDocument artifact) = await RunSharpnessTestAsync(
            sharpnessSampler, extraOverride: c => c with { DetectSharpness = true });

        sharpnessSampler.Invocations.Count(i => i.Method.Name == nameof(ISharpnessSampler.MeasureAsync)).Should().Be(1);
        JsonElement shot0 = artifact.RootElement.GetProperty("shots")[0];
        shot0.GetProperty("visual").GetProperty("sharpness").GetDouble().Should().BeApproximately(0.75, 1e-9);
        artifact.RootElement.GetProperty("provenance").GetProperty("sharpnessAvailable").GetBoolean().Should().BeTrue();
        artifact.RootElement.GetProperty("provenance").GetProperty("sharpnessShotCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task A_sharpness_sampler_failure_degrades_to_null_sharpness_not_a_failed_step()
    {
        var sharpnessSampler = new Mock<ISharpnessSampler>();
        sharpnessSampler
            .Setup(s => s.MeasureAsync(It.IsAny<string>(), It.IsAny<VideoScratchSpace>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ffmpeg boom"));

        (StepExecutionResult result, JsonDocument artifact) = await RunSharpnessTestAsync(
            sharpnessSampler, extraOverride: c => c with { DetectSharpness = true });

        result.Status.Should().Be(StepStatus.Completed);
        JsonElement shot0 = artifact.RootElement.GetProperty("shots")[0];
        shot0.GetProperty("visual").GetProperty("sharpness").ValueKind.Should().Be(JsonValueKind.Null);
        artifact.RootElement.GetProperty("provenance").GetProperty("sharpnessAvailable").GetBoolean().Should().BeFalse();
    }

    // =======================================================================
    // Phase 4 §3: weighted step-progress plan wiring
    // =======================================================================

    [Fact]
    public async Task Progress_percentages_reported_by_a_full_run_are_monotonic_and_reach_one_hundred()
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
                DetectSilence = false,
                Transcription = VideoTranscriptionMode.Off,
                AnalyzeVisuals = true,
                AnalyzeAudioLevels = true,
                DetectNearDuplicates = true,
                Vision = VideoVisionMode.Optional,
                MinCaptionShotSeconds = 0.0,
                OfferMusicTracks = true
            });

        probe.Setup(p => p.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaProbeResult(4, 30, 1, 32, 18, "h264", "aac", 48000));
        var shots = new[] { (0.0, 1.0), (1.0, 2.0), (2.0, 3.0) };
        shotDetector
            .Setup(s => s.DetectShotsAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(shots);

        var frameGridSampler = new Mock<IFrameGridSampler>();
        frameGridSampler
            .Setup(g => g.SampleAsync(It.IsAny<string>(), It.IsAny<VideoScratchSpace>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSyntheticGrid(32, 18, frameCount: 8, fps: 2.0));

        ResolvedInferenceProvider provider = new(
            Guid.NewGuid(), "vision-test", InferenceProviderKind.OpenAICompatible, "http://localhost:9999", "vision-1", "", 30);
        providerResolver
            .Setup(r => r.ResolveVisionAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(provider);

        var shotCaptioner = new Mock<IShotCaptioner>();
        shotCaptioner
            .Setup(c => c.CaptionAsync(It.IsAny<ResolvedInferenceProvider>(), It.IsAny<ShotCaptionRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResolvedInferenceProvider _, ShotCaptionRequest r, int _, CancellationToken _) => new VideoShotCaption(
                r.ShotId, "a caption", [], "action", "setting", "mood", "Medium", "Eye level", [], [],
                "Unknown", "flat", "neutral", "centered", []));

        workspace
            .Setup(w => w.ListFilesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectWorkspaceFile>
            {
                new(Guid.NewGuid(), ProjectId, "bed.mp3", null, "userFiles", "projects/p/userFiles/bed.mp3", "audio/mpeg", 4096, DateTime.UtcNow, null)
            });
        workspace
            .Setup(w => w.UploadArtifactAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .ReturnsAsync("projects/p/agentFiles/video-analysis/e/step-1-analysis.json");

        VideoAnalyzeStepExecutor executor = CreateExecutor(
            workspace, probe, silence, shotDetector, transcriptionFactory, providerResolver,
            audioExtractor: null, frameGridSampler: frameGridSampler, keyframeExtractor: null, shotCaptioner: shotCaptioner);

        var reported = new List<(string Stage, int? Percent)>();
        context.ProgressReporter = (stage, percent, _, _, _, _) =>
        {
            reported.Add((stage, percent));
            return Task.CompletedTask;
        };

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        reported.Should().NotBeEmpty();

        List<int> percents = reported.Where(r => r.Percent.HasValue).Select(r => r.Percent!.Value).ToList();
        percents.Should().NotBeEmpty();
        percents.Should().BeInAscendingOrder("the monotonic clamp must prevent any reported percent from regressing");
        percents[^1].Should().Be(100, "the last stage (UploadArtifact) must reach 100%");
    }

    [Fact]
    public async Task ExtractKeyframes_stage_is_actually_reported_during_a_Vision_enabled_run()
    {
        // VideoAnalyzeProgressPlan.Stage.ExtractKeyframes is added to the enabled-stages list
        // whenever Vision != Off, but keyframe extraction happens inline inside the captioning
        // loop, which — before this fix — only ever reported Stage.CaptionShots. ExtractKeyframes
        // therefore ate 5 weight units off the percentage denominator with zero corresponding
        // progress events. Assert an actual report whose label names the keyframe-extraction work.
        StepExecutionContext context = CreateContext(
            out Mock<IProjectFileWorkspace> workspace,
            out Mock<IMediaProbe> probe,
            out Mock<ISilenceDetector> silence,
            out Mock<IShotDetector> shotDetector,
            out Mock<ITranscriptionClientFactory> transcriptionFactory,
            out Mock<IInferenceProviderResolver> providerResolver,
            configOverride: cfg => cfg with
            {
                DetectSilence = false,
                Transcription = VideoTranscriptionMode.Off,
                AnalyzeVisuals = true,
                Vision = VideoVisionMode.Optional,
                MinCaptionShotSeconds = 0.0
            });

        probe.Setup(p => p.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaProbeResult(4, 30, 1, 32, 18, "h264", "aac", 48000));
        var shots = new[] { (0.0, 1.0), (1.0, 2.0), (2.0, 3.0) };
        shotDetector
            .Setup(s => s.DetectShotsAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(shots);

        var frameGridSampler = new Mock<IFrameGridSampler>();
        frameGridSampler
            .Setup(g => g.SampleAsync(It.IsAny<string>(), It.IsAny<VideoScratchSpace>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSyntheticGrid(32, 18, frameCount: 8, fps: 2.0));

        ResolvedInferenceProvider provider = new(
            Guid.NewGuid(), "vision-test", InferenceProviderKind.OpenAICompatible, "http://localhost:9999", "vision-1", "", 30);
        providerResolver
            .Setup(r => r.ResolveVisionAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(provider);

        var shotCaptioner = new Mock<IShotCaptioner>();
        shotCaptioner
            .Setup(c => c.CaptionAsync(It.IsAny<ResolvedInferenceProvider>(), It.IsAny<ShotCaptionRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResolvedInferenceProvider _, ShotCaptionRequest r, int _, CancellationToken _) => new VideoShotCaption(
                r.ShotId, "a caption", [], "action", "setting", "mood", "Medium", "Eye level", [], [],
                "Unknown", "flat", "neutral", "centered", []));

        workspace
            .Setup(w => w.UploadArtifactAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .ReturnsAsync("projects/p/agentFiles/video-analysis/e/step-1-analysis.json");

        VideoAnalyzeStepExecutor executor = CreateExecutor(
            workspace, probe, silence, shotDetector, transcriptionFactory, providerResolver,
            audioExtractor: null, frameGridSampler: frameGridSampler, keyframeExtractor: null, shotCaptioner: shotCaptioner);

        var reported = new List<string>();
        context.ProgressReporter = (stage, _, _, _, _, _) =>
        {
            reported.Add(stage);
            return Task.CompletedTask;
        };

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        reported.Should().Contain(s => s.Contains("Extracting keyframes"),
            "keyframe extraction must actually report Stage.ExtractKeyframes progress, not silently eat its weight");
    }

    // =======================================================================
    // Phase 3: view.placements carries a distinguishing time window
    //
    // MaxTimeSlicesPerRegion splits ONE long shot's region into several candidate
    // sub-windows. Before startSec/endSec were in the view, those candidates
    // serialized byte-identically except for their id, so MotionGraphicsPlanner had
    // nothing to choose on and defaulted to the first of each identical run. These
    // tests pin the field down at the view layer (OverlayPlacementBuilderTests
    // already covers the slicing itself).
    // =======================================================================

    /// <summary>
    /// One long, perfectly static shot: a uniform grid means zero inter-frame motion, so the whole
    /// shot is one still window and <c>SplitIntoTimeSlices</c> actually splits it — the exact
    /// talking-head shape that produced the byte-identical candidates in production.
    /// </summary>
    private async Task<(StepExecutionResult Result, JsonDocument Artifact)> RunOneLongStaticShotWithPlacementsAsync(
        Func<VideoAnalyzeStepConfig, VideoAnalyzeStepConfig>? extraOverride = null)
    {
        const double ShotSeconds = 30.0;

        StepExecutionContext context = CreateContext(
            out Mock<IProjectFileWorkspace> workspace,
            out Mock<IMediaProbe> probe,
            out Mock<ISilenceDetector> silence,
            out Mock<IShotDetector> shotDetector,
            out _, out _,
            configOverride: cfg =>
            {
                VideoAnalyzeStepConfig c = cfg with
                {
                    DetectSilence = false, Transcription = VideoTranscriptionMode.Off,
                    AnalyzeVisuals = true, AnalyzeAudioLevels = false, DetectNearDuplicates = false,
                    VisualDetail = VideoVisualDetail.Full, MaxOutputChars = 24_000,
                    EmitOverlayPlacements = true, MaxPlacementsPerShot = 1, MaxTimeSlicesPerRegion = 3
                };
                return extraOverride is not null ? extraOverride(c) : c;
            });

        probe.Setup(p => p.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaProbeResult(ShotSeconds, 30, 1, 1920, 1080, "h264", "aac", 48000));
        shotDetector
            .Setup(s => s.DetectShotsAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([(0.0, ShotSeconds)]);

        var frameGridSampler = new Mock<IFrameGridSampler>();
        frameGridSampler
            .Setup(g => g.SampleAsync(
                It.IsAny<string>(), It.IsAny<VideoScratchSpace>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildColoredGrid(4, 4, fps: 2.0, (60, 150, 140, 130)));

        VideoAnalyzeStepExecutor executor = CreateExecutor(
            workspace, probe, silence, shotDetector,
            new Mock<ITranscriptionClientFactory>(), new Mock<IInferenceProviderResolver>(),
            audioExtractor: null, frameGridSampler: frameGridSampler);

        // Added AFTER CreateExecutor so this wins over its own default setup (Moq resolves
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
        return (result, JsonDocument.Parse(capturedArtifactJson!));
    }

    [Fact]
    public async Task Placements_sharing_a_shot_and_region_are_distinguishable_by_their_time_window()
    {
        (StepExecutionResult result, JsonDocument artifact) = await RunOneLongStaticShotWithPlacementsAsync();
        using JsonDocument artifactDoc = artifact;

        using JsonDocument doc = JsonDocument.Parse(result.Output);
        JsonElement placements = doc.RootElement.GetProperty("view").GetProperty("placements");

        List<JsonElement> all = placements.EnumerateArray().ToList();
        all.Should().HaveCountGreaterThan(1,
            "MaxTimeSlicesPerRegion must actually split this one long still shot into several candidates — " +
            "otherwise this test proves nothing about telling them apart");

        // Everything EXCEPT the time window is identical across these candidates: that is exactly
        // the production shape that left the planner with nothing to choose on.
        all.Select(p => p.GetProperty("shotId").GetString()).Distinct().Should().HaveCount(1);
        all.Select(p => p.GetProperty("region").GetString()).Distinct().Should().HaveCount(1);
        all.Select(p => p.GetProperty("fit").GetInt32()).Distinct().Should().HaveCount(1);

        List<double> starts = all.Select(p => p.GetProperty("startSec").GetDouble()).ToList();
        List<double> ends = all.Select(p => p.GetProperty("endSec").GetDouble()).ToList();

        starts.Should().OnlyHaveUniqueItems("two candidates on the same shot+region must not be indistinguishable");
        starts.Should().BeInAscendingOrder();
        for (int i = 0; i < all.Count; i++)
        {
            ends[i].Should().BeGreaterThan(starts[i]);
            starts[i].Should().BeGreaterThanOrEqualTo(0);
            ends[i].Should().BeLessThanOrEqualTo(30.0);
            if (i > 0)
                starts[i].Should().BeGreaterThanOrEqualTo(ends[i - 1], "time slices must not overlap");
        }

        // The whole point: serializing two entries must not produce identical JSON.
        all[0].GetRawText().Should().NotBe(all[1].GetRawText());
    }

    [Fact]
    public async Task Placement_view_time_window_is_the_artifacts_own_window_rounded_never_an_invented_one()
    {
        (StepExecutionResult result, JsonDocument artifact) = await RunOneLongStaticShotWithPlacementsAsync();
        using JsonDocument artifactDoc = artifact;

        Dictionary<string, (double Start, double End)> fromArtifact = artifactDoc.RootElement
            .GetProperty("placements")
            .EnumerateArray()
            .ToDictionary(
                p => p.GetProperty("id").GetString()!,
                p => (p.GetProperty("startSec").GetDouble(), p.GetProperty("endSec").GetDouble()));

        fromArtifact.Should().NotBeEmpty();

        using JsonDocument doc = JsonDocument.Parse(result.Output);
        foreach (JsonElement p in doc.RootElement.GetProperty("view").GetProperty("placements").EnumerateArray())
        {
            string id = p.GetProperty("id").GetString()!;
            fromArtifact.Should().ContainKey(id);
            (double artifactStart, double artifactEnd) = fromArtifact[id];

            p.GetProperty("startSec").GetDouble().Should().Be(Math.Round(artifactStart, 2));
            p.GetProperty("endSec").GetDouble().Should().Be(Math.Round(artifactEnd, 2));
        }
    }

    [Fact]
    public async Task None_detail_still_omits_placements_entirely_even_now_that_they_carry_time()
    {
        (StepExecutionResult result, JsonDocument artifact) = await RunOneLongStaticShotWithPlacementsAsync(
            c => c with { VisualDetail = VideoVisualDetail.None });
        using JsonDocument artifactDoc = artifact;

        using JsonDocument doc = JsonDocument.Parse(result.Output);
        doc.RootElement.GetProperty("view").TryGetProperty("placements", out _).Should().BeFalse(
            "adding a time window must not move placements out of the degrade-before-drop gate");
        doc.RootElement.GetProperty("meta").GetProperty("offeredPlacementIdCount").GetInt32().Should().Be(0);
    }
}
