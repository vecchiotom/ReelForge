using System.Collections.Generic;
using FluentAssertions;
using ReelForge.WorkflowEngine.Agents.Tools;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Exhaustive coverage for <see cref="SandboxTextEditor"/> — the pure find/replace engine behind
/// the <c>EditSandboxFile</c>/<c>ApplySandboxFileEdits</c> tools. Since the class has no I/O of its
/// own, every rule (ambiguity, all-or-nothing sequencing, line-ending normalization, the ordinal
/// matching discipline) is testable directly against plain strings without mocking the sandbox
/// HTTP API.
/// </summary>
public class SandboxTextEditorTests
{
    [Fact]
    public void Single_unambiguous_replace_succeeds()
    {
        SandboxEditOutcome outcome = SandboxTextEditor.Apply(
            "const x = 1;\nconst y = 2;\n",
            [new SandboxEdit("const x = 1;", "const x = 42;")]);

        outcome.Ok.Should().BeTrue();
        outcome.Content.Should().Be("const x = 42;\nconst y = 2;\n");
        outcome.Replacements.Should().Be(1);
        outcome.EditsApplied.Should().Be(1);
        outcome.Error.Should().BeNull();
    }

    [Fact]
    public void Not_found_fails_with_explanatory_error_and_no_content()
    {
        SandboxEditOutcome outcome = SandboxTextEditor.Apply(
            "const x = 1;\n",
            [new SandboxEdit("const z = 99;", "const z = 100;")]);

        outcome.Ok.Should().BeFalse();
        outcome.Content.Should().BeNull();
        outcome.Error.Should().Contain("edit 1").And.Contain("not found");
    }

    [Fact]
    public void Ambiguous_match_without_replaceAll_fails_with_occurrence_count()
    {
        SandboxEditOutcome outcome = SandboxTextEditor.Apply(
            "foo\nfoo\nfoo\n",
            [new SandboxEdit("foo", "bar")]);

        outcome.Ok.Should().BeFalse();
        outcome.Content.Should().BeNull();
        outcome.Error.Should().Contain("edit 1").And.Contain("matched 3 times");
    }

    [Fact]
    public void Ambiguous_match_with_replaceAll_replaces_every_occurrence_and_reports_correct_count()
    {
        SandboxEditOutcome outcome = SandboxTextEditor.Apply(
            "foo\nfoo\nfoo\n",
            [new SandboxEdit("foo", "bar", ReplaceAll: true)]);

        outcome.Ok.Should().BeTrue();
        outcome.Content.Should().Be("bar\nbar\nbar\n");
        outcome.Replacements.Should().Be(3);
    }

    [Fact]
    public void Sequential_edits_apply_each_against_the_result_of_the_previous_one()
    {
        // Edit 2 targets text that only exists AFTER edit 1 has run — proves edits are applied
        // in order against a running buffer, not all matched against the original text.
        SandboxEditOutcome outcome = SandboxTextEditor.Apply(
            "const x = 1;\n",
            [
                new SandboxEdit("const x = 1;", "const x = PLACEHOLDER;"),
                new SandboxEdit("PLACEHOLDER", "42")
            ]);

        outcome.Ok.Should().BeTrue();
        outcome.Content.Should().Be("const x = 42;\n");
        outcome.EditsApplied.Should().Be(2);
        outcome.Replacements.Should().Be(2);
    }

    [Fact]
    public void All_or_nothing_a_later_failing_edit_leaves_the_original_untouched()
    {
        SandboxEditOutcome outcome = SandboxTextEditor.Apply(
            "const x = 1;\nconst y = 2;\n",
            [
                new SandboxEdit("const x = 1;", "const x = 42;"),
                new SandboxEdit("this text does not exist anywhere", "irrelevant")
            ]);

        outcome.Ok.Should().BeFalse();
        outcome.Content.Should().BeNull("a partially-applied multi-edit must never be reported as content to write back");
        outcome.Error.Should().Contain("edit 2");
    }

    [Fact]
    public void Empty_oldText_fails_without_touching_content()
    {
        SandboxEditOutcome outcome = SandboxTextEditor.Apply(
            "const x = 1;\n",
            [new SandboxEdit(string.Empty, "anything")]);

        outcome.Ok.Should().BeFalse();
        outcome.Content.Should().BeNull();
        outcome.Error.Should().Contain("oldText must not be empty");
    }

