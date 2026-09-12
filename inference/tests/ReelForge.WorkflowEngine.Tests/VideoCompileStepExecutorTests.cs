using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Workflows;
using ReelForge.WorkflowEngine.Execution;
using ReelForge.WorkflowEngine.Execution.StepExecutors;
using ReelForge.WorkflowEngine.Services.Storage;
using ReelForge.WorkflowEngine.Services.Video;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Centrepiece test suite for <see cref="VideoCompileStepExecutor"/> (plan §4.3, WS5 DoD). Covers
/// the id-resolution/normalization pipeline that is the primary correctness surface: unknown-id
/// rejection against the OFFERED set (not merely the full artifact), out-of-order/overlap
/// rejection, exact-rational frame quantization, padding/clamping at the media boundaries,
/// MinSegmentMs dropping, MaxSegments capping, empty-Keep and MinRetainedRatio guardrails, and the
/// codec/preset allowlist (R11).
/// </summary>
public class VideoCompileStepExecutorTests
{
    private const string ProjectFileStorageRoot = "video-compile-tests";
    private static readonly Guid ProjectId = Guid.NewGuid();
    private const string AnalysisKey = "projects/p/agentFiles/video-analysis/e/step-1-analysis.json";
    private const string SourceVideoKey = "projects/p/outputFiles/e/render.mp4";

