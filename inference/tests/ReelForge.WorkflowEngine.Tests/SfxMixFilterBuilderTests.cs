using System;
using FluentAssertions;
using ReelForge.WorkflowEngine.Services.Video;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Exact-filter-string tests for <see cref="SfxMixFilterBuilder"/> (the
/// <see cref="MusicMixFilterBuilderTests"/> precedent): every number in a cue branch was computed
/// server-side by <c>VideoCompileStepExecutor.ResolveSfxAsync</c>, so the branch/mix strings are
/// fully deterministic and assertable char-for-char.
/// </summary>
public class SfxMixFilterBuilderTests
{
    private static ResolvedSfxCue Cue(
        double outputStartSec = 4.0,
        double playDurationSec = 4.0,
        double gainLinear = 0.50119,
        double fadeOutSec = 0.12) =>
        new(
            SfxId: "x0",
            AnchorId: "s1",
            ProjectFileId: Guid.NewGuid(),
            ClipName: "whoosh.wav",
            LocalPath: "/scratch/sfx-x0.wav",
            OutputStartSec: outputStartSec,
            PlayDurationSec: playDurationSec,
            GainLinear: gainLinear,
            FadeOutSec: fadeOutSec);

    [Fact]
    public void Cue_branch_trims_formats_gains_declicks_and_delays_in_that_order()
    {
        string branch = SfxMixFilterBuilder.BuildCueBranch(2, 0, Cue());

        branch.Should().Be(
            "[2:a]atrim=end=4,asetpts=N/SR/TB," +
            "aformat=sample_rates=48000:channel_layouts=stereo," +
            "volume=0.50119," +
            "afade=t=out:st=3.88:d=0.12," +
            "adelay=4000|4000[sfx0]");
    }

    [Fact]
    public void Cue_at_output_start_gets_a_zero_delay_not_a_missing_one()
    {
        string branch = SfxMixFilterBuilder.BuildCueBranch(1, 3, Cue(outputStartSec: 0));

        branch.Should().Contain("adelay=0|0[sfx3]",
            "adelay must always be present so every cue branch has the identical filter shape");
    }

    [Fact]
    public void Delay_is_rounded_to_integer_milliseconds()
    {
        // adelay only accepts integer milliseconds — a fractional-second output start must round,
        // never truncate or emit a fractional value ffmpeg would reject.
        string branch = SfxMixFilterBuilder.BuildCueBranch(1, 0, Cue(outputStartSec: 1.23456));

        branch.Should().Contain("adelay=1235|1235");
    }

    [Fact]
    public void Mix_stage_layers_every_cue_over_the_base_with_the_three_load_bearing_amix_options()
    {
        string mix = SfxMixFilterBuilder.BuildMixStage("[abase]", cueCount: 2);

        // normalize=0: without it amix divides every input's level by the input count, quietly
        // reducing the dialogue/base itself. duration=first: the BASE input pins the output
        // length, so a cue near the end can never extend the file. dropout_transition=0: no gain
        // re-ramp when a cue's short branch ends long before the base does (every cue's does).
        mix.Should().Be("[abase][sfx0][sfx1]amix=inputs=3:duration=first:dropout_transition=0:normalize=0[aout]");
    }

    [Fact]
    public void Mix_stage_final_label_is_overridable_for_tail_stage_chaining()
    {
        SfxMixFilterBuilder.BuildMixStage("[abase]", cueCount: 1, finalLabel: "[axfd]")
            .Should().EndWith("normalize=0[axfd]");
    }
}
