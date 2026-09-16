using Microsoft.Extensions.Options;

namespace ReelForge.WorkflowEngine.Services.Video;

public sealed class FfmpegKeyframeExtractor : IKeyframeExtractor
{
    private readonly IVideoToolRunner _runner;
    private readonly VideoEditingOptions _options;
    private readonly ILogger<FfmpegKeyframeExtractor> _logger;

    public FfmpegKeyframeExtractor(
        IVideoToolRunner runner, IOptions<VideoEditingOptions> options, ILogger<FfmpegKeyframeExtractor> logger)
    {
        _runner = runner;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ExtractKeyframeAsync(
        string inputVideoPath, string outputJpgPath, double atSec, int maxWidth, CancellationToken ct)
    {
        string[] args = FfmpegArgvBuilder.BuildKeyframeArgs(inputVideoPath, outputJpgPath, atSec, maxWidth);
        TimeSpan timeout = TimeSpan.FromSeconds(_options.AnalyzeTimeoutSeconds);

        VideoToolResult result = await _runner.RunFfmpegAsync(args, timeout, ct).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            _logger.LogWarning(
                "ffmpeg keyframe extraction failed (exitCode={ExitCode}, timedOut={TimedOut}): {StdErr}",
                result.ExitCode, result.TimedOut, result.StdErr);
            throw new InvalidOperationException(
                $"ffmpeg keyframe extraction failed for '{inputVideoPath}' at {atSec}s " +
                $"(exitCode={result.ExitCode}, timedOut={result.TimedOut}).");
        }
    }

    public async Task ExtractContactSheetAsync(
        string inputVideoPath, string outputJpgPath, IReadOnlyList<double> atSecs, int maxWidth, CancellationToken ct)
    {
        string[] args = FfmpegArgvBuilder.BuildContactSheetArgs(inputVideoPath, outputJpgPath, atSecs, maxWidth);
        TimeSpan timeout = TimeSpan.FromSeconds(_options.AnalyzeTimeoutSeconds);

        VideoToolResult result = await _runner.RunFfmpegAsync(args, timeout, ct).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            _logger.LogWarning(
                "ffmpeg contact-sheet extraction failed (exitCode={ExitCode}, timedOut={TimedOut}): {StdErr}",
                result.ExitCode, result.TimedOut, result.StdErr);
            throw new InvalidOperationException(
                $"ffmpeg contact-sheet extraction failed for '{inputVideoPath}' " +
                $"(exitCode={result.ExitCode}, timedOut={result.TimedOut}).");
        }
    }
}
