using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
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

    // Fixed WorkflowExecution.Id used by CreateGraphicsContext (Phase 3 graphics tests only) so a
    // test can construct a RenderedAssetStorageKey that matches the exact execution-scoped
    // "projects/{ProjectId}/outputFiles/{GraphicsExecutionId}/..." prefix
    // VideoCompileStepExecutor validates a rendered-asset overlay against.
    private static readonly Guid GraphicsExecutionId = Guid.NewGuid();
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
    // Root-cause fix for the mid-sentence-cut defect: a transcript segment's raw ASR EndSec is
    // extended forward, bounded, toward the nearest FOLLOWING detected silence gap before being
    // trusted as a Keep span's resolved end time — see ExtendSegmentEndTowardNextSilence's doc
    // comment on VideoCompileStepExecutor.
    // ---------------------------------------------------------------------

    [Fact]
    public void ExtendSegmentEndTowardNextSilence_snaps_to_a_nearby_following_silence_gap()
    {
        var silences = new[] { new VideoAnalysisSilenceSpan("g0", 10.4, 12.0, null) };

        VideoCompileStepExecutor.ExtendSegmentEndTowardNextSilence(10.1, silences).Should().Be(10.4);
    }

    [Fact]
    public void ExtendSegmentEndTowardNextSilence_leaves_the_raw_end_unchanged_when_no_gap_is_close_enough()
    {
        var silences = new[] { new VideoAnalysisSilenceSpan("g0", 30.0, 31.0, null) };

        VideoCompileStepExecutor.ExtendSegmentEndTowardNextSilence(10.1, silences).Should().Be(10.1);
    }

    [Fact]
    public void ExtendSegmentEndTowardNextSilence_never_extends_backward_to_a_gap_that_already_passed()
    {
        var silences = new[] { new VideoAnalysisSilenceSpan("g0", 5.0, 6.0, null) };

        VideoCompileStepExecutor.ExtendSegmentEndTowardNextSilence(10.1, silences).Should().Be(10.1);
    }

    [Fact]
    public void ExtendSegmentEndTowardNextSilence_picks_the_nearest_of_several_following_gaps()
    {
        var silences = new[]
        {
            new VideoAnalysisSilenceSpan("g0", 10.9, 11.5, null),
            new VideoAnalysisSilenceSpan("g1", 10.3, 10.6, null)
        };

        VideoCompileStepExecutor.ExtendSegmentEndTowardNextSilence(10.1, silences).Should().Be(10.3);
    }

    [Fact]
    public void ExtendSegmentEndTowardNextSilence_respects_the_max_extension_cap_exactly_at_the_boundary()
    {
        // Exactly at MaxSegmentEndExtensionSec (1.0s) -> still extends (<=, not <).
        var atCap = new[] { new VideoAnalysisSilenceSpan("g0", 11.1, 12.0, null) };
        VideoCompileStepExecutor.ExtendSegmentEndTowardNextSilence(10.1, atCap).Should().Be(11.1);

        // Just past the cap -> left unchanged.
        var pastCap = new[] { new VideoAnalysisSilenceSpan("g0", 11.100001, 12.0, null) };
        VideoCompileStepExecutor.ExtendSegmentEndTowardNextSilence(10.1, pastCap).Should().Be(10.1);
    }

    [Theory]
    [InlineData("Hello world.", true)]
    [InlineData("Hello world!", true)]
    [InlineData("Hello world?", true)]
    [InlineData("Hello world", false)]
    [InlineData("He said \"stop.\"", true)]
    [InlineData("Trailing space at the end. ", true)]
    [InlineData("   ", false)]
    public void EndsWithSentenceTerminalPunctuation_matches_expected(string text, bool expected)
    {
        VideoCompileStepExecutor.EndsWithSentenceTerminalPunctuation(text).Should().Be(expected);
    }

    [Fact]
    public async Task SentenceCheck_reports_applicable_false_when_last_kept_id_is_not_a_transcript_segment()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            offeredIds: new[] { "s0" });

        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        StepExecutionContext context = CreateContext(artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace);

        StepExecutionResult result = await CreateExecutor(workspace).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        doc.RootElement.GetProperty("sentenceCheck").GetProperty("applicable").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task SentenceCheck_reports_true_when_last_kept_segment_ends_with_terminal_punctuation()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            segments: new[] { ("t0", 1.0, 4.0) },
            segmentTexts: new[] { "This is a complete sentence." },
            offeredIds: new[] { "s0", "t0" });

        string decisionJson = BuildDecisionJson(("s0", "t0", "keep"));
        StepExecutionContext context = CreateContext(artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace);

        StepExecutionResult result = await CreateExecutor(workspace).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        JsonElement sentenceCheck = doc.RootElement.GetProperty("sentenceCheck");
        sentenceCheck.GetProperty("applicable").GetBoolean().Should().BeTrue();
        sentenceCheck.GetProperty("lastKeptId").GetString().Should().Be("t0");
        sentenceCheck.GetProperty("endsAtSentenceBoundary").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task SentenceCheck_flags_a_mid_sentence_cut_and_notices_the_next_segment_continues()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            segments: new[] { ("t0", 1.0, 4.0), ("t1", 4.2, 6.0) },
            segmentTexts: new[] { "This sentence keeps going", "and finishes here." },
            // t1 deliberately NOT offered — the story editor never saw it, so this is purely
            // deterministic evidence for the review agent, never a signal the compile step would
            // trust to extend the actual cut.
            offeredIds: new[] { "s0", "t0" });

        string decisionJson = BuildDecisionJson(("s0", "t0", "keep"));
        StepExecutionContext context = CreateContext(artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace);

        StepExecutionResult result = await CreateExecutor(workspace).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        JsonElement sentenceCheck = doc.RootElement.GetProperty("sentenceCheck");
        sentenceCheck.GetProperty("applicable").GetBoolean().Should().BeTrue();
        sentenceCheck.GetProperty("endsAtSentenceBoundary").GetBoolean().Should().BeFalse();
        sentenceCheck.GetProperty("lastSegmentText").GetString().Should().Be("This sentence keeps going");
        sentenceCheck.GetProperty("nextSegmentContinues").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData("But one of the big things is that you know, I don't really have a fear", true)]
    [InlineData("So anyway, that's the story.", true)]
    [InlineData("However, the cave is deeper than it looks.", true)]
    [InlineData("Well, I wouldn't say that.", true)]
    [InlineData("\"But that's the whole point,\" he said.", true)]
    [InlineData("and so beautiful at the same time.", false)]      // lowercase -> mid-clause, not a new thought
    [InlineData("but I kept going anyway.", false)]
    [InlineData("Butter is not a discourse marker.", false)]       // prefix match must not fire
    [InlineData("The cave goes on for miles.", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void StartsWithNewThoughtMarker_matches_expected(string text, bool expected)
    {
        VideoCompileStepExecutor.StartsWithNewThoughtMarker(text).Should().Be(expected);
    }

    [Fact]
    public async Task SentenceCheck_does_not_claim_continuation_when_the_next_segment_opens_a_new_thought()
    {
        // The real-run regression: the last kept segment is a complete thought that ASR simply left
        // without terminal punctuation, and the next segment starts 0.44s later on a NEW topic
        // introduced by "But". A pure timing-gap test called that a continuation and contributed to
        // a false "ends mid-sentence" verdict from VideoReviewAgent.
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            segments: new[] { ("t0", 1.0, 4.0), ("t1", 4.44, 6.0) },
            segmentTexts: new[]
            {
                "It's an underground world that is so strange and so beautiful at the same time",
                "But one of the big things is that you know, I don't really have a fear of a lot of different things"
            },
            offeredIds: new[] { "s0", "t0" });

        string decisionJson = BuildDecisionJson(("s0", "t0", "keep"));
        StepExecutionContext context = CreateContext(artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace);

        StepExecutionResult result = await CreateExecutor(workspace).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        JsonElement sentenceCheck = doc.RootElement.GetProperty("sentenceCheck");
        sentenceCheck.GetProperty("applicable").GetBoolean().Should().BeTrue();
        sentenceCheck.GetProperty("endsAtSentenceBoundary").GetBoolean().Should().BeFalse();
        sentenceCheck.GetProperty("nextSegmentContinues").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task SentenceCheck_marks_the_punctuation_signal_unreliable_on_a_barely_punctuated_source()
    {
        // 1 of 5 segments punctuated (ratio 0.2) — the review agent must NOT hard-cap the score
        // at 4 for "ends mid-sentence" here, because the transcript itself carries no boundary
        // information to judge that on.
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            segments: new[] { ("t0", 1.0, 2.0), ("t1", 2.0, 3.0), ("t2", 3.0, 4.0), ("t3", 4.0, 5.0), ("t4", 5.0, 6.0) },
            segmentTexts: new[]
            {
                "so the thing is that we", "and then we tried", "it worked out.", "which meant", "a lot for the team"
            },
            offeredIds: new[] { "s0", "t0", "t1", "t2", "t3" });

        string decisionJson = BuildDecisionJson(("s0", "t3", "keep"));
        StepExecutionContext context = CreateContext(artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace);

        StepExecutionResult result = await CreateExecutor(workspace).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        JsonElement sentenceCheck = doc.RootElement.GetProperty("sentenceCheck");

        sentenceCheck.GetProperty("applicable").GetBoolean().Should().BeTrue();
        sentenceCheck.GetProperty("endsAtSentenceBoundary").GetBoolean().Should().BeFalse();
        sentenceCheck.GetProperty("src").GetInt32().Should().Be(0);
        sentenceCheck.GetProperty("punctuationSampleSize").GetInt32().Should().Be(5);
        sentenceCheck.GetProperty("punctuationRatio").GetDouble().Should().BeApproximately(0.2, 1e-9);
        sentenceCheck.GetProperty("punctuationReliable").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task SentenceCheck_still_reports_continuation_for_a_genuine_split_sentence()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            segments: new[] { ("t0", 1.0, 4.0), ("t1", 4.2, 6.0) },
            segmentTexts: new[]
            {
                "It's an underground world that is so strange",
                "in ways I could never have imagined before going down there."
            },
            offeredIds: new[] { "s0", "t0" });

        string decisionJson = BuildDecisionJson(("s0", "t0", "keep"));
        StepExecutionContext context = CreateContext(artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace);

        StepExecutionResult result = await CreateExecutor(workspace).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        JsonElement sentenceCheck = doc.RootElement.GetProperty("sentenceCheck");
        sentenceCheck.GetProperty("endsAtSentenceBoundary").GetBoolean().Should().BeFalse();
        sentenceCheck.GetProperty("nextSegmentContinues").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task SentenceCheck_marks_the_punctuation_signal_reliable_on_a_well_punctuated_source()
    {
        // 3 of 4 punctuated (0.75): the mid-sentence finding IS trustworthy here, so the review
        // agent's hard cap stays in force.
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            segments: new[] { ("t0", 1.0, 2.0), ("t1", 2.0, 3.0), ("t2", 3.0, 4.0), ("t3", 4.0, 5.0) },
            segmentTexts: new[]
            {
                "We shipped it last week.", "The team was thrilled!", "and then", "everything changed?"
            },
            offeredIds: new[] { "s0", "t0", "t1", "t2" });

        string decisionJson = BuildDecisionJson(("s0", "t2", "keep"));
        StepExecutionContext context = CreateContext(artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace);

        StepExecutionResult result = await CreateExecutor(workspace).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        JsonElement sentenceCheck = doc.RootElement.GetProperty("sentenceCheck");

        sentenceCheck.GetProperty("endsAtSentenceBoundary").GetBoolean().Should()
            .BeFalse("\"and then\" is a genuine mid-sentence ending");
        sentenceCheck.GetProperty("punctuationRatio").GetDouble().Should().BeApproximately(0.75, 1e-9);
        sentenceCheck.GetProperty("punctuationReliable").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task SentenceCheck_does_not_claim_continuation_across_a_long_timing_gap()
    {
        // Marker-independent: the next segment reads like a clean continuation, but it starts a
        // full 5s later — the existing timing test alone still rules continuation out.
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 20.0) },
            segments: new[] { ("t0", 1.0, 4.0), ("t1", 9.0, 12.0) },
            segmentTexts: new[]
            {
                "It's an underground world that is so strange",
                "in ways I could never have imagined before going down there."
            },
            offeredIds: new[] { "s0", "t0" });

        string decisionJson = BuildDecisionJson(("s0", "t0", "keep"));
        StepExecutionContext context = CreateContext(artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace);

        StepExecutionResult result = await CreateExecutor(workspace).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        JsonElement sentenceCheck = doc.RootElement.GetProperty("sentenceCheck");
        sentenceCheck.GetProperty("endsAtSentenceBoundary").GetBoolean().Should().BeFalse();
        sentenceCheck.GetProperty("nextSegmentContinues").GetBoolean().Should().BeFalse();
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

    [Fact]
    public async Task Source_with_no_audio_stream_produces_a_video_only_filtergraph_and_skips_audio_map()
    {
        // Real B-roll/stock footage routinely ships with no audio stream at all. Before this fix,
        // the compile unconditionally built "[0:a]aselect=...[aout]" and unconditionally
        // "-map [aout]"/"-c:a", which made ffmpeg fail outright with "Stream specifier ':a' ...
        // matches no streams" on exactly this kind of source.
        VideoAnalysisArtifact artifact = BuildArtifact(shots: new[] { ("s0", 0.0, 10.0) }, offeredIds: new[] { "s0" });
        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        StepExecutionContext context = CreateContext(artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace);

        IReadOnlyList<string>? capturedArgs = null;
        JsonElement edl = default;
        StepExecutionResult result = await CreateExecutor(
            workspace, ffmpegArgsCaptured: args => capturedArgs ??= args, edlCaptured: e => edl = e,
            configureMediaProbe: mock => mock
                .Setup(p => p.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MediaProbeResult(10, 30, 1, 1920, 1080, "h264", null, null)))
            .ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        capturedArgs.Should().NotBeNull();

        string filterComplex = ExtractFilterComplexValue(capturedArgs!);
        filterComplex.Should().NotContain("[0:a]", "the source has no audio stream, so \"[0:a]\" would fail ffmpeg outright");
        filterComplex.Should().NotContain("aselect");
        filterComplex.Should().NotContain("[aout]", "there is no audio output at all, not even an empty one");

        List<string> argsList = capturedArgs!.ToList();
        argsList.Should().NotContain("[aout]");
        argsList.Should().NotContain("-c:a");

        // Bug group C.1: every OTHER degrade path in this feature (graphics.reason, music.dropped,
        // meta.transcription.degraded) records itself in the EDL — a dropped audio stream must too,
        // rather than leaving a silent-video deliverable an unreported silent surprise.
        JsonElement audio = edl.GetProperty("audio");
        audio.GetProperty("applied").GetBoolean().Should().BeFalse();
        audio.GetProperty("reason").GetString().Should().Be("no_audio_stream_in_source");

        using JsonDocument outputDoc = JsonDocument.Parse(result.Output);
        JsonElement outputAudio = outputDoc.RootElement.GetProperty("audio");
        outputAudio.GetProperty("applied").GetBoolean().Should().BeFalse();
        outputAudio.GetProperty("reason").GetString().Should().Be("no_audio_stream_in_source");
    }

    [Fact]
    public async Task Source_with_audio_records_audio_applied_true_with_no_reason_in_the_edl()
    {
        // The positive counterpart of the test above: a normal source with a real audio stream
        // must report audio.applied=true (and no degrade reason) in both the EDL and outputSummary.
        VideoAnalysisArtifact artifact = BuildArtifact(shots: new[] { ("s0", 0.0, 10.0) }, offeredIds: new[] { "s0" });
        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        StepExecutionContext context = CreateContext(artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace);

        JsonElement edl = default;
        StepExecutionResult result = await CreateExecutor(workspace, edlCaptured: e => edl = e).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);

        JsonElement audio = edl.GetProperty("audio");
        audio.GetProperty("applied").GetBoolean().Should().BeTrue();

        using JsonDocument outputDoc = JsonDocument.Parse(result.Output);
        outputDoc.RootElement.GetProperty("audio").GetProperty("applied").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task No_overlay_compile_with_many_segments_stays_on_the_inline_filter_complex_path()
    {
        // Item F: EnableGraphics=false (default) is documented as byte-identical to the
        // pre-Phase-3 compile path. The filterComplex.Length > 4000 clause added for Phase 3
        // overlays must NOT apply when there are no overlays — otherwise a plain cut-only compile
        // with ~50 segments (well under the 64-segment/count-only threshold) already produces a
        // >4000-char filter string from the between(t,...)-equivalent terms alone and silently
        // switches to -filter_complex_script, breaking the byte-identical claim.
        const int segmentCount = 50;
        var shots = Enumerable.Range(0, segmentCount)
            .Select(i => ($"s{i}", 3.0 * i, 3.0 * i + 1.0)) // 1s shots, 2s gaps — well under MaxSegments=200
            .ToArray();

        // NTSC-style non-integer fps (30000/1001): frame-quantized SnappedStart/SnappedEnd then
        // have long, non-terminating decimal representations (the same reason the audit's own
        // estimate is "~45 chars" per between(t,...)-equivalent term, not a short round number) —
        // a clean fps like 30/1 would frame-quantize these shot boundaries back to short round
        // decimals and this test wouldn't actually exercise the >4000-char scenario Item F fixes.
        VideoAnalysisArtifact artifact = BuildArtifact(
            durationSec: 3.0 * shots.Length + 2.0,
            shots: shots,
            offeredIds: shots.Select(s => s.Item1).ToArray(),
            fpsNum: 30000, fpsDen: 1001);

        (string, string, string)[] spans = shots.Select(s => (s.Item1, s.Item1, "keep")).ToArray();
        string decisionJson = BuildDecisionJson(spans);

        StepExecutionContext context = CreateContext(
            artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace,
            configOverride: cfg => cfg with { PrePaddingMs = 0, PostPaddingMs = 0, MinSegmentMs = 0 });

        IReadOnlyList<string>? capturedArgs = null;
        StepExecutionResult result = await CreateExecutor(
            workspace, ffmpegArgsCaptured: args => capturedArgs ??= args).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        capturedArgs.Should().NotBeNull();

        List<string> argsList = capturedArgs!.ToList();
        int filterIndex = argsList.IndexOf("-filter_complex");
        filterIndex.Should().BeGreaterThanOrEqualTo(0,
            "a no-overlay compile must stay on the count-only threshold (50 segments < 64), " +
            "even though its filter string exceeds 4000 characters");

        string filterComplex = argsList[filterIndex + 1];
        filterComplex.Length.Should().BeGreaterThan(4000,
            "the test is only meaningful if this compile would actually have tripped the length clause");

        argsList.Should().NotContain("-filter_complex_script");
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

    /// <summary>
    /// Bug group A.2 regression: after a ReviewLoop loop-back re-executes the story editor step,
    /// StepOutputHistory can hold two entries for the SAME StepOrder — the stale iteration-1
    /// decision and the fresh iteration-2 one (belt-and-braces: WorkflowExecutorService now prunes
    /// this on loop-back too, but this test constructs the pre-prune shape directly to prove
    /// ResolveDecisionJson's own resolution is independently correct). An explicit
    /// <c>Decision.From=Step</c>/StepOrder reference must resolve to the LATEST matching entry
    /// (LastOrDefault), never the first one appended (FirstOrDefault) — otherwise every loop
    /// iteration after the first would silently re-compile the stale first-iteration decision.
    /// </summary>
    [Fact]
    public async Task Explicit_step_order_decision_reference_resolves_to_the_latest_entry_when_history_has_a_duplicate_StepOrder()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0), ("s1", 10.0, 20.0) },
            offeredIds: new[] { "s0", "s1" });

        string staleDecisionJson = BuildDecisionJson(("s0", "s0", "stale iteration 1 decision"));
        string freshDecisionJson = BuildDecisionJson(("s0", "s1", "fresh iteration 2 decision"));

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
                It.IsAny<Guid>(), SourceVideoKey, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, string, CancellationToken>((_, _, destPath, _) =>
            {
                File.WriteAllBytes(destPath, new byte[] { 0x00, 0x01, 0x02 });
                return Task.CompletedTask;
            });

        VideoCompileStepConfig config = new(
            Version: 1,
            Decision: new ExtractInputRef(ExtractInputSource.Step, StepOrder: 2),
            AnalysisStepOrder: 1);

        var step = new WorkflowStep
        {
            Id = Guid.NewGuid(),
            StepOrder = 4,
            StepType = StepType.VideoCompile,
            VideoCompileConfigJson = JsonSerializer.Serialize(config, ConfigOptions())
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

        // Simulates the exact post-loop-back shape WorkflowExecutorService's StepOutputHistory
        // would hold WITHOUT its own A.3 prune fix: the stale iteration-1 StoryEditor output at
        // StepOrder 2, appended BEFORE the fresh iteration-2 one.
        List<StepOutputHistoryEntry> history =
        [
            new StepOutputHistoryEntry(0, "Render", "{}", OutputStorageKey: SourceVideoKey, ArtifactStorageKey: null),
            new StepOutputHistoryEntry(1, "Analyze", "{}", OutputStorageKey: null, ArtifactStorageKey: AnalysisKey),
            new StepOutputHistoryEntry(2, "StoryEditor", staleDecisionJson, OutputStorageKey: null, ArtifactStorageKey: null),
            new StepOutputHistoryEntry(2, "StoryEditor", freshDecisionJson, OutputStorageKey: null, ArtifactStorageKey: null)
        ];

        StepExecutionContext context = new()
        {
            Execution = new WorkflowExecution { Id = Guid.NewGuid(), ProjectId = ProjectId },
            Step = step,
            AllSteps = [analyzeStep, step],
            AccumulatedOutput = freshDecisionJson,
            StepOutputHistory = history,
            CurrentStepIndex = 3,
            IterationCount = 1,
            CorrelationId = "test",
            CancellationToken = CancellationToken.None
        };

        JsonElement edl = default;
        StepExecutionResult result = await CreateExecutor(workspace, edlCaptured: e => edl = e).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        // Stale decision keeps only s0 (0-10s); fresh decision keeps s0 through s1 (0-20s). Picking
        // the stale entry would compile a 10s output instead of the fresh 20s one.
        edl.GetProperty("totalOutputSeconds").GetDouble().Should().BeApproximately(20.0, 0.01,
            "the FRESH (latest) decision must be resolved, not the stale first-appended one");
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
        Action<IReadOnlyList<string>>? ffmpegArgsCaptured = null,
        Action<Mock<IMediaProbe>>? configureMediaProbe = null,
        bool amixNormalizeAvailable = true,
        bool insertFiltersAvailable = true,
        bool gradeFiltersAvailable = true)
    {
        // The drawtext-availability probe is a process-lifetime static cache in the executor
        // (see ResolveGraphicsAsync/IsDrawtextAvailableAsync) — reset it per test case so each
        // test's own mocked IVideoToolRunner is actually consulted. Same for the amix
        // normalize-option probe (background music — see ResolveMusicAsync/IsAmixNormalizeAvailableAsync)
        // and the perspective/alphamerge probe (tracked screen inserts — see
        // ResolveInsertsAsync/IsPerspectiveAvailableAsync).
        VideoCompileStepExecutor.ResetDrawtextAvailabilityCacheForTests();
        VideoCompileStepExecutor.ResetAmixNormalizeCacheForTests();
        VideoCompileStepExecutor.ResetPerspectiveAvailabilityCacheForTests();
        VideoCompileStepExecutor.ResetColorGradeFiltersCacheForTests();

        string filtersStdOut =
            (drawtextAvailable ? "... drawtext ..." : "... (nothing here) ...") +
            (insertFiltersAvailable ? " perspective alphamerge" : "") +
            (gradeFiltersAvailable ? " colorbalance colorlevels hue  eq  " : "");
        var toolRunner = new Mock<IVideoToolRunner>();
        toolRunner
            .Setup(t => t.RunFfmpegAsync(
                It.Is<IReadOnlyList<string>>(a => a.Contains("-filters")), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VideoToolResult(0, filtersStdOut, string.Empty, false));
        toolRunner
            .Setup(t => t.RunFfmpegAsync(
                It.Is<IReadOnlyList<string>>(a => !a.Contains("-filters")), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<string>, TimeSpan, CancellationToken>((args, _, _) => ffmpegArgsCaptured?.Invoke(args))
            .ReturnsAsync(new VideoToolResult(0, string.Empty, string.Empty, false));
        // The reencode path's main encode call goes through the 4-arg overload (real ffmpeg
        // "-progress pipe:1" percentage — see BuildFfmpegProgressLineHandler) whenever a
        // StepExecutionContext + known output duration were supplied, which the executor always
        // does — so this overload needs its own setup, mirroring the 3-arg one above exactly,
        // or every reencode test would hit an unconfigured mock member and NRE on await.
        toolRunner
            .Setup(t => t.RunFfmpegAsync(
                It.Is<IReadOnlyList<string>>(a => !a.Contains("-filters")), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>(),
                It.IsAny<Action<string>>()))
            .Callback<IReadOnlyList<string>, TimeSpan, CancellationToken, Action<string>>((args, _, _, _) => ffmpegArgsCaptured?.Invoke(args))
            .ReturnsAsync(new VideoToolResult(0, string.Empty, string.Empty, false));
        // Applied AFTER the two catch-all "not -filters" setups above — Moq resolves overlapping
        // setups to the most recently configured one, same precedence discipline the
        // configureMediaProbe comment below documents — so the amix probe (which also doesn't
        // contain "-filters") gets its own realistic response instead of the catch-all's empty
        // stdout (which would make IsAmixNormalizeAvailableAsync always resolve to unavailable).
        toolRunner
            .Setup(t => t.RunFfmpegAsync(
                It.Is<IReadOnlyList<string>>(a => a.Contains("filter=amix")), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            // The "unavailable" fake stdout must NEVER contain the literal substring "normalize" —
            // IsAmixNormalizeAvailableAsync does a plain Contains("normalize") check, so a string
            // like "(no normalize)" would still (wrongly) report available, since "normalize" is
            // itself a substring of "no normalize".
            .ReturnsAsync(new VideoToolResult(0, amixNormalizeAvailable ? "... normalize ..." : "... (basic amix, no options listed) ...", string.Empty, false));

        var mediaProbe = new Mock<IMediaProbe>();
        mediaProbe
            .Setup(p => p.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaProbeResult(10, 30, 1, 1920, 1080, "h264", "aac", 48000));
        // A rendered-asset overlay (path contains "gfx-asset", same discriminator the corrupt-file
        // test below overrides) defaults to a real alpha-carrying pix_fmt — the well-behaved case,
        // since most tests exercising an asset overlay want it to actually apply, not be dropped
        // for AlphaPixelFormats.HasAlpha failing on the catch-all's opaque "h264" default above.
        // Applied AFTER the catch-all so it takes precedence for exactly that one path family.
        mediaProbe
            .Setup(p => p.ProbeAsync(It.Is<string>(s => s.Contains("gfx-asset")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaProbeResult(1.5, 30, 1, 640, 360, "vp9", null, null, "yuva420p"));
        // Applied AFTER both setups above — Moq resolves overlapping setups to the most
        // recently configured one, so a test-supplied override (e.g. "throw for this one asset
        // path", or "no alpha for this one asset path") takes precedence over the default success
        // response without needing to know about every other path this test's executor run will probe.
        configureMediaProbe?.Invoke(mediaProbe);

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
        string[]? offeredPlacementIds = null,
        // Parallel to `segments` (same length/order) — lets a test control each segment's own
        // text for sentence-boundary-check tests without disturbing every other BuildArtifact call
        // site, which never sets this and keeps getting the fixed placeholder "text".
        string[]? segmentTexts = null)
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
            Segments: segments.Select((s, i) => new VideoAnalysisSegment(
                s.Id, null, s.Start, s.End,
                segmentTexts is not null && i < segmentTexts.Length ? segmentTexts[i] : "text")).ToList(),
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
    public async Task Keep_span_naming_a_look_group_id_fails_UNKNOWN_ID_since_look_groups_are_a_separate_namespace()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 5.0), ("s1", 5.0, 10.0) },
            offeredIds: new[] { "s0", "s1" });
        artifact = artifact with
        {
            LookGroups = [new VideoAnalysisLookGroup("k0", ["s0", "s1"], "s0", 90.0, "Warm", "Normal", "Natural")]
        };

        // A Keep span naming "k0" — a look-group id, a purely descriptive namespace never offered
        // to any agent at all — must fail UNKNOWN_ID exactly like a placement/music-track id does
        // (§6.8: BuildIdTimeIndex deliberately excludes it).
        string decisionJson = BuildDecisionJson(("k0", "k0", "wrong namespace"));
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
    public async Task Applied_overlay_records_its_exact_deterministic_frame_coverage_percentage()
    {
        // Evidence for AgentType.VideoReviewAgent's oversized-overlay check (docs/video-editing.md
        // "Review loop") — must be the exact geometry the encode itself uses, not an
        // approximation. Band (0.1, 0.8, 0.6, 0.15) at this artifact's 1920x1080 media shrinks via
        // ComputeAccentBoxPixels's defaults to w=945,h=162 (identical arithmetic to
        // DrawtextFilterBuilderTests' "Box_geometry_is_computed..." test) -> coverage =
        // 945*162/(1920*1080) ≈ 7.4%, well under the ~20-25% VideoReviewAgent's prompt treats as
        // oversized — proving the size fix actually shows up in reviewable evidence.
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
                new { placementId = "p0", kind = "LowerThird", text = "Jane Doe", subtext = "", duration = "Short", emphasis = "Normal", reason = "intro" }
            },
            planRationale = "test"
        });

        StepExecutionContext context = CreateGraphicsContext(
            artifact, decisionJson, graphicsPlanJson, out Mock<IProjectFileWorkspace> workspace);

        JsonElement edl = default;
        StepExecutionResult result = await CreateExecutor(workspace, edlCaptured: e => edl = e).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        JsonElement appliedOverlays = edl.GetProperty("graphics").GetProperty("appliedOverlays");
        appliedOverlays.GetArrayLength().Should().Be(1);
        appliedOverlays[0].GetProperty("placementId").GetString().Should().Be("p0");

        double coveragePct = appliedOverlays[0].GetProperty("coveragePct").GetDouble();
        coveragePct.Should().BeApproximately(7.4, 0.2);
        coveragePct.Should().BeLessThan(20.0, "a compact accent overlay must not be reported as covering a large fraction of the frame");
    }

    // ---------------------------------------------------------------------
    // Phase 3: overlay window resolution — intersect the FULL placement window with the kept
    // spans FIRST, then apply durationMs to whatever survived (the production "p6" defect).
    // ---------------------------------------------------------------------

    /// <summary>
    /// Fixture for the overlay-window-ordering tests. Kept spans (after the decision below) are
    /// source <c>[0,5]</c> and <c>[30,60]</c>, i.e. output <c>[0,5)</c> and <c>[5,35)</c>, with one
    /// long cut region at source <c>[5,30]</c>. Shot <c>s2</c> = <c>[20,45]</c> deliberately STARTS
    /// inside that cut region and continues well past it — exactly the shape that produced the
    /// production p6 drop.
    /// </summary>
    private static VideoAnalysisArtifact BuildOverlayWindowArtifact(params VideoAnalysisPlacement[] placements) =>
        BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0), ("s1", 10.0, 20.0), ("s2", 20.0, 45.0), ("s3", 45.0, 60.0) },
            segments: new[] { ("t0", 0.0, 5.0), ("t1", 5.0, 30.0), ("t2", 30.0, 60.0) },
            offeredIds: new[] { "s0", "s1", "s2", "s3", "t0", "t1", "t2" },
            durationSec: 60.0,
            placements: placements);

    /// <summary>Keeps t0 (source [0,5]) and t2 (source [30,60]), cutting t1 (source [5,30]).</summary>
    private static string OverlayWindowDecisionJson() =>
        BuildDecisionJson(("t0", "t0", "intro"), ("t2", "t2", "body"));

    /// <summary>Zero padding so the resolved spans are exactly the segment times, keeping the expected output windows exact.</summary>
    private static VideoCompileStepConfig NoPadding(VideoCompileStepConfig cfg) =>
        cfg with { PrePaddingMs = 0, PostPaddingMs = 0 };

    /// <summary>
    /// Pulls the distinct <c>between(t,START,END)</c> windows out of a captured ffmpeg filtergraph —
    /// the drawbox/drawtext <c>enable=</c> terms, which are the overlay's ACTUAL on-screen window on
    /// the output timeline (see <see cref="DrawtextFilterBuilder"/>).
    /// </summary>
    private static List<(double Start, double End)> ExtractOverlayEnableWindows(IReadOnlyList<string> args)
    {
        string filterComplex = ExtractFilterComplexValue(args);
        return Regex.Matches(filterComplex, @"between\(t,([0-9.]+),([0-9.]+)\)")
            .Select(m => (
                Start: double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                End: double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)))
            .Distinct()
            .ToList();
    }

    [Fact]
    public async Task Overlay_whose_full_window_overlaps_a_kept_span_survives_even_when_its_first_duration_seconds_are_cut()
    {
        // Regression test for the production "p6" drop. The overlay's candidate window is
        // [placement.StartSec, owningShot.EndSec]; durationMs only decides HOW LONG it stays up.
        // Truncating to durationMs BEFORE intersecting with the kept spans tested only
        // [20.0, 23.0] here — entirely inside the cut region [5,30] — so the overlay was dropped as
        // "cut_away" despite its full window [20,45] overlapping the kept span [30,60] by 15s.
        // (Real run: p6's window was [0, 29.83]s against a kept span of [4.8, 51.4]s — 25s of
        // genuine overlap, of which only the first 3.0s were ever tested.)
        var survives = new VideoAnalysisPlacement(
            "p6", "s2", "LowerThird", new VideoAnalysisRect(0.1, 0.8, 0.6, 0.15),
            "ShotStart", 20.0, 22.0, 0.8, "Light");
        // Its owning shot s1 = [10,20] lies WHOLLY inside the cut region [5,30], so even the full
        // window never overlaps a kept span — this one must still be dropped (no regression on the
        // legitimate-drop case).
        var legitimatelyCutAway = new VideoAnalysisPlacement(
            "p3", "s1", "LowerThird", new VideoAnalysisRect(0.1, 0.8, 0.6, 0.15),
            "ShotMiddle", 12.0, 14.0, 0.8, "Light");

        VideoAnalysisArtifact artifact = BuildOverlayWindowArtifact(survives, legitimatelyCutAway);

        string graphicsPlanJson = JsonSerializer.Serialize(new
        {
            overlays = new[]
            {
                new { placementId = "p6", kind = "LowerThird", text = "Jane Doe", subtext = "", duration = "Medium", emphasis = "Normal", reason = "intro" },
                new { placementId = "p3", kind = "Tag", text = "Cut away", subtext = "", duration = "Medium", emphasis = "Normal", reason = "in a cut region" }
            },
            planRationale = "test"
        });

        StepExecutionContext context = CreateGraphicsContext(
            artifact, OverlayWindowDecisionJson(), graphicsPlanJson,
            out Mock<IProjectFileWorkspace> workspace, configOverride: NoPadding);

        JsonElement edl = default;
        List<string>? capturedArgs = null;
        StepExecutionResult result = await CreateExecutor(
            workspace, edlCaptured: e => edl = e, ffmpegArgsCaptured: a => capturedArgs = a.ToList())
            .ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);

        JsonElement graphics = edl.GetProperty("graphics");
        graphics.GetProperty("applied").GetBoolean().Should().BeTrue();
        graphics.GetProperty("appliedOverlayCount").GetInt32().Should().Be(
            1, "the placement whose FULL window overlaps a kept span must survive");
        graphics.GetProperty("appliedOverlays")[0].GetProperty("placementId").GetString().Should().Be("p6");

        JsonElement dropped = graphics.GetProperty("droppedOverlays");
        dropped.GetArrayLength().Should().Be(1, "only the wholly-cut-away placement may be dropped");
        dropped[0].GetProperty("placementId").GetString().Should().Be("p3");
        dropped[0].GetProperty("reason").GetString().Should().Be("cut_away");

        // The surviving intersection is source [30,45] -> output [5,20); the overlay goes up where
        // that intersection BEGINS (output 5.0s) and stays up for its Medium duration (3.0s).
        capturedArgs.Should().NotBeNull();
        List<(double Start, double End)> windows = ExtractOverlayEnableWindows(capturedArgs!);
        windows.Should().HaveCount(1);
        windows[0].Start.Should().BeApproximately(5.0, 1e-6);
        windows[0].End.Should().BeApproximately(8.0, 1e-6);
    }

    [Fact]
    public async Task Overlay_already_starting_inside_a_kept_span_keeps_its_previous_window_unchanged()
    {
        // Guardrail for the fix above: for a placement whose first durationMs seconds were ALREADY
        // inside a kept span, intersect-then-truncate must produce exactly the same window
        // truncate-then-intersect did. Placement starts at source 31.0, inside the kept span
        // [30,60] -> output 6.0s, + 3.0s Medium = [6,9].
        var placement = new VideoAnalysisPlacement(
            "p1", "s2", "LowerThird", new VideoAnalysisRect(0.1, 0.8, 0.6, 0.15),
            "ShotMiddle", 31.0, 33.0, 0.8, "Light");

        VideoAnalysisArtifact artifact = BuildOverlayWindowArtifact(placement);

        string graphicsPlanJson = JsonSerializer.Serialize(new
        {
            overlays = new[]
            {
                new { placementId = "p1", kind = "LowerThird", text = "Jane Doe", subtext = "", duration = "Medium", emphasis = "Normal", reason = "intro" }
            },
            planRationale = "test"
        });

        StepExecutionContext context = CreateGraphicsContext(
            artifact, OverlayWindowDecisionJson(), graphicsPlanJson,
            out Mock<IProjectFileWorkspace> workspace, configOverride: NoPadding);

        JsonElement edl = default;
        List<string>? capturedArgs = null;
        StepExecutionResult result = await CreateExecutor(
            workspace, edlCaptured: e => edl = e, ffmpegArgsCaptured: a => capturedArgs = a.ToList())
            .ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        edl.GetProperty("graphics").GetProperty("appliedOverlayCount").GetInt32().Should().Be(1);
        edl.GetProperty("graphics").GetProperty("droppedOverlays").GetArrayLength().Should().Be(0);

        List<(double Start, double End)> windows = ExtractOverlayEnableWindows(capturedArgs!);
        windows.Should().HaveCount(1);
        windows[0].Start.Should().BeApproximately(6.0, 1e-6);
        windows[0].End.Should().BeApproximately(9.0, 1e-6);
    }

    [Fact]
    public async Task Overlay_window_is_clamped_to_the_kept_intersection_when_that_is_shorter_than_its_duration()
    {
        // The surviving intersection can itself be shorter than durationMs — the overlay must be
        // clamped to it rather than running on past the cut into unrelated footage. Shot s0 =
        // [0,10] intersected with the kept span [0,5] -> output [0,5); a Hold (6s) duration must
        // still end at 5.0s, not 6.0s.
        var placement = new VideoAnalysisPlacement(
            "p0", "s0", "LowerThird", new VideoAnalysisRect(0.1, 0.8, 0.6, 0.15),
            "ShotStart", 0.0, 2.0, 0.8, "Light");

        VideoAnalysisArtifact artifact = BuildOverlayWindowArtifact(placement);

        string graphicsPlanJson = JsonSerializer.Serialize(new
        {
            overlays = new[]
            {
                new { placementId = "p0", kind = "Title", text = "Hold me", subtext = "", duration = "Hold", emphasis = "Normal", reason = "long title" }
            },
            planRationale = "test"
        });

        StepExecutionContext context = CreateGraphicsContext(
            artifact, OverlayWindowDecisionJson(), graphicsPlanJson,
            out Mock<IProjectFileWorkspace> workspace, configOverride: NoPadding);

        List<string>? capturedArgs = null;
        StepExecutionResult result = await CreateExecutor(
            workspace, ffmpegArgsCaptured: a => capturedArgs = a.ToList()).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        List<(double Start, double End)> windows = ExtractOverlayEnableWindows(capturedArgs!);
        windows.Should().HaveCount(1);
        windows[0].Start.Should().BeApproximately(0.0, 1e-6);
        windows[0].End.Should().BeApproximately(5.0, 1e-6, "the overlay must never outlive the kept portion it was mapped into");
    }

    // ---------------------------------------------------------------------
    // Phase 3: rendered-asset overlays (MotionGraphicsOverlay.RenderedAssetStorageKey)
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Valid_rendered_asset_overlay_is_applied_via_the_overlay_filter_not_drawtext()
    {
        var placement = new VideoAnalysisPlacement(
            "p0", "s0", "LowerThird", new VideoAnalysisRect(0.1, 0.8, 0.6, 0.15),
            "ShotMiddle", 4.0, 6.0, 0.8, "Light");

        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            offeredIds: new[] { "s0" },
            placements: new[] { placement });

        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        string assetKey = $"projects/{ProjectId}/outputFiles/{GraphicsExecutionId:D}/overlay.webm";
        string graphicsPlanJson = JsonSerializer.Serialize(new
        {
            overlays = new[]
            {
                new { placementId = "p0", kind = "LowerThird", text = "", subtext = "", duration = "Short", emphasis = "Normal", renderedAssetStorageKey = assetKey, reason = "designed graphic" }
            },
            planRationale = "test"
        });

        StepExecutionContext context = CreateGraphicsContext(
            artifact, decisionJson, graphicsPlanJson, out Mock<IProjectFileWorkspace> workspace);

        workspace
            .Setup(w => w.DownloadStorageKeyToFileAsync(It.IsAny<Guid>(), assetKey, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, string, CancellationToken>((_, _, destPath, _) =>
            {
                File.WriteAllBytes(destPath, new byte[] { 0x00, 0x01, 0x02 });
                return Task.CompletedTask;
            });

        JsonElement edl = default;
        List<string>? capturedArgs = null;
        StepExecutionResult result = await CreateExecutor(
            workspace, edlCaptured: e => edl = e, ffmpegArgsCaptured: a => capturedArgs = a.ToList())
            .ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed);
        JsonElement graphics = edl.GetProperty("graphics");
        graphics.GetProperty("applied").GetBoolean().Should().BeTrue();
        graphics.GetProperty("appliedOverlayCount").GetInt32().Should().Be(1);
        graphics.GetProperty("droppedOverlays").GetArrayLength().Should().Be(0);

        capturedArgs.Should().NotBeNull();
        string filterComplex = ExtractFilterComplexValue(capturedArgs!);
        filterComplex.Should().Contain("overlay=x=", "the asset overlay must be composited via ffmpeg's overlay filter");
        filterComplex.Should().NotContain("drawtext=", "a pure asset overlay must never fall through to the drawtext path");
        capturedArgs!.Count(a => a == "-i").Should().Be(2, "the main source video AND the rendered asset must each be their own -i input");
    }

    [Fact]
    public async Task Rendered_asset_overlay_with_unrecognized_extension_falls_back_to_webm_for_the_local_scratch_path()
    {
        // RenderedAssetStorageKey is model-authored (see MotionGraphicsOverlay.RenderedAssetStorageKey's
        // doc comment); the prefix check stops it escaping this execution's own outputFiles prefix,
        // but the extension itself was previously trusted verbatim via a bare Path.GetExtension for
        // the LOCAL scratch file path. It must instead be allowlisted to what
        // RenderVideoAndUploadToStorage's own recipe actually produces (.webm/.mp4/.mov) and fall
        // back to .webm for anything else — an in-prefix, otherwise well-formed key with a
        // surprising extension here.
        var placement = new VideoAnalysisPlacement(
            "p0", "s0", "LowerThird", new VideoAnalysisRect(0.1, 0.8, 0.6, 0.15),
            "ShotMiddle", 4.0, 6.0, 0.8, "Light");

        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            offeredIds: new[] { "s0" },
            placements: new[] { placement });

        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        string assetKey = $"projects/{ProjectId}/outputFiles/{GraphicsExecutionId:D}/overlay.exe";
        string graphicsPlanJson = JsonSerializer.Serialize(new
        {
            overlays = new[]
            {
                new { placementId = "p0", kind = "LowerThird", text = "", subtext = "", duration = "Short", emphasis = "Normal", renderedAssetStorageKey = assetKey, reason = "designed graphic" }
            },
            planRationale = "test"
        });

        StepExecutionContext context = CreateGraphicsContext(
            artifact, decisionJson, graphicsPlanJson, out Mock<IProjectFileWorkspace> workspace);

        workspace
            .Setup(w => w.DownloadStorageKeyToFileAsync(It.IsAny<Guid>(), assetKey, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, string, CancellationToken>((_, _, destPath, _) =>
            {
                File.WriteAllBytes(destPath, new byte[] { 0x00, 0x01, 0x02 });
                return Task.CompletedTask;
            });

        JsonElement edl = default;
        List<string>? capturedArgs = null;
        StepExecutionResult result = await CreateExecutor(
            workspace, edlCaptured: e => edl = e, ffmpegArgsCaptured: a => capturedArgs = a.ToList())
            .ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        edl.GetProperty("graphics").GetProperty("applied").GetBoolean().Should().BeTrue(
            "an unrecognized asset extension degrades only the local file naming, never the overlay itself");

        capturedArgs.Should().NotBeNull();
        List<int> inputPositions = capturedArgs!
            .Select((a, i) => (a, i))
            .Where(t => t.a == "-i")
            .Select(t => t.i + 1)
            .ToList();
        string? localAssetInputPath = inputPositions.Select(i => capturedArgs[i]).FirstOrDefault(p => p.Contains("gfx-asset-"));

        localAssetInputPath.Should().NotBeNull();
        Path.GetExtension(localAssetInputPath).Should().Be(
            ".webm", "an extension outside the allowlist (.webm/.mp4/.mov) must fall back to .webm rather than being trusted verbatim");
    }

    [Fact]
    public async Task Rendered_asset_overlay_outside_the_execution_output_prefix_is_dropped_without_downloading_it()
    {
        var placement = new VideoAnalysisPlacement(
            "p0", "s0", "LowerThird", new VideoAnalysisRect(0.1, 0.8, 0.6, 0.15),
            "ShotMiddle", 4.0, 6.0, 0.8, "Light");

        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            offeredIds: new[] { "s0" },
            placements: new[] { placement });

        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        // Wrong execution id in the path — must never be trusted even though it is otherwise a
        // well-formed "projects/{id}/outputFiles/..." key (see RenderVideoAndUploadToStorage's own
        // key construction and VideoCompileStepExecutor's prefix re-validation).
        string foreignAssetKey = $"projects/{ProjectId}/outputFiles/{Guid.NewGuid():D}/overlay.webm";
        string graphicsPlanJson = JsonSerializer.Serialize(new
        {
            overlays = new[]
            {
                new { placementId = "p0", kind = "LowerThird", text = "", subtext = "", duration = "Short", emphasis = "Normal", renderedAssetStorageKey = foreignAssetKey, reason = "bad key" }
            },
            planRationale = "test"
        });

        StepExecutionContext context = CreateGraphicsContext(
            artifact, decisionJson, graphicsPlanJson, out Mock<IProjectFileWorkspace> workspace);

        JsonElement edl = default;
        StepExecutionResult result = await CreateExecutor(workspace, edlCaptured: e => edl = e).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, "an untrustworthy asset key must degrade this ONE overlay, never fail the whole compile");
        JsonElement graphics = edl.GetProperty("graphics");
        graphics.GetProperty("applied").GetBoolean().Should().BeFalse();
        graphics.GetProperty("appliedOverlayCount").GetInt32().Should().Be(0);
        JsonElement dropped = graphics.GetProperty("droppedOverlays");
        dropped.GetArrayLength().Should().Be(1);
        dropped[0].GetProperty("reason").GetString().Should().Be("invalid_asset_storage_key");

        workspace.Verify(
            w => w.DownloadStorageKeyToFileAsync(It.IsAny<Guid>(), foreignAssetKey, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "a storage key outside this execution's own prefix must never even be downloaded");
    }

    [Fact]
    public async Task Rendered_asset_overlay_that_fails_ffprobe_is_dropped_not_a_step_failure()
    {
        // A corrupt/unreadable asset must never be allowed to reach ffmpeg as an extra -i input —
        // that would fail the WHOLE encode (cut included), violating "graphics must never hold the
        // cut hostage". ResolveGraphicsAsync must catch this per-overlay via IMediaProbe and drop
        // just that one overlay instead.
        var placement = new VideoAnalysisPlacement(
            "p0", "s0", "LowerThird", new VideoAnalysisRect(0.1, 0.8, 0.6, 0.15),
            "ShotMiddle", 4.0, 6.0, 0.8, "Light");

        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            offeredIds: new[] { "s0" },
            placements: new[] { placement });

        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        string assetKey = $"projects/{ProjectId}/outputFiles/{GraphicsExecutionId:D}/corrupt.webm";
        string graphicsPlanJson = JsonSerializer.Serialize(new
        {
            overlays = new[]
            {
                new { placementId = "p0", kind = "LowerThird", text = "", subtext = "", duration = "Short", emphasis = "Normal", renderedAssetStorageKey = assetKey, reason = "corrupt asset" }
            },
            planRationale = "test"
        });

        StepExecutionContext context = CreateGraphicsContext(
            artifact, decisionJson, graphicsPlanJson, out Mock<IProjectFileWorkspace> workspace);

        workspace
            .Setup(w => w.DownloadStorageKeyToFileAsync(It.IsAny<Guid>(), assetKey, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, string, CancellationToken>((_, _, destPath, _) =>
            {
                File.WriteAllBytes(destPath, new byte[] { 0xDE, 0xAD });
                return Task.CompletedTask;
            });

        JsonElement edl = default;
        StepExecutionResult result = await CreateExecutor(
            workspace, edlCaptured: e => edl = e,
            configureMediaProbe: probe => probe
                .Setup(p => p.ProbeAsync(It.Is<string>(s => s.Contains("gfx-asset")), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("ffprobe failed for this corrupt file")))
            .ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed);
        JsonElement graphics = edl.GetProperty("graphics");
        graphics.GetProperty("applied").GetBoolean().Should().BeFalse();
        JsonElement dropped = graphics.GetProperty("droppedOverlays");
        dropped.GetArrayLength().Should().Be(1);
        dropped[0].GetProperty("reason").GetString().Should().Be("asset_download_or_probe_failed");
    }

    [Fact]
    public async Task Rendered_asset_overlay_with_no_alpha_channel_is_dropped_not_composited_opaque()
    {
        // A render that ignored the documented --pixel-format=yuva420p --codec=vp9 recipe (e.g.
        // Remotion's default H.264, no alpha plane at all) must never reach the overlay filter —
        // that would composite as a solid, opaque rectangle over the edited video ("a black square
        // background with giant shadows"). ResolveGraphicsAsync must catch this via
        // AlphaPixelFormats.HasAlpha and drop just that one overlay, same degrade-not-fail
        // discipline as a corrupt/unreadable asset.
        var placement = new VideoAnalysisPlacement(
            "p0", "s0", "LowerThird", new VideoAnalysisRect(0.1, 0.8, 0.6, 0.15),
            "ShotMiddle", 4.0, 6.0, 0.8, "Light");

        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            offeredIds: new[] { "s0" },
            placements: new[] { placement });

        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        string assetKey = $"projects/{ProjectId}/outputFiles/{GraphicsExecutionId:D}/opaque.mov";
        string graphicsPlanJson = JsonSerializer.Serialize(new
        {
            overlays = new[]
            {
                new { placementId = "p0", kind = "LowerThird", text = "", subtext = "", duration = "Short", emphasis = "Normal", renderedAssetStorageKey = assetKey, reason = "no-alpha render" }
            },
            planRationale = "test"
        });

        StepExecutionContext context = CreateGraphicsContext(
            artifact, decisionJson, graphicsPlanJson, out Mock<IProjectFileWorkspace> workspace);

        workspace
            .Setup(w => w.DownloadStorageKeyToFileAsync(It.IsAny<Guid>(), assetKey, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, string, CancellationToken>((_, _, destPath, _) =>
            {
                File.WriteAllBytes(destPath, new byte[] { 0x00 });
                return Task.CompletedTask;
            });

        JsonElement edl = default;
        StepExecutionResult result = await CreateExecutor(
            workspace, edlCaptured: e => edl = e,
            configureMediaProbe: probe => probe
                .Setup(p => p.ProbeAsync(It.Is<string>(s => s.Contains("gfx-asset")), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MediaProbeResult(1.5, 30, 1, 640, 360, "h264", null, null, "yuv420p")))
            .ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, "a missing alpha channel must degrade this ONE overlay, never fail the whole compile");
        JsonElement graphics = edl.GetProperty("graphics");
        graphics.GetProperty("applied").GetBoolean().Should().BeFalse();
        graphics.GetProperty("appliedOverlayCount").GetInt32().Should().Be(0);
        JsonElement dropped = graphics.GetProperty("droppedOverlays");
        dropped.GetArrayLength().Should().Be(1);
        dropped[0].GetProperty("reason").GetString().Should().Be("asset_missing_alpha_channel");
    }

    [Fact]
    public async Task Plain_text_overlay_and_rendered_asset_overlay_can_coexist_in_the_same_compile()
    {
        var textPlacement = new VideoAnalysisPlacement(
            "p0", "s0", "LowerThird", new VideoAnalysisRect(0.1, 0.8, 0.6, 0.15),
            "ShotMiddle", 4.0, 6.0, 0.8, "Light");
        var assetPlacement = new VideoAnalysisPlacement(
            "p1", "s0", "UpperThird", new VideoAnalysisRect(0.1, 0.05, 0.6, 0.15),
            "ShotMiddle", 4.0, 6.0, 0.8, "Light");

        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            offeredIds: new[] { "s0" },
            placements: new[] { textPlacement, assetPlacement });

        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        string assetKey = $"projects/{ProjectId}/outputFiles/{GraphicsExecutionId:D}/overlay.webm";
        string graphicsPlanJson = JsonSerializer.Serialize(new
        {
            overlays = new[]
            {
                new { placementId = "p0", kind = "Tag", text = "Live", subtext = "", duration = "Short", emphasis = "Normal", renderedAssetStorageKey = "", reason = "plain text" },
                new { placementId = "p1", kind = "Title", text = "", subtext = "", duration = "Short", emphasis = "Normal", renderedAssetStorageKey = assetKey, reason = "rendered graphic" }
            },
            planRationale = "test"
        });

        StepExecutionContext context = CreateGraphicsContext(
            artifact, decisionJson, graphicsPlanJson, out Mock<IProjectFileWorkspace> workspace);

        workspace
            .Setup(w => w.DownloadStorageKeyToFileAsync(It.IsAny<Guid>(), assetKey, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, string, CancellationToken>((_, _, destPath, _) =>
            {
                File.WriteAllBytes(destPath, new byte[] { 0x00 });
                return Task.CompletedTask;
            });

        JsonElement edl = default;
        List<string>? capturedArgs = null;
        StepExecutionResult result = await CreateExecutor(
            workspace, edlCaptured: e => edl = e, ffmpegArgsCaptured: a => capturedArgs = a.ToList())
            .ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed);
        JsonElement graphics = edl.GetProperty("graphics");
        graphics.GetProperty("appliedOverlayCount").GetInt32().Should().Be(2);

        string filterComplex = ExtractFilterComplexValue(capturedArgs!);
        filterComplex.Should().Contain("drawtext=", "the plain-text overlay must still use the pre-existing drawtext path");
        filterComplex.Should().Contain("overlay=x=", "the rendered-asset overlay must use the new overlay-filter path");
        filterComplex.Should().Contain("[vtxt]", "the text stage must hand off to an internal label rather than [vout] directly when an asset stage follows it");
    }

    /// <summary>Pulls the value that follows "-filter_complex" out of a captured ffmpeg argv (never "-filter_complex_script", which these small test filter graphs never trigger).</summary>
    private static string ExtractFilterComplexValue(IReadOnlyList<string> args)
    {
        int index = args.ToList().IndexOf("-filter_complex");
        index.Should().BeGreaterThan(-1, "test compiles are small enough to never hit the -filter_complex_script threshold");
        return args[index + 1];
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
            // Fixed (not Guid.NewGuid()) so tests can construct a RenderedAssetStorageKey that
            // matches the exact "projects/{ProjectId}/outputFiles/{GraphicsExecutionId}/..."
            // prefix VideoCompileStepExecutor validates against, without needing an extra out
            // parameter threaded through every call site.
            Execution = new WorkflowExecution { Id = GraphicsExecutionId, ProjectId = ProjectId },
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
    // Background music (see docs/video-editing.md "Background music")
    // =======================================================================

    private const string MusicTrackStorageKey = "projects/p/userFiles/music.mp3";
    private static readonly Guid MusicProjectFileId = Guid.NewGuid();
    private static readonly Guid MusicExecutionId = Guid.NewGuid();

    [Fact]
    public async Task EnableMusic_false_leaves_filter_string_and_output_byte_identical_to_pre_music_compile()
    {
        // The load-bearing backward-compatibility guarantee of this whole feature — mirrors
        // EnableGraphics_false_produces_no_graphics_key_at_all_byte_identical_to_pre_phase3 exactly.
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            offeredIds: new[] { "s0" });
        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));

        string? baselineFilter = null;
        StepExecutionContext baselineContext = CreateContext(artifact, decisionJson, out Mock<IProjectFileWorkspace> baselineWorkspace);
        JsonElement baselineEdl = default;
        StepExecutionResult baselineResult = await CreateExecutor(
            baselineWorkspace, edlCaptured: e => baselineEdl = e,
            ffmpegArgsCaptured: args => baselineFilter ??= ExtractFilterComplexValue(args)).ExecuteAsync(baselineContext);

        // Even with a MusicTrackProjectFileId configured, EnableMusic=false must leave everything
        // byte-identical — the flag alone gates the whole feature.
        string? musicOffFilter = null;
        StepExecutionContext musicOffContext = CreateContext(
            artifact, decisionJson, out Mock<IProjectFileWorkspace> musicOffWorkspace,
            configOverride: cfg => cfg with { EnableMusic = false, MusicTrackProjectFileId = Guid.NewGuid() });
        JsonElement musicOffEdl = default;
        StepExecutionResult musicOffResult = await CreateExecutor(
            musicOffWorkspace, edlCaptured: e => musicOffEdl = e,
            ffmpegArgsCaptured: args => musicOffFilter ??= ExtractFilterComplexValue(args)).ExecuteAsync(musicOffContext);

        baselineResult.Status.Should().Be(StepStatus.Completed);
        musicOffResult.Status.Should().Be(StepStatus.Completed);
        musicOffFilter.Should().Be(baselineFilter, "EnableMusic=false must leave the filter string byte-identical");
        baselineFilter.Should().NotContain("amix").And.NotContain("[adial]").And.NotContain("[amus]");

        baselineEdl.TryGetProperty("music", out _).Should().BeFalse();
        musicOffEdl.TryGetProperty("music", out _).Should().BeFalse();
        using JsonDocument outputDoc = JsonDocument.Parse(musicOffResult.Output);
        outputDoc.RootElement.TryGetProperty("music", out _).Should().BeFalse();
    }

    [Fact]
    public async Task EnableMusic_true_with_StreamCopy_fails_MUSIC_REQUIRES_REENCODE()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(shots: new[] { ("s0", 0.0, 10.0) }, offeredIds: new[] { "s0" });
        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        StepExecutionContext context = CreateContext(
            artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace,
            configOverride: cfg => cfg with
            {
                EnableMusic = true,
                MusicTrackProjectFileId = Guid.NewGuid(),
                Mode = VideoCompileMode.StreamCopy,
                AllowKeyframeSnapping = true
            });

        StepExecutionResult result = await CreateExecutor(workspace).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        ErrorCode(result).Should().Be("MUSIC_REQUIRES_REENCODE");
    }

    [Fact]
    public async Task EnableMusic_true_with_AudioCodec_copy_fails_MUSIC_REQUIRES_AUDIO_REENCODE()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(shots: new[] { ("s0", 0.0, 10.0) }, offeredIds: new[] { "s0" });
        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        StepExecutionContext context = CreateContext(
            artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace,
            configOverride: cfg => cfg with
            {
                EnableMusic = true,
                MusicTrackProjectFileId = Guid.NewGuid(),
                AudioCodec = "copy"
            });

        StepExecutionResult result = await CreateExecutor(workspace).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        ErrorCode(result).Should().Be("MUSIC_REQUIRES_AUDIO_REENCODE");
    }

    [Fact]
    public async Task Keep_span_naming_a_music_track_id_fails_UNKNOWN_ID_since_music_tracks_are_a_separate_namespace()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(shots: new[] { ("s0", 0.0, 10.0) }, offeredIds: new[] { "s0" });
        string decisionJson = BuildDecisionJson(("m0", "m0", "wrong namespace"));
        StepExecutionContext context = CreateContext(artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace);

        StepExecutionResult result = await CreateExecutor(workspace).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        ErrorCode(result).Should().Be("UNKNOWN_ID");
    }

    [Fact]
    public async Task Deterministic_music_track_is_applied_and_recorded_in_the_music_block()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(shots: new[] { ("s0", 0.0, 10.0) }, offeredIds: new[] { "s0" });
        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        StepExecutionContext context = CreateMusicContext(artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace);

        JsonElement edl = default;
        StepExecutionResult result = await CreateExecutor(workspace, edlCaptured: e => edl = e).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        JsonElement music = edl.GetProperty("music");
        music.GetProperty("enabled").GetBoolean().Should().BeTrue();
        music.GetProperty("applied").GetBoolean().Should().BeTrue();
        music.GetProperty("source").GetString().Should().Be("config");
        music.GetProperty("trackId").GetString().Should().Be(""); // no plan involved — the config path never assigns an m{n} id
        music.GetProperty("intensity").GetString().Should().Be("Balanced");
        music.GetProperty("ducking").GetString().Should().Be("Normal");
        music.GetProperty("fit").GetString().Should().Be("LoopToFit");
        // No shot in this artifact carries a Phase 1 Audio descriptor, so dialogueHeadroom must
        // report inapplicable rather than fabricate a mean level from nothing.
        music.GetProperty("dialogueHeadroom").GetProperty("applicable").GetBoolean().Should().BeFalse();

        using JsonDocument outputDoc = JsonDocument.Parse(result.Output);
        outputDoc.RootElement.GetProperty("music").GetProperty("applied").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Music_input_is_the_last_ffmpeg_input_and_the_audio_cut_label_flips_to_adial()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(shots: new[] { ("s0", 0.0, 10.0) }, offeredIds: new[] { "s0" });
        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        StepExecutionContext context = CreateMusicContext(artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace);

        IReadOnlyList<string>? capturedArgs = null;
        StepExecutionResult result = await CreateExecutor(
            workspace, ffmpegArgsCaptured: args => capturedArgs ??= args).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        capturedArgs.Should().NotBeNull();

        List<int> inputFlagIndices = capturedArgs!
            .Select((a, i) => (a, i))
            .Where(t => t.a == "-i")
            .Select(t => t.i)
            .ToList();
        inputFlagIndices.Should().HaveCount(2, "the main source input plus the music input");
        capturedArgs[inputFlagIndices[0] + 1].Should().NotContain("music", "input 0 must still be the main source video");
        capturedArgs[inputFlagIndices[^1] + 1].Should().Contain("music", "the music track must be the LAST ffmpeg input");

        string filterComplex = ExtractFilterComplexValue(capturedArgs);
        filterComplex.Should().Contain("[adial]");
        filterComplex.Should().Contain("[amus]");
        filterComplex.Should().Contain("amix=inputs=2:duration=first:dropout_transition=0:normalize=0[aout]");
        filterComplex.Should().Contain("[1:a]atrim=end=", "music is ffmpeg input index 1 here (no asset overlays present)");
    }

    [Fact]
    public async Task Source_with_no_audio_stream_and_music_uses_the_music_track_alone_as_output_audio()
    {
        // No dialogue anywhere in the compile to duck against, so the music branch's own output
        // becomes [aout] directly — no [adial], no amix mixing stage — rather than either crashing
        // ffmpeg (the pre-fix bug) or silently dropping the music the user explicitly configured.
        VideoAnalysisArtifact artifact = BuildArtifact(shots: new[] { ("s0", 0.0, 10.0) }, offeredIds: new[] { "s0" });
        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        StepExecutionContext context = CreateMusicContext(artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace);

        IReadOnlyList<string>? capturedArgs = null;
        JsonElement edl = default;
        StepExecutionResult result = await CreateExecutor(
            workspace, ffmpegArgsCaptured: args => capturedArgs ??= args, edlCaptured: e => edl = e,
            configureMediaProbe: mock => mock
                .Setup(p => p.ProbeAsync(It.Is<string>(path => !path.Contains("music")), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MediaProbeResult(10, 30, 1, 1920, 1080, "h264", null, null)))
            .ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        capturedArgs.Should().NotBeNull();

        string filterComplex = ExtractFilterComplexValue(capturedArgs!);
        filterComplex.Should().NotContain("[0:a]", "the source has no audio stream");
        filterComplex.Should().NotContain("[adial]", "there is no dialogue branch to duck the music against");
        filterComplex.Should().NotContain("amix", "amix only makes sense when mixing two real inputs");
        filterComplex.Should().Contain("[1:a]atrim=end=", "the music branch itself still runs, as ffmpeg input 1");
        filterComplex.Should().Contain("[aout]", "the music branch's own output becomes the final audio output directly");

        List<string> argsList = capturedArgs!.ToList();
        argsList.Should().Contain("[aout]", "there IS audio output overall — it just comes from music alone");
        argsList.Should().Contain("-c:a");

        // Bug group C.2: with no dialogue audio in the final output at all, there is nothing to
        // duck against — ResolveMusicAsync must skip lift-window planning (flat, undocked bed
        // level) and must not report a dialogueHeadroom number describing dialogue that isn't
        // there, rather than sitting the music at the ducked level through what were dialogue
        // regions in the (dropped) source audio.
        JsonElement music = edl.GetProperty("music");
        music.GetProperty("duckBasis").GetString().Should().Be("no_dialogue_audio");
        music.GetProperty("bedDbfs").GetInt32().Should().Be(music.GetProperty("duckedDbfs").GetInt32(),
            "with nothing to duck against, the bed level must stay flat rather than sitting ducked");
        JsonElement headroom = music.GetProperty("dialogueHeadroom");
        headroom.GetProperty("applicable").GetBoolean().Should().BeFalse();
        headroom.GetProperty("reason").GetString().Should().Be("no_dialogue_audio_in_output");
    }

    [Fact]
    public async Task Plan_naming_an_unoffered_track_id_falls_back_to_the_configured_track()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(shots: new[] { ("s0", 0.0, 10.0) }, offeredIds: new[] { "s0" });
        artifact = artifact with
        {
            MusicCandidates = [new VideoAnalysisMusicCandidate("m0", MusicProjectFileId, "music.mp3", "audio/mpeg", 4096)],
            OfferedMusicIds = ["m0"]
        };
        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        string musicPlanJson = JsonSerializer.Serialize(new
        {
            trackId = "m9", // not in OfferedMusicIds
            intensity = "Feature",
            ducking = "Heavy",
            fit = "PlayOnce",
            reason = "test",
            planRationale = "test"
        });

        StepExecutionContext context = CreateMusicContext(
            artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace,
            // Decision must be pointed explicitly at StepOrder 2 (the story editor) here — this
            // test is the only CreateMusicContext caller that appends a 4th history entry
            // (MusicSupervisor, StepOrder 3), so the default Decision: Previous would otherwise
            // resolve to THAT entry instead, exactly the "Previous relative to the compile step
            // would resolve to the MusicSupervisor step's own output, not the story editor's
            // decision" trap CLAUDE.md documents for this feature.
            configOverride: cfg => cfg with
            {
                Decision = new ExtractInputRef(ExtractInputSource.Step, StepOrder: 2),
                MusicPlan = new ExtractInputRef(ExtractInputSource.Step, StepOrder: 3)
            },
            musicPlanJson: musicPlanJson);

        JsonElement edl = default;
        StepExecutionResult result = await CreateExecutor(workspace, edlCaptured: e => edl = e).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        JsonElement music = edl.GetProperty("music");
        music.GetProperty("source").GetString().Should().Be("config");
        JsonElement dropped = music.GetProperty("dropped");
        dropped.GetArrayLength().Should().Be(1);
        dropped[0].GetProperty("reason").GetString().Should().Be("unknown_track_id");
        dropped[0].GetProperty("trackId").GetString().Should().Be("m9");
    }

    [Fact]
    public async Task Non_audio_project_file_is_dropped_as_track_not_audio_and_the_cut_still_succeeds()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(shots: new[] { ("s0", 0.0, 10.0) }, offeredIds: new[] { "s0" });
        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        StepExecutionContext context = CreateMusicContext(
            artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace, musicMimeType: "video/mp4");

        JsonElement edl = default;
        StepExecutionResult result = await CreateExecutor(workspace, edlCaptured: e => edl = e).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, "a bad music track must never fail the cut itself");
        JsonElement music = edl.GetProperty("music");
        music.GetProperty("applied").GetBoolean().Should().BeFalse();
        music.GetProperty("dropped")[0].GetProperty("reason").GetString().Should().Be("track_not_audio");
    }

    [Fact]
    public async Task Track_probe_failure_drops_music_and_the_cut_still_succeeds()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(shots: new[] { ("s0", 0.0, 10.0) }, offeredIds: new[] { "s0" });
        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        StepExecutionContext context = CreateMusicContext(artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace);

        JsonElement edl = default;
        StepExecutionResult result = await CreateExecutor(
            workspace, edlCaptured: e => edl = e,
            configureMediaProbe: probe => probe
                .Setup(p => p.ProbeAsync(It.Is<string>(path => path.Contains("music")), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("corrupt file"))).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, "a bad music track must never fail the cut itself");
        JsonElement music = edl.GetProperty("music");
        music.GetProperty("applied").GetBoolean().Should().BeFalse();
        music.GetProperty("dropped")[0].GetProperty("reason").GetString().Should().Be("track_download_or_probe_failed");
    }

    [Fact]
    public async Task Amix_without_normalize_option_skips_all_music_and_the_cut_still_succeeds()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(shots: new[] { ("s0", 0.0, 10.0) }, offeredIds: new[] { "s0" });
        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        StepExecutionContext context = CreateMusicContext(artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace);

        JsonElement edl = default;
        StepExecutionResult result = await CreateExecutor(
            workspace, edlCaptured: e => edl = e, amixNormalizeAvailable: false).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        JsonElement music = edl.GetProperty("music");
        music.GetProperty("unavailable").GetBoolean().Should().BeTrue();
        music.GetProperty("applied").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task LoopToFit_with_a_track_shorter_than_the_edit_loops_and_trims_to_the_edit_length()
    {
        // 10s edit, 4s track -> loops (ceil(10/4) = 3), -stream_loop present, atrim=end=10.
        VideoAnalysisArtifact artifact = BuildArtifact(shots: new[] { ("s0", 0.0, 10.0) }, offeredIds: new[] { "s0" });
        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        StepExecutionContext context = CreateMusicContext(artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace);

        JsonElement edl = default;
        IReadOnlyList<string>? capturedArgs = null;
        StepExecutionResult result = await CreateExecutor(
            workspace, edlCaptured: e => edl = e, ffmpegArgsCaptured: args => capturedArgs ??= args,
            configureMediaProbe: probe => probe
                .Setup(p => p.ProbeAsync(It.Is<string>(path => path.Contains("music")), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MediaProbeResult(4, 30, 1, 0, 0, null, "mp3", 44100))).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        JsonElement music = edl.GetProperty("music");
        music.GetProperty("fit").GetString().Should().Be("LoopToFit");
        music.GetProperty("loops").GetInt32().Should().Be(3);
        music.GetProperty("playEndSec").GetDouble().Should().BeApproximately(10.0, 0.01);
        capturedArgs.Should().Contain("-stream_loop");

        string filterComplex = ExtractFilterComplexValue(capturedArgs!);
        filterComplex.Should().Contain("atrim=end=10");
    }

    [Fact]
    public async Task PlayOnce_with_a_track_shorter_than_the_edit_never_loops_and_trims_to_the_track_length()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(shots: new[] { ("s0", 0.0, 10.0) }, offeredIds: new[] { "s0" });
        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        StepExecutionContext context = CreateMusicContext(
            artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace,
            configOverride: cfg => cfg with { MusicFitPolicy = MusicFit.PlayOnce });

        JsonElement edl = default;
        IReadOnlyList<string>? capturedArgs = null;
        StepExecutionResult result = await CreateExecutor(
            workspace, edlCaptured: e => edl = e, ffmpegArgsCaptured: args => capturedArgs ??= args,
            configureMediaProbe: probe => probe
                .Setup(p => p.ProbeAsync(It.Is<string>(path => path.Contains("music")), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MediaProbeResult(4, 30, 1, 0, 0, null, "mp3", 44100))).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        JsonElement music = edl.GetProperty("music");
        music.GetProperty("fit").GetString().Should().Be("PlayOnce");
        music.GetProperty("playEndSec").GetDouble().Should().BeApproximately(4.0, 0.01);
        capturedArgs.Should().NotContain("-stream_loop");

        string filterComplex = ExtractFilterComplexValue(capturedArgs!);
        filterComplex.Should().Contain("atrim=end=4");
    }

    /// <summary>Builds a context wired for background music: the deterministic MusicTrackProjectFileId path by default, and an optional 4th history entry (StepOrder 3, a MusicSupervisor step) when <paramref name="musicPlanJson"/> is supplied.</summary>
    // =======================================================================
    // Color grading (see docs/video-editing.md "Color grading")
    // =======================================================================

    [Fact]
    public async Task EnableColorGrade_false_produces_no_colorGrade_key_at_all_byte_identical_to_pre_grade()
    {
        // The load-bearing backward-compatibility guarantee: even with a ColorGradePlan
        // configured, EnableColorGrade=false (the default) must leave the EDL/output shape
        // completely untouched — no "colorGrade" key anywhere.
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            offeredIds: new[] { "s0" });

        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        StepExecutionContext context = CreateContext(
            artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace,
            configOverride: cfg => cfg with
            {
                EnableColorGrade = false,
                ColorGradePlan = new ExtractInputRef(ExtractInputSource.Previous)
            });

        JsonElement edl = default;
        IReadOnlyList<string>? capturedArgs = null;
        StepExecutionResult result = await CreateExecutor(
            workspace, edlCaptured: e => edl = e, ffmpegArgsCaptured: args => capturedArgs ??= args).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        edl.TryGetProperty("colorGrade", out _).Should().BeFalse("EDL must have no colorGrade key when EnableColorGrade=false");

        using JsonDocument outputDoc = JsonDocument.Parse(result.Output);
        outputDoc.RootElement.TryGetProperty("colorGrade", out _).Should().BeFalse();
        string filterComplex = string.Join(" ", capturedArgs!);
        filterComplex.Should().NotContain("colorbalance", "no grade filter may reach ffmpeg when EnableColorGrade=false");
    }

    [Fact]
    public async Task EnableColorGrade_true_with_StreamCopy_fails_COLOR_GRADE_REQUIRES_REENCODE()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            offeredIds: new[] { "s0" });

        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        StepExecutionContext context = CreateContext(
            artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace,
            configOverride: cfg => cfg with
            {
                EnableColorGrade = true,
                Mode = VideoCompileMode.StreamCopy,
                AllowKeyframeSnapping = true
            });

        StepExecutionResult result = await CreateExecutor(workspace).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        ErrorCode(result).Should().Be("COLOR_GRADE_REQUIRES_REENCODE");
    }

    [Fact]
    public async Task Warm_grade_plan_appends_the_first_party_chain_to_the_cut_stage_and_reports_it_in_the_EDL()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(shots: new[] { ("s0", 0.0, 10.0) }, offeredIds: new[] { "s0" });
        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        string gradePlanJson = JsonSerializer.Serialize(new
        {
            look = "Warm",
            strength = "Subtle",
            shadowTone = "Lifted",
            highlightTone = "Neutral",
            reason = "test",
            planRationale = "test"
        });

        StepExecutionContext context = CreateGradeContext(artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace, gradePlanJson);

        JsonElement edl = default;
        IReadOnlyList<string>? capturedArgs = null;
        StepExecutionResult result = await CreateExecutor(
            workspace, edlCaptured: e => edl = e, ffmpegArgsCaptured: args => capturedArgs ??= args).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);

        JsonElement grade = edl.GetProperty("colorGrade");
        grade.GetProperty("applied").GetBoolean().Should().BeTrue();
        grade.GetProperty("look").GetString().Should().Be("Warm");
        grade.GetProperty("strength").GetString().Should().Be("Subtle");
        grade.GetProperty("shadowTone").GetString().Should().Be("Lifted");

        string expectedChain = ColorGradeFilterBuilder.BuildFilterChain("Warm", "Subtle", "Lifted", "Neutral")!;
        grade.GetProperty("filterChain").GetString().Should().Be(expectedChain);

        // The chain must sit in the CUT stage (immediately after setpts) — i.e. before any
        // overlay/insert stage would run — never as a dangling separate stage.
        int fcIndex = capturedArgs!.ToList().IndexOf("-filter_complex");
        fcIndex.Should().BeGreaterThan(0);
        string filterComplex = capturedArgs[fcIndex + 1];
        filterComplex.Should().Contain("setpts=N/FRAME_RATE/TB," + expectedChain);

        using JsonDocument outputDoc = JsonDocument.Parse(result.Output);
        outputDoc.RootElement.GetProperty("colorGrade").GetProperty("applied").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Grade_plan_with_look_None_applies_no_filter_and_reports_look_none()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(shots: new[] { ("s0", 0.0, 10.0) }, offeredIds: new[] { "s0" });
        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        string gradePlanJson = JsonSerializer.Serialize(new
        {
            look = "None", strength = "Normal", shadowTone = "Lifted", highlightTone = "Softened",
            reason = "already consistent", planRationale = "no grade"
        });

        StepExecutionContext context = CreateGradeContext(artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace, gradePlanJson);

        JsonElement edl = default;
        IReadOnlyList<string>? capturedArgs = null;
        StepExecutionResult result = await CreateExecutor(
            workspace, edlCaptured: e => edl = e, ffmpegArgsCaptured: args => capturedArgs ??= args).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        JsonElement grade = edl.GetProperty("colorGrade");
        grade.GetProperty("applied").GetBoolean().Should().BeFalse();
        grade.GetProperty("reason").GetString().Should().Be("look_none");
        int fcIndex = capturedArgs!.ToList().IndexOf("-filter_complex");
        string filterComplex = fcIndex >= 0 ? capturedArgs[fcIndex + 1] : string.Join(" ", capturedArgs!);
        filterComplex.Should().NotContain("colorbalance",
            "a plan whose look is None must leave the compiled bytes carrying no grade filter whatsoever — tone words included");
        filterComplex.Should().NotContain("colorlevels");
    }

    [Fact]
    public async Task Grade_plan_with_an_unknown_look_word_degrades_to_no_grade_and_the_cut_still_succeeds()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(shots: new[] { ("s0", 0.0, 10.0) }, offeredIds: new[] { "s0" });
        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        string gradePlanJson = JsonSerializer.Serialize(new
        {
            look = "Sepia", strength = "Normal", shadowTone = "Neutral", highlightTone = "Neutral",
            reason = "test", planRationale = "test"
        });

        StepExecutionContext context = CreateGradeContext(artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace, gradePlanJson);

        JsonElement edl = default;
        StepExecutionResult result = await CreateExecutor(workspace, edlCaptured: e => edl = e).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, "a bad grade plan must never fail the cut itself");
        JsonElement grade = edl.GetProperty("colorGrade");
        grade.GetProperty("applied").GetBoolean().Should().BeFalse();
        grade.GetProperty("reason").GetString().Should().Be("unknown_look_word");
        grade.GetProperty("look").GetString().Should().Be("Sepia");
    }

    [Fact]
    public async Task Missing_ColorGradePlan_content_produces_grade_free_but_otherwise_successful_compile()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(shots: new[] { ("s0", 0.0, 10.0) }, offeredIds: new[] { "s0" });
        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));

        // ColorGradePlan references a step order that produced no output at all.
        StepExecutionContext context = CreateContext(
            artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace,
            configOverride: cfg => cfg with
            {
                EnableColorGrade = true,
                ColorGradePlan = new ExtractInputRef(ExtractInputSource.Step, StepOrder: 99)
            });

        JsonElement edl = default;
        StepExecutionResult result = await CreateExecutor(workspace, edlCaptured: e => edl = e).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        JsonElement grade = edl.GetProperty("colorGrade");
        grade.GetProperty("applied").GetBoolean().Should().BeFalse();
        grade.GetProperty("reason").GetString().Should().Be("plan_unresolved");
    }

    [Fact]
    public async Task Grade_filters_missing_from_the_ffmpeg_build_skip_the_grade_and_the_cut_still_succeeds()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(shots: new[] { ("s0", 0.0, 10.0) }, offeredIds: new[] { "s0" });
        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        string gradePlanJson = JsonSerializer.Serialize(new
        {
            look = "Cool", strength = "Normal", shadowTone = "Neutral", highlightTone = "Neutral",
            reason = "test", planRationale = "test"
        });

        StepExecutionContext context = CreateGradeContext(artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace, gradePlanJson);

        JsonElement edl = default;
        StepExecutionResult result = await CreateExecutor(
            workspace, edlCaptured: e => edl = e, gradeFiltersAvailable: false).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, "an ffmpeg build without the grade filters must never fail the cut itself");
        JsonElement grade = edl.GetProperty("colorGrade");
        grade.GetProperty("applied").GetBoolean().Should().BeFalse();
        grade.GetProperty("reason").GetString().Should().Be("grade_filters_unavailable");
    }

    /// <summary>
    /// Mirrors <see cref="CreateContext"/> exactly, with one extra history entry: the grade plan
    /// at step order 4 (referenced explicitly, since Previous would resolve to the story
    /// editor's decision) and EnableColorGrade/ColorGradePlan preconfigured — the same pattern
    /// <see cref="CreateMusicContext"/> established for its plan entry.
    /// </summary>
    private static StepExecutionContext CreateGradeContext(
        VideoAnalysisArtifact artifact,
        string decisionJson,
        out Mock<IProjectFileWorkspace> workspace,
        string gradePlanJson)
    {
        StepExecutionContext baseContext = CreateContext(
            artifact, decisionJson, out workspace,
            configOverride: cfg => cfg with
            {
                // Decision must reference the story editor explicitly here — with the grade-plan
                // entry appended at step order 4, "Previous" would resolve to the grade plan
                // itself, exactly the trap the shipped templates' comments document.
                Decision = new ExtractInputRef(ExtractInputSource.Step, StepOrder: 2),
                EnableColorGrade = true,
                ColorGradePlan = new ExtractInputRef(ExtractInputSource.Step, StepOrder: 4)
            });

        List<StepOutputHistoryEntry> history = baseContext.StepOutputHistory.ToList();
        history.Add(new StepOutputHistoryEntry(4, "Colorist", gradePlanJson, OutputStorageKey: null, ArtifactStorageKey: null));

        return new StepExecutionContext
        {
            Execution = baseContext.Execution,
            Step = baseContext.Step,
            AllSteps = baseContext.AllSteps,
            AccumulatedOutput = baseContext.AccumulatedOutput,
            StepOutputHistory = history,
            CurrentStepIndex = baseContext.CurrentStepIndex,
            IterationCount = baseContext.IterationCount,
            CorrelationId = baseContext.CorrelationId,
            CancellationToken = baseContext.CancellationToken
        };
    }

    private static StepExecutionContext CreateMusicContext(
        VideoAnalysisArtifact artifact,
        string decisionJson,
        out Mock<IProjectFileWorkspace> workspace,
        Func<VideoCompileStepConfig, VideoCompileStepConfig>? configOverride = null,
        string musicMimeType = "audio/mpeg",
        string? musicPlanJson = null)
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

        workspace
            .Setup(w => w.DownloadStorageKeyToFileAsync(
                It.IsAny<Guid>(), MusicTrackStorageKey, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, string, CancellationToken>((_, _, destPath, _) =>
            {
                File.WriteAllBytes(destPath, new byte[] { 0x00, 0x01, 0x02 });
                return Task.CompletedTask;
            });

        workspace
            .Setup(w => w.ListFilesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectWorkspaceFile>
            {
                new(MusicProjectFileId, ProjectId, "music.mp3", null, "userFiles", MusicTrackStorageKey, musicMimeType, 4096, DateTime.UtcNow, null)
            });

        VideoCompileStepConfig config = new(
            Version: 1,
            Decision: new ExtractInputRef(ExtractInputSource.Previous),
            AnalysisStepOrder: 1,
            EnableMusic: true,
            MusicTrackProjectFileId: MusicProjectFileId);

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

        if (musicPlanJson is not null)
            history.Add(new StepOutputHistoryEntry(3, "MusicSupervisor", musicPlanJson, OutputStorageKey: null, ArtifactStorageKey: null));

        return new StepExecutionContext
        {
            Execution = new WorkflowExecution { Id = MusicExecutionId, ProjectId = ProjectId },
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

    // =======================================================================
    // Tracked screen inserts (see docs/video-editing.md "Tracked screen inserts (Phase 5)")
    // =======================================================================

    /// <summary>A well-formed, high-confidence static insert-region track spanning [2, 8]s of shot s0 with two corner keyframes.</summary>
    private static VideoInsertRegionTrack BuildInsertTrack(
        string id = "r0", double startSec = 2.0, double endSec = 8.0, double confidence = 0.9)
        => new(
            Id: id, ShotId: "s0", StartSec: startSec, EndSec: endSec,
            Keyframes:
            [
                new VideoInsertQuadKeyframe(startSec, 0.30, 0.20, 0.60, 0.22, 0.31, 0.70, 0.61, 0.72),
                new VideoInsertQuadKeyframe(endSec, 0.35, 0.20, 0.65, 0.22, 0.36, 0.70, 0.66, 0.72)
            ],
            Confidence: confidence, MeanAreaRatio: 0.12, MeanAspectRatio: 0.6,
            MotionClass: "Slow", ColorName: "green");

    private static string BuildInsertsPlanJson(string regionId, string assetKey) =>
        JsonSerializer.Serialize(new
        {
            overlays = Array.Empty<object>(),
            inserts = new[] { new { regionId, renderedAssetStorageKey = assetKey, reason = "screen content" } },
            planRationale = "test"
        });

    private static string InsertAssetKey(string name = "screen.mp4") =>
        $"projects/{ProjectId}/outputFiles/{GraphicsExecutionId:D}/{name}";

    private static void MockAssetDownload(Mock<IProjectFileWorkspace> workspace, string assetKey) =>
        workspace
            .Setup(w => w.DownloadStorageKeyToFileAsync(It.IsAny<Guid>(), assetKey, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, string, CancellationToken>((_, _, destPath, _) =>
            {
                File.WriteAllBytes(destPath, new byte[] { 0x00, 0x01, 0x02 });
                return Task.CompletedTask;
            });

    [Fact]
    public async Task EnableInserts_false_produces_no_inserts_key_at_all_byte_identical_to_pre_inserts()
    {
        // The load-bearing backward-compatibility guarantee — mirrors EnableGraphics/EnableMusic
        // exactly: even with tracked regions in the artifact AND a plan containing inserts,
        // EnableInserts=false must leave the EDL/output shape completely untouched.
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            offeredIds: new[] { "s0" });
        artifact = artifact with
        {
            InsertRegions = [BuildInsertTrack()],
            OfferedInsertRegionIds = ["r0"]
        };

        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        StepExecutionContext context = CreateGraphicsContext(
            artifact, decisionJson, BuildInsertsPlanJson("r0", InsertAssetKey()),
            out Mock<IProjectFileWorkspace> workspace,
            configOverride: cfg => cfg with { EnableGraphics = false, EnableInserts = false });

        JsonElement edl = default;
        StepExecutionResult result = await CreateExecutor(workspace, edlCaptured: e => edl = e).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        edl.TryGetProperty("inserts", out _).Should().BeFalse("EDL must have no inserts key when EnableInserts=false");
        using JsonDocument outputDoc = JsonDocument.Parse(result.Output);
        outputDoc.RootElement.TryGetProperty("inserts", out _).Should().BeFalse();
    }

    [Fact]
    public async Task EnableInserts_true_with_StreamCopy_fails_INSERTS_REQUIRE_REENCODE()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            offeredIds: new[] { "s0" });

        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        StepExecutionContext context = CreateContext(
            artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace,
            configOverride: cfg => cfg with
            {
                EnableInserts = true,
                Mode = VideoCompileMode.StreamCopy,
                AllowKeyframeSnapping = true
            });

        StepExecutionResult result = await CreateExecutor(workspace).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        ErrorCode(result).Should().Be("INSERTS_REQUIRE_REENCODE");
    }

    [Fact]
    public async Task Keep_span_naming_an_insert_region_id_fails_UNKNOWN_ID_since_regions_are_a_separate_namespace()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            offeredIds: new[] { "s0" });
        artifact = artifact with
        {
            InsertRegions = [BuildInsertTrack()],
            OfferedInsertRegionIds = ["r0"]
        };

        // A Keep span naming "r0" — an insert-region id, not a cut-anchor id — must fail
        // UNKNOWN_ID exactly like a placement/music/look id does (BuildIdTimeIndex excludes it).
        string decisionJson = BuildDecisionJson(("r0", "r0", "wrong namespace"));
        StepExecutionContext context = CreateContext(artifact, decisionJson, out Mock<IProjectFileWorkspace> workspace);

        StepExecutionResult result = await CreateExecutor(workspace).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        ErrorCode(result).Should().Be("UNKNOWN_ID");
    }

    [Fact]
    public async Task Insert_naming_unknown_region_id_is_dropped_not_a_step_failure()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            offeredIds: new[] { "s0" });
        artifact = artifact with
        {
            InsertRegions = [BuildInsertTrack()],
            OfferedInsertRegionIds = ["r0"]
        };

        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        StepExecutionContext context = CreateGraphicsContext(
            artifact, decisionJson, BuildInsertsPlanJson("r99", InsertAssetKey()),
            out Mock<IProjectFileWorkspace> workspace,
            configOverride: cfg => cfg with { EnableGraphics = false, EnableInserts = true });

        JsonElement edl = default;
        StepExecutionResult result = await CreateExecutor(workspace, edlCaptured: e => edl = e).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        JsonElement inserts = edl.GetProperty("inserts");
        inserts.GetProperty("applied").GetBoolean().Should().BeFalse();
        JsonElement dropped = inserts.GetProperty("droppedInserts");
        dropped.GetArrayLength().Should().Be(1);
        dropped[0].GetProperty("regionId").GetString().Should().Be("r99");
        dropped[0].GetProperty("reason").GetString().Should().Be("unknown_region_id");
    }

    [Fact]
    public async Task Insert_on_a_tracked_but_unoffered_region_is_dropped_offered_is_stricter_than_exists()
    {
        // The region EXISTS in the artifact but was never OFFERED (e.g. suppressed for view
        // budget) — same "offered is a stricter check than exists" discipline as Keep-span ids.
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            offeredIds: new[] { "s0" });
        artifact = artifact with
        {
            InsertRegions = [BuildInsertTrack()],
            OfferedInsertRegionIds = []
        };

        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        StepExecutionContext context = CreateGraphicsContext(
            artifact, decisionJson, BuildInsertsPlanJson("r0", InsertAssetKey()),
            out Mock<IProjectFileWorkspace> workspace,
            configOverride: cfg => cfg with { EnableGraphics = false, EnableInserts = true });

        JsonElement edl = default;
        StepExecutionResult result = await CreateExecutor(workspace, edlCaptured: e => edl = e).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        JsonElement dropped = edl.GetProperty("inserts").GetProperty("droppedInserts");
        dropped.GetArrayLength().Should().Be(1);
        dropped[0].GetProperty("reason").GetString().Should().Be("unknown_region_id");
    }

    [Fact]
    public async Task Insert_on_low_confidence_region_is_dropped_confidence_below_threshold()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            offeredIds: new[] { "s0" });
        artifact = artifact with
        {
            InsertRegions = [BuildInsertTrack(confidence: 0.3)],
            OfferedInsertRegionIds = ["r0"]
        };

        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        StepExecutionContext context = CreateGraphicsContext(
            artifact, decisionJson, BuildInsertsPlanJson("r0", InsertAssetKey()),
            out Mock<IProjectFileWorkspace> workspace,
            configOverride: cfg => cfg with { EnableGraphics = false, EnableInserts = true });

        JsonElement edl = default;
        StepExecutionResult result = await CreateExecutor(workspace, edlCaptured: e => edl = e).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        JsonElement dropped = edl.GetProperty("inserts").GetProperty("droppedInserts");
        dropped.GetArrayLength().Should().Be(1);
        dropped[0].GetProperty("reason").GetString().Should().Be("confidence_below_threshold");
    }

    [Fact]
    public async Task Insert_whose_region_window_is_entirely_cut_away_is_dropped()
    {
        // Track spans [12, 18]s of s1, but only s0 = [0, 10] is kept — the whole window falls in
        // the cut region, so this insert must drop as cut_away, never fail the compile.
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0), ("s1", 10.0, 20.0) },
            offeredIds: new[] { "s0", "s1" });
        artifact = artifact with
        {
            InsertRegions = [BuildInsertTrack(startSec: 12.0, endSec: 18.0)],
            OfferedInsertRegionIds = ["r0"]
        };

        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        StepExecutionContext context = CreateGraphicsContext(
            artifact, decisionJson, BuildInsertsPlanJson("r0", InsertAssetKey()),
            out Mock<IProjectFileWorkspace> workspace,
            configOverride: cfg => cfg with { EnableGraphics = false, EnableInserts = true, PrePaddingMs = 0, PostPaddingMs = 0 });

        JsonElement edl = default;
        StepExecutionResult result = await CreateExecutor(workspace, edlCaptured: e => edl = e).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        JsonElement dropped = edl.GetProperty("inserts").GetProperty("droppedInserts");
        dropped.GetArrayLength().Should().Be(1);
        dropped[0].GetProperty("reason").GetString().Should().Be("cut_away");
    }

    [Fact]
    public async Task Insert_with_asset_key_outside_this_executions_prefix_is_dropped()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            offeredIds: new[] { "s0" });
        artifact = artifact with
        {
            InsertRegions = [BuildInsertTrack()],
            OfferedInsertRegionIds = ["r0"]
        };

        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        string foreignKey = $"projects/{ProjectId}/outputFiles/{Guid.NewGuid():D}/other.mp4";
        StepExecutionContext context = CreateGraphicsContext(
            artifact, decisionJson, BuildInsertsPlanJson("r0", foreignKey),
            out Mock<IProjectFileWorkspace> workspace,
            configOverride: cfg => cfg with { EnableGraphics = false, EnableInserts = true });

        JsonElement edl = default;
        StepExecutionResult result = await CreateExecutor(workspace, edlCaptured: e => edl = e).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        JsonElement dropped = edl.GetProperty("inserts").GetProperty("droppedInserts");
        dropped.GetArrayLength().Should().Be(1);
        dropped[0].GetProperty("reason").GetString().Should().Be("invalid_asset_storage_key");
    }

    [Fact]
    public async Task Perspective_filters_unavailable_skips_all_inserts_but_still_compiles()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            offeredIds: new[] { "s0" });
        artifact = artifact with
        {
            InsertRegions = [BuildInsertTrack()],
            OfferedInsertRegionIds = ["r0"]
        };

        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        StepExecutionContext context = CreateGraphicsContext(
            artifact, decisionJson, BuildInsertsPlanJson("r0", InsertAssetKey()),
            out Mock<IProjectFileWorkspace> workspace,
            configOverride: cfg => cfg with { EnableGraphics = false, EnableInserts = true });

        JsonElement edl = default;
        StepExecutionResult result = await CreateExecutor(
            workspace, edlCaptured: e => edl = e, insertFiltersAvailable: false).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, "a missing filter must never fail the compile — the cut is the primary deliverable");
        JsonElement inserts = edl.GetProperty("inserts");
        inserts.GetProperty("applied").GetBoolean().Should().BeFalse();
        inserts.GetProperty("unavailable").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Applied_insert_adds_its_own_input_and_an_animated_perspective_alphamerge_filtergraph()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            offeredIds: new[] { "s0" });
        artifact = artifact with
        {
            InsertRegions = [BuildInsertTrack()],
            OfferedInsertRegionIds = ["r0"]
        };

        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        string assetKey = InsertAssetKey();
        StepExecutionContext context = CreateGraphicsContext(
            artifact, decisionJson, BuildInsertsPlanJson("r0", assetKey),
            out Mock<IProjectFileWorkspace> workspace,
            configOverride: cfg => cfg with { EnableGraphics = false, EnableInserts = true, PrePaddingMs = 0, PostPaddingMs = 0 });
        MockAssetDownload(workspace, assetKey);

        JsonElement edl = default;
        List<string>? capturedArgs = null;
        StepExecutionResult result = await CreateExecutor(
            workspace, edlCaptured: e => edl = e, ffmpegArgsCaptured: a => capturedArgs = a.ToList())
            .ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        JsonElement inserts = edl.GetProperty("inserts");
        inserts.GetProperty("applied").GetBoolean().Should().BeTrue();
        inserts.GetProperty("appliedInsertCount").GetInt32().Should().Be(1);
        inserts.GetProperty("droppedInserts").GetArrayLength().Should().Be(0);

        capturedArgs.Should().NotBeNull();
        string filterComplex = ExtractFilterComplexValue(capturedArgs!);
        filterComplex.Should().Contain("perspective=", "the insert must be corner-pinned via the perspective filter");
        filterComplex.Should().Contain("sense=destination:eval=frame", "corners animate per-frame toward the tracked quad");
        filterComplex.Should().Contain("alphamerge", "the warped white-plate mask is what keeps the outside of the quad transparent");
        filterComplex.Should().Contain("overlay=x=0:y=0", "the warped, masked content overlays the base at the origin");
        capturedArgs!.Count(a => a == "-i").Should().Be(2, "the main source video AND the insert asset must each be their own -i input");
    }

    [Fact]
    public void BuildInsertKeyframes_maps_keyframes_through_the_cut_and_skips_those_in_gaps()
    {
        // Spans keep [0,4) and [6,10) of a 30fps source: output timeline is [0,4)+[4,8).
        var spans = new List<VideoCompileStepExecutor.ResolvedSpan>
        {
            new(0, 4, 0, 4, 0, 120),
            new(6, 10, 6, 10, 180, 300)
        };
        OutputTimeline timeline = OutputTimeline.Build(spans, VideoCompileStepExecutor.BuildHardCutSeamsForFallback(spans.Count));
        var media = new VideoAnalysisMedia(10, 30, 1, 1920, 1080);

        // Track [2, 8] with keyframes at t=2 (kept, output 2.0), t=5 (in the cut gap — skipped),
        // t=8 (kept but OUTSIDE the first-overlap window [2,4) the insert is clipped to — skipped).
        VideoInsertRegionTrack track = new(
            "r0", "s0", 2.0, 8.0,
            Keyframes:
            [
                new VideoInsertQuadKeyframe(2.0, 0.3, 0.2, 0.6, 0.2, 0.3, 0.7, 0.6, 0.7),
                new VideoInsertQuadKeyframe(5.0, 0.4, 0.2, 0.7, 0.2, 0.4, 0.7, 0.7, 0.7),
                new VideoInsertQuadKeyframe(8.0, 0.5, 0.2, 0.8, 0.2, 0.5, 0.7, 0.8, 0.7)
            ],
            Confidence: 0.9, MeanAreaRatio: 0.1, MeanAspectRatio: 0.6, MotionClass: "Slow", ColorName: "green");

        (double Start, double End)? window = timeline.MapWindowToOutput(track.StartSec, track.EndSec, 0);
        window.Should().NotBeNull();
        window!.Value.Start.Should().BeApproximately(2.0, 1e-9);
        window.Value.End.Should().BeApproximately(4.0, 1e-9, "the insert is clipped to the FIRST kept portion");

        IReadOnlyList<InsertQuadFrame> keyframes = VideoCompileStepExecutor.BuildInsertKeyframes(
            track, timeline, media, window.Value, maxKeyframes: 96, overscan: 0);

        keyframes.Should().HaveCount(1, "only the t=2 keyframe survives the cut AND the clipped window");
        keyframes[0].FrameIndex.Should().Be(0, "the surviving keyframe sits exactly at the insert's own start");
        keyframes[0].X0.Should().BeApproximately(0.3 * 1920, 1e-6);
        keyframes[0].Y3.Should().BeApproximately(0.7 * 1080, 1e-6);
    }

    [Fact]
    public void BuildInsertKeyframes_overscan_expands_corners_outward_about_the_centroid()
    {
        var spans = new List<VideoCompileStepExecutor.ResolvedSpan> { new(0, 10, 0, 10, 0, 300) };
        OutputTimeline timeline = OutputTimeline.Build(spans, VideoCompileStepExecutor.BuildHardCutSeamsForFallback(spans.Count));
        var media = new VideoAnalysisMedia(10, 30, 1, 1000, 1000);

        // A centered square quad [0.4..0.6]^2 — centroid (0.5, 0.5).
        VideoInsertRegionTrack track = new(
            "r0", "s0", 2.0, 8.0,
            Keyframes: [new VideoInsertQuadKeyframe(2.0, 0.4, 0.4, 0.6, 0.4, 0.4, 0.6, 0.6, 0.6)],
            Confidence: 0.9, MeanAreaRatio: 0.04, MeanAspectRatio: 1.0, MotionClass: "Static", ColorName: "green");

        IReadOnlyList<InsertQuadFrame> keyframes = VideoCompileStepExecutor.BuildInsertKeyframes(
            track, timeline, media, (2.0, 8.0), maxKeyframes: 96, overscan: 0.10);

        // TL corner: 0.5 + (0.4 - 0.5) * 1.1 = 0.39 -> 390px.
        keyframes[0].X0.Should().BeApproximately(390, 1e-6);
        keyframes[0].Y0.Should().BeApproximately(390, 1e-6);
        // BR corner: 0.5 + (0.6 - 0.5) * 1.1 = 0.61 -> 610px.
        keyframes[0].X3.Should().BeApproximately(610, 1e-6);
    }

    [Fact]
    public async Task Second_insert_beyond_MaxInserts_is_dropped_max_inserts_exceeded()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: new[] { ("s0", 0.0, 10.0) },
            offeredIds: new[] { "s0" });
        artifact = artifact with
        {
            InsertRegions = [BuildInsertTrack("r0", 1.0, 4.0), BuildInsertTrack("r1", 5.0, 9.0)],
            OfferedInsertRegionIds = ["r0", "r1"]
        };

        string decisionJson = BuildDecisionJson(("s0", "s0", "keep"));
        string assetKey = InsertAssetKey();
        string planJson = JsonSerializer.Serialize(new
        {
            overlays = Array.Empty<object>(),
            inserts = new[]
            {
                new { regionId = "r0", renderedAssetStorageKey = assetKey, reason = "a" },
                new { regionId = "r1", renderedAssetStorageKey = assetKey, reason = "b" }
            },
            planRationale = "test"
        });

        StepExecutionContext context = CreateGraphicsContext(
            artifact, decisionJson, planJson,
            out Mock<IProjectFileWorkspace> workspace,
            configOverride: cfg => cfg with { EnableGraphics = false, EnableInserts = true, MaxInserts = 1, PrePaddingMs = 0, PostPaddingMs = 0 });
        MockAssetDownload(workspace, assetKey);

        JsonElement edl = default;
        StepExecutionResult result = await CreateExecutor(workspace, edlCaptured: e => edl = e).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        JsonElement inserts = edl.GetProperty("inserts");
        inserts.GetProperty("appliedInsertCount").GetInt32().Should().Be(1);
        JsonElement dropped = inserts.GetProperty("droppedInserts");
        dropped.GetArrayLength().Should().Be(1);
        dropped[0].GetProperty("regionId").GetString().Should().Be("r1");
        dropped[0].GetProperty("reason").GetString().Should().Be("max_inserts_exceeded");
    }
}
