using System.Text.Json;

namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// Pure parser for the JSON produced by
/// <c>ffprobe -v error -print_format json -show_format -show_streams</c>. Deliberately has no
/// dependency on <see cref="IVideoToolRunner"/> or any process I/O, so it is unit-testable
/// against a captured/constructed fixture string with no ffmpeg installation required.
/// </summary>
public static class FfprobeOutputParser
{
    public static MediaProbeResult Parse(string ffprobeJson)
    {
        using JsonDocument doc = JsonDocument.Parse(ffprobeJson);
        JsonElement root = doc.RootElement;

        JsonElement? videoStream = null;
        JsonElement? audioStream = null;
        if (root.TryGetProperty("streams", out JsonElement streams) && streams.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement stream in streams.EnumerateArray())
            {
                string? codecType = GetString(stream, "codec_type");
                if (videoStream is null && codecType == "video")
                    videoStream = stream;
                else if (audioStream is null && codecType == "audio")
                    audioStream = stream;
            }
        }

        double durationSec = 0;
        if (root.TryGetProperty("format", out JsonElement format) &&
            format.TryGetProperty("duration", out JsonElement formatDuration) &&
            TryParseInvariantDouble(formatDuration, out double parsedFormatDuration))
        {
            durationSec = parsedFormatDuration;
        }
        else if (videoStream is { } vs0 &&
                 vs0.TryGetProperty("duration", out JsonElement streamDuration) &&
                 TryParseInvariantDouble(streamDuration, out double parsedStreamDuration))
        {
            durationSec = parsedStreamDuration;
        }

        int fpsNum = 0;
        int fpsDen = 1;
        int width = 0;
        int height = 0;
        string? videoCodec = null;

        if (videoStream is { } vs)
        {
            width = GetInt(vs, "width") ?? 0;
            height = GetInt(vs, "height") ?? 0;
            videoCodec = GetString(vs, "codec_name");

            string? rFrameRate = GetString(vs, "r_frame_rate");
            if (!string.IsNullOrWhiteSpace(rFrameRate) && TryParseRational(rFrameRate, out int num, out int den))
            {
                fpsNum = num;
                fpsDen = den;
            }
        }

        string? audioCodec = null;
        int? audioSampleRate = null;
        if (audioStream is { } asElem)
        {
            audioCodec = GetString(asElem, "codec_name");
            string? sampleRateRaw = GetString(asElem, "sample_rate");
            if (sampleRateRaw is not null && int.TryParse(sampleRateRaw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int sr))
                audioSampleRate = sr;
        }

        return new MediaProbeResult(durationSec, fpsNum, fpsDen, width, height, videoCodec, audioCodec, audioSampleRate);
    }

    /// <summary>
    /// Parses an exact rational like <c>"30000/1001"</c> or <c>"30/1"</c> into its numerator and
    /// denominator without ever collapsing through a <c>double</c> (R9). A malformed or
    /// zero-denominator value is rejected rather than guessed at.
    /// </summary>
    public static bool TryParseRational(string value, out int numerator, out int denominator)
    {
        numerator = 0;
        denominator = 1;

        string[] parts = value.Split('/');
        if (parts.Length != 2)
            return false;

        if (!int.TryParse(parts[0], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int num))
            return false;
        if (!int.TryParse(parts[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int den))
            return false;
        if (den == 0)
            return false;

        numerator = num;
        denominator = den;
        return true;
    }

    private static bool TryParseInvariantDouble(JsonElement element, out double value)
    {
        value = 0;
        string? raw = element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
        return double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int i)
            ? i
            : null;
}
