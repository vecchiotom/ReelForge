using ReelForge.Shared.Workflows;

namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// Builds the ffmpeg audio/video filter fragments for per-seam cut transitions (see
/// docs/video-editing.md "Cut transitions"). Pure — no I/O, no ffmpeg — mirroring
/// <see cref="MusicMixFilterBuilder"/>/<see cref="DrawtextFilterBuilder"/>'s role for their own
/// features. Two distinct code paths, matching <c>VideoCompileStepExecutor</c>'s own two encode
/// strategies:
/// <list type="bullet">
/// <item>
/// The <c>select</c>-based single-source path (<c>EncodeReencodeAsync</c>) can only ever carry
/// non-overlapping treatments (<see cref="SeamTreatment.HardCut"/>/<see cref="SeamTreatment.AudioOnly"/>/
/// <see cref="SeamTreatment.DipCut"/>) — any seam needing a real crossfade forces the segmented
/// path instead (<c>select</c> cannot express a crossfade). <see cref="BuildAudioSeamRampExpression"/>
/// (a single global <c>volume=eval=frame</c> trapezoid chain, mirroring
/// <see cref="MusicMixFilterBuilder.BuildVolumeExpression"/>'s own nested-<c>max</c> technique) and
/// <see cref="BuildDipCutVideoFilterSuffix"/> (a chained <c>fade=out</c>/<c>fade=in</c> pair per
/// dipped seam) serve this path.
/// </item>
/// <item>
/// The segmented (<c>concat</c>-based) path (<c>EncodeReencodeSegmentedAsync</c>) handles every
/// treatment, including real crossfades: <see cref="BuildSpanVideoFadeSuffix"/>/
/// <see cref="BuildSpanAudioFadeSuffix"/> add a local, span-relative fade to a
/// <see cref="SeamTreatment.DipCut"/>/<see cref="SeamTreatment.AudioOnly"/> span's own trim/atrim
/// branch, and <see cref="BuildSegmentedTransitions"/> groups spans into maximal non-overlapping
/// "blocks" (each one plain <c>concat</c>), chained pairwise via <c>xfade</c>/<c>acrossfade</c> at
/// every overlapping seam.
/// </item>
/// </list>
/// </summary>
public static class TransitionFilterBuilder
{
    /// <summary>Above this many <see cref="SeamTreatment.AudioOnly"/> seams, the select-path global ramp expression is skipped entirely rather than built arbitrarily long.</summary>
    public const int MaxAudioRampTerms = 64;

    private static string Num(double value) => FfmpegArgvFormat.Number(Math.Round(value, 5));

    // =====================================================================
    // select-path (non-overlapping treatments only)
    // =====================================================================

    /// <summary>
    /// The <c>volume=</c> expression value alone (no <c>volume=eval=frame:volume='...'</c>
    /// wrapper — the caller adds that, exactly like <see cref="MusicMixFilterBuilder.BuildVolumeExpression"/>
    /// is consumed by its own caller) that ramps audio down to silence and back up through every
    /// <see cref="SeamTreatment.AudioOnly"/> seam's own output-timeline second. <c>null</c> when
    /// there are no <see cref="SeamTreatment.AudioOnly"/> seams at all, or when there are more than
    /// <see cref="MaxAudioRampTerms"/> of them (the caller records <c>audioRampSkipped: true</c> in
    /// that case and emits no ramp filter, never an arbitrarily long expression).
    /// </summary>
    public static string? BuildAudioSeamRampExpression(
        IReadOnlyList<SeamPlan> seams, OutputTimeline timeline, double rampSec)
    {
        List<double> seamSeconds = new();
        for (int i = 0; i < seams.Count; i++)
        {
            if (seams[i].Treatment == SeamTreatment.AudioOnly)
                seamSeconds.Add(timeline.Placements[i].OutputEndSec);
        }

        if (seamSeconds.Count == 0 || seamSeconds.Count > MaxAudioRampTerms)
            return null;

        string r = Num(Math.Max(0.001, rampSec));
        string BTerm(double s) => $"clip(1-abs(t-{Num(s)})/{r},0,1)";

        string chain = BTerm(seamSeconds[^1]);
        for (int i = seamSeconds.Count - 2; i >= 0; i--)
            chain = $"max({BTerm(seamSeconds[i])},{chain})";

        return $"1-{chain}";
    }

