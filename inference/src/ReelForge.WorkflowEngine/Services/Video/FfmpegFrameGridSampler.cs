using Microsoft.Extensions.Options;

namespace ReelForge.WorkflowEngine.Services.Video;

public sealed class FfmpegFrameGridSampler : IFrameGridSampler
{
    /// <summary>
    /// "Use the compiled-in default" fallback for <c>maxSampleFrames &lt;= 0</c> — mirrors
    /// <see cref="ReelForge.Shared.Workflows.VideoAnalyzeStepConfig.MaxVisualSampleFrames"/>'s own
    /// default, so an explicit 0/negative value behaves the same as leaving the field unset rather
    /// than disabling the sample-count cap entirely (see docs/video-editing.md, Item D cleanup).
    /// </summary>
    private const int DefaultMaxVisualSampleFrames = 4000;

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
        VideoScratchSpace scratch,
        double sampleFps,
        int gridWidth,
        int gridHeight,
        double totalDurationSec,
        int maxSampleFrames,
        CancellationToken ct)
    {
        double effectiveFps = ComputeEffectiveFps(sampleFps, totalDurationSec, maxSampleFrames);

        // Goes through VideoScratchSpace.GetPath, like every other scratch-file path in this
        // codebase (IAudioExtractor/IKeyframeExtractor callers included), rather than deriving a
        // sibling path from localFilePath's directory manually — see docs/video-editing.md, Item J
        // cleanup. Safe either way in practice (localFilePath is already scratch-resident), but
        // this keeps the path-containment assertion consistent everywhere.
        string outputPath = scratch.GetPath("grid.rgb");

        string[] args = FfmpegArgvBuilder.BuildGridSampleArgs(localFilePath, outputPath, effectiveFps, gridWidth, gridHeight, _options.FfmpegThreads);
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

    /// <summary>
    /// <c>effectiveFps = min(sampleFps, effectiveMax / totalDurationSec)</c>, where
    /// <paramref name="maxSampleFrames"/> &lt;= 0 falls back to
    /// <see cref="DefaultMaxVisualSampleFrames"/> rather than disabling the cap entirely (Item D
    /// cleanup — previously, <c>MaxVisualSampleFrames: 0</c>/negative skipped the clamp altogether
    /// instead of being treated as "use the default"). Always clamped to a minimum of 0.01 so a
    /// pathological config can never reach ffmpeg as <c>fps&lt;=0</c>. Factored out as a pure,
    /// directly-unit-testable method — the rest of <see cref="SampleAsync"/> invokes ffmpeg and
    /// isn't independently testable without it.
    /// </summary>
    internal static double ComputeEffectiveFps(double sampleFps, double totalDurationSec, int maxSampleFrames)
    {
        int effectiveMax = maxSampleFrames > 0 ? maxSampleFrames : DefaultMaxVisualSampleFrames;

        double effectiveFps = sampleFps;
        if (totalDurationSec > 0)
        {
            double capFps = effectiveMax / totalDurationSec;
            effectiveFps = Math.Min(effectiveFps, capFps);
        }

        return Math.Max(effectiveFps, 0.01); // never let a pathological config reach ffmpeg as fps<=0
    }
}
