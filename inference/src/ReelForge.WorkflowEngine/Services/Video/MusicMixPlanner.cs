using ReelForge.Shared.Workflows;

namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// One window, on the compiled edit's OUTPUT timeline, where the background-music bed should be
/// lifted above its ducked baseline (see docs/video-editing.md "Background music",
/// <see cref="MusicMixFilterBuilder"/>).
/// </summary>
public sealed record MusicLiftWindow(double StartSec, double EndSec);

/// <summary>
/// The result of <see cref="MusicMixPlanner.PlanLiftWindows"/>: the final, capped, merged, output-
/// timeline lift windows plus the reporting numbers surfaced in the compile step's own output JSON
/// (<c>music.duckBasis</c>/<c>music.liftCoveragePct</c>/<c>music.speechCoveragePct</c>).
/// </summary>
public sealed record MusicLiftPlan(
    IReadOnlyList<MusicLiftWindow> Windows,
    string Basis,
    double SpeechCoveragePct,
    double LiftCoveragePct);

/// <summary>
/// Plans WHERE (on the compiled edit's own output timeline) a background-music bed should be
/// lifted above its ducked baseline, from the already-computed silence gaps / transcript segments
/// a <c>VideoAnalyze</c> step produced. Pure static, no I/O, no ffmpeg — the
/// <c>OverlayPlacementBuilder</c> precedent for this feature's deterministic planning code.
/// </summary>
/// <remarks>
/// <b>Why a deterministic envelope over silence gaps, not a runtime compressor
/// (<c>sidechaincompress</c>):</b> the artifact already carries silence gaps (available with zero
/// dependency on ASR) and transcript segments, and <see cref="MusicMixFilterBuilder.BuildVolumeExpression"/>
/// turns whatever this method decides into an EXACT, assertable ffmpeg filter string. A
/// sidechain compressor's behavior instead depends on the actual waveform at encode time — nothing
/// here could assert an exact filter string for it, explain "the music was lifted in these 3
/// windows" in the step's own output, or guarantee it behaves sanely on a source whose dialogue
/// track already has music baked in. See docs/video-editing.md "Background music".
///
/// <para>
/// <b>Basis selection</b> (in order — the first one with any data wins): <c>silenceGaps</c>
/// (preferred — always available with <c>DetectSilence</c>, and the same physically-grounded
/// "speech has actually stopped" signal <c>VideoCompileStepExecutor.ExtendSegmentEndTowardNextSilence</c>
/// already trusts over ASR boundaries); else <c>speechComplement</c> (the gaps BETWEEN transcript
/// segments, per source clip); else <c>noSpeechDetected</c> — a single lift window spanning the
/// WHOLE edit, since there is no dialogue anywhere in it to protect the bed from.
/// </para>
/// </remarks>
public static class MusicMixPlanner
{
    /// <summary>
    /// A large-but-finite sentinel used as the open end of a "from the end of the last segment of
    /// this source to the end of time" complement window — the real source duration is not known
    /// to this pure planner, but it does not need to be: <paramref name="mapSourceWindowToOutput"/>
    /// clips every candidate window to the kept spans anyway, so an over-long candidate simply maps
    /// to whatever portion is actually kept (or null if none is).
    /// </summary>
    private const double FarFutureSec = 1_000_000.0;

