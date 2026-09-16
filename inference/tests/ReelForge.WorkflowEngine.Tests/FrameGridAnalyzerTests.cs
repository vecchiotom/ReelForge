using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using ReelForge.Shared.Workflows;
using ReelForge.WorkflowEngine.Services.Video;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Tests <see cref="FrameGridAnalyzer"/> against small synthetic RGB grid buffers constructed
/// directly in C# — no ffmpeg involved. Mirrors the plan's Phase 1 test list.
/// </summary>
public class FrameGridAnalyzerTests
{
    private static readonly FrameGridAnalyzer.Options DefaultOptions = new();

    private static byte[] MakeFrame(int width, int height, Func<int, int, byte> pixel)
    {
        var frame = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte v = pixel(x, y);
                int idx = (y * width + x) * 3;
                frame[idx] = v;
                frame[idx + 1] = v;
                frame[idx + 2] = v;
            }
        }

        return frame;
    }

    private static byte[] MakeColorFrame(int width, int height, Func<int, int, (byte R, byte G, byte B)> pixel)
    {
        var frame = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                (byte r, byte g, byte b) = pixel(x, y);
                int idx = (y * width + x) * 3;
                frame[idx] = r;
                frame[idx + 1] = g;
                frame[idx + 2] = b;
            }
        }

        return frame;
    }

    private static byte[] UniformColorFrame(int width, int height, byte r, byte g, byte b) =>
        MakeColorFrame(width, height, (_, _) => (r, g, b));

    [Fact]
    public void Identical_frames_are_classified_Static_with_near_zero_motion()
    {
        const int width = 8, height = 6;
        byte[] frame = MakeFrame(width, height, (_, _) => 100);
        List<byte[]> frames = Enumerable.Repeat(frame, 5).ToList();

        VideoAnalysisShotVisual visual = FrameGridAnalyzer.AnalyzeShot(frames, width, height, fps: 2.0, DefaultOptions);

        visual.MotionClass.Should().Be("Static");
        visual.MotionMean.Should().BeLessThan(0.005);
        visual.CameraMove.Should().Be("Static");
    }

    [Fact]
    public void Content_shifting_rightward_each_frame_is_classified_Pan()
    {
        const int width = 32, height = 4;
        var frames = new List<byte[]>();
        int barX = 4;
        for (int f = 0; f < 6; f++)
        {
            int x0 = barX;
            frames.Add(MakeFrame(width, height, (x, _) => x >= x0 && x < x0 + 4 ? (byte)230 : (byte)10));
            barX += 2; // consistent rightward shift every frame
        }

        VideoAnalysisShotVisual visual = FrameGridAnalyzer.AnalyzeShot(frames, width, height, fps: 2.0, DefaultOptions);

        visual.CameraMove.Should().Be("Pan");
        visual.CameraMoveConfidence.Should().BeGreaterThan(0.5);
    }

    [Fact]
    public void Content_shifting_with_alternating_sign_is_classified_Handheld()
    {
        const int width = 32, height = 4;
        var frames = new List<byte[]>();
        int[] positions = { 10, 13, 10, 13, 10, 13 }; // alternating +3/-3 shift each consecutive pair
        foreach (int x0 in positions)
        {
            frames.Add(MakeFrame(width, height, (x, _) => x >= x0 && x < x0 + 4 ? (byte)230 : (byte)10));
        }

        VideoAnalysisShotVisual visual = FrameGridAnalyzer.AnalyzeShot(frames, width, height, fps: 2.0, DefaultOptions);

        visual.CameraMove.Should().Be("Handheld");
    }

    [Fact]
    public void Dark_bottom_band_region_reports_light_text_and_reasonable_suitability()
    {
        const int width = 9, height = 9; // divisible by 3 for a clean 3x3 grid + bands
        byte[] frame = MakeFrame(width, height, (_, y) => y >= 6 ? (byte)60 : (byte)200);
        var frames = new List<byte[]> { frame };

        VideoAnalysisShotVisual visual = FrameGridAnalyzer.AnalyzeShot(frames, width, height, fps: 2.0, DefaultOptions);

        VideoAnalysisRegion? lowerThird = visual.Regions.FirstOrDefault(r => r.Name == "LowerThird");
        lowerThird.Should().NotBeNull();
        lowerThird!.TextColor.Should().Be("Light");
        lowerThird.Suitability.Should().BeGreaterThan(0.5);
    }

    [Fact]
    public void Identical_shot_signatures_are_grouped_with_similarity_near_one()
    {
        const int width = 8, height = 8;
        byte[] frame = MakeFrame(width, height, (x, y) => (byte)((x * 20 + y * 10) % 256));
        var framesA = new List<byte[]> { frame, frame };
        var framesB = new List<byte[]> { frame, frame };

        FrameGridAnalyzer.ShotSignature sigA = FrameGridAnalyzer.ComputeSignature(framesA, width, height);
        FrameGridAnalyzer.ShotSignature sigB = FrameGridAnalyzer.ComputeSignature(framesB, width, height);

        double similarity = FrameGridAnalyzer.Similarity(sigA, sigB);
        similarity.Should().BeApproximately(1.0, 1e-6);

        IReadOnlyList<VideoAnalysisDuplicateGroup> groups = FrameGridAnalyzer.GroupDuplicates(
            shotIds: ["s0", "s1"],
            signatures: [sigA, sigB],
            takeQualities: [0.5, 0.6],
            similarityThreshold: 0.90,
            windowShots: 20,
            out IReadOnlyDictionary<string, (string GroupId, int GroupRank, bool IsBestTake)> assignments);

        groups.Should().HaveCount(1);
        groups[0].ShotIds.Should().BeEquivalentTo(new[] { "s0", "s1" });
        assignments.Should().ContainKey("s0");
        assignments.Should().ContainKey("s1");
    }

    [Fact]
    public void Very_different_shot_signatures_are_not_grouped()
    {
        const int width = 8, height = 8;
        byte[] darkFrame = MakeFrame(width, height, (_, _) => 5);
        byte[] checkerFrame = MakeFrame(width, height, (x, y) => (x + y) % 2 == 0 ? (byte)250 : (byte)5);

        FrameGridAnalyzer.ShotSignature sigA = FrameGridAnalyzer.ComputeSignature([darkFrame, darkFrame], width, height);
        FrameGridAnalyzer.ShotSignature sigB = FrameGridAnalyzer.ComputeSignature([checkerFrame, checkerFrame], width, height);

        double similarity = FrameGridAnalyzer.Similarity(sigA, sigB);
        similarity.Should().BeLessThan(0.90);

        IReadOnlyList<VideoAnalysisDuplicateGroup> groups = FrameGridAnalyzer.GroupDuplicates(
            shotIds: ["s0", "s1"],
            signatures: [sigA, sigB],
            takeQualities: [0.5, 0.6],
            similarityThreshold: 0.90,
            windowShots: 20,
            out IReadOnlyDictionary<string, (string GroupId, int GroupRank, bool IsBestTake)> assignments);

        groups.Should().BeEmpty();
        assignments.Should().BeEmpty();
    }

    [Fact]
    public void A_large_chained_duplicate_group_computes_its_mean_similarity_in_bounded_time()
    {
        // Item E: the clustering step itself is correctly windowed (a shot is only ever compared
        // against the previous windowShots shots), but single-linkage chaining can still grow one
        // group arbitrarily large when there's a long run of consecutively-similar shots — e.g. a
        // long, static-camera video. MeanPairwiseSimilarity must not then compute all O(m^2) pairs
        // within that group. 200 identical shots here all chain into a single group.
        const int width = 8, height = 8;
        const int shotCount = 200;
        byte[] frame = MakeFrame(width, height, (x, y) => (byte)((x * 20 + y * 10) % 256));

        var shotIds = new List<string>(shotCount);
        var signatures = new List<FrameGridAnalyzer.ShotSignature>(shotCount);
        var takeQualities = new List<double>(shotCount);
        for (int i = 0; i < shotCount; i++)
        {
            shotIds.Add($"s{i}");
            signatures.Add(FrameGridAnalyzer.ComputeSignature([frame, frame], width, height));
            takeQualities.Add(0.5);
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        IReadOnlyList<VideoAnalysisDuplicateGroup> groups = FrameGridAnalyzer.GroupDuplicates(
            shotIds, signatures, takeQualities,
            similarityThreshold: 0.90, windowShots: 20,
            out IReadOnlyDictionary<string, (string GroupId, int GroupRank, bool IsBestTake)> assignments);
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2),
            "MeanPairwiseSimilarity must bound its cost instead of computing all O(m^2) pairs in a large chained group");

        groups.Should().HaveCount(1);
        groups[0].ShotIds.Should().HaveCount(shotCount);
        groups[0].MeanSimilarity.Should().BeApproximately(1.0, 1e-6);
        assignments.Should().HaveCount(shotCount);
    }

    [Fact]
    public void AnalyzeShot_still_windows_are_reported_in_absolute_source_timeline_seconds_not_shot_relative()
    {
        const int width = 4, height = 4;
        const double fps = 2.0;
        const int frameCount = 40; // 20s of grid samples at 2fps, covering the whole source video.
        byte[] frame = MakeFrame(width, height, (_, _) => 100); // identical frames => fully still.
        int frameSize = frame.Length;
        var pixelData = new byte[frameSize * frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            Buffer.BlockCopy(frame, 0, pixelData, i * frameSize, frameSize);
        }

        // The shot under analysis occupies [10s, 16s) of a much longer source video — a
        // deliberately non-zero start, so a still window wrongly reported relative to the start
        // of the SLICED subarray (i.e. near 0s) is caught rather than accidentally matching.
        const double shotStartSec = 10.0, shotEndSec = 16.0;
        List<byte[]> frames = FrameGridAnalyzer.SliceShotFrames(
            pixelData, frameCount, width, height, fps, shotStartSec, shotEndSec, out int startFrameIndex);
        double shotStartOffsetSec = startFrameIndex / fps;

        VideoAnalysisShotVisual visual = FrameGridAnalyzer.AnalyzeShot(
            frames, width, height, fps, DefaultOptions, shotStartOffsetSec);

        visual.StillWindows.Should().NotBeEmpty();
        visual.StillWindows.Should().OnlyContain(w => w.StartSec >= shotStartSec && w.EndSec <= shotEndSec);

        // The pre-fix code computed these relative to the sliced subarray (offset 0), which would
        // have placed them within [0, 6] instead — well outside the shot's actual window.
        visual.StillWindows.Should().OnlyContain(w => w.StartSec >= 9.99 && w.EndSec > 6.0);
    }

    [Fact]
    public void SliceShotFrames_returns_at_least_one_frame_for_a_very_short_shot()
    {
        const int width = 4, height = 4;
        byte[] frame = MakeFrame(width, height, (_, _) => 50);
        var pixelData = new byte[frame.Length * 10];
        for (int i = 0; i < 10; i++)
        {
            Buffer.BlockCopy(frame, 0, pixelData, i * frame.Length, frame.Length);
        }

        List<byte[]> slice = FrameGridAnalyzer.SliceShotFrames(
            pixelData, frameCount: 10, gridWidth: width, gridHeight: height, fps: 2.0,
            shotStartSec: 0.0, shotEndSec: 0.05);

        slice.Count.Should().BeGreaterThanOrEqualTo(1);
    }

    // -----------------------------------------------------------------------------------------
    // Phase 4 — D1 (colour temperature)
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void Warm_frame_reports_Warm_color_temperature_and_positive_warmth()
    {
        byte[] frame = UniformColorFrame(4, 4, r: 200, g: 120, b: 50);

        VideoAnalysisShotVisual visual = FrameGridAnalyzer.AnalyzeShot([frame], 4, 4, fps: 2.0, DefaultOptions);

        visual.ColorTemperatureClass.Should().Be("Warm");
        visual.Warmth.Should().BeGreaterThan(0.2);
    }

    [Fact]
    public void Cool_frame_reports_Cool()
    {
        byte[] frame = UniformColorFrame(4, 4, r: 50, g: 120, b: 200);

        VideoAnalysisShotVisual visual = FrameGridAnalyzer.AnalyzeShot([frame], 4, 4, fps: 2.0, DefaultOptions);

        visual.ColorTemperatureClass.Should().Be("Cool");
        visual.Warmth.Should().BeLessThan(-0.2);
    }

    [Fact]
    public void Near_monochrome_frame_is_Neutral_regardless_of_channel_drift()
    {
        // R/G/B differ slightly (non-zero channel drift), but max-min stays under the
        // saturationMean < 0.05 guard, so ColorTemperatureClass must be "Neutral" regardless.
        byte[] frame = UniformColorFrame(4, 4, r: 130, g: 128, b: 126);

        VideoAnalysisShotVisual visual = FrameGridAnalyzer.AnalyzeShot([frame], 4, 4, fps: 2.0, DefaultOptions);

        visual.SaturationMean.Should().BeLessThan(0.05);
        visual.ColorTemperatureClass.Should().Be("Neutral");
    }

    [Fact]
    public void Tint_is_positive_for_green_cast_and_negative_for_magenta()
    {
        byte[] greenCast = UniformColorFrame(4, 4, r: 100, g: 180, b: 100);
        byte[] magenta = UniformColorFrame(4, 4, r: 180, g: 100, b: 180);

        VideoAnalysisShotVisual greenVisual = FrameGridAnalyzer.AnalyzeShot([greenCast], 4, 4, fps: 2.0, DefaultOptions);
        VideoAnalysisShotVisual magentaVisual = FrameGridAnalyzer.AnalyzeShot([magenta], 4, 4, fps: 2.0, DefaultOptions);

        greenVisual.Tint.Should().BeGreaterThan(0);
        magentaVisual.Tint.Should().BeLessThan(0);
    }

    // -----------------------------------------------------------------------------------------
    // Phase 4 — D2 (tone curve)
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void Low_contrast_lifted_black_frames_are_classified_Flat()
    {
        byte[] frame = MakeFrame(4, 4, (_, _) => 102); // luma ~0.40 uniform: dynamicRange 0, blackPoint 0.40

        VideoAnalysisShotVisual visual = FrameGridAnalyzer.AnalyzeShot([frame], 4, 4, fps: 2.0, DefaultOptions);

        visual.ToneClass.Should().Be("Flat");
    }

    [Fact]
    public void Full_range_frames_are_classified_Contrasty()
    {
        byte[] frame = MakeFrame(4, 4, (_, y) => y < 2 ? (byte)10 : (byte)245); // half low, half high

        VideoAnalysisShotVisual visual = FrameGridAnalyzer.AnalyzeShot([frame], 4, 4, fps: 2.0, DefaultOptions);

        visual.ToneClass.Should().Be("Contrasty");
    }

    [Fact]
    public void Mostly_white_frames_are_classified_Blown_before_Contrasty()
    {
        // Also satisfies the Contrasty numeric thresholds (dynamicRange>0.75, blackPoint<0.06) —
        // this is the ordering guard: an actual exposure defect (Blown) must win first.
        byte[] frame = MakeFrame(4, 4, (_, y) => y < 2 ? (byte)10 : (byte)253);

        VideoAnalysisShotVisual visual = FrameGridAnalyzer.AnalyzeShot([frame], 4, 4, fps: 2.0, DefaultOptions);

        visual.ClippedHighlightRatio.Should().BeGreaterThan(0.05);
        visual.ToneClass.Should().Be("Blown");
    }

    [Fact]
    public void Black_and_white_points_bracket_a_spread_distribution()
    {
        byte[] frame = MakeFrame(4, 4, (_, y) => y < 2 ? (byte)10 : (byte)245);

        VideoAnalysisShotVisual visual = FrameGridAnalyzer.AnalyzeShot([frame], 4, 4, fps: 2.0, DefaultOptions);

        visual.BlackPoint.Should().BeLessThanOrEqualTo(visual.WhitePoint);
        visual.BlackPoint.Should().BeApproximately(10 / 255.0, 0.01);
        visual.WhitePoint.Should().BeApproximately(245 / 255.0, 0.01);
    }

    [Fact]
    public void Percentile_of_a_uniform_histogram_is_that_value()
    {
        byte[] frame = MakeFrame(4, 4, (_, _) => 128);

        VideoAnalysisShotVisual visual = FrameGridAnalyzer.AnalyzeShot([frame], 4, 4, fps: 2.0, DefaultOptions);

        visual.BlackPoint.Should().BeApproximately(128 / 255.0, 0.01);
        visual.WhitePoint.Should().BeApproximately(128 / 255.0, 0.01);
    }

    // -----------------------------------------------------------------------------------------
    // Phase 4 — D3 (saturation character)
    // -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(100, 95, 90, "Muted")]     // sat ~0.05
    [InlineData(100, 85, 70, "Natural")]   // sat 0.30
    [InlineData(100, 70, 40, "Vivid")]     // sat 0.60
    public void SaturationClass_buckets_Muted_Natural_Vivid(byte r, byte g, byte b, string expected)
    {
        byte[] frame = UniformColorFrame(4, 4, r, g, b);

        VideoAnalysisShotVisual visual = FrameGridAnalyzer.AnalyzeShot([frame], 4, 4, fps: 2.0, DefaultOptions);

        visual.SaturationClass.Should().Be(expected);
    }

    // -----------------------------------------------------------------------------------------
    // Phase 4 — D4 (look grouping)
    // -----------------------------------------------------------------------------------------

    private static FrameGridAnalyzer.LookSignature Look(
        double warmth = 0, double tint = 0, double brightness = 0.5,
        double blackPoint = 0.1, double whitePoint = 0.9, double saturation = 0.3) =>
        new(warmth, tint, brightness, blackPoint, whitePoint, saturation);

    [Fact]
    public void Two_shots_with_the_same_look_are_grouped_and_a_third_warm_one_is_not()
    {
        var shotIds = new List<string> { "s0", "s1", "s2" };
        var signatures = new List<FrameGridAnalyzer.LookSignature>
        {
            Look(warmth: 0.0),
            Look(warmth: 0.02),
            Look(warmth: 1.0), // clearly different look
        };

        IReadOnlyList<VideoAnalysisLookGroup> groups = FrameGridAnalyzer.GroupLooks(
            shotIds, signatures, similarityThreshold: 0.88,
            out IReadOnlyDictionary<string, (string GroupId, int LookRank)> assignments);

        groups.Should().HaveCount(1);
        groups[0].ShotIds.Should().BeEquivalentTo(new[] { "s0", "s1" });
        assignments.Should().ContainKey("s0");
        assignments.Should().ContainKey("s1");
        assignments.Should().NotContainKey("s2");
    }

    [Fact]
    public void LookDistance_is_zero_for_identical_signatures_and_symmetric()
    {
        FrameGridAnalyzer.LookSignature a = Look(warmth: 0.3, tint: -0.2, saturation: 0.4);
        FrameGridAnalyzer.LookSignature b = Look(warmth: 0.7, tint: 0.1, saturation: 0.1);

        FrameGridAnalyzer.LookDistance(a, a).Should().Be(0);
        FrameGridAnalyzer.LookDistance(a, b).Should().Be(FrameGridAnalyzer.LookDistance(b, a));
    }

    [Fact]
    public void Warmth_dominates_look_distance_over_an_equal_magnitude_saturation_difference()
    {
        FrameGridAnalyzer.LookSignature baseline = Look(warmth: 0, saturation: 0.3);
        FrameGridAnalyzer.LookSignature warmthShift = Look(warmth: 0.4, saturation: 0.3);
        FrameGridAnalyzer.LookSignature saturationShift = Look(warmth: 0, saturation: 0.7);

        double warmthDistance = FrameGridAnalyzer.LookDistance(baseline, warmthShift);
        double saturationDistance = FrameGridAnalyzer.LookDistance(baseline, saturationShift);

        warmthDistance.Should().BeGreaterThan(saturationDistance);
    }

    [Fact]
    public void GroupLooks_assigns_rank_zero_to_the_member_closest_to_the_group_centroid()
    {
        var shotIds = new List<string> { "s0", "s1", "s2" };
        // Centroid warmth ~ (0.10+0.12+0.50)/3 = 0.24; s1 (0.12) is farther from 0.24 than s0 (0.10)?
        // Use values where the middle one is obviously closest to the mean.
        var signatures = new List<FrameGridAnalyzer.LookSignature>
        {
            Look(warmth: 0.10),
            Look(warmth: 0.20),
            Look(warmth: 0.30),
        };

        IReadOnlyList<VideoAnalysisLookGroup> groups = FrameGridAnalyzer.GroupLooks(
            shotIds, signatures, similarityThreshold: 0.5,
            out IReadOnlyDictionary<string, (string GroupId, int LookRank)> assignments);

        groups.Should().HaveCount(1);
        assignments["s1"].LookRank.Should().Be(0);
        groups[0].RepresentativeShotId.Should().Be("s1");
    }

    [Fact]
    public void GroupLooks_leaves_a_singleton_ungrouped()
    {
        var shotIds = new List<string> { "s0", "s1" };
        var signatures = new List<FrameGridAnalyzer.LookSignature> { Look(warmth: 0.1), Look(warmth: 0.95) };

        IReadOnlyList<VideoAnalysisLookGroup> groups = FrameGridAnalyzer.GroupLooks(
            shotIds, signatures, similarityThreshold: 0.88,
            out IReadOnlyDictionary<string, (string GroupId, int LookRank)> assignments);

        groups.Should().BeEmpty();
        assignments.Should().BeEmpty();
    }

    [Fact]
    public void GroupLooks_group_ids_are_assigned_in_first_member_order()
    {
        var shotIds = new List<string> { "s0", "s1", "s2", "s3" };
        var signatures = new List<FrameGridAnalyzer.LookSignature>
        {
            Look(warmth: 0.0), Look(warmth: 1.0), Look(warmth: 0.02), Look(warmth: 0.98),
        };

        IReadOnlyList<VideoAnalysisLookGroup> groups = FrameGridAnalyzer.GroupLooks(
            shotIds, signatures, similarityThreshold: 0.88,
            out IReadOnlyDictionary<string, (string GroupId, int LookRank)> _);

        groups.Should().HaveCount(2);
        groups[0].Id.Should().Be("k0");
        groups[0].ShotIds.Should().Contain("s0");
        groups[1].Id.Should().Be("k1");
        groups[1].ShotIds.Should().Contain("s1");
    }

    [Fact]
    public void A_large_chained_look_group_computes_cohesion_in_bounded_time()
    {
        const int shotCount = 200;
        var shotIds = new List<string>(shotCount);
        var signatures = new List<FrameGridAnalyzer.LookSignature>(shotCount);
        for (int i = 0; i < shotCount; i++)
        {
            shotIds.Add($"s{i}");
            signatures.Add(Look(warmth: 0.1));
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        IReadOnlyList<VideoAnalysisLookGroup> groups = FrameGridAnalyzer.GroupLooks(
            shotIds, signatures, similarityThreshold: 0.88,
            out IReadOnlyDictionary<string, (string GroupId, int LookRank)> assignments);
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2),
            "MeanPairwiseSimilarity must bound its cost even for a large all-pairs look group");

        groups.Should().HaveCount(1);
        groups[0].ShotIds.Should().HaveCount(shotCount);
        groups[0].Cohesion.Should().BeApproximately(1.0, 1e-6);
        assignments.Should().HaveCount(shotCount);
    }

    // -----------------------------------------------------------------------------------------
    // Phase 4 — D6 (letterbox / pillarbox)
    // -----------------------------------------------------------------------------------------

    private static byte[] BarredFrame(int width, int height, int top, int bottom, int left, int right) =>
        MakeFrame(width, height, (x, y) =>
            y < top || y >= height - bottom || x < left || x >= width - right ? (byte)0 : (byte)200);

    [Fact]
    public void Letterboxed_frames_report_an_ActiveCrop_excluding_the_black_bars()
    {
        byte[] frame = BarredFrame(20, 20, top: 4, bottom: 4, left: 0, right: 0);

        VideoAnalysisShotVisual visual = FrameGridAnalyzer.AnalyzeShot([frame, frame], 20, 20, fps: 2.0, DefaultOptions);

        visual.ActiveCrop.Should().NotBeNull();
        visual.ActiveCrop!.Y.Should().BeApproximately(0.2, 0.01);
        visual.ActiveCrop.H.Should().BeApproximately(0.6, 0.01);
        visual.ActiveCrop.X.Should().BeApproximately(0.0, 0.01);
        visual.ActiveCrop.W.Should().BeApproximately(1.0, 0.01);
    }

    [Fact]
    public void Pillarboxed_frames_report_an_ActiveCrop_excluding_the_side_bars()
    {
        byte[] frame = BarredFrame(20, 20, top: 0, bottom: 0, left: 4, right: 4);

        VideoAnalysisShotVisual visual = FrameGridAnalyzer.AnalyzeShot([frame, frame], 20, 20, fps: 2.0, DefaultOptions);

        visual.ActiveCrop.Should().NotBeNull();
        visual.ActiveCrop!.X.Should().BeApproximately(0.2, 0.01);
        visual.ActiveCrop.W.Should().BeApproximately(0.6, 0.01);
    }

    [Fact]
    public void A_one_sided_black_band_is_not_reported_as_a_crop()
    {
        byte[] frame = BarredFrame(20, 20, top: 6, bottom: 0, left: 0, right: 0);

        VideoAnalysisShotVisual visual = FrameGridAnalyzer.AnalyzeShot([frame, frame], 20, 20, fps: 2.0, DefaultOptions);

        visual.ActiveCrop.Should().BeNull();
    }

    [Fact]
    public void An_entirely_black_shot_reports_no_ActiveCrop()
    {
        byte[] frame = MakeFrame(20, 20, (_, _) => 0);

        VideoAnalysisShotVisual visual = FrameGridAnalyzer.AnalyzeShot([frame, frame], 20, 20, fps: 2.0, DefaultOptions);

        visual.ActiveCrop.Should().BeNull();
    }

    [Fact]
    public void A_bar_thicker_than_forty_percent_is_not_reported_as_a_crop()
    {
        byte[] frame = BarredFrame(20, 20, top: 10, bottom: 10, left: 0, right: 0);

        VideoAnalysisShotVisual visual = FrameGridAnalyzer.AnalyzeShot([frame, frame], 20, 20, fps: 2.0, DefaultOptions);

        visual.ActiveCrop.Should().BeNull();
    }

    [Fact]
    public void DetectLetterbox_false_leaves_ActiveCrop_null()
    {
        byte[] frame = BarredFrame(20, 20, top: 4, bottom: 4, left: 0, right: 0);
        var options = DefaultOptions with { DetectLetterbox = false };

        VideoAnalysisShotVisual visual = FrameGridAnalyzer.AnalyzeShot([frame, frame], 20, 20, fps: 2.0, options);

        visual.ActiveCrop.Should().BeNull();
    }

    // -----------------------------------------------------------------------------------------
    // Phase 4 — D7 (backlit candidate)
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void Bright_border_and_dark_center_is_flagged_BacklitCandidate()
    {
        const int width = 9, height = 9;
        byte[] frame = MakeFrame(width, height,
            (x, y) => x is >= 3 and < 6 && y is >= 3 and < 6 ? (byte)26 : (byte)153);

        VideoAnalysisShotVisual visual = FrameGridAnalyzer.AnalyzeShot([frame], width, height, fps: 2.0, DefaultOptions);

        visual.BacklitCandidate.Should().BeTrue();
    }

    [Fact]
    public void An_evenly_lit_frame_is_not_flagged_BacklitCandidate()
    {
        const int width = 9, height = 9;
        byte[] frame = MakeFrame(width, height, (_, _) => 128);

        VideoAnalysisShotVisual visual = FrameGridAnalyzer.AnalyzeShot([frame], width, height, fps: 2.0, DefaultOptions);

        visual.BacklitCandidate.Should().BeFalse();
    }

    // -----------------------------------------------------------------------------------------
    // Phase 4 — ComputeTakeQuality gains real sharpness
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void ComputeTakeQuality_with_a_real_sharpness_value_differs_from_the_neutral_placeholder()
    {
        double withNeutral = FrameGridAnalyzer.ComputeTakeQuality(0.05, -20, 0.5, 4.0);
        double withRealSharp = FrameGridAnalyzer.ComputeTakeQuality(0.05, -20, 0.5, 4.0, sharpness: 1.0);

        withRealSharp.Should().NotBe(withNeutral);
    }

    [Fact]
    public void ComputeTakeQuality_with_null_sharpness_is_identical_to_the_pre_Phase_4_result()
    {
        double withDefault = FrameGridAnalyzer.ComputeTakeQuality(0.05, -20, 0.5, 4.0);
        double withExplicitNull = FrameGridAnalyzer.ComputeTakeQuality(0.05, -20, 0.5, 4.0, sharpness: null);

        withExplicitNull.Should().Be(withDefault);
    }
}
