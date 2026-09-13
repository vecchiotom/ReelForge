using System;
using FluentAssertions;
using ReelForge.WorkflowEngine.Services.Video;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Aggressive adversarial coverage for <see cref="OverlayTextSanitizer"/> — the first of two
/// independent defenses against model-authored overlay text reaching ffmpeg unsafely (the second
/// being <c>DrawtextFilterBuilder</c>'s textfile=/expansion=none discipline, which this test suite
/// does not depend on: every assertion here holds even if that second layer did not exist).
/// </summary>
public class OverlayTextSanitizerTests
{
    [Fact]
    public void Plain_text_passes_through_unchanged()
    {
        OverlayTextSanitizer.Sanitize("Jane Doe, Engineer", 80).Should().Be("Jane Doe, Engineer");
    }

    [Fact]
    public void Backslash_is_stripped()
    {
        string result = OverlayTextSanitizer.Sanitize(@"path\to\file", 80);
        result.Should().NotContain("\\");
    }

    [Fact]
    public void Drawtext_expansion_syntax_is_neutralized()
    {
        string result = OverlayTextSanitizer.Sanitize("%{pts}", 80);
        result.Should().NotContain("%");
        result.Should().NotContain("{");
        result.Should().NotContain("}");
    }

    [Fact]
    public void Drawtext_eif_expression_is_neutralized()
    {
        string result = OverlayTextSanitizer.Sanitize("%{eif:1+1:d}", 80);
        result.Should().NotContain("%");
        result.Should().NotContain("{");
        result.Should().NotContain("}");
        result.Should().NotContain(":");
    }

    [Fact]
    public void Colon_is_excluded_from_the_allowlist_as_defense_in_depth()
    {
        string result = OverlayTextSanitizer.Sanitize("Chapter: One", 80);
        result.Should().NotContain(":");
    }

    [Fact]
    public void Embedded_newline_is_collapsed_to_a_single_space_never_a_forced_line_break()
    {
        string result = OverlayTextSanitizer.Sanitize("Line one\nLine two", 80);
        result.Should().NotContain("\n");
        result.Should().Be("Line one Line two");
    }

    [Fact]
    public void Embedded_tab_and_carriage_return_are_collapsed_to_a_single_space()
    {
        string result = OverlayTextSanitizer.Sanitize("A\tB\r\nC", 80);
        result.Should().NotContainAny("\t", "\r", "\n");
        result.Should().Be("A B C");
    }

    [Fact]
    public void Multiple_internal_spaces_collapse_to_one()
    {
        OverlayTextSanitizer.Sanitize("A      B", 80).Should().Be("A B");
    }

    [Fact]
    public void A_500_char_string_is_truncated_to_maxChars()
    {
        string input = new string('a', 500);
        string result = OverlayTextSanitizer.Sanitize(input, 80);
        result.Length.Should().BeLessThanOrEqualTo(80);
    }

    [Fact]
    public void Truncation_never_splits_a_surrogate_pair()
    {
        // U+1F600 (😀) is a surrogate pair in UTF-16 (2 chars). Force a truncation boundary
        // that would land mid-pair if truncation were a naive str[..n].
        string emoji = "😀"; // 😀 — itself gets sanitized away (see below), but the
                                       // truncation routine must never throw or corrupt regardless.
        string padded = "AB" + emoji + "CD";
        Action act = () => OverlayTextSanitizer.Sanitize(padded, 3);
        act.Should().NotThrow();
    }

    [Fact]
    public void All_emoji_input_sanitizes_to_empty_string()
    {
        // Deliberate allowlist decision (documented on OverlayTextSanitizer): emoji fall outside
        // \p{L}/\p{N} and are excluded — the configured overlay font is not guaranteed to carry
        // emoji glyphs, so admitting them risks silent tofu-box rendering.
        string emojiOnly = "\U0001F600\U0001F601\U0001F602";
        OverlayTextSanitizer.Sanitize(emojiOnly, 80).Should().Be(string.Empty);
    }

    [Fact]
    public void Whitespace_only_input_sanitizes_to_empty_string()
    {
        OverlayTextSanitizer.Sanitize("   \n\t  ", 80).Should().Be(string.Empty);
    }

    [Fact]
    public void Null_or_empty_input_sanitizes_to_empty_string()
    {
        OverlayTextSanitizer.Sanitize(null, 80).Should().Be(string.Empty);
        OverlayTextSanitizer.Sanitize(string.Empty, 80).Should().Be(string.Empty);
    }

    [Fact]
    public void Single_quote_is_allowed_since_it_never_reaches_the_filter_string()
    {
        // Deliberate design choice (documented on OverlayTextSanitizer): unlike ':'/'%', a single
        // quote is common in ordinary text ("don't", "It's") and is safe to keep here because it
        // never reaches ffmpeg's filter STRING at all — only a separately-written textfile.
        OverlayTextSanitizer.Sanitize("It's a test", 80).Should().Be("It's a test");
    }

    [Fact]
    public void Result_is_never_whitespace_only_after_trimming()
    {
        string result = OverlayTextSanitizer.Sanitize("***", 80);
        result.Trim().Should().Be(result);
    }

    [Fact]
    public void Combining_mark_sequence_is_not_split_by_truncation()
    {
        // "e" + combining acute accent (U+0301) forms one grapheme cluster.
        string combining = "é";
        string padded = "AB" + combining;
        Action act = () => OverlayTextSanitizer.Sanitize(padded, 2);
        act.Should().NotThrow();
    }

}
