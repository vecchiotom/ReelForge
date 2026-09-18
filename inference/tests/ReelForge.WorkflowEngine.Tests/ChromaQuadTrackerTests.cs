using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using ReelForge.Shared.Workflows;
using ReelForge.WorkflowEngine.Services.Video;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Tests for <see cref="ChromaQuadTracker"/> — the deterministic chroma-plate quad tracker behind
/// tracked screen inserts (docs/video-editing.md "Tracked screen inserts (Phase 5)"). All fixtures
/// are synthetic in-memory RGB24 frames with a known solid-color rectangle drawn at known
/// coordinates, so every assertion checks the tracker against ground truth it can be held to
/// exactly — no real footage involved (matching this feature's validation plan).
/// </summary>
public class ChromaQuadTrackerTests
{
    private const int W = 160;
    private const int H = 90;
    private const double Fps = 10.0;

    private static readonly ChromaQuadTracker.Options DefaultOptions = new(
        ColorName: "green", MinTrackSeconds: 1.0);

    /// <summary>One RGB24 frame: neutral gray background with a filled axis-aligned rectangle of the given color.</summary>
    private static byte[] MakeFrame(int rectX, int rectY, int rectW, int rectH, (byte R, byte G, byte B)? color = null)
    {
        (byte r, byte g, byte b) = color ?? ((byte)30, (byte)220, (byte)40);
        var frame = new byte[W * H * 3];
        for (int i = 0; i < W * H; i++)
        {
            frame[i * 3] = 100;
            frame[i * 3 + 1] = 100;
            frame[i * 3 + 2] = 100;
        }

        for (int y = Math.Max(0, rectY); y < Math.Min(H, rectY + rectH); y++)
        {
            for (int x = Math.Max(0, rectX); x < Math.Min(W, rectX + rectW); x++)
            {
                int p = (y * W + x) * 3;
                frame[p] = r;
                frame[p + 1] = g;
                frame[p + 2] = b;
            }
        }

        return frame;
    }

    private static byte[] Concat(IEnumerable<byte[]> frames)
    {
        List<byte[]> list = frames.ToList();
        var buffer = new byte[list.Count * W * H * 3];
        for (int i = 0; i < list.Count; i++)
            Buffer.BlockCopy(list[i], 0, buffer, i * W * H * 3, W * H * 3);
        return buffer;
    }

    [Fact]
    public void Static_green_rectangle_is_tracked_with_accurate_corners_and_high_confidence()
    {
        // 20 frames (2s at 10fps) of a fixed 40x30 rectangle at (60, 30).
        byte[] pixels = Concat(Enumerable.Range(0, 20).Select(_ => MakeFrame(60, 30, 40, 30)));

        IReadOnlyList<VideoInsertRegionTrack> tracks = ChromaQuadTracker.Track(pixels, 20, W, H, Fps, DefaultOptions);

        tracks.Should().HaveCount(1);
        VideoInsertRegionTrack track = tracks[0];
        track.Id.Should().Be("r0");
        track.StartSec.Should().BeApproximately(0.0, 1e-9);
        track.EndSec.Should().BeApproximately(2.0, 1e-9);
        track.Confidence.Should().BeGreaterThan(0.75, "every frame detects a clean rectangular plate");
        track.MotionClass.Should().Be("Static");
        track.ColorName.Should().Be("green");
        track.MeanAreaRatio.Should().BeApproximately(40.0 * 30 / (W * H), 0.01);
        track.MeanAspectRatio.Should().BeApproximately(40.0 / 30, 0.15);

        // Corner ground truth (normalized pixel centers): TL=(60.5/160, 30.5/90), BR=(99.5/160, 59.5/90).
        VideoInsertQuadKeyframe k = track.Keyframes[track.Keyframes.Count / 2];
        k.X0.Should().BeApproximately(60.5 / W, 0.02);
        k.Y0.Should().BeApproximately(30.5 / H, 0.02);
        k.X3.Should().BeApproximately(99.5 / W, 0.02);
        k.Y3.Should().BeApproximately(59.5 / H, 0.02);
        // Corner ORDER matters (ffmpeg perspective order): TR right of TL, BL below TL.
        k.X1.Should().BeGreaterThan(k.X0);
        k.Y2.Should().BeGreaterThan(k.Y0);
    }

