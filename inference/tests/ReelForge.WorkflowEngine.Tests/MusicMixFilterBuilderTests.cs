using System;
using System.Collections.Generic;
using System.Globalization;
using FluentAssertions;
using ReelForge.WorkflowEngine.Services.Video;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Exact-string tests for <see cref="MusicMixFilterBuilder"/> (see docs/video-editing.md
/// "Background music"). Mirrors <c>DrawtextFilterBuilderTests</c>'/<c>FfmpegArgvBuilderTests</c>'
/// exact-string-assertion discipline for this feature's ffmpeg-argv-adjacent code.
/// </summary>
public class MusicMixFilterBuilderTests
{
    private static ResolvedMusic Music(
        IReadOnlyList<MusicLiftWindow> windows,
        double bedGain = 0.1,
        double duckGain = 0.03,
        double rampSec = 0.4,
        double playEndSec = 20,
        double fadeInSec = 1.5,
        double fadeOutSec = 2.5) =>
        new(
            TrackId: "m0", ProjectFileId: Guid.NewGuid(), TrackName: "track.mp3", LocalPath: "/scratch/music.mp3",
            TrackDurationSec: 30, OutputDurationSec: 20, PlayEndSec: playEndSec, LoopInput: false,
            BedGainLinear: bedGain, DuckGainLinear: duckGain, RampSec: rampSec,
            FadeInSec: fadeInSec, FadeOutSec: fadeOutSec, LiftWindows: windows);

    [Fact]
    public void Zero_windows_collapses_to_the_bare_duck_constant()
    {
        string expr = MusicMixFilterBuilder.BuildVolumeExpression(Music([]));

        expr.Should().Be("0.03");
        expr.Should().NotContain("max(");
    }

    [Fact]
    public void One_window_is_a_single_trapezoid_with_no_max()
    {
        string expr = MusicMixFilterBuilder.BuildVolumeExpression(
            Music([new MusicLiftWindow(2, 6)], bedGain: 0.1, duckGain: 0.03, rampSec: 0.4));

        expr.Should().Be("0.03+0.07*clip((t-2)/0.4,0,1)*clip((6-t)/0.4,0,1)");
        expr.Should().NotContain("max(");
    }

    [Fact]
    public void Three_windows_produce_exactly_two_nested_max_calls()
    {
        string expr = MusicMixFilterBuilder.BuildVolumeExpression(
            Music(
            [
                new MusicLiftWindow(1, 2),
                new MusicLiftWindow(5, 6),
                new MusicLiftWindow(9, 10)
            ], bedGain: 0.1, duckGain: 0.02, rampSec: 0.2));

        int occurrences = 0;
        int idx = 0;
        while ((idx = expr.IndexOf("max(", idx, StringComparison.Ordinal)) >= 0)
        {
            occurrences++;
            idx += 4;
        }

        occurrences.Should().Be(2);
        expr.Should().StartWith("0.02+0.08*max(");
    }

    [Fact]
    public void BuildMusicBranch_contains_normalize_zero_and_dropout_transition_only_in_mix_stage()
    {
        ResolvedMusic music = Music([new MusicLiftWindow(2, 6)]);
        string branch = MusicMixFilterBuilder.BuildMusicBranch(2, music);
        string mix = MusicMixFilterBuilder.BuildMixStage("[adial]");

        branch.Should().StartWith("[2:a]atrim=end=20,asetpts=N/SR/TB,");
        branch.Should().Contain("aformat=sample_rates=48000:channel_layouts=stereo");
        branch.Should().Contain("volume=eval=frame:volume='");
        branch.Should().EndWith("[amus]");
        branch.Should().NotContain("-stream_loop");

        mix.Should().Be("[adial][amus]amix=inputs=2:duration=first:dropout_transition=0:normalize=0[aout]");
        mix.Should().Contain("normalize=0");
        mix.Should().Contain("duration=first");
        mix.Should().Contain("dropout_transition=0");
    }

    [Fact]
    public void FadeOut_start_equals_playEnd_minus_fadeOut()
    {
        ResolvedMusic music = Music([new MusicLiftWindow(2, 6)], playEndSec: 20, fadeOutSec: 2.5);
        string branch = MusicMixFilterBuilder.BuildMusicBranch(2, music);

        branch.Should().Contain("afade=t=out:st=17.5:d=2.5");
    }

    [Fact]
    public void Volume_expression_never_contains_a_comma_decimal_under_de_DE_culture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            ResolvedMusic music = Music(
                [new MusicLiftWindow(2.4, 6.7), new MusicLiftWindow(9.1, 12.25)],
                bedGain: 0.10, duckGain: 0.028, rampSec: 0.4);
            string branch = MusicMixFilterBuilder.BuildMusicBranch(2, music);

            branch.Should().NotContain(",4");
            branch.Should().NotContain(",7");
            // Should still parse as ordinary invariant-culture decimal numbers.
            branch.Should().Contain("2.4").And.Contain("6.7").And.Contain("9.1").And.Contain("12.25");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
