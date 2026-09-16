using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using ReelForge.Shared.Workflows;
using ReelForge.WorkflowEngine.Services.Video;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Pure unit tests for <see cref="MusicMixPlanner.PlanLiftWindows"/> — no ffmpeg, no executor, just
/// the deterministic basis-selection/merge/drop/cap logic (see docs/video-editing.md "Background
/// music"). A fake <c>mapSourceWindowToOutput</c> delegate stands in for
/// <c>VideoCompileStepExecutor.MapSourceWindowToOutput</c> so these tests never depend on the
/// executor's own span-resolution machinery.
/// </summary>
public class MusicMixPlannerTests
{
    private static VideoAnalysisSilenceSpan Silence(string id, double start, double end, int src = 0) =>
        new(id, start, end, null, src);

    private static VideoAnalysisSegment Segment(string id, double start, double end, int src = 0) =>
        new(id, null, start, end, "text", src);

    /// <summary>Identity mapping (1:1 source-to-output, single kept span covering [0, cap)) — the simplest fake for tests that don't care about cut boundaries.</summary>
    private static Func<double, double, int, (double, double)?> IdentityMap(double cap = 1_000, int onlySource = 0) =>
        (start, end, src) =>
        {
            if (src != onlySource)
                return null;
            double s = Math.Max(0, start);
            double e = Math.Min(cap, end);
            return e > s ? (s, e) : null;
        };

    [Fact]
    public void Silence_gaps_are_preferred_over_transcript_complement()
    {
        MusicLiftPlan plan = MusicMixPlanner.PlanLiftWindows(
            silenceSpans: [Silence("g0", 2, 5)],
            segments: [Segment("t0", 0, 2)],
            mapSourceWindowToOutput: IdentityMap(),
            outputDurationSec: 10, rampSec: 0.2, minWindowSec: 0.5, mergeSec: 0.1, maxWindows: 10);

        plan.Basis.Should().Be("silenceGaps");
        plan.Windows.Should().ContainSingle(w => w.StartSec == 2 && w.EndSec == 5);
    }

    [Fact]
    public void Falls_back_to_speech_complement_when_no_silence_spans_exist()
    {
        MusicLiftPlan plan = MusicMixPlanner.PlanLiftWindows(
            silenceSpans: [],
            segments: [Segment("t0", 3, 6)],
            mapSourceWindowToOutput: IdentityMap(cap: 6),
            outputDurationSec: 6, rampSec: 0.2, minWindowSec: 0.5, mergeSec: 0.1, maxWindows: 10);

        plan.Basis.Should().Be("speechComplement");
        // Complement of [3,6): [0,3) before the segment. The open-ended tail after 6 maps to
        // nothing because IdentityMap caps at 6.
        plan.Windows.Should().ContainSingle(w => w.StartSec == 0 && w.EndSec == 3);
    }

    [Fact]
    public void No_silence_and_no_segments_lifts_the_whole_edit()
    {
        MusicLiftPlan plan = MusicMixPlanner.PlanLiftWindows(
            silenceSpans: [],
            segments: [],
            mapSourceWindowToOutput: IdentityMap(),
            outputDurationSec: 12, rampSec: 0.2, minWindowSec: 0.5, mergeSec: 0.1, maxWindows: 10);

        plan.Basis.Should().Be("noSpeechDetected");
        plan.Windows.Should().ContainSingle(w => w.StartSec == 0 && w.EndSec == 12);
        plan.LiftCoveragePct.Should().Be(100);
    }

    [Fact]
    public void A_gap_entirely_inside_a_cut_region_produces_no_window()
    {
        // mapSourceWindowToOutput returns null for anything in [2,5) — simulating a cut gap.
        Func<double, double, int, (double, double)?> mapWithGap = (start, end, src) =>
        {
            if (start >= 2 && end <= 5)
                return null;
            return (start, end);
        };

        MusicLiftPlan plan = MusicMixPlanner.PlanLiftWindows(
            silenceSpans: [Silence("g0", 2, 5)],
            segments: [],
            mapSourceWindowToOutput: mapWithGap,
            outputDurationSec: 10, rampSec: 0.2, minWindowSec: 0.5, mergeSec: 0.1, maxWindows: 10);

        plan.Windows.Should().BeEmpty();
    }

    [Fact]
    public void A_gap_straddling_a_cut_boundary_yields_only_its_first_kept_portion()
    {
        // Simulate MapSourceWindowToOutput's own "first kept portion" contract: a window
        // [2,8) straddling a cut at 5 maps to only [2,5) in this fake.
        Func<double, double, int, (double, double)?> mapClipped = (start, end, src) => (start, Math.Min(end, 5));

        MusicLiftPlan plan = MusicMixPlanner.PlanLiftWindows(
            silenceSpans: [Silence("g0", 2, 8)],
            segments: [],
            mapSourceWindowToOutput: mapClipped,
            outputDurationSec: 10, rampSec: 0.2, minWindowSec: 0.5, mergeSec: 0.1, maxWindows: 10);

        plan.Windows.Should().ContainSingle(w => w.StartSec == 2 && w.EndSec == 5);
    }