    public static MusicLiftPlan PlanLiftWindows(
        IReadOnlyList<VideoAnalysisSilenceSpan> silenceSpans,
        IReadOnlyList<VideoAnalysisSegment> segments,
        Func<double, double, int, (double Start, double End)?> mapSourceWindowToOutput,
        double outputDurationSec,
        double rampSec,
        double minWindowSec,
        double mergeSec,
        int maxWindows)
    {
        double d = Math.Max(0, outputDurationSec);

        string basis;
        List<(double Start, double End, int SourceIndex)> candidates;

        if (silenceSpans.Count > 0)
        {
            basis = "silenceGaps";
            candidates = silenceSpans
                .Select(s => (s.StartSec, s.EndSec, s.SourceIndex))
                .ToList();
        }
        else if (segments.Count > 0)
        {
            basis = "speechComplement";
            candidates = BuildSpeechComplementWindows(segments);
        }
        else
        {
            basis = "noSpeechDetected";
            candidates = [];
        }

        List<MusicLiftWindow> mapped = new();
        foreach ((double start, double end, int sourceIndex) in candidates)
        {
            if (end <= start)
                continue;

            (double Start, double End)? outWindow = mapSourceWindowToOutput(start, end, sourceIndex);
            if (outWindow is null)
                continue;

            mapped.Add(new MusicLiftWindow(outWindow.Value.Start, outWindow.Value.End));
        }

        // noSpeechDetected: there is no dialogue anywhere in the kept edit, so the music should not
        // be needlessly ducked for the whole video — lift the entire output timeline.
        if (basis == "noSpeechDetected" && d > 0)
            mapped.Add(new MusicLiftWindow(0, d));

        List<MusicLiftWindow> merged = MergeAndClamp(mapped, mergeSec, d);

        double minKeep = Math.Max(minWindowSec, 2 * rampSec + 0.2);
        // Epsilon-tolerant: a window whose duration is meant to land EXACTLY on minKeep (e.g. a
        // silence gap of [2, 2.4] against a minKeep computed as 2*0.1+0.2) can differ from minKeep
        // by ~1e-16 purely from IEEE-754 subtraction/addition order, which would otherwise reject a
        // window that should survive. Wall-clock second thresholds tolerate this; frame-count math
        // elsewhere in this feature (R9) does not and must never gain a similar epsilon.
        const double DurationEpsilon = 1e-9;
        List<MusicLiftWindow> aboveMin = merged.Where(w => w.EndSec - w.StartSec >= minKeep - DurationEpsilon).ToList();

        List<MusicLiftWindow> capped = aboveMin.Count > maxWindows
            ? aboveMin
                .OrderByDescending(w => w.EndSec - w.StartSec)
                .Take(Math.Max(0, maxWindows))
                .OrderBy(w => w.StartSec)
                .ToList()
            : aboveMin;

        double liftCoveragePct = d > 0 ? Math.Round(capped.Sum(w => w.EndSec - w.StartSec) / d * 100, 1) : 0;
        double speechCoveragePct = ComputeSpeechCoveragePct(segments, mapSourceWindowToOutput, d);

        return new MusicLiftPlan(capped, basis, speechCoveragePct, liftCoveragePct);
    }

    /// <summary>
    /// Per source clip (grouped by <see cref="VideoAnalysisSegment.SourceIndex"/>), the gaps
    /// BETWEEN consecutive transcript segments — before the first, between each pair, and after
    /// the last (open-ended, see <see cref="FarFutureSec"/>).
    /// </summary>
    private static List<(double Start, double End, int SourceIndex)> BuildSpeechComplementWindows(
        IReadOnlyList<VideoAnalysisSegment> segments)
    {
        List<(double Start, double End, int SourceIndex)> windows = new();

        foreach (IGrouping<int, VideoAnalysisSegment> group in segments.GroupBy(s => s.SourceIndex))
        {
            List<VideoAnalysisSegment> ordered = group.OrderBy(s => s.StartSec).ToList();
            int sourceIndex = group.Key;

            double cursor = 0;
            foreach (VideoAnalysisSegment seg in ordered)
            {
                if (seg.StartSec > cursor)
                    windows.Add((cursor, seg.StartSec, sourceIndex));
                cursor = Math.Max(cursor, seg.EndSec);
            }

            windows.Add((cursor, cursor + FarFutureSec, sourceIndex));
        }

        return windows;
    }

    private static List<MusicLiftWindow> MergeAndClamp(
        List<MusicLiftWindow> windows, double mergeSec, double durationSec)
    {
        List<MusicLiftWindow> clamped = windows
            .Select(w => new MusicLiftWindow(
                Math.Clamp(w.StartSec, 0, durationSec),
                Math.Clamp(w.EndSec, 0, durationSec)))
            .Where(w => w.EndSec > w.StartSec)
            .OrderBy(w => w.StartSec)
            .ToList();

        List<MusicLiftWindow> merged = new();
        foreach (MusicLiftWindow w in clamped)
        {
            if (merged.Count > 0 && w.StartSec - merged[^1].EndSec < mergeSec)
            {
                MusicLiftWindow last = merged[^1];
                merged[^1] = new MusicLiftWindow(last.StartSec, Math.Max(last.EndSec, w.EndSec));
            }
            else
            {
                merged.Add(w);
            }
        }

        return merged;
    }

    private static double ComputeSpeechCoveragePct(
        IReadOnlyList<VideoAnalysisSegment> segments,
        Func<double, double, int, (double Start, double End)?> mapSourceWindowToOutput,
        double durationSec)
    {
        if (segments.Count == 0 || durationSec <= 0)
            return 0;

        double covered = 0;
        foreach (VideoAnalysisSegment seg in segments)
        {
            if (seg.EndSec <= seg.StartSec)
                continue;

            (double Start, double End)? mapped = mapSourceWindowToOutput(seg.StartSec, seg.EndSec, seg.SourceIndex);
            if (mapped is null)
                continue;

            double start = Math.Clamp(mapped.Value.Start, 0, durationSec);
            double end = Math.Clamp(mapped.Value.End, 0, durationSec);
            if (end > start)
                covered += end - start;
        }

        return Math.Round(Math.Clamp(covered / durationSec * 100, 0, 100), 1);
    }
}
