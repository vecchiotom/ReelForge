namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// Builds the exact argv token arrays for every ffmpeg/ffprobe invocation this feature makes.
/// Centralizing this (rather than letting each detector interpolate its own strings) is what
/// makes R8 (culture-invariant numeric formatting), R11 (no unsanitized config/model input
/// reaching argv) and the "-protocol_whitelist file" containment discipline enforceable in one
/// place and testable without a real ffmpeg process.
///
/// Every returned array is a `string[]` of individually-quoted tokens, never a joined command
/// line — <see cref="IVideoToolRunner"/> passes these straight into
/// <c>ProcessStartInfo.ArgumentList</c>, so there is no shell to (mis)quote for and no injection
/// surface via spaces or shell metacharacters in a path.
/// </summary>
public static class FfmpegArgvBuilder
{
    /// <summary>
    /// Global flags common to every ffmpeg invocation in this feature: no interactive stdin
    /// (<c>-nostdin</c>), no version banner, unconditional overwrite of scratch outputs
    /// (<c>-y</c>), and a locked-down protocol whitelist so nothing this process spawns can
    /// touch anything but a local file (no <c>http://</c>, no <c>concat:</c>/<c>subfile:</c>
    /// tricks reaching outside the scratch dir) — see plan §2.
    /// </summary>
    private static readonly string[] BaseFlags = { "-nostdin", "-hide_banner", "-y" };

    private static readonly string[] ProtocolWhitelist = { "-protocol_whitelist", "file" };

    public static string[] BuildProbeArgs(string inputPath) =>
        new[]
        {
            "-v", "error",
            "-print_format", "json",
            "-show_format", "-show_streams",
            "-protocol_whitelist", "file",
            inputPath
        };

    /// <summary>
    /// <c>-af silencedetect=noise={db}dB:d={sec}</c>, discarding decoded output to <c>-f null -</c>.
    /// silencedetect logs its start/end markers at ffmpeg's "info" level, so loglevel is raised
    /// from the feature's default "error" specifically so <see cref="ISilenceDetector"/> has
    /// something to parse on stderr.
    /// </summary>
    public static string[] BuildSilenceDetectArgs(string inputPath, double thresholdDb, double minSilenceSeconds)
    {
        List<string> args = new(BaseFlags) { "-loglevel", "info" };
        args.AddRange(ProtocolWhitelist);
        args.Add("-i");
        args.Add(inputPath);
        args.Add("-af");
        args.Add($"silencedetect=noise={FfmpegArgvFormat.Number(thresholdDb)}dB:d={FfmpegArgvFormat.Number(minSilenceSeconds)}");
        args.Add("-f");
        args.Add("null");
        args.Add("-");
        return args.ToArray();
    }

    /// <summary>
    /// <c>-vf select='gt(scene,{threshold})',showinfo</c>, discarding decoded output to
    /// <c>-f null -</c>. showinfo logs one line per frame it receives at ffmpeg's "info" level
    /// (only frames the <c>select</c> filter passed through, i.e. detected scene changes), so
    /// loglevel is raised the same way as silencedetect.
    /// </summary>
    public static string[] BuildShotDetectArgs(string inputPath, double sceneThreshold)
    {
        List<string> args = new(BaseFlags) { "-loglevel", "info" };
        args.AddRange(ProtocolWhitelist);
        args.Add("-i");
        args.Add(inputPath);
        args.Add("-vf");
        args.Add($"select='gt(scene,{FfmpegArgvFormat.Number(sceneThreshold)})',showinfo");
        args.Add("-f");
        args.Add("null");
        args.Add("-");
        return args.ToArray();
    }

    /// <summary>
    /// Extracts 16 kHz mono signed-16-bit PCM WAV — the fixed format both silence detection and
    /// WS3's ASR expect. An optional <c>[startSec, startSec+durationSec)</c> range restricts the
    /// extraction to a chunk (used for ASR chunk splitting); <c>-ss</c> is placed before
    /// <c>-i</c> for fast input seeking.
    /// </summary>
    public static string[] BuildExtractAudioArgs(
        string inputPath,
        string outputWavPath,
        double? startSeconds = null,
        double? durationSeconds = null)
    {
        List<string> args = new(BaseFlags) { "-loglevel", "error" };
        args.AddRange(ProtocolWhitelist);
        if (startSeconds.HasValue)
        {
            args.Add("-ss");
            args.Add(FfmpegArgvFormat.Number(startSeconds.Value));
        }

        args.Add("-i");
        args.Add(inputPath);

        if (durationSeconds.HasValue)
        {
            args.Add("-t");
            args.Add(FfmpegArgvFormat.Number(durationSeconds.Value));
        }

        args.Add("-vn");
        args.Add("-ac");
        args.Add("1");
        args.Add("-ar");
        args.Add("16000");
        args.Add("-sample_fmt");
        args.Add("s16");
        args.Add("-f");
        args.Add("wav");
        args.Add(outputWavPath);
        return args.ToArray();
    }
}
