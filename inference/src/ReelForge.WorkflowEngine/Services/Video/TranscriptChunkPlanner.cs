namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// Pure planning logic for splitting a long audio track into ASR-sized chunks. No I/O: takes an
/// already-probed duration/byte-size estimate and an already-detected silence map, and returns an
/// ordered list of absolute <c>[StartSec, EndSec)</c> ranges the caller can extract with ffmpeg
/// and transcribe independently.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> Most <c>/audio/transcriptions</c> implementations (OpenAI,
/// Azure, and the self-hosted whisper.cpp-server/faster-whisper-server family) cap request body
/// size, commonly around 25&#160;MB. A 16&#160;kHz mono s16 WAV runs roughly 32,000&#160;bytes/sec, so
/// anything past ~10&#160;minutes needs to be split before it is sent. Splitting on a fixed time
/// interval risks cutting a chunk boundary in the middle of a spoken word, which corrupts both
/// chunks' transcripts right at the seam. Splitting only at silence would ignore the caller's
/// byte budget and could produce a chunk far larger than the ASR backend accepts. This planner
/// does both: it targets an ideal chunk length derived from the byte budget, then nudges each
/// boundary into the nearest nearby silence gap.
/// </para>
///
/// <para><b>Algorithm.</b></para>
/// <list type="number">
/// <item>Estimate bytes-per-second as <c>estimatedTotalBytes / totalDurationSec</c> (assumes
/// roughly constant bitrate, true for the fixed-format WAV this planner is always fed — see
/// <c>FfmpegAudioExtractor</c>). From that, compute an ideal chunk length in seconds:
/// <c>idealChunkSec = maxChunkBytes / bytesPerSecond</c>.</item>
/// <item>If the whole track already fits under <c>maxChunkBytes</c> (or the byte/duration inputs
/// are degenerate — zero or negative), return it as a single chunk. No splitting, no silence
/// search, nothing to get wrong.</item>
/// <item>Otherwise walk forward from <c>currentStart = 0</c>. At each step the next "ideal"
/// boundary is <c>currentStart + idealChunkSec</c>. If that boundary would land at or past the
/// end of the track, the current chunk simply runs to <c>totalDurationSec</c> and the walk ends —
/// there is nothing left to split.</item>
/// <item>Otherwise, search <paramref name="silenceSpans"/> — assumed sorted ascending by
/// <c>StartSec</c>, as every silence detector in this codebase already produces them — for a span
/// that overlaps a symmetric search window around the ideal boundary
/// (<c>idealBoundary &#177; SearchToleranceFraction * idealChunkSec</c>). Among overlapping
/// candidates, the one whose interval is closest to the ideal boundary wins. The actual cut point
/// is the ideal boundary <i>clamped into that span</i>
/// (<c>Math.Clamp(idealBoundary, span.StartSec, span.EndSec)</c>): if the ideal boundary already
/// falls inside the span the cut lands exactly there (deepest into the silence, safest); if the
/// span is nearby but not overlapping the ideal instant, the cut lands at whichever edge of the
/// span is closer, still guaranteed inside the gap. Either way the cut is provably inside a
/// silence span, so it can never fall mid-word.</item>
/// <item>If no silence span overlaps the search window, fall back to a hard cut exactly at the
/// ideal boundary — accepting the (rare, no-silence-nearby) risk of a mid-word cut rather than
/// producing a chunk wildly different from the byte budget.</item>
/// <item>The next chunk starts exactly where the previous one ended — chunk boundaries are
/// therefore contiguous and gapless, which is what makes chunk-local ASR timestamps safe to
/// convert to absolute time by simple addition: <c>absoluteSec = chunkStartSec +
/// chunkRelativeSec</c>. This is the offset arithmetic <see cref="TranscriptChunkPlannerTests"/>
/// exercises explicitly for a 3-chunk case.</item>
/// </list>
///
/// <para><b>Deliberately not handled</b> (out of scope for a pure planner): merging a very short
/// trailing chunk into its predecessor, and re-balancing chunk lengths to be more even. Neither
/// affects correctness of the offset arithmetic, and both add complexity for a cosmetic gain.</para>
/// </remarks>
public static class TranscriptChunkPlanner
{
    /// <summary>
    /// How far from the ideal boundary (as a fraction of the ideal chunk length) the planner will
    /// look for a nearby silence span to cut into. 20% keeps the search local — a silence span
    /// halfway across the whole track should never be preferred over a hard cut just because it's
    /// the closest one available.
    /// </summary>
    private const double SearchToleranceFraction = 0.20;

