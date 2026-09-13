using Microsoft.Extensions.Options;

namespace ReelForge.WorkflowEngine.Services.Video;

public sealed class FfmpegAudioExtractor : IAudioExtractor
{
    private readonly IVideoToolRunner _runner;
    private readonly VideoEditingOptions _options;
    private readonly ILogger<FfmpegAudioExtractor> _logger;

    public FfmpegAudioExtractor(
        IVideoToolRunner runner, IOptions<VideoEditingOptions> options, ILogger<FfmpegAudioExtractor> logger)
    {
        _runner = runner;
        _options = options.Value;
        _logger = logger;
    }

    public Task ExtractWavAsync(string inputVideoPath, string outputWavPath, CancellationToken ct) =>
        RunExtractAsync(inputVideoPath, outputWavPath, startSeconds: null, durationSeconds: null, ct);

    public Task ExtractWavRangeAsync(
        string inputVideoPath, string outputWavPath, double startSec, double endSec, CancellationToken ct)
    {
        if (endSec <= startSec)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endSec), endSec, $"endSec ({endSec}) must be greater than startSec ({startSec}).");
        }

        return RunExtractAsync(inputVideoPath, outputWavPath, startSec, endSec - startSec, ct);
    }

    private async Task RunExtractAsync(
        string inputVideoPath,
        string outputWavPath,
        double? startSeconds,
        double? durationSeconds,
        CancellationToken ct)
    {
        string[] args = FfmpegArgvBuilder.BuildExtractAudioArgs(
            inputVideoPath, outputWavPath, startSeconds, durationSeconds);
        TimeSpan timeout = TimeSpan.FromSeconds(_options.AnalyzeTimeoutSeconds);

        VideoToolResult result = await _runner.RunFfmpegAsync(args, timeout, ct).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            _logger.LogWarning(
                "ffmpeg audio extraction failed (exitCode={ExitCode}, timedOut={TimedOut}): {StdErr}",
                result.ExitCode, result.TimedOut, result.StdErr);
            throw new InvalidOperationException(
                $"ffmpeg audio extraction failed for '{inputVideoPath}' (exitCode={result.ExitCode}, timedOut={result.TimedOut}).");
        }
    }
}
