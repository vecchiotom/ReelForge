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

    // ---------------------------------------------------------------------
    // Phase 4 (§7.4): ChooseKeyframeSecs, and (judgment call 9) source round-robin
    // ---------------------------------------------------------------------

    [Fact]
    public void ChooseKeyframeSecs_with_n_equal_one_returns_exactly_ChooseKeyframeSec()
    {
        VideoAnalysisShot shot = new("s0", 0.0, 10.0);

        IReadOnlyList<double> secs = KeyframeSelector.ChooseKeyframeSecs(shot, 1);

        secs.Should().Equal(KeyframeSelector.ChooseKeyframeSec(shot));
    }

    [Fact]
    public void ChooseKeyframeSecs_with_n_equal_three_returns_quarter_half_three_quarter_points_inside_the_shot()
    {
        VideoAnalysisShot shot = new("s0", 0.0, 8.0);

        IReadOnlyList<double> secs = KeyframeSelector.ChooseKeyframeSecs(shot, 3);

        secs.Should().Equal(2.0, 4.0, 6.0);
        secs.Should().OnlyContain(s => s >= shot.StartSec && s <= shot.EndSec);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(5, 3)]
    public void ChooseKeyframeSecs_clamps_n_to_one_through_three(int requestedN, int expectedCount)
    {
        VideoAnalysisShot shot = new("s0", 0.0, 8.0);

        IReadOnlyList<double> secs = KeyframeSelector.ChooseKeyframeSecs(shot, requestedN);

        secs.Should().HaveCount(expectedCount);
    }

    [Fact]
    public void Pass_two_fill_round_robins_across_source_clips()
    {
        // 2 sources x 5 shots (all equally eligible, all the same duration so pure longest-first
        // tie-breaking wouldn't visibly round-robin) — budget 4 should take 2 from each source, not
        // 4 from one and 0 from the other.
        var shots = new List<VideoAnalysisShot>();
        for (int src = 0; src < 2; src++)
            for (int i = 0; i < 5; i++)
                shots.Add(new VideoAnalysisShot($"s{src * 5 + i}", i, i + 2, SourceIndex: src));

        IReadOnlyList<string> selected = KeyframeSelector.SelectShotsToCaption(
            shots, duplicateGroups: null, VideoCaptionSelection.LongestShots, maxCaptionedShots: 4, minCaptionShotSeconds: 0.0);

        selected.Should().HaveCount(4);
        int fromSource0 = selected.Count(id => shots.First(s => s.Id == id).SourceIndex == 0);
        int fromSource1 = selected.Count(id => shots.First(s => s.Id == id).SourceIndex == 1);
        fromSource0.Should().Be(2);
        fromSource1.Should().Be(2);
    }

    [Fact]
    public void Single_source_selection_is_byte_identical_to_the_pre_round_robin_order()
    {
        // All shots SourceIndex 0 (the default) -> one round-robin "group" -> must be identical to
        // plain longest-first, pinning judgment call 9's safety property.
        var shots = new List<VideoAnalysisShot>
        {
            new("s0", 0, 3),   // 3s
            new("s1", 3, 4),   // 1s
            new("s2", 4, 9),   // 5s <- longest
            new("s3", 9, 11),  // 2s
        };

        IReadOnlyList<string> selected = KeyframeSelector.SelectShotsToCaption(
            shots, duplicateGroups: null, VideoCaptionSelection.LongestShots, maxCaptionedShots: 2, minCaptionShotSeconds: 0.0);

        // Chronological order of the two longest: s2 (5s), s0 (3s).
        selected.Should().Equal("s0", "s2");
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
