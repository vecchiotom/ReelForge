using ReelForge.Shared.Workflows;
using ReelForge.WorkflowEngine.Execution.StepExecutors;

namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// One cut-seam's final, fully-resolved treatment — what <see cref="TransitionFilterBuilder"/> and
/// <see cref="OutputTimeline"/> consume. <see cref="Rule"/> records which rule in
/// <see cref="SeamTransitionPlanner"/>'s table matched (e.g. <c>"R1:sameStillShot"</c>), kept even
/// after a later demotion/downgrade changed <see cref="Treatment"/>, since the density cap's own
/// downgrade priority reads it. <see cref="AudioLeadSec"/> is reserved for a future J-cut/L-cut
/// phase — this implementation never sets it to anything but 0 (see the "Explicitly rejected"
/// list in docs/video-editing.md "Cut transitions").
/// </summary>
public sealed record SeamPlan(
    int SeamIndex,
    SeamTreatment Treatment,
    double DurationSec,
    double OverlapSec,
    string XfadeTransition,
    double AudioLeadSec,
    string Rule);

/// <summary>
/// Deterministic (zero LLM/model input) per-seam transition treatment selection, purely from
/// measured <see cref="SeamFacts"/> and workflow-level config — see docs/video-editing.md "Cut
/// transitions". Pure — no I/O, no logging, no static mutable state, fully unit-testable.
/// </summary>
public static class SeamTransitionPlanner
{
    private const string RuleR1 = "R1:sameStillShot";
    private const string RuleR2 = "R2:sameShot";
    private const string RuleR3 = "R3:dupPair";
    private const string RuleR4 = "R4:sectionBreak";
    private const string RuleR5 = "R5:sourceOrLookChange";
    private const string RuleR5b = "R5b:panWhip";
    private const string RuleR6 = "R6:bothMoving";
    private const string RuleR7 = "R7:default";
    private const string RulePolicyAudioOnly = "policy:audioOnly";
    private const string RuleCapped = "capped:segment_count_over_cap";

    /// <summary>Rule prefixes in density-cap downgrade priority order — lowest-priority-match first.</summary>
    private static readonly string[] DensityDowngradeOrder = ["R6", "R3", "R2", "R5", "R4"];

