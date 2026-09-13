using Microsoft.Extensions.Options;

namespace ReelForge.WorkflowEngine.Services.Video;

public sealed class FfmpegFrameGridSampler : IFrameGridSampler
{
    private readonly IVideoToolRunner _runner;
    private readonly VideoEditingOptions _options;
    private readonly ILogger<FfmpegFrameGridSampler> _logger;

    public FfmpegFrameGridSampler(
        IVideoToolRunner runner, IOptions<VideoEditingOptions> options, ILogger<FfmpegFrameGridSampler> logger)
    {
        _runner = runner;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<FrameGridResult> SampleAsync(
        string localFilePath,
        double sampleFps,
        int gridWidth,
        int gridHeight,
        double totalDurationSec,
        int maxSampleFrames,
        CancellationToken ct)
    {
        double effectiveFps = sampleFps;
        if (maxSampleFrames > 0 && totalDurationSec > 0)
        {
            double capFps = maxSampleFrames / totalDurationSec;
            effectiveFps = Math.Min(effectiveFps, capFps);
        }

        effectiveFps = Math.Max(effectiveFps, 0.01); // never let a pathological config reach ffmpeg as fps<=0

        string outputPath = Path.Combine(
            Path.GetDirectoryName(localFilePath) ?? Path.GetTempPath(), "grid.rgb");

        string[] args = FfmpegArgvBuilder.BuildGridSampleArgs(localFilePath, outputPath, effectiveFps, gridWidth, gridHeight);
        TimeSpan timeout = TimeSpan.FromSeconds(_options.AnalyzeTimeoutSeconds);

        VideoToolResult result = await _runner.RunFfmpegAsync(args, timeout, ct).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            _logger.LogWarning(
                "ffmpeg grid sampling failed (exitCode={ExitCode}, timedOut={TimedOut}): {StdErr}",
                result.ExitCode, result.TimedOut, result.StdErr);
            throw new InvalidOperationException(
                $"ffmpeg grid sampling failed for '{localFilePath}' (exitCode={result.ExitCode}, timedOut={result.TimedOut}).");
        }

        byte[] pixelData = await File.ReadAllBytesAsync(outputPath, ct).ConfigureAwait(false);
        int frameSize = gridWidth * gridHeight * 3;
        int frameCount = frameSize > 0 ? pixelData.Length / frameSize : 0;

        return new FrameGridResult(pixelData, gridWidth, gridHeight, effectiveFps, frameCount);
    }
}
