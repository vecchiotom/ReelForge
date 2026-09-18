using FluentAssertions;
using ReelForge.Shared.Workflows;
using ReelForge.WorkflowEngine.Services.Video;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Exact-string tests for <see cref="ProgramEnvelopeFilterBuilder"/>. See docs/video-editing.md
/// "Cut transitions".
/// </summary>
public class ProgramEnvelopeFilterBuilderTests
{
    private static VideoCompileStepConfig Config(
        int fadeInMs = 0, int fadeOutMs = 0, int audioFadeInMs = 0, int audioFadeOutMs = 0, string color = "black") =>
        new(
            Version: 1, Decision: new ExtractInputRef(ExtractInputSource.Previous), AnalysisStepOrder: 1,
            ProgramFadeInMs: fadeInMs, ProgramFadeOutMs: fadeOutMs,
            ProgramAudioFadeInMs: audioFadeInMs, ProgramAudioFadeOutMs: audioFadeOutMs, ProgramFadeColor: color);

    [Fact]
    public void IsEnabled_false_when_every_fade_is_zero()
    {
        ProgramEnvelopeFilterBuilder.IsEnabled(Config()).Should().BeFalse();
    }

    [Theory]
    [InlineData(500, 0, 0, 0)]
    [InlineData(0, 500, 0, 0)]
    [InlineData(0, 0, 500, 0)]
    [InlineData(0, 0, 0, 500)]
    public void IsEnabled_true_when_any_fade_is_positive(int vin, int vout, int ain, int aout)
    {
        ProgramEnvelopeFilterBuilder.IsEnabled(Config(vin, vout, ain, aout)).Should().BeTrue();
    }

    [Fact]
    public void Resolve_converts_ms_to_seconds_without_collapsing_when_within_budget()
    {
        ProgramFadeResolved fade = ProgramEnvelopeFilterBuilder.Resolve(Config(500, 800, 300, 900), totalSec: 30);

        fade.VideoInSec.Should().Be(0.5);
        fade.VideoOutSec.Should().Be(0.8);
        fade.AudioInSec.Should().Be(0.3);
        fade.AudioOutSec.Should().Be(0.9);
    }

    [Fact]
    public void Resolve_collapses_video_fades_to_a_third_of_total_when_they_would_overlap()
    {
        // 6s in + 6s out = 12s > 10s total -> collapse both to 10/3.
        ProgramFadeResolved fade = ProgramEnvelopeFilterBuilder.Resolve(Config(6000, 6000), totalSec: 10);

        fade.VideoInSec.Should().BeApproximately(10.0 / 3.0, 1e-9);
        fade.VideoOutSec.Should().BeApproximately(10.0 / 3.0, 1e-9);
    }

    [Fact]
    public void Resolve_collapses_audio_fades_independently_of_video_fades()
    {
        ProgramFadeResolved fade = ProgramEnvelopeFilterBuilder.Resolve(
            Config(fadeInMs: 500, fadeOutMs: 500, audioFadeInMs: 6000, audioFadeOutMs: 6000), totalSec: 10);

        fade.VideoInSec.Should().Be(0.5);
        fade.VideoOutSec.Should().Be(0.5);
        fade.AudioInSec.Should().BeApproximately(10.0 / 3.0, 1e-9);
        fade.AudioOutSec.Should().BeApproximately(10.0 / 3.0, 1e-9);
    }

    [Fact]
    public void BuildVideoFadeSuffix_both_fades()
    {
        var fade = new ProgramFadeResolved(VideoInSec: 0.5, VideoOutSec: 0.8, AudioInSec: 0, AudioOutSec: 0, Color: "black");
        string suffix = ProgramEnvelopeFilterBuilder.BuildVideoFadeSuffix(fade, totalSec: 20);

        suffix.Should().Be(",fade=t=in:st=0:d=0.5:color=black,fade=t=out:st=19.2:d=0.8:color=black");
    }

    [Fact]
    public void BuildVideoFadeSuffix_empty_when_both_zero()
    {
        var fade = new ProgramFadeResolved(0, 0, 0, 0, "black");
        ProgramEnvelopeFilterBuilder.BuildVideoFadeSuffix(fade, 20).Should().Be("");
    }

    [Fact]
    public void BuildAudioFadeSuffix_both_fades_use_tri_curve()
    {
        var fade = new ProgramFadeResolved(VideoInSec: 0, VideoOutSec: 0, AudioInSec: 0.3, AudioOutSec: 0.9, Color: "black");
        string suffix = ProgramEnvelopeFilterBuilder.BuildAudioFadeSuffix(fade, totalSec: 20);

        suffix.Should().Be(",afade=t=in:st=0:d=0.3:curve=tri,afade=t=out:st=19.1:d=0.9:curve=tri");
    }

    [Fact]
    public void BuildVideoFadeSuffix_respects_a_non_black_allowlisted_color()
    {
        var fade = new ProgramFadeResolved(0.5, 0, 0, 0, "white");
        ProgramEnvelopeFilterBuilder.BuildVideoFadeSuffix(fade, 20).Should().Be(",fade=t=in:st=0:d=0.5:color=white");
    }
}