    public static IReadOnlyList<(double StartSec, double EndSec)> PlanChunks(
        double totalDurationSec,
        long estimatedTotalBytes,
        IReadOnlyList<(double StartSec, double EndSec)> silenceSpans,
        long maxChunkBytes)
    {
        ArgumentNullException.ThrowIfNull(silenceSpans);

        if (totalDurationSec <= 0)
        {
            return Array.Empty<(double, double)>();
        }

        if (estimatedTotalBytes <= 0 || maxChunkBytes <= 0 || estimatedTotalBytes <= maxChunkBytes)
        {
            // Degenerate byte inputs can't drive a meaningful split, and a track that already
            // fits the budget needs no splitting at all - both cases are exactly one chunk.
            return new List<(double, double)> { (0.0, totalDurationSec) };
        }

        double bytesPerSecond = estimatedTotalBytes / totalDurationSec;
        double idealChunkSec = maxChunkBytes / bytesPerSecond;

        if (idealChunkSec <= 0 || idealChunkSec >= totalDurationSec)
        {
            return new List<(double, double)> { (0.0, totalDurationSec) };
        }

        List<(double StartSec, double EndSec)> sortedSilence = silenceSpans
            .OrderBy(s => s.StartSec)
            .ToList();

        List<(double StartSec, double EndSec)> chunks = new();
        double currentStart = 0.0;

        while (currentStart < totalDurationSec)
        {
            double idealBoundary = currentStart + idealChunkSec;

            if (idealBoundary >= totalDurationSec)
            {
                chunks.Add((currentStart, totalDurationSec));
                break;
            }

            double toleranceSec = idealChunkSec * SearchToleranceFraction;
            double windowStart = idealBoundary - toleranceSec;
            double windowEnd = idealBoundary + toleranceSec;

            double cutPoint = FindSilenceAlignedCut(sortedSilence, idealBoundary, windowStart, windowEnd)
                               ?? idealBoundary; // no silence nearby: hard cut

            // Guard forward progress: a pathological silence span (e.g. one that starts before
            // currentStart) must never produce a zero-or-negative-length chunk.
            if (cutPoint <= currentStart)
            {
                cutPoint = idealBoundary;
            }

            chunks.Add((currentStart, cutPoint));
            currentStart = cutPoint;
        }

        return chunks;
    }

    /// <summary>
    /// Finds the silence span closest to <paramref name="idealBoundary"/> among those overlapping
    /// the <c>[windowStart, windowEnd]</c> search window, and returns the ideal boundary clamped
    /// into that span. Returns <c>null</c> when no span overlaps the window.
    /// </summary>
    private static double? FindSilenceAlignedCut(
        IReadOnlyList<(double StartSec, double EndSec)> sortedSilence,
        double idealBoundary,
        double windowStart,
        double windowEnd)
    {
        (double StartSec, double EndSec)? best = null;
        double bestDistance = double.MaxValue;

        foreach ((double StartSec, double EndSec) span in sortedSilence)
        {
            bool overlapsWindow = span.EndSec >= windowStart && span.StartSec <= windowEnd;
            if (!overlapsWindow)
            {
                continue;
            }

            double distance = DistanceToBoundary(span, idealBoundary);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = span;
            }
        }

        if (best is null)
        {
            return null;
        }

        return Math.Clamp(idealBoundary, best.Value.StartSec, best.Value.EndSec);
    }

    private static double DistanceToBoundary((double StartSec, double EndSec) span, double idealBoundary)
    {
        if (idealBoundary >= span.StartSec && idealBoundary <= span.EndSec)
        {
            return 0.0;
        }

        return idealBoundary < span.StartSec
            ? span.StartSec - idealBoundary
            : idealBoundary - span.EndSec;
    }
}
