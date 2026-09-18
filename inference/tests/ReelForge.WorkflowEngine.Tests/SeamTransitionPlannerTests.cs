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
/// Tests for <see cref="SeamTransitionPlanner"/> — one case per rule R1-R7 (+R5b), the whole-plan
/// policies (Off/AudioOnly), the xfade-unavailable demotion path, and the clamping/density-cap
/// machinery. See docs/video-editing.md "Cut transitions".
/// </summary>
public class SeamTransitionPlannerTests
{
    // MaxTransitionRatioPct defaults to 100 here (not the production default of 35) so these
    // rule-isolation tests are not incidentally cross-cut by the density cap — floor(seamCount *
    // pct/100) can floor to 0 for a single-seam fixture at the production default, which is its
    // own behavior covered separately by the dedicated density-cap tests below.
    private static VideoCompileStepConfig Config(VideoTransitionPolicy policy) => new(
        Version: 1, Decision: new ExtractInputRef(ExtractInputSource.Previous), AnalysisStepOrder: 1,
        TransitionPolicy: policy, MaxTransitionRatioPct: 100);

    private static SeamFacts Facts(
        bool sameSource = true, double? removedGap = null, bool sameShot = false, bool lookJump = false,
        bool cutOutStill = false, bool cutInStill = false, bool dupPair = false,
        string? outAudioChar = null, string? inAudioChar = null,
        string? outMove = null, string? inMove = null, double? outMoveConf = null, double? inMoveConf = null,
        bool endsSentence = false) =>
        new(0, sameSource, removedGap, "s0", "s1", sameShot, null, null, lookJump, cutOutStill, cutInStill,
            dupPair, outAudioChar, inAudioChar, outMove, inMove, outMoveConf, inMoveConf, endsSentence);

    private static List<ResolvedSpan> TwoSpans(double dur1 = 10, double dur2 = 10) =>
    [
        new(0, dur1, 0, dur1, 0, 0),
        new(dur1, dur1 + dur2, dur1, dur1 + dur2, 0, 0)
    ];

    [Theory]
    [InlineData(VideoTransitionPolicy.Auto)]
    [InlineData(VideoTransitionPolicy.Expressive)]
    public void R1_same_still_shot_is_AudioOnly(VideoTransitionPolicy policy)
    {
        SeamFacts f = Facts(sameShot: true, cutOutStill: true, cutInStill: true);
        IReadOnlyList<SeamPlan> plan = SeamTransitionPlanner.Plan([f], TwoSpans(), Config(policy), xfadeAvailable: true, out bool capped);

        plan[0].Treatment.Should().Be(SeamTreatment.AudioOnly);
        plan[0].Rule.Should().StartWith("R1");
        plan[0].OverlapSec.Should().Be(0);
        capped.Should().BeFalse();
    }

    [Theory]
    [InlineData(VideoTransitionPolicy.Auto)]
    [InlineData(VideoTransitionPolicy.Expressive)]
    public void R2_same_shot_moving_is_SoftCut(VideoTransitionPolicy policy)
    {
        SeamFacts f = Facts(sameShot: true, cutOutStill: false, cutInStill: true);
        IReadOnlyList<SeamPlan> plan = SeamTransitionPlanner.Plan([f], TwoSpans(), Config(policy), xfadeAvailable: true, out _);

        plan[0].Treatment.Should().Be(SeamTreatment.SoftCut);
        plan[0].Rule.Should().StartWith("R2");
        plan[0].OverlapSec.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData(VideoTransitionPolicy.Auto)]
    [InlineData(VideoTransitionPolicy.Expressive)]
    public void R3_duplicate_pair_is_SoftCut(VideoTransitionPolicy policy)
    {
        SeamFacts f = Facts(dupPair: true);
        IReadOnlyList<SeamPlan> plan = SeamTransitionPlanner.Plan([f], TwoSpans(), Config(policy), xfadeAvailable: true, out _);

        plan[0].Treatment.Should().Be(SeamTreatment.SoftCut);
        plan[0].Rule.Should().StartWith("R3");
    }