    [Fact]
    public void Moving_rectangle_produces_monotonically_advancing_corners_and_a_motion_class()
    {
        // Rectangle slides right by 2px per frame over 30 frames (3s): 60px total travel.
        byte[] pixels = Concat(Enumerable.Range(0, 30).Select(i => MakeFrame(10 + i * 2, 30, 40, 30)));

        IReadOnlyList<VideoInsertRegionTrack> tracks = ChromaQuadTracker.Track(pixels, 30, W, H, Fps, DefaultOptions);

        tracks.Should().HaveCount(1);
        VideoInsertRegionTrack track = tracks[0];
        track.MotionClass.Should().BeOneOf("Slow", "Moving");

        // Smoothed corner x must advance monotonically (allowing tiny numerical slack).
        for (int i = 1; i < track.Keyframes.Count; i++)
            track.Keyframes[i].X0.Should().BeGreaterThanOrEqualTo(track.Keyframes[i - 1].X0 - 1e-9);

        // Total tracked travel ~ 58/160 (extreme-point corners at the moved positions).
        (track.Keyframes[^1].X0 - track.Keyframes[0].X0).Should().BeApproximately(58.0 / W, 0.06);
    }

    [Fact]
    public void No_plate_produces_no_tracks()
    {
        byte[] pixels = Concat(Enumerable.Range(0, 20).Select(_ => MakeFrame(0, 0, 0, 0)));

        ChromaQuadTracker.Track(pixels, 20, W, H, Fps, DefaultOptions).Should().BeEmpty();
    }

    [Fact]
    public void Plate_smaller_than_MinAreaRatio_is_ignored()
    {
        // 2x2 = 4px of 14400 = 0.03% < default 0.4%.
        byte[] pixels = Concat(Enumerable.Range(0, 20).Select(_ => MakeFrame(60, 30, 2, 2)));

        ChromaQuadTracker.Track(pixels, 20, W, H, Fps, DefaultOptions).Should().BeEmpty();
    }

    [Fact]
    public void Track_shorter_than_MinTrackSeconds_is_dropped()
    {
        // Plate present for only 5 frames (0.5s at 10fps) of a 20-frame clip.
        byte[] pixels = Concat(Enumerable.Range(0, 20).Select(i =>
            i is >= 5 and < 10 ? MakeFrame(60, 30, 40, 30) : MakeFrame(0, 0, 0, 0)));

        ChromaQuadTracker.Track(pixels, 20, W, H, Fps, DefaultOptions).Should().BeEmpty();
    }

    [Fact]
    public void Two_temporally_separated_plates_become_two_tracks_in_chronological_order()
    {
        byte[] pixels = Concat(Enumerable.Range(0, 50).Select(i =>
            i < 15 ? MakeFrame(20, 20, 40, 30)
            : i < 30 ? MakeFrame(0, 0, 0, 0)
            : MakeFrame(90, 40, 40, 30)));

        IReadOnlyList<VideoInsertRegionTrack> tracks = ChromaQuadTracker.Track(pixels, 50, W, H, Fps, DefaultOptions);

        tracks.Should().HaveCount(2);
        tracks[0].Id.Should().Be("r0");
        tracks[1].Id.Should().Be("r1");
        tracks[0].StartSec.Should().BeLessThan(tracks[1].StartSec);
    }