    /// <summary>
    /// Plans every seam's treatment. <paramref name="segmentCountOverCap"/> is <c>true</c> only
    /// when the whole-plan "too many seams" degrade fired (<see cref="VideoCompileStepConfig.MaxTransitionSegments"/>)
    /// — the executor surfaces this as <c>transitions.reason: "segment_count_over_cap"</c>.
    /// </summary>
    internal static IReadOnlyList<SeamPlan> Plan(
        IReadOnlyList<SeamFacts> facts,
        IReadOnlyList<VideoCompileStepExecutor.ResolvedSpan> resolvedSpans,
        VideoCompileStepConfig config,
        bool xfadeAvailable,
        out bool segmentCountOverCap)
    {
        segmentCountOverCap = false;
        int seamCount = Math.Max(0, resolvedSpans.Count - 1);
        if (seamCount == 0)
            return [];

        if (config.TransitionPolicy == VideoTransitionPolicy.Off)
            return BuildHardCutSeams(seamCount);

        if (resolvedSpans.Count - 1 > Math.Max(0, config.MaxTransitionSegments))
        {
            segmentCountOverCap = true;
            return BuildAudioOnlySeams(seamCount, config, RuleCapped);
        }

        if (config.TransitionPolicy == VideoTransitionPolicy.AudioOnly)
            return BuildAudioOnlySeams(seamCount, config, RulePolicyAudioOnly);

        bool expressive = config.TransitionPolicy == VideoTransitionPolicy.Expressive;

        // ---- 1. Rule table (top-down, first match wins) ----
        var plans = new List<SeamPlan>(seamCount);
        for (int i = 0; i < seamCount; i++)
        {
            SeamFacts f = facts.Count > i ? facts[i] : EmptyFacts(i);
            (SeamTreatment treatment, string rule) = EvaluateRule(f, expressive, config);

            // ---- 2. xfade/acrossfade unavailable: demote to DipCut ----
            if (!xfadeAvailable && NeedsXfade(treatment))
                treatment = SeamTreatment.DipCut;

            (double durationSec, double overlapSec, string xfadeTransition) = DurationsFor(treatment, config);
            plans.Add(new SeamPlan(i, treatment, durationSec, overlapSec, xfadeTransition, AudioLeadSec: 0, rule));
        }

        // ---- 3. Per-seam overlap clamp ----
        double maxTransitionSec = Math.Max(0, config.MaxTransitionMs) / 1000.0;
        for (int i = 0; i < plans.Count; i++)
        {
            if (plans[i].OverlapSec <= 0)
                continue;

            double leftSpanDur = SpanDurationSec(resolvedSpans[i]);
            double rightSpanDur = SpanDurationSec(resolvedSpans[i + 1]);
            double cap = Math.Min(maxTransitionSec, 0.5 * Math.Min(leftSpanDur, rightSpanDur));
            double clamped = Math.Min(plans[i].OverlapSec, Math.Max(0, cap));
            if (clamped < plans[i].OverlapSec)
                plans[i] = plans[i] with { OverlapSec = clamped, DurationSec = clamped };
        }

        // ---- 4. Neighbour-sum clamp: a span's own left+right overlap must not exceed 60% of its duration ----
        for (int spanIdx = 1; spanIdx < resolvedSpans.Count - 1; spanIdx++)
        {
            int leftSeam = spanIdx - 1;
            int rightSeam = spanIdx;
            double spanDur = SpanDurationSec(resolvedSpans[spanIdx]);
            double limit = 0.6 * spanDur;

            while (plans[leftSeam].OverlapSec + plans[rightSeam].OverlapSec > limit &&
                   (plans[leftSeam].OverlapSec > 0 || plans[rightSeam].OverlapSec > 0))
            {
                int larger = plans[leftSeam].OverlapSec >= plans[rightSeam].OverlapSec ? leftSeam : rightSeam;
                plans[larger] = DowngradeToAudioOnly(plans[larger], config);
            }
        }

        // ---- 5. Density cap: at most MaxTransitionRatioPct% of seams may carry an overlapping treatment ----
        int maxOverlapping = (int)Math.Floor(seamCount * Math.Clamp(config.MaxTransitionRatioPct, 0, 100) / 100.0);
        List<int> overlappingIndices = plans
            .Select((p, i) => (p, i))
            .Where(t => t.p.OverlapSec > 0)
            .Select(t => t.i)
            .ToList();

        if (overlappingIndices.Count > maxOverlapping)
        {
            int toDowngrade = overlappingIndices.Count - maxOverlapping;
            var remaining = new HashSet<int>(overlappingIndices);

            foreach (string prefix in DensityDowngradeOrder)
            {
                if (toDowngrade <= 0)
                    break;

                List<int> group = remaining
                    .Where(i => plans[i].Rule.StartsWith(prefix, StringComparison.Ordinal))
                    .OrderBy(i => facts.Count > i ? facts[i].RemovedGapSec ?? double.MaxValue : double.MaxValue)
                    .ThenBy(i => i)
                    .ToList();

                foreach (int i in group)
                {
                    if (toDowngrade <= 0)
                        break;

                    plans[i] = DowngradeToAudioOnly(plans[i], config);
                    remaining.Remove(i);
                    toDowngrade--;
                }
            }
        }

        return plans;
    }

    private static SeamFacts EmptyFacts(int seamIndex) => new(
        seamIndex, SameSource: false, RemovedGapSec: null, OutShotId: null, InShotId: null, SameShot: false,
        OutLook: null, InLook: null, LookJump: false, CutOutStill: false, CutInStill: false, DupPair: false,
        OutAudioChar: null, InAudioChar: null, OutMove: null, InMove: null, OutMoveConfidence: null,
        InMoveConfidence: null, EndsSentenceAtSeam: false);

    private static double SpanDurationSec(VideoCompileStepExecutor.ResolvedSpan span) =>
        Math.Max(0, span.SnappedEnd - span.SnappedStart);

    private static bool NeedsXfade(SeamTreatment t) =>
        t is SeamTreatment.SoftCut or SeamTreatment.Dissolve or SeamTreatment.DipToBlack or SeamTreatment.WhipBlur;

    private static SeamPlan DowngradeToAudioOnly(SeamPlan plan, VideoCompileStepConfig config)
    {
        (double durationSec, _, _) = DurationsFor(SeamTreatment.AudioOnly, config);
        return plan with { Treatment = SeamTreatment.AudioOnly, DurationSec = durationSec, OverlapSec = 0, XfadeTransition = "" };
    }

