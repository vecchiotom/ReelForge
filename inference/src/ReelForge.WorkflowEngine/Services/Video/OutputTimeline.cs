using ReelForge.WorkflowEngine.Execution.StepExecutors;

namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// Where one resolved span landed on the OUTPUT timeline — <c>[OutputStartSec, OutputEndSec)</c>,
/// always exactly <c>SnappedEnd - SnappedStart</c> long (a seam's overlap only ever shifts a span's
/// START position backward into its predecessor's tail; it never shortens the span's own presented
/// duration — see <see cref="OutputTimeline.Build"/>).
/// </summary>
public sealed record TimelinePlacement(int SpanIndex, double OutputStartSec, double OutputEndSec);

/// <summary>
/// The compiled OUTPUT timeline a resolved cut list + its planned seam transitions produce —
/// generalizes the pre-transitions single source-to-output mapping
/// (<c>VideoCompileStepExecutor.MapSourceToOutputSec</c>/<c>MapSourceWindowToOutput</c>) to account
/// for overlapping crossfade seams, which make consecutive spans occupy overlapping output time.
/// When every <see cref="SeamPlan.OverlapSec"/> is 0 this produces IDENTICAL results to those two
/// pre-existing static methods — see docs/video-editing.md "Cut transitions".
/// </summary>
public sealed class OutputTimeline
{
    private readonly IReadOnlyList<VideoCompileStepExecutor.ResolvedSpan> _spans;

    public IReadOnlyList<TimelinePlacement> Placements { get; }

    public IReadOnlyList<SeamPlan> Seams { get; }

    public double TotalSec { get; }

    private OutputTimeline(
        IReadOnlyList<TimelinePlacement> placements,
        IReadOnlyList<SeamPlan> seams,
        double totalSec,
        IReadOnlyList<VideoCompileStepExecutor.ResolvedSpan> spans)
    {
        Placements = placements;
        Seams = seams;
        TotalSec = totalSec;
        _spans = spans;
    }

    /// <summary>
    /// Builds the output timeline: walks <paramref name="spans"/> in order, placing each one right
    /// after its predecessor except pulled backward by the preceding seam's own
    /// <see cref="SeamPlan.OverlapSec"/> (0 for every non-overlapping treatment, so this reduces to
    /// simple concatenation exactly as before this addition).
    /// </summary>
    internal static OutputTimeline Build(
        IReadOnlyList<VideoCompileStepExecutor.ResolvedSpan> spans, IReadOnlyList<SeamPlan> seams)
    {
        var placements = new List<TimelinePlacement>(spans.Count);
        double cursor = 0;
        for (int i = 0; i < spans.Count; i++)
        {
            if (i > 0)
                cursor -= seams[i - 1].OverlapSec;

            double duration = Math.Max(0, spans[i].SnappedEnd - spans[i].SnappedStart);
            placements.Add(new TimelinePlacement(i, cursor, cursor + duration));
            cursor += duration;
        }

        return new OutputTimeline(placements, seams, cursor, spans);
    }

    /// <summary>
    /// Maps a source-timeline second to its position in the compiled output timeline. Returns
    /// <c>null</c> when <paramref name="sourceSec"/> falls inside a cut region — including before
    /// the first kept span of this source or after its last.
    /// </summary>
    public double? MapToOutputSec(double sourceSec, int sourceIndex = 0)
    {
        foreach (TimelinePlacement placement in Placements)
        {
            VideoCompileStepExecutor.ResolvedSpan span = _spans[placement.SpanIndex];
            if (span.SourceIndex != sourceIndex)
                continue;

            if (sourceSec < span.SnappedStart)
                return null;

            if (sourceSec < span.SnappedEnd)
                return placement.OutputStartSec + (sourceSec - span.SnappedStart);
        }

        return null;
    }

    /// <summary>
    /// Intersects a source-timeline <c>[startSec, endSec)</c> window with the kept spans of
    /// <paramref name="sourceIndex"/> and maps the surviving (first-overlapping) portion to the
    /// output timeline — mirrors the pre-existing <c>MapSourceWindowToOutput</c> exactly.
    /// </summary>
    public (double Start, double End)? MapWindowToOutput(double startSec, double endSec, int sourceIndex = 0)
    {
        if (endSec <= startSec)
            return null;

        foreach (TimelinePlacement placement in Placements)
        {
            VideoCompileStepExecutor.ResolvedSpan span = _spans[placement.SpanIndex];
            if (span.SourceIndex != sourceIndex)
                continue;

            double overlapStart = Math.Max(startSec, span.SnappedStart);
            double overlapEnd = Math.Min(endSec, span.SnappedEnd);

            if (overlapEnd > overlapStart)
            {
                double outStart = placement.OutputStartSec + (overlapStart - span.SnappedStart);
                double outEnd = placement.OutputStartSec + (overlapEnd - span.SnappedStart);
                return (outStart, outEnd);
            }
        }

        return null;
    }
}