    // ---------------------------------------------------------------------
    // Frame-exact rational arithmetic (R9) — direct, no I/O needed.
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(1.0, 30, 1, 30)]
    [InlineData(0.0, 30, 1, 0)]
    [InlineData(2.5, 30, 1, 75)]
    public void ToStartFrame_at_30_1_is_exact(double startSec, int fpsNum, int fpsDen, long expectedFrame)
    {
        VideoCompileStepExecutor.ToStartFrame(startSec, fpsNum, fpsDen).Should().Be(expectedFrame);
    }

    [Fact]
    public void Frame_quantization_at_30000_1001_is_exact_not_approximate()
    {
        // 30000/1001 ≈ 29.97003 fps. 1.0s * 30000/1001 = 29.9700... -> floor=29, ceil=30.
        VideoCompileStepExecutor.ToStartFrame(1.0, 30000, 1001).Should().Be(29);
        VideoCompileStepExecutor.ToEndFrame(1.0, 30000, 1001).Should().Be(30);

        // Round-trip: frame -> seconds uses frame * fpsDen / fpsNum exactly.
        double backToSecStart = VideoCompileStepExecutor.FrameToSec(29, 30000, 1001);
        double backToSecEnd = VideoCompileStepExecutor.FrameToSec(30, 30000, 1001);
        backToSecStart.Should().BeApproximately(29.0 * 1001 / 30000, 1e-9);
        backToSecEnd.Should().BeApproximately(30.0 * 1001 / 30000, 1e-9);
    }

    [Fact]
    public void Frame_quantization_at_30_1_matches_naive_multiplication()
    {
        VideoCompileStepExecutor.ToStartFrame(10.0, 30, 1).Should().Be(300);
        VideoCompileStepExecutor.ToEndFrame(10.0, 30, 1).Should().Be(300);
        VideoCompileStepExecutor.FrameToSec(300, 30, 1).Should().Be(10.0);
    }

    // ---------------------------------------------------------------------
    // Unknown id rejection
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Keep_span_referencing_id_present_in_artifact_but_not_offered_is_rejected()
    {
        // "w0" exists in the full artifact's Words list (for audit) but is never an OfferedId —
        // the compile step must reject it even though ExtractJsonValue-style lookups could find it
        // "in the artifact" if we looked at the wrong collection.
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0), ("s1", 10.0, 20.0) },
            words: new[] { ("w0", 1.0, 1.5) },
            offeredIds: new[] { "s0", "s1" }); // w0 deliberately excluded

        string decisionJson = BuildDecisionJson(("s0", "w0", "keep"));
        StepExecutionContext context = CreateContext(artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace);

        StepExecutionResult result = await CreateExecutor(workspace).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        ErrorCode(result).Should().Be("UNKNOWN_ID");
    }

    [Fact]
    public async Task Keep_span_referencing_a_completely_unknown_id_is_rejected()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            offeredIds: new[] { "s0" });

        string decisionJson = BuildDecisionJson(("s0", "s99", "keep"));
        StepExecutionContext context = CreateContext(artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace);

        StepExecutionResult result = await CreateExecutor(workspace).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        ErrorCode(result).Should().Be("UNKNOWN_ID");
    }

    // ---------------------------------------------------------------------
    // Ordering / overlap
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Out_of_order_keep_spans_fail_with_retry_usable_diagnostic()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0), ("s1", 10.0, 20.0), ("s2", 20.0, 30.0) },
            offeredIds: new[] { "s0", "s1", "s2" });

        // Second span (s0) starts before the first span (s2) — reordering is not supported in v1.
        string decisionJson = BuildDecisionJson(("s2", "s2", "later"), ("s0", "s0", "earlier"));
        StepExecutionContext context = CreateContext(artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace);

        StepExecutionResult result = await CreateExecutor(workspace).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        ErrorCode(result).Should().Be("SPANS_OUT_OF_ORDER");
        result.ErrorDetails.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Overlapping_keep_spans_fail()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0), ("s1", 10.0, 20.0), ("s2", 15.0, 25.0) },
            offeredIds: new[] { "s0", "s1", "s2" });

        // [0,20) via s0..s1, then [15,25) via s2..s2 — overlaps the first span.
        string decisionJson = BuildDecisionJson(("s0", "s1", "first"), ("s2", "s2", "second"));
        StepExecutionContext context = CreateContext(artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace);

        StepExecutionResult result = await CreateExecutor(workspace).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        ErrorCode(result).Should().Be("SPANS_OVERLAP");
    }

    // ---------------------------------------------------------------------
    // Empty Keep / MinRetainedRatio
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Empty_keep_list_fails()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            offeredIds: new[] { "s0" });

        string decisionJson = """{"keep":[],"editRationale":"nothing","suggestedTitle":"x"}""";
        StepExecutionContext context = CreateContext(artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace);

        StepExecutionResult result = await CreateExecutor(workspace).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        ErrorCode(result).Should().Be("EMPTY_KEEP");
    }

    [Fact]
    public async Task MinRetainedRatio_violation_fails()
    {
        // Duration 100s; keep only [0,5) => 5% retained, well under a 0.9 minimum.
        VideoAnalysisArtifact artifact = BuildArtifact(
            durationSec: 100.0,
            shots: new[] { ("s0", 0.0, 5.0), ("s1", 5.0, 100.0) },
            offeredIds: new[] { "s0", "s1" });

        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        StepExecutionContext context = CreateContext(
            artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace,
            configOverride: cfg => cfg with { Expect = new VideoCompileExpectation(MinRetainedRatio: 0.9) });

        StepExecutionResult result = await CreateExecutor(workspace).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        ErrorCode(result).Should().Be("EXPECT_FAILED");
    }

    // ---------------------------------------------------------------------
    // Padding clamped at t=0 / t=duration
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Padding_is_clamped_at_media_boundaries_and_does_not_go_negative_or_past_duration()
    {
        double duration = 50.0;
        VideoAnalysisArtifact artifact = BuildArtifact(
            durationSec: duration,
            shots: new[] { ("s0", 0.0, 2.0), ("s1", 48.0, 50.0) },
            offeredIds: new[] { "s0", "s1" });

        // Large padding (500ms) around spans that already touch t=0 and t=duration.
        string decisionJson = BuildDecisionJson(("s0", "s0", "start"), ("s1", "s1", "end"));
        StepExecutionContext context = CreateContext(
            artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace,
            configOverride: cfg => cfg with { PrePaddingMs = 500, PostPaddingMs = 500, MinSegmentMs = 0 });

        JsonElement edl = default;
        StepExecutionResult result = await CreateExecutor(workspace, edlCaptured: e => edl = e).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed);
        JsonElement segments = edl.GetProperty("segments");
        segments.GetArrayLength().Should().Be(2);
        segments[0].GetProperty("snappedStartSec").GetDouble().Should().BeGreaterThanOrEqualTo(0.0);
        segments[segments.GetArrayLength() - 1].GetProperty("snappedEndSec").GetDouble().Should().BeLessThanOrEqualTo(duration + 1e-6);
    }

    // ---------------------------------------------------------------------
    // MinSegmentMs drop
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Spans_shorter_than_MinSegmentMs_are_dropped()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(
            durationSec: 100.0,
            shots: new[] { ("s0", 0.0, 0.1), ("s1", 20.0, 40.0) }, // s0 is only 100ms long
            offeredIds: new[] { "s0", "s1" });

        string decisionJson = BuildDecisionJson(("s0", "s0", "tiny"), ("s1", "s1", "real"));
        StepExecutionContext context = CreateContext(
            artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace,
            configOverride: cfg => cfg with { MinSegmentMs = 250, PrePaddingMs = 0, PostPaddingMs = 0 });

        JsonElement edl = default;
        StepExecutionResult result = await CreateExecutor(workspace, edlCaptured: e => edl = e).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed);
        edl.GetProperty("segments").GetArrayLength().Should().Be(1, "the 100ms span must be dropped as shorter than MinSegmentMs=250");
    }

    // ---------------------------------------------------------------------
    // MaxSegments cap
    // ---------------------------------------------------------------------

    [Fact]
    public async Task MaxSegments_caps_the_number_of_compiled_segments()
    {
        var shots = Enumerable.Range(0, 10)
            .Select(i => ($"s{i}", (double)i * 10, (double)(i * 10 + 5))) // 10 separate 5s shots with 5s gaps
            .ToArray();

        VideoAnalysisArtifact artifact = BuildArtifact(
            durationSec: 100.0,
            shots: shots,
            offeredIds: shots.Select(s => s.Item1).ToArray());

        (string, string, string)[] spans = shots.Select(s => (s.Item1, s.Item1, "keep")).ToArray();
        string decisionJson = BuildDecisionJson(spans);

        StepExecutionContext context = CreateContext(
            artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace,
            configOverride: cfg => cfg with { MaxSegments = 3, PrePaddingMs = 0, PostPaddingMs = 0, MinSegmentMs = 0 });

        JsonElement edl = default;
        StepExecutionResult result = await CreateExecutor(workspace, edlCaptured: e => edl = e).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed);
        edl.GetProperty("segments").GetArrayLength().Should().Be(3);
        edl.GetProperty("droppedSegmentsOverCap").GetInt32().Should().Be(7);
    }

    // ---------------------------------------------------------------------
    // Codec / preset allowlist (R11)
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Disallowed_video_codec_is_rejected_even_though_it_came_from_config_not_the_model()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            offeredIds: new[] { "s0" });

        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        StepExecutionContext context = CreateContext(
            artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace,
            configOverride: cfg => cfg with { VideoCodec = "libx264; rm -rf /" });

        StepExecutionResult result = await CreateExecutor(workspace).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        ErrorCode(result).Should().Be("CODEC_NOT_ALLOWED");
    }

    [Fact]
    public async Task Disallowed_preset_is_rejected()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            offeredIds: new[] { "s0" });

        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        StepExecutionContext context = CreateContext(
            artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace,
            configOverride: cfg => cfg with { Preset = "not-a-real-preset" });

        StepExecutionResult result = await CreateExecutor(workspace).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        ErrorCode(result).Should().Be("CODEC_NOT_ALLOWED");
    }

    // ---------------------------------------------------------------------
    // Happy path sanity
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Successful_compile_sets_ArtifactStorageKey_and_OutputStorageKey_and_never_throws()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0), ("s1", 10.0, 20.0) },
            offeredIds: new[] { "s0", "s1" });

        string decisionJson = BuildDecisionJson(("s0", "s1", "keep the whole thing"));
        StepExecutionContext context = CreateContext(artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace);

        StepExecutionResult result = await CreateExecutor(workspace).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed);
        result.ArtifactStorageKey.Should().NotBeNullOrWhiteSpace();
        result.OutputStorageKey.Should().NotBeNullOrWhiteSpace();

        Action parse = () => JsonDocument.Parse(result.Output);
        parse.Should().NotThrow();
    }

    [Fact]
    public async Task Missing_config_json_fails_with_valid_json_never_throws()
    {
        var step = new WorkflowStep
        {
            Id = Guid.NewGuid(),
            StepOrder = 3,
            StepType = StepType.VideoCompile,
            VideoCompileConfigJson = null
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

        StepExecutionResult result = await CreateExecutor(new Mock<IProjectFileWorkspace>()).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        Action parse = () => JsonDocument.Parse(result.Output);
        parse.Should().NotThrow();
        ErrorCode(result).Should().Be("CONFIG_INVALID");
    }

    // =======================================================================
    // Test infrastructure
    // =======================================================================

    private static VideoCompileStepExecutor CreateExecutor(
        Mock<IProjectFileWorkspace> workspace, Action<JsonElement>? edlCaptured = null)
    {
        var toolRunner = new Mock<IVideoToolRunner>();
        toolRunner
            .Setup(t => t.RunFfmpegAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VideoToolResult(0, string.Empty, string.Empty, false));

        var mediaProbe = new Mock<IMediaProbe>();
        mediaProbe
            .Setup(p => p.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaProbeResult(10, 30, 1, 1920, 1080, "h264", "aac", 48000));

        if (edlCaptured is not null)
        {
            workspace
                .Setup(w => w.UploadArtifactAsync(
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<CancellationToken>(), It.IsAny<string>()))
                .Callback<Guid, string, string, string, CancellationToken, string>((_, path, _, _, _, _) =>
                {
                    string json = File.ReadAllText(path);
                    edlCaptured(JsonDocument.Parse(json).RootElement.Clone());
                })
                .ReturnsAsync("projects/p/agentFiles/video-analysis/e/step-3-edl.json");
        }
        else
        {
            workspace
                .Setup(w => w.UploadArtifactAsync(
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<CancellationToken>(), It.IsAny<string>()))
                .ReturnsAsync("projects/p/agentFiles/video-analysis/e/step-3-edl.json");
        }

        workspace
            .Setup(w => w.UploadBinaryFileAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<SummaryStatus>(), It.IsAny<FileIndexingStatus>(), It.IsAny<CancellationToken>(),
                It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync((Guid projectId, string _, string fileName, string mime, SummaryStatus _, FileIndexingStatus _,
                CancellationToken _, string category, string? _) =>
                new ProjectWorkspaceFile(
                    Guid.NewGuid(), projectId, fileName, null, category,
                    $"projects/{projectId}/{category}/{Guid.NewGuid():D}.mp4", mime, 1024, DateTime.UtcNow, null));

        string tempScratchRoot = Path.Combine(Path.GetTempPath(), ProjectFileStorageRoot, Guid.NewGuid().ToString("N"));
        var options = Options.Create(new VideoEditingOptions
        {
            ScratchPath = tempScratchRoot,
            MaxConcurrentJobs = 1,
            AnalyzeTimeoutSeconds = 30,
            CompileTimeoutSeconds = 30
        });

        var scopeFactory = new Mock<IServiceScopeFactory>();

        return new VideoCompileStepExecutor(
            toolRunner.Object,
            mediaProbe.Object,
            workspace.Object,
            scopeFactory.Object,
            options,
            NullLogger<VideoCompileStepExecutor>.Instance);
    }

    private static StepExecutionContext CreateContext(
        VideoAnalysisArtifact artifact,
        string decisionJson,
        out Mock<IProjectFileWorkspace> workspace,
        Func<VideoCompileStepConfig, VideoCompileStepConfig>? configOverride = null)
    {
        workspace = new Mock<IProjectFileWorkspace>();

        string artifactJson = JsonSerializer.Serialize(artifact, ArtifactOptions());

        workspace
            .Setup(w => w.DownloadStorageKeyToFileAsync(
                It.IsAny<Guid>(), AnalysisKey, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, string, CancellationToken>((_, _, destPath, _) =>
            {
                File.WriteAllText(destPath, artifactJson);
                return Task.CompletedTask;
            });

        workspace
            .Setup(w => w.DownloadStorageKeyToFileAsync(
                It.IsAny<Guid>(), SourceVideoKey, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, string, CancellationToken>((_, _, destPath, _) =>
            {
                File.WriteAllBytes(destPath, new byte[] { 0x00, 0x01, 0x02 });
                return Task.CompletedTask;
            });

        VideoCompileStepConfig config = new(
            Version: 1,
            Decision: new ExtractInputRef(ExtractInputSource.Previous),
            AnalysisStepOrder: 1);

        if (configOverride is not null)
            config = configOverride(config);

        string configJson = JsonSerializer.Serialize(config, ConfigOptions());

        var step = new WorkflowStep
        {
            Id = Guid.NewGuid(),
            StepOrder = 3,
            StepType = StepType.VideoCompile,
            VideoCompileConfigJson = configJson
        };

        List<StepOutputHistoryEntry> history =
        [
            new StepOutputHistoryEntry(0, "Render", "{}", OutputStorageKey: SourceVideoKey, ArtifactStorageKey: null),
            new StepOutputHistoryEntry(1, "Analyze", "{}", OutputStorageKey: null, ArtifactStorageKey: AnalysisKey),
            new StepOutputHistoryEntry(2, "StoryEditor", decisionJson, OutputStorageKey: null, ArtifactStorageKey: null)
        ];

        return new StepExecutionContext
        {
            Execution = new WorkflowExecution { Id = Guid.NewGuid(), ProjectId = ProjectId },
            Step = step,
            AllSteps = [step],
            AccumulatedOutput = decisionJson,
            StepOutputHistory = history,
            CurrentStepIndex = 2,
            IterationCount = 0,
            CorrelationId = "test",
            CancellationToken = CancellationToken.None
        };
    }

    private static string ErrorCode(StepExecutionResult result)
    {
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        return doc.RootElement.GetProperty("error").GetProperty("code").GetString() ?? string.Empty;
    }

    private static string BuildDecisionJson(params (string FromId, string ToId, string Reason)[] spans)
    {
        var keep = spans.Select(s => new { fromId = s.FromId, toId = s.ToId, reason = s.Reason });
        return JsonSerializer.Serialize(new { keep, editRationale = "test", suggestedTitle = "Test Edit" });
    }

    private static VideoAnalysisArtifact BuildArtifact(
        (string Id, double Start, double End)[] shots,
        (string Id, double Start, double End)[]? silences = null,
        (string Id, double Start, double End)[]? segments = null,
        (string Id, double Start, double End)[]? words = null,
        string[]? offeredIds = null,
        double? durationSec = null,
        int fpsNum = 30,
        int fpsDen = 1)
    {
        silences ??= Array.Empty<(string, double, double)>();
        segments ??= Array.Empty<(string, double, double)>();
        words ??= Array.Empty<(string, double, double)>();

        double duration = durationSec ?? (shots.Length > 0 ? shots.Max(s => s.End) : 60.0);

        return new VideoAnalysisArtifact(
            Version: 1,
            Media: new VideoAnalysisMedia(duration, fpsNum, fpsDen, 1920, 1080),
            Shots: shots.Select(s => new VideoAnalysisShot(s.Id, s.Start, s.End)).ToList(),
            SilenceSpans: silences.Select(s => new VideoAnalysisSilenceSpan(s.Id, s.Start, s.End, null)).ToList(),
            Segments: segments.Select(s => new VideoAnalysisSegment(s.Id, null, s.Start, s.End, "text")).ToList(),
            Words: words.Select(s => new VideoAnalysisWord(s.Id, s.Start, s.End, "word")).ToList(),
            OfferedIds: offeredIds?.ToList() ?? shots.Select(s => s.Id).Concat(silences.Select(s => s.Id)).Concat(segments.Select(s => s.Id)).ToList(),
            Provenance: new VideoAnalysisProvenance(VideoTranscriptionMode.Off, false, false));
    }

    private static JsonSerializerOptions ConfigOptions() => new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private static JsonSerializerOptions ArtifactOptions() => new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
}
