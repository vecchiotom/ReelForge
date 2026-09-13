using System.Globalization;

namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// The single shared helper every ffmpeg/ffprobe argv builder must route numeric formatting
/// through (R8). ffmpeg's own argument parser expects a decimal point regardless of host locale;
/// under a comma-decimal culture (e.g. de-DE), <c>value.ToString()</c> silently produces
/// "12,4" instead of "12.4", which ffmpeg then misparses — usually splitting it into two
/// tokens or truncating at the comma — with no error surfaced back to us. Every numeric token
/// that reaches an argv array in this feature must be produced by this class, never by a bare
/// <c>ToString()</c> or interpolated <c>$"{value}"</c>.
/// </summary>
public static class FfmpegArgvFormat
{
    public static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}
