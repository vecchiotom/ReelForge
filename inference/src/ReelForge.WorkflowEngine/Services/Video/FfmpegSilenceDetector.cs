using Microsoft.Extensions.Options;

namespace ReelForge.WorkflowEngine.Services.Video;

public sealed class FfmpegSilenceDetector : ISilenceDetector
{
    private readonly IVideoToolRunner _runner;
    private readonly VideoEditingOptions _options;
    private readonly ILogger<FfmpegSilenceDetector> _logger;

    public FfmpegSilenceDetector(
        IVideoToolRunner runner, IOptions<VideoEditingOptions> options, ILogger<FfmpegSilenceDetector> logger)
    {
        _runner = runner;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<(double StartSec, double EndSec)>> DetectAsync(
        string localFilePath,
        double thresholdDb,
        double minSilenceSeconds,
        double? totalDurationSec,
        CancellationToken ct)
    {
        string[] args = FfmpegArgvBuilder.BuildSilenceDetectArgs(localFilePath, thresholdDb, minSilenceSeconds);
        TimeSpan timeout = TimeSpan.FromSeconds(_options.AnalyzeTimeoutSeconds);

        VideoToolResult result = await _runner.RunFfmpegAsync(args, timeout, ct).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            _logger.LogWarning(
                "ffmpeg silencedetect failed (exitCode={ExitCode}, timedOut={TimedOut}): {StdErr}",
                result.ExitCode, result.TimedOut, result.StdErr);
            throw new InvalidOperationException(
                $"ffmpeg silencedetect failed for '{localFilePath}' (exitCode={result.ExitCode}, timedOut={result.TimedOut}).");
        }

        return SilenceDetectOutputParser.Parse(result.StdErr, totalDurationSec);
    }
}