    [Fact]
    public void R4_section_break_is_Dissolve_under_Auto()
    {
        SeamFacts f = Facts(removedGap: 10, endsSentence: true);
        IReadOnlyList<SeamPlan> plan = SeamTransitionPlanner.Plan(
            [f], TwoSpans(), Config(VideoTransitionPolicy.Auto), xfadeAvailable: true, out _);

        plan[0].Treatment.Should().Be(SeamTreatment.Dissolve);
        plan[0].Rule.Should().StartWith("R4");
        plan[0].XfadeTransition.Should().Be("fade");
    }

    [Fact]
    public void R4_section_break_is_DipToBlack_under_Expressive()
    {
        SeamFacts f = Facts(removedGap: 10, inAudioChar: "Silent");
        IReadOnlyList<SeamPlan> plan = SeamTransitionPlanner.Plan(
            [f], TwoSpans(), Config(VideoTransitionPolicy.Expressive), xfadeAvailable: true, out _);

        plan[0].Treatment.Should().Be(SeamTreatment.DipToBlack);
        plan[0].Rule.Should().StartWith("R4");
        plan[0].XfadeTransition.Should().Be("fadeblack");
    }

    [Fact]
    public void R4_does_not_match_when_gap_is_short()
    {
        SeamFacts f = Facts(removedGap: 1, endsSentence: true);
        IReadOnlyList<SeamPlan> plan = SeamTransitionPlanner.Plan(
            [f], TwoSpans(), Config(VideoTransitionPolicy.Auto), xfadeAvailable: true, out _);

        plan[0].Rule.Should().NotStartWith("R4");
    }

    [Theory]
    [InlineData(VideoTransitionPolicy.Auto)]
    [InlineData(VideoTransitionPolicy.Expressive)]
    public void R5_different_source_is_Dissolve(VideoTransitionPolicy policy)
    {
        SeamFacts f = Facts(sameSource: false);
        IReadOnlyList<SeamPlan> plan = SeamTransitionPlanner.Plan([f], TwoSpans(), Config(policy), xfadeAvailable: true, out _);

        plan[0].Treatment.Should().Be(SeamTreatment.Dissolve);
        plan[0].Rule.Should().StartWith("R5:");
    }

    [Theory]
    [InlineData(VideoTransitionPolicy.Auto)]
    [InlineData(VideoTransitionPolicy.Expressive)]
    public void R5_look_jump_within_same_source_is_Dissolve(VideoTransitionPolicy policy)
    {
        SeamFacts f = new(0, true, null, "s0", "s1", false, "k0", "k1", true, false, false, false,
            null, null, null, null, null, null, false);

        IReadOnlyList<SeamPlan> plan = SeamTransitionPlanner.Plan([f], TwoSpans(), Config(policy), xfadeAvailable: true, out _);

        plan[0].Treatment.Should().Be(SeamTreatment.Dissolve);
        plan[0].Rule.Should().StartWith("R5:");
    }

    [Fact]
    public void R5b_confident_pan_pair_is_WhipBlur_under_Expressive()
    {
        SeamFacts f = Facts(sameSource: false, outMove: "Pan", inMove: "Pan", outMoveConf: 0.9, inMoveConf: 0.8);
        IReadOnlyList<SeamPlan> plan = SeamTransitionPlanner.Plan(
            [f], TwoSpans(), Config(VideoTransitionPolicy.Expressive), xfadeAvailable: true, out _);

        plan[0].Treatment.Should().Be(SeamTreatment.WhipBlur);
        plan[0].Rule.Should().StartWith("R5b");
        plan[0].XfadeTransition.Should().Be("hblur");
    }

    [Fact]
    public void R5b_confident_pan_pair_stays_Dissolve_under_Auto()
    {
        SeamFacts f = Facts(sameSource: false, outMove: "Pan", inMove: "Pan", outMoveConf: 0.9, inMoveConf: 0.8);
        IReadOnlyList<SeamPlan> plan = SeamTransitionPlanner.Plan(
            [f], TwoSpans(), Config(VideoTransitionPolicy.Auto), xfadeAvailable: true, out _);

        plan[0].Treatment.Should().Be(SeamTreatment.Dissolve);
        plan[0].Rule.Should().StartWith("R5b");
    }

