using FluentAssertions;
using ReelForge.WorkflowEngine.Execution.StepExecutors;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// <see cref="ReviewLoopStepExecutor.ParseReviewScore"/>/<see cref="ReviewLoopStepExecutor.ExtractFeedbackSummary"/>
/// — the two pure-JSON-parsing helpers the review loop (both the main promo pipeline's
/// AgentType.ReviewAgent and the video-editing templates' AgentType.VideoReviewAgent) shares.
/// </summary>
public class ReviewLoopStepExecutorTests
{
    [Fact]
    public void ParseReviewScore_reads_the_canonical_score_property()
    {
        // VideoReviewOutput.Score serializes as "score" — the property this method checks first.
        ReviewLoopStepExecutor.ParseReviewScore("""{"score":8,"passesReview":true,"summary":"ok"}""")
            .Should().Be(8);
    }

    [Fact]
    public void ParseReviewScore_falls_back_to_overallScore_for_the_main_pipelines_ReviewOutput_shape()
    {
        // Regression test for a real pre-existing bug: ReviewOutput.OverallScore serializes as
        // "overallScore", not "score" — this method previously only ever checked "score", so the
        // main pipeline's review score silently always parsed as 0 regardless of what the review
        // agent actually judged (every ReviewLoop step always looped until MaxIterations).
        ReviewLoopStepExecutor.ParseReviewScore("""{"overallScore":9,"passesReview":true,"summary":"ok"}""")
            .Should().Be(9);
    }

    [Fact]
    public void ParseReviewScore_prefers_score_over_overallScore_when_both_are_somehow_present()
    {
        ReviewLoopStepExecutor.ParseReviewScore("""{"score":3,"overallScore":9}""").Should().Be(3);
    }

    [Fact]
    public void ParseReviewScore_returns_zero_for_malformed_json()
    {
        ReviewLoopStepExecutor.ParseReviewScore("not json at all").Should().Be(0);
    }

    [Fact]
    public void ParseReviewScore_returns_zero_when_neither_property_is_present()
    {
        ReviewLoopStepExecutor.ParseReviewScore("""{"passesReview":false}""").Should().Be(0);
    }

    [Fact]
    public void ExtractFeedbackSummary_combines_summary_and_video_review_issues()
    {
        string? feedback = ReviewLoopStepExecutor.ExtractFeedbackSummary(
            """{"score":4,"passesReview":false,"issues":["ends mid-sentence","overlay p0 covers 30% of the frame"],"summary":"Cut ends mid-sentence."}""");

        feedback.Should().NotBeNull();
        feedback.Should().Contain("Cut ends mid-sentence.");
        feedback.Should().Contain("ends mid-sentence");
        feedback.Should().Contain("overlay p0 covers 30% of the frame");
    }

    [Fact]
    public void ExtractFeedbackSummary_combines_summary_and_main_pipelines_improvementAreas()
    {
        // Schema-tolerant across both review output shapes, since both flow through the same
        // ReviewLoopStepExecutor.
        string? feedback = ReviewLoopStepExecutor.ExtractFeedbackSummary(
            """{"overallScore":5,"passesReview":false,"improvementAreas":["missing captions"],"summary":"Needs work."}""");

        feedback.Should().NotBeNull();
        feedback.Should().Contain("Needs work.");
        feedback.Should().Contain("missing captions");
    }

    [Fact]
    public void ExtractFeedbackSummary_returns_null_for_malformed_json()
    {
        ReviewLoopStepExecutor.ExtractFeedbackSummary("not json").Should().BeNull();
    }

    [Fact]
    public void ExtractFeedbackSummary_returns_null_when_nothing_usable_is_present()
    {
        ReviewLoopStepExecutor.ExtractFeedbackSummary("""{"score":9,"passesReview":true}""").Should().BeNull();
    }

    [Fact]
    public void ExtractFeedbackSummary_ignores_empty_or_whitespace_only_issue_strings()
    {
        string? feedback = ReviewLoopStepExecutor.ExtractFeedbackSummary(
            """{"score":4,"issues":["  ","real issue"],"summary":""}""");

        feedback.Should().NotBeNull();
        feedback.Should().Contain("real issue");
    }

    // ---- Bug group B: AgentType.VideoReviewAgent has tools bound (same as the story editor), so
    // its raw completion can carry trailing prose/markdown/leaked tool-call-closing tokens after a
    // perfectly valid JSON object — the exact failure mode RobustJsonExtractor.ExtractJsonObject
    // was written for. A bare JsonDocument.Parse over the whole string previously threw, the
    // catch (JsonException) swallowed it, and the score silently came back 0 (looping every run
    // regardless of the model's actual verdict) with no feedback extracted at all. ----

    [Fact]
    public void ParseReviewScore_tolerates_trailing_prose_after_the_json_object()
    {
        string raw = """{"score":8,"passesReview":true,"summary":"ok"}""" +
            "\n\nI have completed the review. </invoke></tool_call>";

        ReviewLoopStepExecutor.ParseReviewScore(raw).Should().Be(8);
    }

    [Fact]
    public void ExtractFeedbackSummary_tolerates_trailing_prose_after_the_json_object()
    {
        string raw = """{"score":4,"passesReview":false,"issues":["ends mid-sentence"],"summary":"Cut ends mid-sentence."}""" +
            "\n\n```\nDone.\n```";

        string? feedback = ReviewLoopStepExecutor.ExtractFeedbackSummary(raw);

        feedback.Should().NotBeNull();
        feedback.Should().Contain("Cut ends mid-sentence.");
        feedback.Should().Contain("ends mid-sentence");
    }

    // ---- Bug group B: a non-integer JSON number (e.g. 8.5 — plausible output for a "score 1-10"
    // prompt) previously crashed ParseReviewScore uncaught: JsonElement.GetInt32() throws
    // FormatException for a non-integer number, which the surrounding catch (JsonException) does
    // not cover. ----

    [Fact]
    public void ParseReviewScore_rounds_a_non_integer_score_instead_of_throwing()
    {
        ReviewLoopStepExecutor.ParseReviewScore("""{"score":8.5,"passesReview":true}""")
            .Should().Be(9);
    }

    [Fact]
    public void ParseReviewScore_rounds_a_non_integer_overallScore_instead_of_throwing()
    {
        ReviewLoopStepExecutor.ParseReviewScore("""{"overallScore":6.2}""")
            .Should().Be(6);
    }
}
