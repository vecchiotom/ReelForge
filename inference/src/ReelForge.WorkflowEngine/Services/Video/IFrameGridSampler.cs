namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// The raw output of one grid-sampling ffmpeg pass: a flat RGB24 buffer of
/// <see cref="FrameCount"/> frames, each <c>GridWidth * GridHeight * 3</c> bytes, in time order.
/// Frame <c>i</c>'s sample time is exactly <c>i / EffectiveFps</c> — the <c>fps</c> filter emits
/// constant-frame-rate output from t=0, so no timestamp parsing is needed anywhere downstream,
/// only byte-offset arithmetic.
/// </summary>
public sealed record FrameGridResult(
    byte[] PixelData,
    int GridWidth,
    int GridHeight,
    double EffectiveFps,
    int FrameCount);

/// <summary>
/// Decimates and downscales a source video to a low-res raw RGB pixel grid — the single ffmpeg
/// pass every Phase 1 visual descriptor is derived from (see docs/video-editing.md). The box-
/// average downscale (<c>scale=...:flags=area</c>) is load-bearing: each output pixel is the
/// exact mean of its source block, which is what makes per-region statistics meaningful.
/// </summary>
public interface IFrameGridSampler
{
    /// <summary>
    /// <paramref name="sampleFps"/> is clamped downward for very long videos so the grid buffer
    /// never exceeds <paramref name="maxSampleFrames"/> frames:
    /// <c>effectiveFps = min(sampleFps, maxSampleFrames / totalDurationSec)</c> —
    /// <paramref name="maxSampleFrames"/> &lt;= 0 falls back to a compiled-in default rather than
    /// disabling the cap (see <c>FfmpegFrameGridSampler.ComputeEffectiveFps</c>).
    /// <paramref name="scratch"/> resolves the sampler's own scratch-file path(s) (e.g. the raw
    /// grid buffer) the same way every other video scratch-file consumer does, via
    /// <see cref="VideoScratchSpace.GetPath"/>.
    /// </summary>
    Task<FrameGridResult> SampleAsync(
        string localFilePath,
        VideoScratchSpace scratch,
        double sampleFps,
        int gridWidth,
        int gridHeight,
        double totalDurationSec,
        int maxSampleFrames,
        CancellationToken ct);
}