    /// <summary>
    /// A chained <c>fade=t=out:...,fade=t=in:...</c> pair per <see cref="SeamTreatment.DipCut"/>
    /// seam, in seam order — appended (via a leading filter, never a leading comma; the caller
    /// concatenates label+this+label) onto the select/setpts video output. <c>null</c> when there
    /// are no dipped seams.
    /// </summary>
    public static string? BuildDipCutVideoFilterSuffix(IReadOnlyList<SeamPlan> seams, OutputTimeline timeline)
    {
        List<string> parts = new();
        for (int i = 0; i < seams.Count; i++)
        {
            if (seams[i].Treatment != SeamTreatment.DipCut)
                continue;

            double half = seams[i].DurationSec / 2.0;
            double seamSec = timeline.Placements[i].OutputEndSec;
            string d = Num(half);
            parts.Add($"fade=t=out:st={Num(Math.Max(0, seamSec - half))}:d={d}:color=black");
            parts.Add($"fade=t=in:st={Num(seamSec)}:d={d}:color=black");
        }

        return parts.Count == 0 ? null : string.Join(",", parts);
    }

    // =====================================================================
    // segmented-path (per-span local fades for non-overlapping seams)
    // =====================================================================

    /// <summary>
    /// A local, span-relative video fade suffix (starting with a leading comma, ready to append
    /// directly after a span's own <c>trim=...,setpts=PTS-STARTPTS,scale=...</c> chain) for a
    /// <see cref="SeamTreatment.DipCut"/> seam touching this span on either side. Empty string when
    /// neither neighbor seam is a dip.
    /// </summary>
    public static string BuildSpanVideoFadeSuffix(double spanDurationSec, SeamPlan? seamBefore, SeamPlan? seamAfter)
    {
        List<string> parts = new();

        if (seamBefore is { Treatment: SeamTreatment.DipCut } before)
        {
            double d = before.DurationSec / 2.0;
            parts.Add($"fade=t=in:st=0:d={Num(d)}:color=black");
        }

        if (seamAfter is { Treatment: SeamTreatment.DipCut } after)
        {
            double d = after.DurationSec / 2.0;
            parts.Add($"fade=t=out:st={Num(Math.Max(0, spanDurationSec - d))}:d={Num(d)}:color=black");
        }

        return parts.Count == 0 ? "" : "," + string.Join(",", parts);
    }

    /// <summary>
    /// The audio analogue of <see cref="BuildSpanVideoFadeSuffix"/> for a
    /// <see cref="SeamTreatment.AudioOnly"/> seam touching this span — a local <c>afade</c> at the
    /// span's own head/tail (ramp width <see cref="SeamPlan.DurationSec"/>/2 on each side, mirroring
    /// the select-path global ramp's own half-width-per-side trapezoid).
    /// </summary>
    public static string BuildSpanAudioFadeSuffix(double spanDurationSec, SeamPlan? seamBefore, SeamPlan? seamAfter)
    {
        List<string> parts = new();

        if (seamBefore is { Treatment: SeamTreatment.AudioOnly } before)
        {
            double r = before.DurationSec / 2.0;
            parts.Add($"afade=t=in:st=0:d={Num(r)}");
        }

        if (seamAfter is { Treatment: SeamTreatment.AudioOnly } after)
        {
            double r = after.DurationSec / 2.0;
            parts.Add($"afade=t=out:st={Num(Math.Max(0, spanDurationSec - r))}:d={Num(r)}");
        }

        return parts.Count == 0 ? "" : "," + string.Join(",", parts);
    }

    // =====================================================================
    // segmented-path (overlapping treatments: block grouping + xfade/acrossfade chain)
    // =====================================================================

    public sealed record SegmentedTransitionResult(
        IReadOnlyList<string> FilterParts,
        string VideoOutLabel,
        string? AudioOutLabel,
        IReadOnlyList<int> DegradedSeamIndices);