    [Fact]
    public void Windows_within_merge_threshold_are_merged()
    {
        MusicLiftPlan plan = MusicMixPlanner.PlanLiftWindows(
            silenceSpans: [Silence("g0", 2, 4), Silence("g1", 4.2, 6)],
            segments: [],
            mapSourceWindowToOutput: IdentityMap(),
            outputDurationSec: 10, rampSec: 0.1, minWindowSec: 0.1, mergeSec: 0.5, maxWindows: 10);

        plan.Windows.Should().ContainSingle(w => w.StartSec == 2 && w.EndSec == 6);
    }

    [Fact]
    public void Windows_shorter_than_min_window_are_dropped()
    {
        MusicLiftPlan plan = MusicMixPlanner.PlanLiftWindows(
            silenceSpans: [Silence("g0", 2, 2.3)], // 0.3s, below the 0.5*2+0.2=1.2s floor
            segments: [],
            mapSourceWindowToOutput: IdentityMap(),
            outputDurationSec: 10, rampSec: 0.5, minWindowSec: 0.1, mergeSec: 0.05, maxWindows: 10);

        plan.Windows.Should().BeEmpty();
    }

    [Fact]
    public void Window_exactly_at_the_min_window_boundary_survives()
    {
        // minKeep = max(minWindowSec, 2*rampSec+0.2) = max(0.1, 2*0.1+0.2) = 0.4
        MusicLiftPlan plan = MusicMixPlanner.PlanLiftWindows(
            silenceSpans: [Silence("g0", 2, 2.4)],
            segments: [],
            mapSourceWindowToOutput: IdentityMap(),
            outputDurationSec: 10, rampSec: 0.1, minWindowSec: 0.1, mergeSec: 0.01, maxWindows: 10);

        plan.Windows.Should().ContainSingle();
    }

    [Fact]
    public void MaxWindows_keeps_the_longest_then_resorts_chronologically()
    {
        MusicLiftPlan plan = MusicMixPlanner.PlanLiftWindows(
            silenceSpans:
            [
                Silence("g0", 0, 1),   // 1s
                Silence("g1", 10, 13), // 3s (longest)
                Silence("g2", 20, 21.5) // 1.5s
            ],
            segments: [],
            mapSourceWindowToOutput: IdentityMap(cap: 100),
            outputDurationSec: 100, rampSec: 0.1, minWindowSec: 0.1, mergeSec: 0.01, maxWindows: 2);

        plan.Windows.Should().HaveCount(2);
        plan.Windows[0].StartSec.Should().Be(10, "the two longest windows are g1 and g2, re-sorted chronologically");
        plan.Windows[1].StartSec.Should().Be(20);
    }

    [Fact]
    public void A_gap_on_one_source_never_maps_against_a_different_source_overlapping_range()
    {
        // mapSourceWindowToOutput only honors source 1; source 0 always returns null (simulating
        // a two-source artifact where source 0's numeric range happens to overlap source 1's).
        Func<double, double, int, (double, double)?> perSourceMap = (start, end, src) =>
            src == 1 ? (start, end) : null;

        MusicLiftPlan plan = MusicMixPlanner.PlanLiftWindows(
            silenceSpans: [Silence("g0", 2, 5, src: 0), Silence("g1", 2, 5, src: 1)],
            segments: [],
            mapSourceWindowToOutput: perSourceMap,
            outputDurationSec: 10, rampSec: 0.1, minWindowSec: 0.1, mergeSec: 0.01, maxWindows: 10);

        plan.Windows.Should().ContainSingle(w => w.StartSec == 2 && w.EndSec == 5);
    }

    [Fact]
    public void SpeechCoveragePct_and_LiftCoveragePct_arithmetic()
    {
        MusicLiftPlan plan = MusicMixPlanner.PlanLiftWindows(
            silenceSpans: [Silence("g0", 0, 3)],
            segments: [Segment("t0", 3, 8)], // 5s of an 10s output = 50% speech coverage
            mapSourceWindowToOutput: IdentityMap(cap: 10),
            outputDurationSec: 10, rampSec: 0.1, minWindowSec: 0.1, mergeSec: 0.01, maxWindows: 10);

        plan.LiftCoveragePct.Should().Be(30); // 3s lift / 10s output
        plan.SpeechCoveragePct.Should().Be(50);
    }
}
