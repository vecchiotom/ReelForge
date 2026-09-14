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
}
