using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using ReelForge.Shared.Workflows;
using ReelForge.WorkflowEngine.Execution.StepExecutors;
using ReelForge.WorkflowEngine.Services.Video;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

using ResolvedSpan = VideoCompileStepExecutor.ResolvedSpan;

/// <summary>
/// Exact-string tests for <see cref="TransitionFilterBuilder"/> — mirrors
/// <c>MusicMixFilterBuilderTests</c>'/<c>DrawtextFilterBuilderTests</c>' exact-string-assertion
/// discipline. See docs/video-editing.md "Cut transitions".
/// </summary>
public class TransitionFilterBuilderTests
{
    private static List<ResolvedSpan> ThreeTenSecondSpans() =>
    [
        new(0, 10, 0, 10, 0, 0),
        new(10, 20, 10, 20, 0, 0),
        new(20, 30, 20, 30, 0, 0)
    ];

    private static SeamPlan Seam(int idx, SeamTreatment t, double duration, double overlap, string xfade = "") =>
        new(idx, t, duration, overlap, xfade, 0, "test");

    [Fact]
    public void BuildAudioSeamRampExpression_two_AudioOnly_seams_produces_nested_max()
    {
        List<ResolvedSpan> spans = ThreeTenSecondSpans();
        var seams = new List<SeamPlan>
        {
            Seam(0, SeamTreatment.AudioOnly, 1, 0),
            Seam(1, SeamTreatment.AudioOnly, 1, 0)
        };
        OutputTimeline timeline = OutputTimeline.Build(spans, seams);

        string? expr = TransitionFilterBuilder.BuildAudioSeamRampExpression(seams, timeline, rampSec: 0.5);

        expr.Should().Be("1-max(clip(1-abs(t-10)/0.5,0,1),clip(1-abs(t-20)/0.5,0,1))");
    }

    [Fact]
    public void BuildAudioSeamRampExpression_returns_null_when_no_AudioOnly_seams()
    {
        List<ResolvedSpan> spans = ThreeTenSecondSpans();
        var seams = new List<SeamPlan> { Seam(0, SeamTreatment.HardCut, 0, 0), Seam(1, SeamTreatment.HardCut, 0, 0) };
        OutputTimeline timeline = OutputTimeline.Build(spans, seams);

        TransitionFilterBuilder.BuildAudioSeamRampExpression(seams, timeline, 0.5).Should().BeNull();
    }

    [Fact]
    public void BuildAudioSeamRampExpression_returns_null_above_the_max_term_cap()
    {
        // MaxAudioRampTerms + 2 spans => MaxAudioRampTerms + 1 seams, one more than the cap allows.
        int spanCount = TransitionFilterBuilder.MaxAudioRampTerms + 2;
        var spans = new List<ResolvedSpan>(spanCount);
        for (int i = 0; i < spanCount; i++)
            spans.Add(new ResolvedSpan(i, i + 1, i, i + 1, 0, 0));

        var seams = new List<SeamPlan>(spanCount - 1);
        for (int i = 0; i < spanCount - 1; i++)
            seams.Add(Seam(i, SeamTreatment.AudioOnly, 0.1, 0));

        seams.Count.Should().Be(TransitionFilterBuilder.MaxAudioRampTerms + 1);

        OutputTimeline timeline = OutputTimeline.Build(spans, seams);

        TransitionFilterBuilder.BuildAudioSeamRampExpression(seams, timeline, 0.1).Should().BeNull();
    }

    [Fact]
    public void BuildDipCutVideoFilterSuffix_one_dipped_seam_produces_a_fade_pair()
    {
        List<ResolvedSpan> spans = ThreeTenSecondSpans();
        var seams = new List<SeamPlan>
        {
            Seam(0, SeamTreatment.DipCut, 0.4, 0),
            Seam(1, SeamTreatment.HardCut, 0, 0)
        };
        OutputTimeline timeline = OutputTimeline.Build(spans, seams);

        string? suffix = TransitionFilterBuilder.BuildDipCutVideoFilterSuffix(seams, timeline);

        suffix.Should().Be("fade=t=out:st=9.8:d=0.2:color=black,fade=t=in:st=10:d=0.2:color=black");
    }

    [Fact]
    public void BuildDipCutVideoFilterSuffix_returns_null_with_no_dips()
    {
        List<ResolvedSpan> spans = ThreeTenSecondSpans();
        var seams = new List<SeamPlan> { Seam(0, SeamTreatment.HardCut, 0, 0), Seam(1, SeamTreatment.AudioOnly, 1, 0) };
        OutputTimeline timeline = OutputTimeline.Build(spans, seams);

        TransitionFilterBuilder.BuildDipCutVideoFilterSuffix(seams, timeline).Should().BeNull();
    }

    [Fact]
    public void BuildSpanVideoFadeSuffix_dip_after_only()
    {
        SeamPlan after = Seam(0, SeamTreatment.DipCut, 0.4, 0);
        string suffix = TransitionFilterBuilder.BuildSpanVideoFadeSuffix(5.0, seamBefore: null, seamAfter: after);

        suffix.Should().Be(",fade=t=out:st=4.8:d=0.2:color=black");
    }

    [Fact]
    public void BuildSpanVideoFadeSuffix_dip_before_and_after()
    {
        SeamPlan before = Seam(0, SeamTreatment.DipCut, 0.4, 0);
        SeamPlan after = Seam(1, SeamTreatment.DipCut, 0.6, 0);
        string suffix = TransitionFilterBuilder.BuildSpanVideoFadeSuffix(5.0, before, after);

        suffix.Should().Be(",fade=t=in:st=0:d=0.2:color=black,fade=t=out:st=4.7:d=0.3:color=black");
    }