    private static (SeamTreatment Treatment, string Rule) EvaluateRule(SeamFacts f, bool expressive, VideoCompileStepConfig config)
    {
        // R1: same shot, cut moment on BOTH sides falls inside a measured still window.
        if (f.SameShot && f.CutOutStill && f.CutInStill)
            return (SeamTreatment.AudioOnly, RuleR1);

        // R2: same shot otherwise (at least one side is moving).
        if (f.SameShot)
            return (SeamTreatment.SoftCut, RuleR2);

        // R3: a near-duplicate/multi-take pair.
        if (f.DupPair)
            return (SeamTreatment.SoftCut, RuleR3);

        // R4: a genuine section break — a long same-source removed gap landing on a sentence/quiet boundary.
        double sectionBreakSec = Math.Max(0, config.SectionBreakGapMs) / 1000.0;
        if (f.SameSource && f.RemovedGapSec is { } gap && gap >= sectionBreakSec &&
            (f.EndsSentenceAtSeam || f.InAudioChar is "Silent" or "Ambient"))
        {
            return (expressive ? SeamTreatment.DipToBlack : SeamTreatment.Dissolve, RuleR4);
        }

        // R5 / R5b: a different source clip, or a look-group jump within the same source.
        if (!f.SameSource || f.LookJump)
        {
            bool bothConfidentPans =
                f.OutMove == "Pan" && f.InMove == "Pan" &&
                (f.OutMoveConfidence ?? 0) >= 0.6 && (f.InMoveConfidence ?? 0) >= 0.6;

            if (bothConfidentPans)
                return (expressive ? SeamTreatment.WhipBlur : SeamTreatment.Dissolve, RuleR5b);

            return (SeamTreatment.Dissolve, RuleR5);
        }

        // R6: neither side is still (both moving) — a gentle soft cut hides the motion mismatch.
        if (!f.CutOutStill && !f.CutInStill)
            return (SeamTreatment.SoftCut, RuleR6);

        // R7: default — nothing distinctive measured about this seam.
        return (SeamTreatment.AudioOnly, RuleR7);
    }

    private static (double DurationSec, double OverlapSec, string XfadeTransition) DurationsFor(
        SeamTreatment treatment, VideoCompileStepConfig config) => treatment switch
        {
            SeamTreatment.HardCut => (0, 0, ""),
            SeamTreatment.AudioOnly => (2 * SecOf(config.AudioSeamRampMs), 0, ""),
            SeamTreatment.DipCut => (2 * SecOf(config.DipCutMs), 0, ""),
            SeamTreatment.SoftCut => (SecOf(config.SoftCutMs), SecOf(config.SoftCutMs), "fade"),
            SeamTreatment.Dissolve => (SecOf(config.DissolveMs), SecOf(config.DissolveMs), "fade"),
            SeamTreatment.DipToBlack => (SecOf(config.DipToBlackMs), SecOf(config.DipToBlackMs), "fadeblack"),
            // WhipBlur is a dissolve-family crossfade (a whip-pan blend) using the Dissolve duration
            // budget — only the xfade transition name differs from R5/R5b's Dissolve outcome.
            SeamTreatment.WhipBlur => (SecOf(config.DissolveMs), SecOf(config.DissolveMs), "hblur"),
            _ => (0, 0, "")
        };

    private static double SecOf(int ms) => Math.Max(0, ms) / 1000.0;

    private static List<SeamPlan> BuildHardCutSeams(int seamCount)
    {
        var list = new List<SeamPlan>(seamCount);
        for (int i = 0; i < seamCount; i++)
            list.Add(new SeamPlan(i, SeamTreatment.HardCut, 0, 0, "", 0, "off:hardCut"));
        return list;
    }

    private static List<SeamPlan> BuildAudioOnlySeams(int seamCount, VideoCompileStepConfig config, string rule)
    {
        (double durationSec, _, _) = DurationsFor(SeamTreatment.AudioOnly, config);
        var list = new List<SeamPlan>(seamCount);
        for (int i = 0; i < seamCount; i++)
            list.Add(new SeamPlan(i, SeamTreatment.AudioOnly, durationSec, 0, "", 0, rule));
        return list;
    }
}
