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
    /// Decimates/downscales to a raw RGB24 pixel grid for <see cref="IFrameGridSampler"/>:
    /// <c>-vf fps={sampleFps},scale={gridWidth}:{gridHeight}:flags=area,format=rgb24</c>, written
    /// as headerless <c>-f rawvideo -pix_fmt rgb24</c>. <c>flags=area</c> is load-bearing — a true
    /// box-average downscale, so each output pixel is the exact mean of its source block. No audio
    /// or subtitle streams are decoded (<c>-an -sn</c>).
    /// </summary>
    public static string[] BuildGridSampleArgs(
        string inputPath, string outputRawPath, double sampleFps, int gridWidth, int gridHeight)
    {
        List<string> args = new(BaseFlags) { "-loglevel", "error" };
        args.AddRange(ProtocolWhitelist);
        args.Add("-i");
        args.Add(inputPath);
        args.Add("-an");
        args.Add("-sn");
        args.Add("-vf");
        args.Add(
            $"fps={FfmpegArgvFormat.Number(sampleFps)}," +
            $"scale={FfmpegArgvFormat.Number(gridWidth)}:{FfmpegArgvFormat.Number(gridHeight)}:flags=area," +
            "format=rgb24");
        args.Add("-f");
        args.Add("rawvideo");
        args.Add("-pix_fmt");
        args.Add("rgb24");
        args.Add(outputRawPath);
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

    /// <summary>
    /// Extracts a single representative frame as a JPEG for Phase 2 vision captioning:
    /// <c>-ss {atSec} -i {inputPath} -frames:v 1 -vf scale='min({maxWidth},iw)':-2 -f image2
    /// -c:v mjpeg -q:v 4 {outputJpgPath}</c>. <c>-ss</c> is placed before <c>-i</c> for fast input
    /// seeking, matching <see cref="BuildExtractAudioArgs"/>'s convention. The scale expression
    /// never upscales (<c>min(maxWidth, iw)</c>) and preserves aspect ratio (<c>-2</c> on height).
    /// </summary>
    public static string[] BuildKeyframeArgs(string inputPath, string outputJpgPath, double atSec, int maxWidth)
    {
        List<string> args = new(BaseFlags) { "-loglevel", "error" };
        args.AddRange(ProtocolWhitelist);
        args.Add("-ss");
        args.Add(FfmpegArgvFormat.Number(Math.Max(0, atSec)));
        args.Add("-i");
        args.Add(inputPath);
        args.Add("-frames:v");
        args.Add("1");
        args.Add("-vf");
        args.Add($"scale='min({FfmpegArgvFormat.Number(maxWidth)},iw)':-2");
        args.Add("-f");
        args.Add("image2");
        args.Add("-c:v");
        args.Add("mjpeg");
        args.Add("-q:v");
        args.Add("4");
        args.Add(outputJpgPath);
        return args.ToArray();
    }
}
