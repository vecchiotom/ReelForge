using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using ReelForge.Shared.Workflows;
using ReelForge.WorkflowEngine.Services.Video;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// <see cref="OverlayPlacementBuilder"/> — pure, deterministic derivation of Phase 3 overlay
/// placement candidates from Phase 1's per-shot region data (see docs/video-editing.md
/// "Motion graphics (Phase 3)").
/// </summary>
public class OverlayPlacementBuilderTests
{
    private static VideoAnalysisRegion Region(string name, double suitability) =>
        new(name, new VideoAnalysisRect(0.1, 0.8, 0.6, 0.15), 40, 5, 0.01, suitability, "Light");

    private static VideoAnalysisShotVisual VisualWithRegions(params VideoAnalysisRegion[] regions) => new(
        MotionMean: 0.01, MotionPeak: 0.02, MotionStdDev: 0.01, MotionClass: "Static",
        CameraMove: "Static", CameraMoveConfidence: 0.9, HeadMotion: 0.01, TailMotion: 0.01,
        StillWindows: [], BrightnessMean: 0.5, BrightnessStdDev: 0.05, ContrastRms: 0.1,
        ClippedHighlightRatio: 0, CrushedBlackRatio: 0, SaturationMean: 0.3,
        DominantColors: [], Regions: regions, BestOverlayRegion: null, ActiveCrop: null,
        Sharpness: null, KenBurnsCandidate: false, KenBurnsReason: null,
        DuplicateGroupId: null, GroupRank: null, IsBestTake: false);

    private static VideoAnalysisShot ShotWithRegions(
        string id, double start, double end, params VideoAnalysisRegion[] regions) =>
        new(id, start, end, Visual: VisualWithRegions(regions));

    [Fact]
    public void No_placements_for_shots_without_visual_data()
    {
        var shots = new List<VideoAnalysisShot> { new("s0", 0, 10, Visual: null) };

        IReadOnlyList<VideoAnalysisPlacement> placements = OverlayPlacementBuilder.BuildPlacements(shots, maxPerShot: 2, maxTotal: 40);

        placements.Should().BeEmpty();
    }

    [Fact]
    public void Placements_only_derived_from_the_three_named_overlay_bands_not_the_3x3_grid_cells()
    {
        var shots = new List<VideoAnalysisShot>
        {
            ShotWithRegions("s0", 0, 10,
                Region("R0", 0.99), Region("LowerThird", 0.5), Region("UpperThird", 0.4), Region("CenterBand", 0.3))
        };

        IReadOnlyList<VideoAnalysisPlacement> placements = OverlayPlacementBuilder.BuildPlacements(shots, maxPerShot: 3, maxTotal: 40);

        placements.Should().OnlyContain(p => p.Region == "LowerThird" || p.Region == "UpperThird" || p.Region == "CenterBand");
    }

    [Fact]
    public void Placements_are_ranked_by_suitability_descending_and_capped_per_shot()
    {
        var shots = new List<VideoAnalysisShot>
        {
            ShotWithRegions("s0", 0, 10,
                Region("LowerThird", 0.3), Region("UpperThird", 0.9), Region("CenterBand", 0.6))
        };

        IReadOnlyList<VideoAnalysisPlacement> placements = OverlayPlacementBuilder.BuildPlacements(shots, maxPerShot: 2, maxTotal: 40);

        placements.Should().HaveCount(2);
        placements[0].Region.Should().Be("UpperThird");
        placements[1].Region.Should().Be("CenterBand");
    }

    [Fact]
    public void Ids_are_sequential_and_globally_unique_across_shots()
    {
        var shots = new List<VideoAnalysisShot>
        {
            ShotWithRegions("s0", 0, 10, Region("LowerThird", 0.9)),
            ShotWithRegions("s1", 10, 20, Region("LowerThird", 0.8), Region("UpperThird", 0.7)),
        };

        IReadOnlyList<VideoAnalysisPlacement> placements = OverlayPlacementBuilder.BuildPlacements(shots, maxPerShot: 2, maxTotal: 40);

        placements.Select(p => p.Id).Should().Equal("p0", "p1", "p2");
        placements.Select(p => p.Id).Distinct().Should().HaveCount(3);
    }

    [Fact]
    public void MaxTotal_caps_the_whole_artifact_dropping_lowest_suitability_first()
    {
        var shots = new List<VideoAnalysisShot>
        {
            ShotWithRegions("s0", 0, 10, Region("LowerThird", 0.95)),
            ShotWithRegions("s1", 10, 20, Region("LowerThird", 0.10)),
            ShotWithRegions("s2", 20, 30, Region("LowerThird", 0.50)),
        };

        IReadOnlyList<VideoAnalysisPlacement> placements = OverlayPlacementBuilder.BuildPlacements(shots, maxPerShot: 1, maxTotal: 2);

        placements.Should().HaveCount(2);
        placements.Select(p => p.ShotId).Should().Equal("s0", "s2"); // s1 (lowest suitability) dropped
        placements.Select(p => p.Id).Should().Equal("p0", "p1"); // re-numbered sequentially after capping
    }

