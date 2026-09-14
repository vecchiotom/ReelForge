using FluentAssertions;
using ReelForge.WorkflowEngine.Services.Video;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Covers <see cref="FfmpegFrameGridSampler.ComputeEffectiveFps"/> in isolation — the one piece of
/// <see cref="FfmpegFrameGridSampler"/> that is pure and directly testable without invoking ffmpeg
/// (the rest of <c>SampleAsync</c> shells out via <c>IVideoToolRunner</c> and isn't independently
/// unit tested here, matching this codebase's existing coverage pattern for the ffmpeg-invoking
/// pieces of the video-editing feature). See docs/video-editing.md, Item D cleanup.
/// </summary>
public class FfmpegFrameGridSamplerTests
{
    [Fact]
    public void MaxSampleFrames_zero_behaves_the_same_as_leaving_it_at_the_default()
    {
        // Item D: MaxVisualSampleFrames <= 0 must fall back to the compiled-in default (mirroring
        // VideoAnalyzeStepConfig.MaxVisualSampleFrames's own default of 4000), never disable the
        // sample-count cap entirely.
        double withExplicitDefault = FfmpegFrameGridSampler.ComputeEffectiveFps(
            sampleFps: 10.0, totalDurationSec: 10_000, maxSampleFrames: 4000);
        double withZero = FfmpegFrameGridSampler.ComputeEffectiveFps(
            sampleFps: 10.0, totalDurationSec: 10_000, maxSampleFrames: 0);
        double withNegative = FfmpegFrameGridSampler.ComputeEffectiveFps(
            sampleFps: 10.0, totalDurationSec: 10_000, maxSampleFrames: -5);

        withZero.Should().Be(withExplicitDefault);
        withNegative.Should().Be(withExplicitDefault);

        // And the cap must actually be doing something here: 10_000s * 10fps would be 100,000
        // frames, far more than the 4000-frame default allows, so the fps must have been clamped
        // down from the requested 10.0.
        withZero.Should().BeLessThan(10.0);
    }

    [Fact]
    public void MaxSampleFrames_positive_value_is_honored_as_an_explicit_cap()
    {
        double effectiveFps = FfmpegFrameGridSampler.ComputeEffectiveFps(
            sampleFps: 10.0, totalDurationSec: 100, maxSampleFrames: 50);

        // 50 frames / 100s = 0.5 fps cap, below the requested 10.0.
        effectiveFps.Should().BeApproximately(0.5, 0.0001);
    }

    [Fact]
    public void Effective_fps_never_reaches_ffmpeg_as_zero_or_negative()
    {
        double effectiveFps = FfmpegFrameGridSampler.ComputeEffectiveFps(
            sampleFps: 0.0, totalDurationSec: 100, maxSampleFrames: 1);

        effectiveFps.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Zero_duration_skips_the_frame_count_cap_but_still_returns_a_positive_fps()
    {
        double effectiveFps = FfmpegFrameGridSampler.ComputeEffectiveFps(
            sampleFps: 5.0, totalDurationSec: 0, maxSampleFrames: 10);

        effectiveFps.Should().Be(5.0);
    }
}