    [Fact]
    public void Identical_old_and_new_text_fails_as_a_no_op()
    {
        SandboxEditOutcome outcome = SandboxTextEditor.Apply(
            "const x = 1;\n",
            [new SandboxEdit("const x = 1;", "const x = 1;")]);

        outcome.Ok.Should().BeFalse();
        outcome.Content.Should().BeNull();
        outcome.Error.Should().Contain("identical").And.Contain("nothing to change");
    }

    [Fact]
    public void Crlf_oldText_matches_against_an_lf_only_file()
    {
        string lfFile = "line1\nline2\nline3\n";
        SandboxEditOutcome outcome = SandboxTextEditor.Apply(
            lfFile,
            [new SandboxEdit("line1\r\nline2", "REPLACED")]);

        outcome.Ok.Should().BeTrue();
        outcome.Content.Should().Be("REPLACED\nline3\n");
    }

    [Fact]
    public void Lf_oldText_matches_against_a_crlf_only_file()
    {
        string crlfFile = "line1\r\nline2\r\nline3\r\n";
        SandboxEditOutcome outcome = SandboxTextEditor.Apply(
            crlfFile,
            [new SandboxEdit("line1\nline2", "REPLACED")]);

        outcome.Ok.Should().BeTrue();
        outcome.Content.Should().Be("REPLACED\r\nline3\r\n");
    }

    [Fact]
    public void More_than_fifty_edits_are_rejected()
    {
        List<SandboxEdit> edits = new();
        for (int i = 0; i < 51; i++)
            edits.Add(new SandboxEdit($"needle{i}", $"replacement{i}"));

        SandboxEditOutcome outcome = SandboxTextEditor.Apply("irrelevant content", edits);

        outcome.Ok.Should().BeFalse();
        outcome.Content.Should().BeNull();
        outcome.Error.Should().Contain("too many edits");
    }

    [Fact]
    public void Exactly_fifty_edits_is_allowed()
    {
        // Build a file with fifty distinct, unique markers so all fifty edits legitimately apply.
        // Delimited on both sides (MARK{i}END) so e.g. "MARK1END" is never a substring of
        // "MARK10END" — a plain "marker{i}" prefix would collide (marker1 inside marker10..19).
        List<string> markerLines = new();
        List<SandboxEdit> edits = new();
        for (int i = 0; i < 50; i++)
        {
            markerLines.Add($"MARK{i}END");
            edits.Add(new SandboxEdit($"MARK{i}END", $"REPLACED{i}"));
        }

        string content = string.Join("\n", markerLines);
        SandboxEditOutcome outcome = SandboxTextEditor.Apply(content, edits);

        outcome.Ok.Should().BeTrue();
        outcome.EditsApplied.Should().Be(50);
    }

    [Fact]
    public void OldText_containing_regex_metacharacters_is_matched_literally_not_as_a_regex()
    {
        // ".*" and "$1" are meaningful regex metacharacters/backreferences; a regex-based
        // implementation would either fail to match this literal text or match something else
        // entirely. Ordinal string matching treats them as plain characters.
        string content = "pattern = /.*$1/;\nother = 1;\n";
        SandboxEditOutcome outcome = SandboxTextEditor.Apply(
            content,
            [new SandboxEdit("/.*$1/", "/literal/")]);

        outcome.Ok.Should().BeTrue();
        outcome.Content.Should().Be("pattern = /literal/;\nother = 1;\n");
    }

    [Fact]
    public void Unrelated_text_that_merely_resembles_a_regex_pattern_does_not_falsely_match()
    {
        // If matching were regex-based, the pattern "a.c" would also match "abc"/"axc" etc. Ordinal
        // matching must NOT find a match for a literal "a.c" snippet against text that only
        // contains regex-lookalike near-misses.
        SandboxEditOutcome outcome = SandboxTextEditor.Apply(
            "abc\naxc\n",
            [new SandboxEdit("a.c", "REPLACED")]);

        outcome.Ok.Should().BeFalse();
        outcome.Error.Should().Contain("not found");
    }

    [Fact]
    public void Null_or_empty_edit_list_fails_cleanly()
    {
        SandboxTextEditor.Apply("content", []).Ok.Should().BeFalse();
        SandboxTextEditor.Apply("content", null!).Ok.Should().BeFalse();
    }
}
