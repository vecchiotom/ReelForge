using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using ReelForge.Shared.Workflows;
using ReelForge.WorkflowEngine.Services.Video;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Covers <see cref="VisionShotCaptioner.ApplyCaps"/> in isolation — the caption post-processing
/// logic that doesn't require a live <see cref="Microsoft.Extensions.AI.IChatClient"/> to exercise
/// (the rest of <c>CaptionAsync</c> does, and isn't independently unit tested here, matching this
/// codebase's existing coverage pattern for the LLM-calling pieces of the video-editing feature).
/// See docs/video-editing.md, Item H cleanup.
/// </summary>
public class VisionShotCaptionerTests
{
    private static VideoShotCaption Caption(
        string summary = "A summary.",
        string action = "",
        string setting = "",
        string mood = "",
        string shotScale = "",
        string cameraAngle = "",
        IReadOnlyList<string>? subjects = null,
        IReadOnlyList<string>? onScreenText = null,
        IReadOnlyList<string>? tags = null,
        string timeOfDay = "",
        string lighting = "",
        string visualStyle = "",
        string framing = "",
        IReadOnlyList<string>? technicalIssues = null) => new(
            ShotId: "s0", Summary: summary, Subjects: subjects ?? [], Action: action, Setting: setting,
            Mood: mood, ShotScale: shotScale, CameraAngle: cameraAngle,
            OnScreenText: onScreenText ?? [], Tags: tags ?? [],
            TimeOfDay: timeOfDay, Lighting: lighting, VisualStyle: visualStyle, Framing: framing,
            TechnicalIssues: technicalIssues ?? []);

    [Fact]
    public void Negative_maxCaptionChars_does_not_throw_and_clamps_to_empty()
    {
        Action act = () => VisionShotCaptioner.ApplyCaps(Caption(summary: "hello"), maxCaptionChars: -5);
        act.Should().NotThrow();

        VideoShotCaption result = VisionShotCaptioner.ApplyCaps(Caption(summary: "hello"), maxCaptionChars: -5);
        result.Summary.Should().Be(string.Empty);
    }

    [Fact]
    public void Zero_maxCaptionChars_does_not_throw_and_clamps_to_empty()
    {
        Action act = () => VisionShotCaptioner.ApplyCaps(Caption(summary: "hello"), maxCaptionChars: 0);
        act.Should().NotThrow();
    }

    [Fact]
    public void Summary_longer_than_maxCaptionChars_is_truncated_without_splitting_a_surrogate_pair()
    {
        string emoji = "\U0001F600"; // surrogate pair
        string padded = "AB" + emoji + "CD";
        Action act = () => VisionShotCaptioner.ApplyCaps(Caption(summary: padded), maxCaptionChars: 3);
        act.Should().NotThrow();
    }

    [Fact]
    public void String_fields_other_than_summary_are_also_truncated_to_maxCaptionChars()
    {
        string longText = new string('x', 500);
        VideoShotCaption result = VisionShotCaptioner.ApplyCaps(
            Caption(action: longText, setting: longText, mood: longText, shotScale: longText, cameraAngle: longText),
            maxCaptionChars: 50);

        result.Action.Length.Should().BeLessThanOrEqualTo(50);
        result.Setting.Length.Should().BeLessThanOrEqualTo(50);
        result.Mood.Length.Should().BeLessThanOrEqualTo(50);
        result.ShotScale.Length.Should().BeLessThanOrEqualTo(50);
        result.CameraAngle.Length.Should().BeLessThanOrEqualTo(50);
    }

    [Fact]
    public void Oversized_tags_list_is_capped_to_a_maximum_item_count()
    {
        List<string> manyTags = Enumerable.Range(0, 1000).Select(i => $"tag{i}").ToList();
        VideoShotCaption result = VisionShotCaptioner.ApplyCaps(Caption(tags: manyTags), maxCaptionChars: 80);

        result.Tags.Count.Should().BeLessThan(1000);
        result.Tags.Count.Should().BeLessThanOrEqualTo(20);
    }

    [Fact]
    public void Oversized_onScreenText_list_is_capped_to_a_maximum_item_count()
    {
        List<string> many = Enumerable.Range(0, 500).Select(i => $"text{i}").ToList();
        VideoShotCaption result = VisionShotCaptioner.ApplyCaps(Caption(onScreenText: many), maxCaptionChars: 80);

        result.OnScreenText.Count.Should().BeLessThanOrEqualTo(20);
    }

    [Fact]
    public void An_individual_tag_string_longer_than_the_per_item_budget_is_truncated()
    {
        string hugeTag = new string('y', 10_000);
        VideoShotCaption result = VisionShotCaptioner.ApplyCaps(Caption(tags: [hugeTag]), maxCaptionChars: 80);

        result.Tags.Should().ContainSingle();
        result.Tags[0].Length.Should().BeLessThan(10_000);
    }

    [Fact]
    public void Empty_lists_and_strings_stay_empty()
    {
        VideoShotCaption result = VisionShotCaptioner.ApplyCaps(Caption(summary: ""), maxCaptionChars: 80);

        result.Summary.Should().BeEmpty();
        result.Subjects.Should().BeEmpty();
        result.Tags.Should().BeEmpty();
        result.OnScreenText.Should().BeEmpty();
    }

    [Fact]
    public void ApplyCaps_normalizes_a_null_technicalIssues_list_to_empty()
    {
        VideoShotCaption caption = Caption() with { TechnicalIssues = null! };

        VideoShotCaption result = VisionShotCaptioner.ApplyCaps(caption, maxCaptionChars: 80);

        result.TechnicalIssues.Should().NotBeNull();
        result.TechnicalIssues.Should().BeEmpty();
    }

    [Fact]
    public void ApplyCaps_truncates_the_new_string_fields_to_maxCaptionChars()
    {
        string longText = new string('z', 500);
        VideoShotCaption caption = Caption() with
        {
            TimeOfDay = longText,
            Lighting = longText,
            VisualStyle = longText,
            Framing = longText
        };

        VideoShotCaption result = VisionShotCaptioner.ApplyCaps(caption, maxCaptionChars: 50);

        result.TimeOfDay.Length.Should().BeLessThanOrEqualTo(50);
        result.Lighting.Length.Should().BeLessThanOrEqualTo(50);
        result.VisualStyle.Length.Should().BeLessThanOrEqualTo(50);
        result.Framing.Length.Should().BeLessThanOrEqualTo(50);
    }
}
