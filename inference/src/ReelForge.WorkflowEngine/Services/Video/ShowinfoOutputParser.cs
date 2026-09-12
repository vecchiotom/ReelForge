using System.Text.RegularExpressions;

namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// Pure parser for the stderr produced by ffmpeg's <c>showinfo</c> filter when chained after
/// <c>select='gt(scene,threshold)'</c> (<see cref="FfmpegArgvBuilder.BuildShotDetectArgs"/>). No
/// dependency on <see cref="IVideoToolRunner"/> or any process I/O, so it is unit-testable against
/// a captured fixture string with no ffmpeg installation required.
/// </summary>
/// <remarks>
/// showinfo logs one "info"-level line per frame it receives — here, only frames the upstream
/// <c>select</c> filter passed through, i.e. detected scene changes — of the shape:
/// <code>
/// [Parsed_showinfo_1 @ 0x55d1a2b3c4e0] n:   3 pts:  90000 pts_time:3.75    pos:123456 ...
/// </code>
/// ffmpeg's own <c>select</c> filter never selects frame 0 (there is no prior frame to compare a
/// scene score against), so boundary timestamps from <see cref="ParsePtsTimes"/> alone almost
/// never include 0 or the file's end — <see cref="BuildShotSpans"/> adds both sentinels itself so
/// every second of the file belongs to exactly one shot.
/// </remarks>
public static class ShowinfoOutputParser
{
    private static readonly Regex PtsTimePattern = new(
        @"pts_time:\s*(-?[0-9]+(?:\.[0-9]+)?)", RegexOptions.Compiled);

    /// <summary>
    /// Extracts every <c>pts_time:</c> value logged by showinfo, sorted ascending with duplicates
    /// removed. Lines without a numeric <c>pts_time</c> (e.g. the rare <c>pts_time:N/A</c> for a
    /// frame with no valid timestamp) are skipped rather than throwing.
    /// </summary>
    public static IReadOnlyList<double> ParsePtsTimes(string ffmpegStdErr)
    {
        SortedSet<double> boundaries = new();

        foreach (string line in ffmpegStdErr.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            Match match = PtsTimePattern.Match(line);
            if (!match.Success)
            {
                continue;
            }

            if (double.TryParse(
                    match.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double ptsTime))
            {
                boundaries.Add(ptsTime);
            }
        }

        return boundaries.ToList();
    }

    /// <summary>
    /// Turns a set of interior scene-change boundary timestamps into contiguous, gapless
    /// <c>[StartSec, EndSec)</c> shot spans covering <c>[0, totalDurationSec)</c>. Boundaries at or
    /// outside that range (0 itself, or a boundary at/after <paramref name="totalDurationSec"/>,
    /// which can happen from floating-point noise in ffmpeg's own timestamp arithmetic) are
    /// dropped rather than producing a zero-length or inverted span.
    /// </summary>
    public static IReadOnlyList<(double StartSec, double EndSec)> BuildShotSpans(
        IReadOnlyList<double> boundaryTimes, double totalDurationSec)
    {
        if (totalDurationSec <= 0)
        {
            return Array.Empty<(double, double)>();
        }

        SortedSet<double> allBoundaries = new() { 0.0, totalDurationSec };
        foreach (double boundary in boundaryTimes)
        {
            if (boundary > 0.0 && boundary < totalDurationSec)
            {
                allBoundaries.Add(boundary);
            }
        }

        List<double> ordered = allBoundaries.ToList();
        List<(double StartSec, double EndSec)> spans = new(ordered.Count - 1);
        for (int i = 0; i < ordered.Count - 1; i++)
        {
            spans.Add((ordered[i], ordered[i + 1]));
        }

        return spans;
    }
}