    [Fact]
    public void BuildSpanVideoFadeSuffix_empty_when_neighbours_are_not_DipCut()
    {
        SeamPlan neighbour = Seam(0, SeamTreatment.AudioOnly, 1, 0);
        TransitionFilterBuilder.BuildSpanVideoFadeSuffix(5.0, neighbour, neighbour).Should().Be("");
    }

    [Fact]
    public void BuildSpanAudioFadeSuffix_audioOnly_before_and_after()
    {
        SeamPlan before = Seam(0, SeamTreatment.AudioOnly, 0.048, 0);
        SeamPlan after = Seam(1, SeamTreatment.AudioOnly, 0.048, 0);
        string suffix = TransitionFilterBuilder.BuildSpanAudioFadeSuffix(5.0, before, after);

        suffix.Should().Be(",afade=t=in:st=0:d=0.024,afade=t=out:st=4.976:d=0.024");
    }

    [Fact]
    public void BuildSegmentedTransitions_non_overlapping_seams_produce_a_single_block_concat()
    {
        List<ResolvedSpan> spans =
        [
            new(0, 5, 0, 5, 0, 0),
            new(5, 10, 5, 10, 0, 0)
        ];
        var seams = new List<SeamPlan> { Seam(0, SeamTreatment.AudioOnly, 0.05, 0) };
        OutputTimeline timeline = OutputTimeline.Build(spans, seams);

        TransitionFilterBuilder.SegmentedTransitionResult result = TransitionFilterBuilder.BuildSegmentedTransitions(
            seams, timeline, spans.Count, hasAudio: true, i => $"[v{i}]", i => $"[a{i}]");

        result.FilterParts.Should().ContainSingle().Which.Should().Be("[v0][a0][v1][a1]concat=n=2:v=1:a=1[vout][aout]");
        result.DegradedSeamIndices.Should().BeEmpty();
    }

    [Fact]
    public void BuildSegmentedTransitions_overlapping_seam_splits_into_two_blocks_joined_by_xfade()
    {
        List<ResolvedSpan> spans =
        [
            new(0, 5, 0, 5, 0, 0),
            new(5, 10, 5, 10, 0, 0),
            new(10, 15, 10, 15, 0, 0)
        ];
        var seams = new List<SeamPlan>
        {
            Seam(0, SeamTreatment.HardCut, 0, 0),
            Seam(1, SeamTreatment.Dissolve, 1, 1, "fade")
        };
        OutputTimeline timeline = OutputTimeline.Build(spans, seams);

        TransitionFilterBuilder.SegmentedTransitionResult result = TransitionFilterBuilder.BuildSegmentedTransitions(
            seams, timeline, spans.Count, hasAudio: true, i => $"[v{i}]", i => $"[a{i}]");

        result.FilterParts.Should().ContainInOrder(
            "[v0][a0][v1][a1]concat=n=2:v=1:a=1[blk0v][blk0a]",
            "[v2][a2]concat=n=1:v=1:a=1[blk1v][blk1a]",
            "[blk0v][blk1v]xfade=transition=fade:duration=1:offset=9[vout]",
            "[blk0a][blk1a]acrossfade=d=1:c1=tri:c2=tri[aout]");
        result.DegradedSeamIndices.Should().BeEmpty();
    }

    [Fact]
    public void BuildSegmentedTransitions_negative_offset_degrades_the_seam_instead_of_throwing()
    {
        List<ResolvedSpan> spans =
        [
            new(0, 0.5, 0, 0.5, 0, 0),
            new(0.5, 5.5, 0.5, 5.5, 0, 0)
        ];
        var seams = new List<SeamPlan> { Seam(0, SeamTreatment.Dissolve, 1, 1, "fade") };
        OutputTimeline timeline = OutputTimeline.Build(spans, seams);

        TransitionFilterBuilder.SegmentedTransitionResult result = TransitionFilterBuilder.BuildSegmentedTransitions(
            seams, timeline, spans.Count, hasAudio: true, i => $"[v{i}]", i => $"[a{i}]");

        result.DegradedSeamIndices.Should().ContainSingle().Which.Should().Be(0);
        result.FilterParts.Should().ContainSingle().Which.Should().Be("[v0][a0][v1][a1]concat=n=2:v=1:a=1[vout][aout]");
    }

    [Fact]
    public void BuildSegmentedTransitions_no_audio_omits_acrossfade_and_uses_a_0()
    {
        List<ResolvedSpan> spans =
        [
            new(0, 5, 0, 5, 0, 0),
            new(5, 10, 5, 10, 0, 0)
        ];
        var seams = new List<SeamPlan> { Seam(0, SeamTreatment.Dissolve, 1, 1, "fade") };
        OutputTimeline timeline = OutputTimeline.Build(spans, seams);

        TransitionFilterBuilder.SegmentedTransitionResult result = TransitionFilterBuilder.BuildSegmentedTransitions(
            seams, timeline, spans.Count, hasAudio: false, i => $"[v{i}]", i => $"[a{i}]");

        result.FilterParts.Should().OnlyContain(p => !p.Contains("acrossfade") && !p.Contains(":a=1"));
        result.AudioOutLabel.Should().BeNull();
    }
}