    [Theory]
    [InlineData(VideoTransitionPolicy.Auto)]
    [InlineData(VideoTransitionPolicy.Expressive)]
    public void R6_both_moving_is_SoftCut(VideoTransitionPolicy policy)
    {
        SeamFacts f = Facts(cutOutStill: false, cutInStill: false);
        IReadOnlyList<SeamPlan> plan = SeamTransitionPlanner.Plan([f], TwoSpans(), Config(policy), xfadeAvailable: true, out _);

        plan[0].Treatment.Should().Be(SeamTreatment.SoftCut);
        plan[0].Rule.Should().StartWith("R6");
    }

    [Theory]
    [InlineData(VideoTransitionPolicy.Auto)]
    [InlineData(VideoTransitionPolicy.Expressive)]
    public void R7_default_is_AudioOnly(VideoTransitionPolicy policy)
    {
        // Nothing distinctive: same source, not same shot, no gap, both sides still (so R6 does not match).
        SeamFacts f = Facts(cutOutStill: true, cutInStill: true);
        IReadOnlyList<SeamPlan> plan = SeamTransitionPlanner.Plan([f], TwoSpans(), Config(policy), xfadeAvailable: true, out _);

        plan[0].Treatment.Should().Be(SeamTreatment.AudioOnly);
        plan[0].Rule.Should().StartWith("R7");
    }

    [Fact]
    public void Off_policy_produces_all_hard_cuts_with_zero_audio_lead()
    {
        SeamFacts f = Facts(sameSource: false); // would otherwise match R5
        IReadOnlyList<SeamPlan> plan = SeamTransitionPlanner.Plan(
            [f], TwoSpans(), Config(VideoTransitionPolicy.Off), xfadeAvailable: true, out bool capped);

        plan[0].Treatment.Should().Be(SeamTreatment.HardCut);
        plan[0].DurationSec.Should().Be(0);
        plan[0].OverlapSec.Should().Be(0);
        plan[0].AudioLeadSec.Should().Be(0);
        capped.Should().BeFalse();
    }

    [Fact]
    public void AudioOnly_policy_forces_every_seam_regardless_of_table()
    {
        SeamFacts f = Facts(sameSource: false); // would otherwise match R5 (Dissolve)
        IReadOnlyList<SeamPlan> plan = SeamTransitionPlanner.Plan(
            [f], TwoSpans(), Config(VideoTransitionPolicy.AudioOnly), xfadeAvailable: true, out _);

        plan[0].Treatment.Should().Be(SeamTreatment.AudioOnly);
        plan[0].OverlapSec.Should().Be(0);
    }

    [Fact]
    public void Every_seam_AudioLeadSec_is_always_zero_regardless_of_treatment()
    {
        SeamFacts f = Facts(sameSource: false);
        IReadOnlyList<SeamPlan> plan = SeamTransitionPlanner.Plan(
            [f], TwoSpans(), Config(VideoTransitionPolicy.Expressive), xfadeAvailable: true, out _);

        plan[0].AudioLeadSec.Should().Be(0);
    }

    [Fact]
    public void Xfade_unavailable_demotes_overlapping_treatments_to_DipCut()
    {
        SeamFacts f = Facts(sameSource: false); // R5 -> Dissolve when xfade available
        IReadOnlyList<SeamPlan> plan = SeamTransitionPlanner.Plan(
            [f], TwoSpans(), Config(VideoTransitionPolicy.Auto), xfadeAvailable: false, out _);

        plan[0].Treatment.Should().Be(SeamTreatment.DipCut);
        plan[0].OverlapSec.Should().Be(0);
        plan[0].XfadeTransition.Should().BeEmpty();
    }

