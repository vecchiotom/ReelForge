using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using ReelForge.Shared.Workflows;
using ReelForge.WorkflowEngine.Services.Video;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Tests for <see cref="KeyframeSelector"/> — pure, no ffmpeg/no I/O (Phase 2, see
/// docs/video-editing.md "Vision captioning").
/// </summary>
public class KeyframeSelectorTests
{
    // ---------------------------------------------------------------------
    // ChooseKeyframeSec
    // ---------------------------------------------------------------------

    [Fact]
    public void ChooseKeyframeSec_picks_the_midpoint_of_the_longest_still_window_when_multiple_exist()
    {
        VideoAnalysisShotVisual visual = MakeVisual(stillWindows:
        [
            new VideoAnalysisStillWindow(1.0, 1.5, 0.0),   // 0.5s
            new VideoAnalysisStillWindow(3.0, 5.0, 0.0),   // 2.0s <- longest
            new VideoAnalysisStillWindow(6.0, 6.8, 0.0)    // 0.8s
        ]);
        VideoAnalysisShot shot = new("s1", 0.0, 10.0, Visual: visual);

        double result = KeyframeSelector.ChooseKeyframeSec(shot);

        result.Should().Be(4.0); // midpoint of [3.0, 5.0)
    }

    [Fact]
    public void ChooseKeyframeSec_falls_back_to_shot_midpoint_with_no_still_windows()
    {
        VideoAnalysisShotVisual visual = MakeVisual(stillWindows: []);
        VideoAnalysisShot shot = new("s1", 2.0, 8.0, Visual: visual);

        double result = KeyframeSelector.ChooseKeyframeSec(shot);

        result.Should().Be(5.0); // midpoint of [2.0, 8.0)
    }

    [Fact]
    public void ChooseKeyframeSec_falls_back_to_shot_midpoint_when_visual_is_null()
    {
        VideoAnalysisShot shot = new("s1", 4.0, 6.0, Visual: null);

        double result = KeyframeSelector.ChooseKeyframeSec(shot);

        result.Should().Be(5.0);
    }

    // ---------------------------------------------------------------------
    // SelectShotsToCaption — PerDuplicateGroup
    // ---------------------------------------------------------------------

    [Fact]
    public void PerDuplicateGroup_picks_each_groups_best_take_before_filling_with_longest_remaining()
    {
        var shots = new List<VideoAnalysisShot>
        {
            new("s0", 0, 2),    // group d1 best take (2s)
            new("s1", 2, 3),    // group d1 other take (1s)
            new("s2", 3, 4),    // ungrouped, short (1s)
            new("s3", 4, 9),    // ungrouped, long (5s)
            new("s4", 9, 10.5)  // ungrouped, medium (1.5s)
        };
        var groups = new List<VideoAnalysisDuplicateGroup>
        {
            new("d1", ["s0", "s1"], BestShotId: "s0", MeanSimilarity: 0.95)
        };

        IReadOnlyList<string> selected = KeyframeSelector.SelectShotsToCaption(
            shots, groups, VideoCaptionSelection.PerDuplicateGroup, maxCaptionedShots: 3, minCaptionShotSeconds: 0.0);

        // Group's best take (s0) always wins over its sibling (s1), then remaining budget (2 more)
        // goes to the longest not-yet-selected shots: s3 (5s), s4 (1.5s) beats s2 (1s).
        selected.Should().Equal("s0", "s3", "s4"); // chronological order
    }

    [Fact]
    public void PerDuplicateGroup_respects_minCaptionShotSeconds()
    {
        var shots = new List<VideoAnalysisShot>
        {
            new("s0", 0, 0.5),  // too short (0.5s)
            new("s1", 0.5, 3.0) // eligible (2.5s)
        };

        IReadOnlyList<string> selected = KeyframeSelector.SelectShotsToCaption(
            shots, duplicateGroups: null, VideoCaptionSelection.PerDuplicateGroup,
            maxCaptionedShots: 10, minCaptionShotSeconds: 1.0);

        selected.Should().Equal("s1");
    }

    [Fact]
    public void PerDuplicateGroup_respects_maxCaptionedShots_budget()
    {
        var shots = Enumerable.Range(0, 10)
            .Select(i => new VideoAnalysisShot($"s{i}", i, i + 1))
            .ToList();

        IReadOnlyList<string> selected = KeyframeSelector.SelectShotsToCaption(
            shots, duplicateGroups: null, VideoCaptionSelection.PerDuplicateGroup,
            maxCaptionedShots: 3, minCaptionShotSeconds: 0.0);

        selected.Should().HaveCount(3);
    }

    [Fact]
    public void PerDuplicateGroup_ignores_a_group_whose_best_shot_id_is_unknown_or_ineligible()
    {
        var shots = new List<VideoAnalysisShot>
        {
            new("s0", 0, 1),
            new("s1", 1, 4)
        };
        var groups = new List<VideoAnalysisDuplicateGroup>
        {
            new("d1", ["s99"], BestShotId: "s99", MeanSimilarity: 0.9) // s99 doesn't exist
        };

        IReadOnlyList<string> selected = KeyframeSelector.SelectShotsToCaption(
            shots, groups, VideoCaptionSelection.PerDuplicateGroup, maxCaptionedShots: 10, minCaptionShotSeconds: 0.0);

        selected.Should().Equal("s0", "s1");
    }

    // ---------------------------------------------------------------------
    // SelectShotsToCaption — LongestShots / EvenlySpaced
    // ---------------------------------------------------------------------

    [Fact]
    public void LongestShots_selects_the_N_longest_eligible_shots_in_chronological_order()
    {
        var shots = new List<VideoAnalysisShot>
        {
            new("s0", 0, 1),   // 1s
            new("s1", 1, 6),   // 5s <- longest
            new("s2", 6, 7),   // 1s
            new("s3", 7, 10)   // 3s <- 2nd longest
        };

        IReadOnlyList<string> selected = KeyframeSelector.SelectShotsToCaption(
            shots, duplicateGroups: null, VideoCaptionSelection.LongestShots, maxCaptionedShots: 2, minCaptionShotSeconds: 0.0);

        selected.Should().Equal("s1", "s3"); // chronological, not by length
    }

    [Fact]
    public void LongestShots_excludes_shots_shorter_than_minCaptionShotSeconds()
    {
        var shots = new List<VideoAnalysisShot>
        {
            new("s0", 0, 0.2),
            new("s1", 0.2, 5.0)
        };

        IReadOnlyList<string> selected = KeyframeSelector.SelectShotsToCaption(
            shots, duplicateGroups: null, VideoCaptionSelection.LongestShots, maxCaptionedShots: 10, minCaptionShotSeconds: 1.0);

        selected.Should().Equal("s1");
    }

    [Fact]
    public void EvenlySpaced_respects_the_budget_and_stays_in_chronological_order()
    {
        var shots = Enumerable.Range(0, 20)
            .Select(i => new VideoAnalysisShot($"s{i}", i, i + 1))
            .ToList();

        IReadOnlyList<string> selected = KeyframeSelector.SelectShotsToCaption(
            shots, duplicateGroups: null, VideoCaptionSelection.EvenlySpaced, maxCaptionedShots: 5, minCaptionShotSeconds: 0.0);

        selected.Count.Should().BeLessThanOrEqualTo(5);
        List<int> indices = selected.Select(ParseShotIndex).ToList();
        indices.Should().BeInAscendingOrder();
        // Spread across the whole list, not clustered at the start.
        (indices.Last() - indices.First()).Should().BeGreaterThan(10);
    }

    [Fact]
    public void EvenlySpaced_returns_every_eligible_shot_when_budget_exceeds_the_shot_count()
    {
        var shots = new List<VideoAnalysisShot>
        {
            new("s0", 0, 1),
            new("s1", 1, 2),
            new("s2", 2, 3)
        };

        IReadOnlyList<string> selected = KeyframeSelector.SelectShotsToCaption(
            shots, duplicateGroups: null, VideoCaptionSelection.EvenlySpaced, maxCaptionedShots: 10, minCaptionShotSeconds: 0.0);

        selected.Should().Equal("s0", "s1", "s2");
    }

    // ---------------------------------------------------------------------
    // Edge cases common to every strategy
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(VideoCaptionSelection.PerDuplicateGroup)]
    [InlineData(VideoCaptionSelection.LongestShots)]
    [InlineData(VideoCaptionSelection.EvenlySpaced)]
    public void Empty_shot_list_selects_nothing(VideoCaptionSelection strategy)
    {
        IReadOnlyList<string> selected = KeyframeSelector.SelectShotsToCaption(
            [], duplicateGroups: null, strategy, maxCaptionedShots: 10, minCaptionShotSeconds: 0.0);

        selected.Should().BeEmpty();
    }

    [Theory]
    [InlineData(VideoCaptionSelection.PerDuplicateGroup)]
    [InlineData(VideoCaptionSelection.LongestShots)]
    [InlineData(VideoCaptionSelection.EvenlySpaced)]
    public void Zero_budget_selects_nothing(VideoCaptionSelection strategy)
    {
        var shots = new List<VideoAnalysisShot> { new("s0", 0, 5) };

        IReadOnlyList<string> selected = KeyframeSelector.SelectShotsToCaption(
            shots, duplicateGroups: null, strategy, maxCaptionedShots: 0, minCaptionShotSeconds: 0.0);

        selected.Should().BeEmpty();
    }

    private static int ParseShotIndex(string shotId) => int.Parse(shotId[1..]);

    private static VideoAnalysisShotVisual MakeVisual(IReadOnlyList<VideoAnalysisStillWindow> stillWindows) => new(
        MotionMean: 0.0, MotionPeak: 0.0, MotionStdDev: 0.0, MotionClass: "Static",
        CameraMove: "Static", CameraMoveConfidence: 1.0, HeadMotion: 0.0, TailMotion: 0.0,
        StillWindows: stillWindows,
        BrightnessMean: 0.5, BrightnessStdDev: 0.0, ContrastRms: 0.0,
        ClippedHighlightRatio: 0.0, CrushedBlackRatio: 0.0, SaturationMean: 0.0,
        DominantColors: [], Regions: [], BestOverlayRegion: null, ActiveCrop: null, Sharpness: null,
        KenBurnsCandidate: false, KenBurnsReason: null, DuplicateGroupId: null, GroupRank: null, IsBestTake: false);
}
