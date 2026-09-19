using System.Linq;
using System.Text.Json;
using FluentAssertions;
using ReelForge.WorkflowEngine.Execution.StepExecutors;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Covers <see cref="RobustJsonExtractor"/> directly — pure string-scanning logic with no
/// dependency on a live model, worth exercising against representative near-miss inputs rather
/// than only indirectly through the executors that use it.
/// </summary>
public class RobustJsonExtractorTests
{
    [Fact]
    public void ExtractJsonObject_strips_leading_and_trailing_prose()
    {
        string raw = "Sure, here you go:\n{\"a\": 1}\nHope that helps!";

        RobustJsonExtractor.ExtractJsonObject(raw).Should().Be("{\"a\": 1}");
    }

    [Fact]
    public void ExtractJsonObject_returns_null_when_unbalanced()
    {
        RobustJsonExtractor.ExtractJsonObject("{\"a\": {\"b\": 1}").Should().BeNull();
    }

    [Fact]
    public void ExtractJsonObject_returns_null_when_no_brace_present()
    {
        RobustJsonExtractor.ExtractJsonObject("no json here").Should().BeNull();
    }

    [Fact]
    public void ExtractLastJsonObject_skips_incidental_early_braces_from_chain_of_thought()
    {
        // Regression test: found live producing a real promo video. VideoStoryEditor's own
        // reasoning used small, complete, informal brace-pair shorthand ("{s0,s0}") for candidate
        // spans thousands of characters before the real final decision — ExtractJsonObject's
        // first-match strategy would latch onto that instead of the real answer.
        string raw =
            "Let me think about spans: {s0,s0} and {s1,s1}. Hmm, two single-shot spans. " +
            "After weighing it, here is my decision:\n{\"keep\":[{\"fromId\":\"s0\",\"toId\":\"s0\",\"reason\":\"open\"}],\"editRationale\":\"test\",\"suggestedTitle\":\"Title\"}";

        string? extracted = RobustJsonExtractor.ExtractLastJsonObject(raw);

        extracted.Should().NotBeNull();
        JsonDocument doc = JsonDocument.Parse(extracted!);
        doc.RootElement.GetProperty("suggestedTitle").GetString().Should().Be("Title");
        doc.RootElement.GetProperty("keep").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void ExtractLastJsonObject_strips_leading_and_trailing_prose_with_only_one_object()
    {
        string raw = "Sure, here you go:\n{\"a\": 1}\nHope that helps!";

        RobustJsonExtractor.ExtractLastJsonObject(raw).Should().Be("{\"a\": 1}");
    }

    [Fact]
    public void ExtractLastJsonObject_returns_null_when_no_brace_present()
    {
        RobustJsonExtractor.ExtractLastJsonObject("no json here").Should().BeNull();
    }

    [Fact]
    public void ExtractLastJsonObject_returns_null_when_unbalanced()
    {
        RobustJsonExtractor.ExtractLastJsonObject("{\"a\": {\"b\": 1}").Should().BeNull();
    }

    [Fact]
    public void ExtractLastJsonObject_ignores_string_literal_braces_when_finding_the_last_object()
    {
        string raw = "{\"skip\": \"a { fake } brace\"} then more talk {\"real\": true}";

        string? extracted = RobustJsonExtractor.ExtractLastJsonObject(raw);

        extracted.Should().NotBeNull();
        JsonDocument doc = JsonDocument.Parse(extracted!);
        doc.RootElement.GetProperty("real").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void NormalizeQuotedStrings_converts_a_simple_python_dict_literal_to_strict_json()
    {
        string raw = "{'summary': 'a quiet room', 'tags': ['calm', 'indoor']}";

        string? normalized = RobustJsonExtractor.NormalizeQuotedStrings(raw);

        normalized.Should().NotBeNull();
        JsonDocument doc = JsonDocument.Parse(normalized!);
        doc.RootElement.GetProperty("summary").GetString().Should().Be("a quiet room");
        doc.RootElement.GetProperty("tags").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("calm", "indoor");
    }

    [Fact]
    public void NormalizeQuotedStrings_handles_an_apostrophe_inside_a_single_quoted_string()
    {
        // A bare apostrophe used as punctuation, not as the string's own delimiter, must survive.
        string raw = @"{'summary': 'it\'s a bright day'}";

        string? normalized = RobustJsonExtractor.NormalizeQuotedStrings(raw);

        normalized.Should().NotBeNull();
        JsonDocument doc = JsonDocument.Parse(normalized!);
        doc.RootElement.GetProperty("summary").GetString().Should().Be("it's a bright day");
    }

    [Fact]
    public void NormalizeQuotedStrings_escapes_a_bare_double_quote_found_inside_a_single_quoted_string()
    {
        string raw = @"{'onScreenText': 'the sign says ""EXIT""'}";

        string? normalized = RobustJsonExtractor.NormalizeQuotedStrings(raw);

        normalized.Should().NotBeNull();
        JsonDocument doc = JsonDocument.Parse(normalized!);
        doc.RootElement.GetProperty("onScreenText").GetString().Should().Be(@"the sign says ""EXIT""");
    }

    [Fact]
    public void NormalizeQuotedStrings_leaves_already_strict_json_unchanged_in_meaning()
    {
        string raw = "{\"summary\": \"fine already\"}";

        string? normalized = RobustJsonExtractor.NormalizeQuotedStrings(raw);

        normalized.Should().NotBeNull();
        JsonDocument doc = JsonDocument.Parse(normalized!);
        doc.RootElement.GetProperty("summary").GetString().Should().Be("fine already");
    }

    [Fact]
    public void NormalizeQuotedStrings_returns_null_for_an_unterminated_string()
    {
        RobustJsonExtractor.NormalizeQuotedStrings("{'summary': 'never closed").Should().BeNull();
    }

    [Fact]
    public void NormalizeQuotedStrings_preserves_non_string_json_escapes_like_newline()
    {
        string raw = @"{'summary': 'line one\nline two'}";

        string? normalized = RobustJsonExtractor.NormalizeQuotedStrings(raw);

        normalized.Should().NotBeNull();
        JsonDocument doc = JsonDocument.Parse(normalized!);
        doc.RootElement.GetProperty("summary").GetString().Should().Be("line one\nline two");
    }

    [Fact]
    public void CollapseDuplicateLeadingBrace_repairs_a_doubled_opening_brace_with_only_one_close()
    {
        string raw = "{{\"shotId\": \"shot-001\", \"summary\": \"a room\"}";

        string collapsed = RobustJsonExtractor.CollapseDuplicateLeadingBrace(raw);

        JsonDocument doc = JsonDocument.Parse(collapsed);
        doc.RootElement.GetProperty("shotId").GetString().Should().Be("shot-001");
    }

    [Fact]
    public void CollapseDuplicateLeadingBrace_is_a_no_op_for_already_well_formed_json()
    {
        string raw = "{\"shotId\": \"shot-001\"}";

        RobustJsonExtractor.CollapseDuplicateLeadingBrace(raw).Should().Be(raw);
    }

    [Fact]
    public void CollapseDuplicateLeadingBrace_tolerates_leading_whitespace_before_the_braces()
    {
        string raw = "  \n  {{\"shotId\": \"shot-001\"}";

        string collapsed = RobustJsonExtractor.CollapseDuplicateLeadingBrace(raw);

        JsonDocument doc = JsonDocument.Parse(collapsed);
        doc.RootElement.GetProperty("shotId").GetString().Should().Be("shot-001");
    }

    [Fact]
    public void CollapseDuplicateLeadingBrace_is_a_no_op_when_there_is_no_leading_brace_at_all()
    {
        string raw = "not json at all";

        RobustJsonExtractor.CollapseDuplicateLeadingBrace(raw).Should().Be(raw);
    }
}
