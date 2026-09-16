using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using ReelForge.WorkflowEngine.Services.Video;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

using Stage = VideoAnalyzeProgressPlan.Stage;

/// <summary>
/// Pure unit tests for <see cref="VideoAnalyzeProgressPlan"/> — no executor, no I/O. See Phase 4
/// plan §3 for the weighted-percent algorithm this pins down.
/// </summary>
public class VideoAnalyzeProgressPlanTests
{
    private static readonly Stage[] PerSourceStagesInOrder =
    {
        Stage.DownloadSource, Stage.ProbeSource, Stage.DetectSilence, Stage.DetectShots,
        Stage.ResolveTranscription, Stage.Transcribe, Stage.SampleAudioLevels,
        Stage.SampleFrameGrid, Stage.AnalyzeShots, Stage.SampleSharpness, Stage.GroupDuplicates
    };

    private static readonly Stage[] StepStagesInOrder =
    {
        Stage.MatchLooks, Stage.ListMusicCandidates, Stage.ExtractKeyframes, Stage.CaptionShots,
        Stage.BuildView, Stage.UploadArtifact
    };

    private static readonly Stage[] AllStages = Enum.GetValues<Stage>();

    [Fact]
    public void Percent_is_monotonically_non_decreasing_across_the_full_stage_sequence_and_ends_at_100()
    {
        const int sourceCount = 2;
        var plan = new VideoAnalyzeProgressPlan(AllStages, sourceCount);
        var observed = new List<int>();

        for (int i = 0; i < sourceCount; i++)
            foreach (Stage stage in PerSourceStagesInOrder)
            {
                observed.Add(plan.Percent(stage, i, 0));
                observed.Add(plan.Percent(stage, i, 0.5));
                observed.Add(plan.Percent(stage, i, 1));
            }

        foreach (Stage stage in StepStagesInOrder)
        {
            observed.Add(plan.Percent(stage, 0, 0));
            observed.Add(plan.Percent(stage, 0, 0.5));
            observed.Add(plan.Percent(stage, 0, 1));
        }

        observed.Should().BeInAscendingOrder(because: "the monotonic clamp must never let percent regress");
        observed[^1].Should().Be(100);
    }

    [Fact]
    public void Percent_never_goes_backwards_at_a_source_boundary()
    {
        var plan = new VideoAnalyzeProgressPlan(AllStages, sourceCount: 3);

        int endOfSource0 = plan.Percent(Stage.GroupDuplicates, sourceIndex: 0, fractionWithinStage: 1);
        int startOfSource1 = plan.Percent(Stage.DownloadSource, sourceIndex: 1, fractionWithinStage: 0);

        startOfSource1.Should().BeGreaterThanOrEqualTo(endOfSource0);
    }

    [Fact]
    public void Disabled_stage_weight_is_redistributed_across_the_enabled_ones()
    {
        var withTranscription = new VideoAnalyzeProgressPlan(
            new[] { Stage.DownloadSource, Stage.ProbeSource, Stage.DetectSilence, Stage.DetectShots,
                     Stage.ResolveTranscription, Stage.Transcribe, Stage.BuildView, Stage.UploadArtifact },
            sourceCount: 1);
        var withoutTranscription = new VideoAnalyzeProgressPlan(
            new[] { Stage.DownloadSource, Stage.ProbeSource, Stage.DetectSilence, Stage.DetectShots,
                     Stage.BuildView, Stage.UploadArtifact },
            sourceCount: 1);

        int withPercent = withTranscription.Percent(Stage.DetectShots, 0, 1);
        int withoutPercent = withoutTranscription.Percent(Stage.DetectShots, 0, 1);

        withoutPercent.Should().BeGreaterThan(withPercent,
            "the same completed work is a larger share of a smaller enabled total");
    }

    [Fact]
    public void Two_sources_split_the_per_source_block_in_half()
    {
        var plan1 = new VideoAnalyzeProgressPlan(PerSourceStagesInOrder, sourceCount: 2);
        int endOfSource0 = plan1.Percent(Stage.GroupDuplicates, 0, 1);

        var plan2 = new VideoAnalyzeProgressPlan(PerSourceStagesInOrder, sourceCount: 2);
        int startOfSource1 = plan2.Percent(Stage.DownloadSource, 1, 0);

        Math.Abs(endOfSource0 - startOfSource1).Should().BeLessThanOrEqualTo(1,
            "source 0 finishing its block and source 1 starting its own should land at ~the same percent");
    }

    [Fact]
    public void A_stage_that_was_never_enabled_still_returns_a_clamped_monotonic_percent()
    {
        var plan = new VideoAnalyzeProgressPlan(new[] { Stage.DownloadSource, Stage.BuildView, Stage.UploadArtifact }, sourceCount: 1);

        int beforeDisabledCall = plan.Percent(Stage.DownloadSource, 0, 1);
        int disabledCall = plan.Percent(Stage.SampleSharpness, 0, 1); // never in `enabled`
        int afterward = plan.Percent(Stage.BuildView, 0, 1);

        disabledCall.Should().BeInRange(0, 100);
        disabledCall.Should().BeGreaterThanOrEqualTo(beforeDisabledCall);
        afterward.Should().BeGreaterThanOrEqualTo(disabledCall);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(50)]
    [InlineData(500)]
    [InlineData(5000)]
    public void ShouldReportItem_emits_at_most_about_twenty_events_for_any_count(int count)
    {
        int events = Enumerable.Range(0, count).Count(i => VideoAnalyzeProgressPlan.ShouldReportItem(i, count));

        events.Should().BeLessThanOrEqualTo(22);
        VideoAnalyzeProgressPlan.ShouldReportItem(0, count).Should().BeTrue();
        VideoAnalyzeProgressPlan.ShouldReportItem(count - 1, count).Should().BeTrue();
    }

    [Fact]
    public void Fraction_within_stage_interpolates_inside_that_stages_band_only()
    {
        var plan = new VideoAnalyzeProgressPlan(new[] { Stage.DownloadSource, Stage.ProbeSource, Stage.BuildView, Stage.UploadArtifact }, sourceCount: 1);

        int atStart = plan.Percent(Stage.DownloadSource, 0, 0);

        var plan2 = new VideoAnalyzeProgressPlan(new[] { Stage.DownloadSource, Stage.ProbeSource, Stage.BuildView, Stage.UploadArtifact }, sourceCount: 1);
        int atHalf = plan2.Percent(Stage.DownloadSource, 0, 0.5);

        var plan3 = new VideoAnalyzeProgressPlan(new[] { Stage.DownloadSource, Stage.ProbeSource, Stage.BuildView, Stage.UploadArtifact }, sourceCount: 1);
        int atFull = plan3.Percent(Stage.DownloadSource, 0, 1);

        var plan4 = new VideoAnalyzeProgressPlan(new[] { Stage.DownloadSource, Stage.ProbeSource, Stage.BuildView, Stage.UploadArtifact }, sourceCount: 1);
        int nextStageStart = plan4.Percent(Stage.ProbeSource, 0, 0);

        atStart.Should().BeLessThanOrEqualTo(atHalf);
        atHalf.Should().BeLessThanOrEqualTo(atFull);
        atFull.Should().BeLessThanOrEqualTo(nextStageStart);
    }
}