    [Fact]
    public void Segment_count_over_cap_drops_every_overlap_and_reports_capped()
    {
        var facts = new List<SeamFacts>
        {
            Facts(sameSource: false), // seam 0: source break -> would be Dissolve
            Facts(sameSource: false)  // seam 1: source break -> would be Dissolve
        };
        List<ResolvedSpan> spans =
        [
            new(0, 10, 0, 10, 0, 0),
            new(10, 20, 10, 20, 0, 0),
            new(20, 30, 20, 30, 0, 0)
        ];
        VideoCompileStepConfig config = Config(VideoTransitionPolicy.Auto) with { MaxTransitionSegments = 1 };

        IReadOnlyList<SeamPlan> plan = SeamTransitionPlanner.Plan(facts, spans, config, xfadeAvailable: true, out bool capped);

        capped.Should().BeTrue();
        plan.Should().OnlyContain(p => p.OverlapSec == 0);
    }

    [Fact]
    public void Neighbour_sum_clamp_downgrades_the_larger_overlap_when_a_spans_own_budget_is_exceeded()
    {
        // Two consecutive seams around the SAME middle span (span 1), both wanting a big Dissolve
        // overlap that together exceed 60% of span 1's own duration (2s) — the middle span here is
        // deliberately short so both 0.5s overlaps (1s total) exceed 0.6*2=1.2s only marginally;
        // use a source-break gap large enough to force the point home.
        var facts = new List<SeamFacts>
        {
            Facts(sameSource: false),
            Facts(sameSource: false)
        };
        List<ResolvedSpan> spans =
        [
            new(0, 10, 0, 10, 0, 0),
            new(10, 11, 10, 11, 0, 0), // 1s middle span
            new(11, 21, 11, 21, 0, 0)
        ];
        VideoCompileStepConfig config = Config(VideoTransitionPolicy.Auto) with { DissolveMs = 800, MaxTransitionMs = 5000 };

        IReadOnlyList<SeamPlan> plan = SeamTransitionPlanner.Plan(facts, spans, config, xfadeAvailable: true, out _);

        (plan[0].OverlapSec + plan[1].OverlapSec).Should().BeLessThanOrEqualTo(0.6 * 1.0 + 1e-9);
        plan.Should().Contain(p => p.Treatment == SeamTreatment.AudioOnly);
    }

    [Fact]
    public void Density_cap_downgrades_R6_matches_before_R4_matches()
    {
        // Four seams: two R6 (both-moving SoftCut) and two R4 (section-break Dissolve), long spans
        // so per-seam/neighbour clamps never fire — only the density cap should downgrade anything.
        var facts = new List<SeamFacts>
        {
            Facts(cutOutStill: false, cutInStill: false),               // R6
            Facts(removedGap: 10, endsSentence: true),                  // R4
            Facts(cutOutStill: false, cutInStill: false),               // R6
            Facts(removedGap: 10, endsSentence: true)                   // R4
        };
        List<ResolvedSpan> spans =
        [
            new(0, 100, 0, 100, 0, 0),
            new(100, 200, 100, 200, 0, 0),
            new(200, 300, 200, 300, 0, 0),
            new(300, 400, 300, 400, 0, 0),
            new(400, 500, 400, 500, 0, 0)
        ];
        VideoCompileStepConfig config = Config(VideoTransitionPolicy.Auto) with { MaxTransitionRatioPct = 25 };

        IReadOnlyList<SeamPlan> plan = SeamTransitionPlanner.Plan(facts, spans, config, xfadeAvailable: true, out _);

        // Only 1 of 4 seams may keep an overlap (floor(4*0.25)=1) — the R6 matches must be the ones
        // downgraded first, so the surviving overlap must be an R4 (Dissolve) seam, not an R6 (SoftCut) one.
        plan.Count(p => p.OverlapSec > 0).Should().Be(1);
        plan.Single(p => p.OverlapSec > 0).Rule.Should().StartWith("R4");
        plan.Where(p => p.Rule.StartsWith("R6")).Should().OnlyContain(p => p.Treatment == SeamTreatment.AudioOnly);
    }
}