    [Fact]
    public void Short_detection_dropout_is_bridged_by_interpolation_not_split_into_two_tracks()
    {
        // Two dropout frames (<= MaxGapFrames=3) in the middle of an otherwise continuous plate.
        byte[] pixels = Concat(Enumerable.Range(0, 20).Select(i =>
            i is 9 or 10 ? MakeFrame(0, 0, 0, 0) : MakeFrame(60, 30, 40, 30)));

        IReadOnlyList<VideoInsertRegionTrack> tracks = ChromaQuadTracker.Track(pixels, 20, W, H, Fps, DefaultOptions);

        tracks.Should().HaveCount(1, "a dropout within MaxGapFrames must be bridged, not split");
        tracks[0].EndSec.Should().BeApproximately(2.0, 1e-9);
        // The bridged frames exist in the keyframe list (interpolated), keeping it temporally dense.
        tracks[0].Keyframes.Count.Should().Be(20);
    }

    [Fact]
    public void Strongly_non_quadrilateral_blob_is_rejected_by_the_fill_ratio_gate()
    {
        // A thin L-shape: its extreme-point quad covers far more area than the component itself.
        byte[] frame = MakeFrame(20, 20, 60, 4);
        byte[] vertical = MakeFrame(20, 20, 4, 50);
        for (int i = 0; i < frame.Length; i += 3)
        {
            if (vertical[i + 1] == 220)
            {
                frame[i] = 30;
                frame[i + 1] = 220;
                frame[i + 2] = 40;
            }
        }

        ChromaQuadTracker.FrameQuad? quad = ChromaQuadTracker.DetectQuad(frame, 0, W, H, 0, DefaultOptions);

        quad.Should().BeNull("an L-shaped component must not be fitted as a quad plate");
    }

    [Fact]
    public void Blue_plate_is_tracked_when_ColorName_is_blue_and_ignored_under_green()
    {
        byte[] pixels = Concat(Enumerable.Range(0, 20).Select(_ =>
            MakeFrame(60, 30, 40, 30, color: ((byte)30, (byte)40, (byte)230))));

        ChromaQuadTracker.Track(pixels, 20, W, H, Fps, DefaultOptions).Should().BeEmpty();

        IReadOnlyList<VideoInsertRegionTrack> blueTracks = ChromaQuadTracker.Track(
            pixels, 20, W, H, Fps, DefaultOptions with { ColorName = "blue" });
        blueTracks.Should().HaveCount(1);
        blueTracks[0].ColorName.Should().Be("blue");
    }

    [Theory]
    [InlineData("green", "green")]
    [InlineData("Blue", "blue")]
    [InlineData("MAGENTA", "magenta")]
    [InlineData("chartreuse", "green")]
    [InlineData(null, "green")]
    public void NormalizeColorName_defaults_unknown_values_to_green(string? raw, string expected)
    {
        ChromaQuadTracker.NormalizeColorName(raw).Should().Be(expected);
    }

    [Fact]
    public void MaxRegions_keeps_the_longest_tracks_re_sorted_chronologically()
    {
        // Three plates: 1.2s, 3s, 1.5s — MaxRegions=2 keeps the 3s and 1.5s ones, in time order.
        byte[] pixels = Concat(Enumerable.Range(0, 80).Select(i =>
            i < 12 ? MakeFrame(20, 20, 30, 20)
            : i is >= 20 and < 50 ? MakeFrame(60, 30, 30, 20)
            : i is >= 60 and < 75 ? MakeFrame(100, 50, 30, 20)
            : MakeFrame(0, 0, 0, 0)));

        IReadOnlyList<VideoInsertRegionTrack> tracks = ChromaQuadTracker.Track(
            pixels, 80, W, H, Fps, DefaultOptions with { MaxRegions = 2 });

        tracks.Should().HaveCount(2);
        tracks[0].StartSec.Should().BeApproximately(2.0, 1e-9);
        tracks[1].StartSec.Should().BeApproximately(6.0, 1e-9);
        tracks.Select(t => t.Id).Should().ContainInOrder("r0", "r1");
    }
}
