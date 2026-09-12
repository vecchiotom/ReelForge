using System.Text.RegularExpressions;

namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// Pure parser for the stderr produced by ffmpeg's <c>silencedetect</c> filter
/// (<see cref="FfmpegArgvBuilder.BuildSilenceDetectArgs"/>). No dependency on
/// <see cref="IVideoToolRunner"/> or any process I/O, so it is unit-testable against a captured
/// fixture string with no ffmpeg installation required.
/// </summary>
/// <remarks>
/// silencedetect logs one line per boundary, interleaved with ffmpeg's other "info"-level
/// output, e.g.:
/// <code>
/// [silencedetect @ 0x55d1a2b3c4e0] silence_start: 12.1023
/// [silencedetect @ 0x55d1a2b3c4e0] silence_end: 13.9012 | silence_duration: 1.79885
/// </code>
/// Silence spans never overlap (audio cannot be simultaneously silent from two starts), so a
/// simple "most recent open start" pointer is sufficient — no stack is needed.
/// </remarks>
public static class SilenceDetectOutputParser
{
    private static readonly Regex SilenceStartPattern = new(
        @"silence_start:\s*(-?[0-9]+(?:\.[0-9]+)?)", RegexOptions.Compiled);

    private static readonly Regex SilenceEndPattern = new(
        @"silence_end:\s*(-?[0-9]+(?:\.[0-9]+)?)", RegexOptions.Compiled);

    public static IReadOnlyList<(double StartSec, double EndSec)> Parse(
        string ffmpegStdErr, double? totalDurationSec = null)
    {
        List<(double StartSec, double EndSec)> spans = new();
        double? pendingStart = null;

        foreach (string line in SplitLines(ffmpegStdErr))
        {
            Match startMatch = SilenceStartPattern.Match(line);
            if (startMatch.Success)
            {
                // A new silence_start while one is already pending would mean silencedetect
                // logged two starts with no end between them — not physically possible for a
                // single audio stream. Guard defensively by keeping the most recent one.
                pendingStart = ParseInvariantDouble(startMatch.Groups[1].Value);
                continue;
            }

            Match endMatch = SilenceEndPattern.Match(line);
            if (endMatch.Success && pendingStart.HasValue)
            {
                double end = ParseInvariantDouble(endMatch.Groups[1].Value);
                if (end > pendingStart.Value)
                {
                    spans.Add((pendingStart.Value, end));
                }

                pendingStart = null;
            }
        }

        // Trailing open silence: silence_start was logged but the stream ended before a matching
        // silence_end. Close it at the probed duration when we have one; otherwise drop it rather
        // than fabricate an end time.
        if (pendingStart.HasValue && totalDurationSec.HasValue && totalDurationSec.Value > pendingStart.Value)
        {
            spans.Add((pendingStart.Value, totalDurationSec.Value));
        }

        return spans;
    }

    private static IEnumerable<string> SplitLines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static double ParseInvariantDouble(string raw) =>
        double.Parse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
}
