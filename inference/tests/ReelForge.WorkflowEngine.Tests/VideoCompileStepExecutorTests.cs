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
    public async Task Explicit_null_keep_fails_with_EMPTY_KEEP_not_an_uncaught_exception()
    {
        // An explicit `"keep": null` (as opposed to an omitted/absent field) overwrites the
        // `= new()` property-initializer default with a real null under System.Text.Json — this
        // must degrade to EMPTY_KEEP exactly like `"keep": []` does, never escape as
        // UNEXPECTED_ERROR via an uncaught NullReferenceException.
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            offeredIds: new[] { "s0" });

        string decisionJson = """{"keep":null,"editRationale":"nothing","suggestedTitle":"x"}""";
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
    // Half-open select/aselect filter expression (R9 cut-accuracy) — ffmpeg's between(x,min,max)
    // is inclusive on both ends, so using it for the cut selects one extra frame (the frame whose
    // PTS is exactly SnappedEnd) per kept span, compounding drift across every span in the cut.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Encoder_select_filter_uses_half_open_gte_lt_not_inclusive_between()
    {
        // 10 short, well-separated kept spans — enough that a systematic +1-frame-per-span error
        // would compound noticeably across the whole select expression, not just wobble once.
        var shots = Enumerable.Range(0, 10)
            .Select(i => ($"s{i}", 3.0 * i, 3.0 * i + 1.0)) // 1s shots, 2s gaps
            .ToArray();

        VideoAnalysisArtifact artifact = BuildArtifact(
            durationSec: 3.0 * shots.Length + 2.0,
            shots: shots,
            offeredIds: shots.Select(s => s.Item1).ToArray());

        (string, string, string)[] spans = shots.Select(s => (s.Item1, s.Item1, "keep")).ToArray();
        string decisionJson = BuildDecisionJson(spans);

        StepExecutionContext context = CreateContext(artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace);

        IReadOnlyList<string>? capturedArgs = null;
        StepExecutionResult result = await CreateExecutor(
            workspace, ffmpegArgsCaptured: args => capturedArgs ??= args).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed);
        capturedArgs.Should().NotBeNull();

        List<string> argsList = capturedArgs!.ToList();
        int filterIndex = argsList.IndexOf("-filter_complex");
        filterIndex.Should().BeGreaterThanOrEqualTo(0, "10 spans stays well under the filter-complex-script threshold");
        string filterComplex = argsList[filterIndex + 1];

        filterComplex.Should().NotContain("between(t,",
            "ffmpeg's between() is inclusive on both ends, which selects one extra frame (at exactly SnappedEnd) per span");

        int gteCount = System.Text.RegularExpressions.Regex.Matches(filterComplex, @"gte\(t,").Count;
        int ltCount = System.Text.RegularExpressions.Regex.Matches(filterComplex, @"lt\(t,").Count;
        // Each of the 10 spans appears once in the video select and once in the audio aselect.
        gteCount.Should().Be(shots.Length * 2);
        ltCount.Should().Be(shots.Length * 2);
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

    /// <summary>
    /// Regression test for a live-testing-only-discoverable bug: the source video must resolve
    /// via the referenced VideoAnalyze step's own <c>Source</c> (here <c>VideoSourceKind.ProjectFile</c>
    /// — an uploaded video with NO prior step output at all), never via a
    /// "find any prior StepOutputHistory.OutputStorageKey" heuristic. That heuristic can never
    /// succeed for ProjectFile sources since they are never represented as a step output — before
    /// the fix, every VideoCompile step in a standalone (no preceding render step) workflow
    /// against an uploaded video failed with SOURCE_UNRESOLVED.
    /// </summary>
    [Fact]
    public async Task Successful_compile_resolves_ProjectFile_source_with_no_prior_step_output_at_all()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0), ("s1", 10.0, 20.0) },
            offeredIds: new[] { "s0", "s1" });
        string decisionJson = BuildDecisionJson(("s0", "s1", "keep the whole thing"));

        Guid projectFileId = Guid.NewGuid();
        const string projectFileStorageKey = "projects/p/userFiles/uploaded.mp4";

        var workspace = new Mock<IProjectFileWorkspace>();
        workspace
            .Setup(w => w.DownloadStorageKeyToFileAsync(
                It.IsAny<Guid>(), AnalysisKey, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, string, CancellationToken>((_, _, destPath, _) =>
            {
                File.WriteAllText(destPath, JsonSerializer.Serialize(artifact, ArtifactOptions()));
                return Task.CompletedTask;
            });
        workspace
            .Setup(w => w.DownloadStorageKeyToFileAsync(
                It.IsAny<Guid>(), projectFileStorageKey, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, string, CancellationToken>((_, _, destPath, _) =>
            {
                File.WriteAllBytes(destPath, new byte[] { 0x00, 0x01, 0x02 });
                return Task.CompletedTask;
            });
        workspace
            .Setup(w => w.ListFilesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectWorkspaceFile>
            {
                new(projectFileId, ProjectId, "uploaded.mp4", null, "userFiles",
                    projectFileStorageKey, "video/mp4", 12345, DateTime.UtcNow, null)
            });

        VideoCompileStepConfig config = new(
            Version: 1,
            Decision: new ExtractInputRef(ExtractInputSource.Previous),
            AnalysisStepOrder: 1);

        var step = new WorkflowStep
        {
            Id = Guid.NewGuid(),
            StepOrder = 3,
            StepType = StepType.VideoCompile,
            VideoCompileConfigJson = JsonSerializer.Serialize(config, ConfigOptions())
        };

        VideoAnalyzeStepConfig analyzeConfig = new(
            Version: 1,
            Source: new VideoSourceRef(VideoSourceKind.ProjectFile, ProjectFileId: projectFileId));
        var analyzeStep = new WorkflowStep
        {
            Id = Guid.NewGuid(),
            StepOrder = 1,
            StepType = StepType.VideoAnalyze,
            VideoAnalyzeConfigJson = JsonSerializer.Serialize(analyzeConfig, ConfigOptions())
        };

        // Deliberately NO entry at all produces an OutputStorageKey — the old heuristic
        // ("search StepOutputHistory for any prior OutputStorageKey") would find nothing here.
        List<StepOutputHistoryEntry> history =
        [
            new StepOutputHistoryEntry(1, "Analyze", "{}", OutputStorageKey: null, ArtifactStorageKey: AnalysisKey),
            new StepOutputHistoryEntry(2, "StoryEditor", decisionJson, OutputStorageKey: null, ArtifactStorageKey: null)
        ];

        StepExecutionContext context = new()
        {
            Execution = new WorkflowExecution { Id = Guid.NewGuid(), ProjectId = ProjectId },
            Step = step,
            AllSteps = [analyzeStep, step],
            AccumulatedOutput = decisionJson,
            StepOutputHistory = history,
            CurrentStepIndex = 2,
            IterationCount = 0,
            CorrelationId = "test",
            CancellationToken = CancellationToken.None
        };

        StepExecutionResult result = await CreateExecutor(workspace).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed);
        result.OutputStorageKey.Should().NotBeNullOrWhiteSpace();
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
        Mock<IProjectFileWorkspace> workspace, Action<JsonElement>? edlCaptured = null, bool drawtextAvailable = true,
        Action<IReadOnlyList<string>>? ffmpegArgsCaptured = null)
    {
        // The drawtext-availability probe is a process-lifetime static cache in the executor
        // (see ResolveGraphicsAsync/IsDrawtextAvailableAsync) — reset it per test case so each
        // test's own mocked IVideoToolRunner is actually consulted.
        VideoCompileStepExecutor.ResetDrawtextAvailabilityCacheForTests();

        var toolRunner = new Mock<IVideoToolRunner>();
        toolRunner
            .Setup(t => t.RunFfmpegAsync(
                It.Is<IReadOnlyList<string>>(a => a.Contains("-filters")), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VideoToolResult(0, drawtextAvailable ? "... drawtext ..." : "... (no drawtext) ...", string.Empty, false));
        toolRunner
            .Setup(t => t.RunFfmpegAsync(
                It.Is<IReadOnlyList<string>>(a => !a.Contains("-filters")), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<string>, TimeSpan, CancellationToken>((args, _, _) => ffmpegArgsCaptured?.Invoke(args))
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

        // VideoCompileStepExecutor resolves the source video by reading the referenced
        // VideoAnalyze step's OWN VideoAnalyzeConfigJson.Source (never a StepOutputHistory
        // heuristic — see ResolveSourceStorageKeyAsync) — so a real analyze step, matching
        // AnalysisStepOrder, must be present in AllSteps. Source=PreviousStepOutput here mirrors
        // the shipped video-derush-edit template chaining off a render step at StepOrder 0.
        VideoAnalyzeStepConfig analyzeConfig = new(
            Version: 1,
            Source: new VideoSourceRef(VideoSourceKind.PreviousStepOutput));
        var analyzeStep = new WorkflowStep
        {
            Id = Guid.NewGuid(),
            StepOrder = 1,
            StepType = StepType.VideoAnalyze,
            VideoAnalyzeConfigJson = JsonSerializer.Serialize(analyzeConfig, ConfigOptions())
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
            AllSteps = [analyzeStep, step],
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
        int fpsDen = 1,
        IReadOnlyList<VideoAnalysisPlacement>? placements = null,
        string[]? offeredPlacementIds = null)
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
            Provenance: new VideoAnalysisProvenance(VideoTranscriptionMode.Off, false, false),
            Placements: placements,
            OfferedPlacementIds: offeredPlacementIds?.ToList() ?? (placements is not null ? placements.Select(p => p.Id).ToList() : null));
    }

    private static JsonSerializerOptions ConfigOptions() => new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private static JsonSerializerOptions ArtifactOptions() => new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    // =======================================================================
    // Phase 3: motion graphics
    // =======================================================================

    [Fact]
    public async Task EnableGraphics_false_produces_no_graphics_key_at_all_byte_identical_to_pre_phase3()
    {
        // The load-bearing backward-compatibility guarantee of Phase 3: even with a GraphicsPlan
        // configured, EnableGraphics=false (the default) must leave the EDL/output shape
        // completely untouched — no "graphics" key anywhere.
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0), ("s1", 10.0, 20.0) },
            offeredIds: new[] { "s0", "s1" });

        string decisionJson = BuildDecisionJson(("s0", "s1", "keep"));
        StepExecutionContext context = CreateContext(
            artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace,
            configOverride: cfg => cfg with
            {
                EnableGraphics = false,
                GraphicsPlan = new ExtractInputRef(ExtractInputSource.Previous)
            });

        JsonElement edl = default;
        StepExecutionResult result = await CreateExecutor(workspace, edlCaptured: e => edl = e).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed);
        edl.TryGetProperty("graphics", out _).Should().BeFalse("EDL must have no graphics key when EnableGraphics=false");

        using JsonDocument outputDoc = JsonDocument.Parse(result.Output);
        outputDoc.RootElement.TryGetProperty("graphics", out _).Should().BeFalse("output summary must have no graphics key when EnableGraphics=false");
    }

    [Fact]
    public async Task EnableGraphics_true_with_StreamCopy_fails_GRAPHICS_REQUIRE_REENCODE()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            offeredIds: new[] { "s0" });

        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        StepExecutionContext context = CreateContext(
            artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace,
            configOverride: cfg => cfg with
            {
                EnableGraphics = true,
                Mode = VideoCompileMode.StreamCopy,
                AllowKeyframeSnapping = true
            });

        StepExecutionResult result = await CreateExecutor(workspace).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        ErrorCode(result).Should().Be("GRAPHICS_REQUIRE_REENCODE");
    }

    [Fact]
    public async Task Keep_span_naming_a_placement_id_fails_UNKNOWN_ID_since_placements_are_a_separate_namespace()
    {
        var placement = new VideoAnalysisPlacement(
            "p0", "s0", "LowerThird", new VideoAnalysisRect(0.1, 0.8, 0.6, 0.15),
            "ShotMiddle", 4.0, 6.0, 0.8, "Light");

        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            offeredIds: new[] { "s0" },
            placements: new[] { placement });

        // A Keep span naming "p0" — a placement id, not a cut-anchor id — must fail UNKNOWN_ID
        // exactly like any other id BuildIdTimeIndex does not contain.
        string decisionJson = BuildDecisionJson(("p0", "p0", "wrong namespace"));
        StepExecutionContext context = CreateContext(artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace);

        StepExecutionResult result = await CreateExecutor(workspace).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        ErrorCode(result).Should().Be("UNKNOWN_ID");
    }

    [Fact]
    public async Task Missing_GraphicsPlan_content_produces_graphics_free_but_otherwise_successful_compile()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            offeredIds: new[] { "s0" });

        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));

        // GraphicsPlan points at a step order with no history entry at all -> unresolvable.
        StepExecutionContext context = CreateContext(
            artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace,
            configOverride: cfg => cfg with
            {
                EnableGraphics = true,
                GraphicsPlan = new ExtractInputRef(ExtractInputSource.Step, StepOrder: 99)
            });

        JsonElement edl = default;
        StepExecutionResult result = await CreateExecutor(workspace, edlCaptured: e => edl = e).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, "a missing graphics plan must never fail the compile — the cut is the primary deliverable");
        JsonElement graphics = edl.GetProperty("graphics");
        graphics.GetProperty("enabled").GetBoolean().Should().BeTrue();
        graphics.GetProperty("applied").GetBoolean().Should().BeFalse();
        graphics.TryGetProperty("reason", out JsonElement reason).Should().BeTrue();
        reason.GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Overlay_naming_unknown_placement_id_is_dropped_not_a_step_failure()
    {
        var placement = new VideoAnalysisPlacement(
            "p0", "s0", "LowerThird", new VideoAnalysisRect(0.1, 0.8, 0.6, 0.15),
            "ShotMiddle", 4.0, 6.0, 0.8, "Light");

        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            offeredIds: new[] { "s0" },
            placements: new[] { placement });

        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        string graphicsPlanJson = JsonSerializer.Serialize(new
        {
            overlays = new[]
            {
                new { placementId = "p99", kind = "Tag", text = "Nope", subtext = "", duration = "Short", emphasis = "Normal", reason = "bad id" }
            },
            planRationale = "test"
        });

        StepExecutionContext context = CreateGraphicsContext(
            artifact, decisionJson, graphicsPlanJson, out Mock<IProjectFileWorkspace> workspace);

        JsonElement edl = default;
        StepExecutionResult result = await CreateExecutor(workspace, edlCaptured: e => edl = e).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed);
        JsonElement graphics = edl.GetProperty("graphics");
        graphics.GetProperty("applied").GetBoolean().Should().BeFalse();
        graphics.GetProperty("appliedOverlayCount").GetInt32().Should().Be(0);
        JsonElement dropped = graphics.GetProperty("droppedOverlays");
        dropped.GetArrayLength().Should().Be(1);
        dropped[0].GetProperty("placementId").GetString().Should().Be("p99");
        dropped[0].GetProperty("reason").GetString().Should().Be("unknown_placement_id");
    }

    [Fact]
    public async Task Valid_overlay_is_applied_and_recorded_in_the_graphics_block()
    {
        var placement = new VideoAnalysisPlacement(
            "p0", "s0", "LowerThird", new VideoAnalysisRect(0.1, 0.8, 0.6, 0.15),
            "ShotMiddle", 4.0, 6.0, 0.8, "Light");

        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            offeredIds: new[] { "s0" },
            placements: new[] { placement });

        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        string graphicsPlanJson = JsonSerializer.Serialize(new
        {
            overlays = new[]
            {
                new { placementId = "p0", kind = "LowerThird", text = "Jane Doe", subtext = "Engineer", duration = "Short", emphasis = "Normal", reason = "intro" }
            },
            planRationale = "test"
        });

        StepExecutionContext context = CreateGraphicsContext(
            artifact, decisionJson, graphicsPlanJson, out Mock<IProjectFileWorkspace> workspace);

        JsonElement edl = default;
        StepExecutionResult result = await CreateExecutor(workspace, edlCaptured: e => edl = e).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed);
        JsonElement graphics = edl.GetProperty("graphics");
        graphics.GetProperty("applied").GetBoolean().Should().BeTrue();
        graphics.GetProperty("appliedOverlayCount").GetInt32().Should().Be(1);
        graphics.GetProperty("droppedOverlays").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Explicit_null_overlays_in_graphics_plan_degrades_to_no_graphics_not_UNEXPECTED_ERROR()
    {
        // An explicit `"overlays": null` overwrites MotionGraphicsPlanOutput.Overlays' `= new()`
        // default with a real null under System.Text.Json — this must degrade the same way a
        // missing/unresolvable GraphicsPlan already does (cut succeeds, no graphics applied),
        // never escape as UNEXPECTED_ERROR via an uncaught NullReferenceException.
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            offeredIds: new[] { "s0" });

        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        string graphicsPlanJson = """{"overlays":null,"planRationale":"nothing to show"}""";

        StepExecutionContext context = CreateGraphicsContext(
            artifact, decisionJson, graphicsPlanJson, out Mock<IProjectFileWorkspace> workspace);

        JsonElement edl = default;
        StepExecutionResult result = await CreateExecutor(workspace, edlCaptured: e => edl = e).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, "a null overlays list must never fail the compile — the cut is the primary deliverable");
        JsonElement graphics = edl.GetProperty("graphics");
        graphics.GetProperty("enabled").GetBoolean().Should().BeTrue();
        graphics.GetProperty("applied").GetBoolean().Should().BeFalse();
        graphics.GetProperty("appliedOverlayCount").GetInt32().Should().Be(0);
    }

    /// <summary>Builds a context wired for Phase 3 graphics: a 4th history entry (StepOrder 3, the MotionGraphicsPlanner step) plus EnableGraphics=true, GraphicsPlan pointed at it.</summary>
    private static StepExecutionContext CreateGraphicsContext(
        VideoAnalysisArtifact artifact,
        string decisionJson,
        string graphicsPlanJson,
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
            Decision: new ExtractInputRef(ExtractInputSource.Step, StepOrder: 2),
            AnalysisStepOrder: 1,
            EnableGraphics: true,
            GraphicsPlan: new ExtractInputRef(ExtractInputSource.Step, StepOrder: 3));

        if (configOverride is not null)
            config = configOverride(config);

        string configJson = JsonSerializer.Serialize(config, ConfigOptions());

        var step = new WorkflowStep
        {
            Id = Guid.NewGuid(),
            StepOrder = 4,
            StepType = StepType.VideoCompile,
            VideoCompileConfigJson = configJson
        };

        VideoAnalyzeStepConfig analyzeConfig = new(
            Version: 1,
            Source: new VideoSourceRef(VideoSourceKind.PreviousStepOutput));
        var analyzeStep = new WorkflowStep
        {
            Id = Guid.NewGuid(),
            StepOrder = 1,
            StepType = StepType.VideoAnalyze,
            VideoAnalyzeConfigJson = JsonSerializer.Serialize(analyzeConfig, ConfigOptions())
        };

        List<StepOutputHistoryEntry> history =
        [
            new StepOutputHistoryEntry(0, "Render", "{}", OutputStorageKey: SourceVideoKey, ArtifactStorageKey: null),
            new StepOutputHistoryEntry(1, "Analyze", "{}", OutputStorageKey: null, ArtifactStorageKey: AnalysisKey),
            new StepOutputHistoryEntry(2, "StoryEditor", decisionJson, OutputStorageKey: null, ArtifactStorageKey: null),
            new StepOutputHistoryEntry(3, "MotionGraphicsPlanner", graphicsPlanJson, OutputStorageKey: null, ArtifactStorageKey: null)
        ];

        return new StepExecutionContext
        {
            Execution = new WorkflowExecution { Id = Guid.NewGuid(), ProjectId = ProjectId },
            Step = step,
            AllSteps = [analyzeStep, step],
            AccumulatedOutput = graphicsPlanJson,
            StepOutputHistory = history,
            CurrentStepIndex = 3,
            IterationCount = 0,
            CorrelationId = "test",
            CancellationToken = CancellationToken.None
        };
    }

    // =======================================================================
    // Phase 3: source-timeline -> output-timeline mapping (MapSourceToOutputSec /
    // MapSourceWindowToOutput) — the single most important correctness function in Phase 3.
    // Three kept spans, at 30fps (so SnappedStart/End equal the given seconds exactly): [0,10),
    // [20,30) (a 10s cut gap in between), [40,45) (a 10s cut gap before it).
    // Output timeline: [0,10) -> [0,10) ; [20,30) -> [10,20) ; [40,45) -> [20,25).
    // =======================================================================

    private static List<VideoCompileStepExecutor.ResolvedSpan> ThreeSpanFixture() =>
    [
        new(0.0, 10.0, 0.0, 10.0, 0, 300),
        new(20.0, 30.0, 20.0, 30.0, 600, 900),
        new(40.0, 45.0, 40.0, 45.0, 1200, 1350)
    ];

    [Fact]
    public void MapSourceToOutputSec_inside_first_span_maps_directly()
    {
        VideoCompileStepExecutor.MapSourceToOutputSec(ThreeSpanFixture(), 5.0).Should().Be(5.0);
    }

    [Fact]
    public void MapSourceToOutputSec_inside_second_span_accounts_for_first_spans_duration()
    {
        // Source 25.0 is 5s into the second span; first span contributed 10s of output already.
        VideoCompileStepExecutor.MapSourceToOutputSec(ThreeSpanFixture(), 25.0).Should().Be(15.0);
    }

    [Fact]
    public void MapSourceToOutputSec_inside_a_cut_gap_returns_null()
    {
        VideoCompileStepExecutor.MapSourceToOutputSec(ThreeSpanFixture(), 15.0).Should().BeNull();
    }

    [Fact]
    public void MapSourceToOutputSec_past_the_last_span_returns_null()
    {
        VideoCompileStepExecutor.MapSourceToOutputSec(ThreeSpanFixture(), 46.0).Should().BeNull();
    }

    [Fact]
    public void MapSourceWindowToOutput_fully_inside_span2_maps_with_correct_offset()
    {
        // [22, 27) sits fully inside the second kept span [20,30) -> output [12, 17).
        (double Start, double End)? window = VideoCompileStepExecutor.MapSourceWindowToOutput(ThreeSpanFixture(), 22.0, 27.0);
        window.Should().NotBeNull();
        window!.Value.Start.Should().BeApproximately(12.0, 1e-9);
        window.Value.End.Should().BeApproximately(17.0, 1e-9);
    }

    [Fact]
    public void MapSourceWindowToOutput_entirely_inside_a_cut_gap_returns_null()
    {
        // [12, 18) sits entirely inside the [10,20) cut gap between span 1 and span 2.
        VideoCompileStepExecutor.MapSourceWindowToOutput(ThreeSpanFixture(), 12.0, 18.0).Should().BeNull();
    }

    [Fact]
    public void MapSourceWindowToOutput_straddling_a_cut_boundary_is_clipped_to_the_kept_portion()
    {
        // [8, 25) straddles the cut gap [10,20): overlaps span 1 first ([8,10) kept portion),
        // and this implementation clips to the FIRST kept portion it overlaps — output [8,10).
        (double Start, double End)? window = VideoCompileStepExecutor.MapSourceWindowToOutput(ThreeSpanFixture(), 8.0, 25.0);
        window.Should().NotBeNull();
        window!.Value.Start.Should().BeApproximately(8.0, 1e-9);
        window.Value.End.Should().BeApproximately(10.0, 1e-9);
    }

    [Fact]
    public void MapSourceWindowToOutput_degenerate_window_returns_null()
    {
        VideoCompileStepExecutor.MapSourceWindowToOutput(ThreeSpanFixture(), 5.0, 5.0).Should().BeNull();
    }

    [Fact]
    public void MapSourceToOutputSec_accumulates_exact_frame_counts_across_many_spans_no_per_span_drift()
    {
        // 12 kept spans of varying frame-lengths at 30fps, each independently computed from an
        // exact frame count (never a naive seconds multiplication) — enough spans that a
        // systematic +1-frame-per-span accumulation error would produce a clearly wrong, linearly
        // growing offset by the last span, not just sub-frame rounding noise.
        const int fps = 30;
        int[] frameLengths = { 7, 3, 11, 5, 2, 9, 4, 6, 8, 3, 10, 5 };
        var spans = new List<VideoCompileStepExecutor.ResolvedSpan>();
        var expectedOutputStartFrame = new long[frameLengths.Length];

        long cursor = 0;
        long accumulatedFrames = 0;
        for (int i = 0; i < frameLengths.Length; i++)
        {
            long startFrame = cursor + 20; // a 20-frame cut gap before every kept span
            long endFrame = startFrame + frameLengths[i];
            double snappedStart = VideoCompileStepExecutor.FrameToSec(startFrame, fps, 1);
            double snappedEnd = VideoCompileStepExecutor.FrameToSec(endFrame, fps, 1);
            spans.Add(new VideoCompileStepExecutor.ResolvedSpan(snappedStart, snappedEnd, snappedStart, snappedEnd, startFrame, endFrame));

            expectedOutputStartFrame[i] = accumulatedFrames;
            accumulatedFrames += frameLengths[i];
            cursor = endFrame;
        }

        for (int i = 0; i < spans.Count; i++)
        {
            double expectedOutputStartSec = expectedOutputStartFrame[i] / (double)fps;
            double? actual = VideoCompileStepExecutor.MapSourceToOutputSec(spans, spans[i].SnappedStart);
            actual.Should().NotBeNull();
            actual!.Value.Should().BeApproximately(expectedOutputStartSec, 1e-9,
                $"span {i}'s output-timeline start must reflect the exact frame count of every prior span, not an off-by-one-frame-per-span drift");
        }
    }
}
