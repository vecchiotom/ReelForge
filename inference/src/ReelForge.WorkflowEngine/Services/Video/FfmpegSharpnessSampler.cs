using Microsoft.Extensions.Options;

namespace ReelForge.WorkflowEngine.Services.Video;

public sealed class FfmpegSharpnessSampler : ISharpnessSampler
{
    private readonly IVideoToolRunner _runner;
    private readonly VideoEditingOptions _options;
    private readonly ILogger<FfmpegSharpnessSampler> _logger;

    public FfmpegSharpnessSampler(
        IVideoToolRunner runner, IOptions<VideoEditingOptions> options, ILogger<FfmpegSharpnessSampler> logger)
    {
        _runner = runner;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<double?> MeasureAsync(
        string inputVideoPath, VideoScratchSpace scratch, string scratchFileName,
        double atSec, int patch, CancellationToken ct)
    {
        if (patch <= 0)
            return null;

        try
        {
            string rawPath = scratch.GetPath(scratchFileName);
            string[] args = FfmpegArgvBuilder.BuildSharpnessPatchArgs(inputVideoPath, rawPath, atSec, patch);
            TimeSpan timeout = TimeSpan.FromSeconds(_options.AnalyzeTimeoutSeconds);

            VideoToolResult result = await _runner.RunFfmpegAsync(args, timeout, ct).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                _logger.LogWarning(
                    "ffmpeg sharpness patch extraction failed (exitCode={ExitCode}, timedOut={TimedOut}): {StdErr}",
                    result.ExitCode, result.TimedOut, result.StdErr);
                return null;
            }

            if (!File.Exists(rawPath))
                return null;

            byte[] bytes = await File.ReadAllBytesAsync(rawPath, ct).ConfigureAwait(false);
            if (bytes.Length < patch * patch)
                return null;

            return ComputeFocusScore(bytes, patch);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sharpness measurement failed for '{InputPath}' at {AtSec}s; returning null.", inputVideoPath, atSec);
            return null;
        }
    }

    /// <summary>
    /// 4-neighbour discrete Laplacian, variance over the interior, normalized so a typical
    /// in-focus native-res patch lands near the top of the range. The constant is a documented
    /// heuristic scale, not a physical unit — Sharpness is only ever compared BETWEEN shots of the
    /// same source.
    /// </summary>
    private static double ComputeFocusScore(byte[] patchBytes, int patch)
    {
        if (patch < 3)
            return 0.0;

        var laplacian = new List<double>((patch - 2) * (patch - 2));
        for (int y = 1; y < patch - 1; y++)
        {
            for (int x = 1; x < patch - 1; x++)
            {
                int center = patchBytes[y * patch + x];
                int up = patchBytes[(y - 1) * patch + x];
                int down = patchBytes[(y + 1) * patch + x];
                int left = patchBytes[y * patch + x - 1];
                int right = patchBytes[y * patch + x + 1];
                double value = Math.Abs(4 * center - up - down - left - right) / 255.0;
                laplacian.Add(value);
            }
        }

        if (laplacian.Count == 0)
            return 0.0;

        double mean = laplacian.Average();
        double variance = laplacian.Sum(v => (v - mean) * (v - mean)) / laplacian.Count;
        return Math.Clamp(variance / 0.02, 0, 1);
    }
}
