using Microsoft.Extensions.Options;

namespace ReelForge.WorkflowEngine.Services.Video;

public sealed class FfmpegShotDetector : IShotDetector
{
    private readonly IVideoToolRunner _runner;
    private readonly VideoEditingOptions _options;
    private readonly ILogger<FfmpegShotDetector> _logger;

    public FfmpegShotDetector(
        IVideoToolRunner runner, IOptions<VideoEditingOptions> options, ILogger<FfmpegShotDetector> logger)
    {
        _runner = runner;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<(double StartSec, double EndSec)>> DetectShotsAsync(
        string localFilePath,
        double sceneThreshold,
        double totalDurationSec,
        CancellationToken ct)
    {
        string[] args = FfmpegArgvBuilder.BuildShotDetectArgs(localFilePath, sceneThreshold, _options.FfmpegThreads);
        TimeSpan timeout = TimeSpan.FromSeconds(_options.AnalyzeTimeoutSeconds);

        VideoToolResult result = await _runner.RunFfmpegAsync(args, timeout, ct).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            _logger.LogWarning(
                "ffmpeg shot detection failed (exitCode={ExitCode}, timedOut={TimedOut}): {StdErr}",
                result.ExitCode, result.TimedOut, result.StdErr);
            throw new InvalidOperationException(
                $"ffmpeg shot detection failed for '{localFilePath}' (exitCode={result.ExitCode}, timedOut={result.TimedOut}).");
        }

        IReadOnlyList<double> boundaries = ShowinfoOutputParser.ParsePtsTimes(result.StdErr);
        return ShowinfoOutputParser.BuildShotSpans(boundaries, totalDurationSec);
    }
}