    /// <summary>
    /// Groups <paramref name="spanCount"/> spans into maximal runs ("blocks") joined only by
    /// non-overlapping seams, concatenates each block, then chains the blocks pairwise via
    /// <c>xfade</c>/<c>acrossfade</c> at every seam whose <see cref="SeamPlan.OverlapSec"/> is
    /// greater than 0. A seam whose computed <c>xfade</c> offset would be negative (the planner's
    /// own clamps are supposed to prevent this, but this is guarded independently — see
    /// docs/video-editing.md "Cut transitions") is treated as non-overlapping instead (merges its
    /// two blocks) and reported in <see cref="SegmentedTransitionResult.DegradedSeamIndices"/>;
    /// this method never throws.
    /// </summary>
    public static SegmentedTransitionResult BuildSegmentedTransitions(
        IReadOnlyList<SeamPlan> seams,
        OutputTimeline timeline,
        int spanCount,
        bool hasAudio,
        Func<int, string> videoLabelForSpan,
        Func<int, string> audioLabelForSpan,
        string videoFinalLabel = "[vout]",
        string audioFinalLabel = "[aout]")
    {
        var degraded = new List<int>();
        var effectiveOverlap = new double[Math.Max(0, seams.Count)];
        for (int i = 0; i < seams.Count; i++)
        {
            double overlap = seams[i].OverlapSec;
            if (overlap > 0)
            {
                double offset = timeline.Placements[i].OutputEndSec - seams[i].DurationSec;
                if (offset < 0)
                {
                    overlap = 0;
                    degraded.Add(i);
                }
            }

            effectiveOverlap[i] = overlap;
        }

        // ---- Group into blocks ----
        List<(int Start, int End)> blocks = new();
        int blockStart = 0;
        for (int seamIdx = 0; seamIdx < spanCount - 1; seamIdx++)
        {
            if (effectiveOverlap[seamIdx] > 0)
            {
                blocks.Add((blockStart, seamIdx));
                blockStart = seamIdx + 1;
            }
        }

        blocks.Add((blockStart, spanCount - 1));

        var filterParts = new List<string>();

        // ---- Concat each block (always via concat, even a single-span "block", so every block
        // uniformly ends up on its own fresh label the xfade chain below can reference). ----
        var blockVideoLabels = new List<string>(blocks.Count);
        var blockAudioLabels = new List<string>(blocks.Count);
        for (int b = 0; b < blocks.Count; b++)
        {
            (int start, int end) = blocks[b];
            int count = end - start + 1;

            var inputs = new System.Text.StringBuilder();
            for (int i = start; i <= end; i++)
            {
                inputs.Append(videoLabelForSpan(i));
                if (hasAudio)
                    inputs.Append(audioLabelForSpan(i));
            }

            bool isLastBlock = b == blocks.Count - 1;
            string videoLabel = blocks.Count == 1 ? videoFinalLabel : $"[blk{b}v]";
            string? audioLabel = !hasAudio ? null : blocks.Count == 1 ? audioFinalLabel : $"[blk{b}a]";

            filterParts.Add(hasAudio
                ? $"{inputs}concat=n={count}:v=1:a=1{videoLabel}{audioLabel}"
                : $"{inputs}concat=n={count}:v=1:a=0{videoLabel}");

            blockVideoLabels.Add(videoLabel);
            blockAudioLabels.Add(audioLabel ?? "");
            _ = isLastBlock;
        }

        // ---- Chain blocks pairwise via xfade/acrossfade at each surviving overlapping seam ----
        string currentVideoLabel = blockVideoLabels[0];
        string currentAudioLabel = blockAudioLabels[0];

        for (int b = 1; b < blocks.Count; b++)
        {
            int seamIdx = blocks[b - 1].End; // the seam connecting block b-1's last span to block b's first span
            SeamPlan seam = seams[seamIdx];
            bool isLast = b == blocks.Count - 1;

            string outVideoLabel = isLast ? videoFinalLabel : $"[xf{b}v]";
            double offset = timeline.Placements[blocks[b - 1].End].OutputEndSec - seam.DurationSec;
            filterParts.Add(
                $"{currentVideoLabel}{blockVideoLabels[b]}xfade=transition={seam.XfadeTransition}:" +
                $"duration={Num(seam.DurationSec)}:offset={Num(Math.Max(0, offset))}{outVideoLabel}");
            currentVideoLabel = outVideoLabel;

            if (hasAudio)
            {
                string outAudioLabel = isLast ? audioFinalLabel : $"[xf{b}a]";
                filterParts.Add(
                    $"{currentAudioLabel}{blockAudioLabels[b]}acrossfade=d={Num(seam.DurationSec)}:c1=tri:c2=tri{outAudioLabel}");
                currentAudioLabel = outAudioLabel;
            }
        }

        return new SegmentedTransitionResult(
            filterParts, videoFinalLabel, hasAudio ? audioFinalLabel : null, degraded);
    }
}
