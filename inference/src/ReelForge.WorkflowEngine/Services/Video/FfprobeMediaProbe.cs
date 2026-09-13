using Microsoft.Extensions.Options;

namespace ReelForge.WorkflowEngine.Services.Video;

public sealed class FfprobeMediaProbe : IMediaProbe
{
    private readonly IVideoToolRunner _runner;
    private readonly VideoEditingOptions _options;
    private readonly ILogger<FfprobeMediaProbe> _logger;

    public FfprobeMediaProbe(IVideoToolRunner runner, IOptions<VideoEditingOptions> options, ILogger<FfprobeMediaProbe> logger)
    {
        _runner = runner;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<MediaProbeResult> ProbeAsync(string localFilePath, CancellationToken ct)
    {
        string[] args = FfmpegArgvBuilder.BuildProbeArgs(localFilePath);
        TimeSpan timeout = TimeSpan.FromSeconds(_options.AnalyzeTimeoutSeconds);

        VideoToolResult result = await _runner.RunFfprobeAsync(args, timeout, ct).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            _logger.LogWarning("ffprobe failed (exitCode={ExitCode}, timedOut={TimedOut}): {StdErr}",
                result.ExitCode, result.TimedOut, result.StdErr);
            throw new InvalidOperationException(
                $"ffprobe failed for '{localFilePath}' (exitCode={result.ExitCode}, timedOut={result.TimedOut}). stderr: {Truncate(result.StdErr)}");
        }

        return FfprobeOutputParser.Parse(result.StdOut);
    }

    private static string Truncate(string value, int max = 2000) =>
        value.Length <= max ? value : value[^max..];
}