    [Fact]
    public void Window_prefers_longest_still_window_when_present()
    {
        var regions = new[] { Region("LowerThird", 0.9) };
        var visual = VisualWithRegions(regions) with
        {
            StillWindows = [new VideoAnalysisStillWindow(1.0, 1.5, 0.01), new VideoAnalysisStillWindow(3.0, 5.0, 0.01)]
        };
        var shots = new List<VideoAnalysisShot> { new("s0", 0, 10, Visual: visual) };

        IReadOnlyList<VideoAnalysisPlacement> placements = OverlayPlacementBuilder.BuildPlacements(shots, maxPerShot: 1, maxTotal: 40);

        placements.Should().ContainSingle();
        placements[0].TimeAnchor.Should().Be("LongestStillWindow");
        placements[0].StartSec.Should().Be(3.0);
        placements[0].EndSec.Should().Be(5.0);
    }

    [Fact]
    public void Window_falls_back_to_shot_middle_when_no_still_window_exists()
    {
        var shots = new List<VideoAnalysisShot> { ShotWithRegions("s0", 0, 10, Region("LowerThird", 0.9)) };

        IReadOnlyList<VideoAnalysisPlacement> placements = OverlayPlacementBuilder.BuildPlacements(shots, maxPerShot: 1, maxTotal: 40);

        placements.Should().ContainSingle();
        placements[0].TimeAnchor.Should().Be("ShotMiddle");
        placements[0].StartSec.Should().BeGreaterThanOrEqualTo(0);
        placements[0].EndSec.Should().BeLessThanOrEqualTo(10);
        placements[0].EndSec.Should().BeGreaterThan(placements[0].StartSec);
    }

    [Fact]
    public void Window_is_always_clamped_to_the_owning_shots_own_bounds()
    {
        // A very short shot (1s) forces the default ~2.5s window to clamp.
        var shots = new List<VideoAnalysisShot> { ShotWithRegions("s0", 5.0, 6.0, Region("LowerThird", 0.9)) };

        IReadOnlyList<VideoAnalysisPlacement> placements = OverlayPlacementBuilder.BuildPlacements(shots, maxPerShot: 1, maxTotal: 40);

        placements.Should().ContainSingle();
        placements[0].StartSec.Should().BeGreaterThanOrEqualTo(5.0);
        placements[0].EndSec.Should().BeLessThanOrEqualTo(6.0);
    }

    [Fact]
    public void MaxPerShot_zero_or_maxTotal_zero_produces_no_placements()
    {
        var shots = new List<VideoAnalysisShot> { ShotWithRegions("s0", 0, 10, Region("LowerThird", 0.9)) };

        OverlayPlacementBuilder.BuildPlacements(shots, maxPerShot: 0, maxTotal: 40).Should().BeEmpty();
        OverlayPlacementBuilder.BuildPlacements(shots, maxPerShot: 2, maxTotal: 0).Should().BeEmpty();
    }

    [Fact]
    public void A_long_still_window_is_split_into_multiple_non_overlapping_time_slices_per_region()
    {
        // A single continuous 45s shot with one still window spanning nearly the whole thing
        // (the exact real-world shape that produced two overlay candidates sharing one identical
        // window before this fix) should now offer several distinct moments per region instead
        // of the same 35.5s window reused for every candidate.
        var regions = new[] { Region("CenterBand", 0.9), Region("UpperThird", 0.8) };
        var visual = VisualWithRegions(regions) with
        {
            StillWindows = [new VideoAnalysisStillWindow(7.0, 42.5, 0.01)]
        };
        var shots = new List<VideoAnalysisShot> { new("s0", 0, 45.226, Visual: visual) };

        IReadOnlyList<VideoAnalysisPlacement> placements =
            OverlayPlacementBuilder.BuildPlacements(shots, maxPerShot: 2, maxTotal: 40, maxTimeSlicesPerRegion: 3);

        List<VideoAnalysisPlacement> centerBandSlices = placements.Where(p => p.Region == "CenterBand").ToList();
        centerBandSlices.Should().HaveCount(3, "a 35.5s window should split into the requested 3 slices");

        // Contiguous and non-overlapping: each slice's start equals the previous slice's end.
        for (int i = 1; i < centerBandSlices.Count; i++)
            centerBandSlices[i].StartSec.Should().Be(centerBandSlices[i - 1].EndSec);

        centerBandSlices[0].StartSec.Should().Be(7.0);
        centerBandSlices[^1].EndSec.Should().Be(42.5);

        // A different region gets its own independent set of slices covering the same window —
        // so an agent can pick a CenterBand slice and an UpperThird slice that don't collide.
        List<VideoAnalysisPlacement> upperThirdSlices = placements.Where(p => p.Region == "UpperThird").ToList();
        upperThirdSlices.Should().HaveCount(3);
    }

    [Fact]
    public void A_short_window_is_not_split_even_when_slices_are_allowed()
    {
        var shots = new List<VideoAnalysisShot> { ShotWithRegions("s0", 0, 10, Region("LowerThird", 0.9)) };

        IReadOnlyList<VideoAnalysisPlacement> placements =
            OverlayPlacementBuilder.BuildPlacements(shots, maxPerShot: 1, maxTotal: 40, maxTimeSlicesPerRegion: 3);

        placements.Should().ContainSingle("a ~2.5s ShotMiddle window is under the 4s minimum slice width");
    }
}
