namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// Measures native-resolution focus (Phase 4) via a dedicated per-shot ffmpeg invocation
/// (<see cref="FfmpegArgvBuilder.BuildSharpnessPatchArgs"/>) plus a pure-C# Laplacian-variance
/// metric — see docs/video-editing.md. Mirrors <see cref="IKeyframeExtractor"/>'s calling
/// convention (a thin seam over <see cref="IVideoToolRunner"/>), except this seam also reads the
/// raw output back and computes the metric itself, since the caller only wants a number.
/// </summary>
public interface ISharpnessSampler
{
    /// <summary>
    /// Returns a 0..1 focus score, or null when the patch could not be produced/parsed — never
    /// throws. <paramref name="scratchFileName"/> is resolved against <paramref name="scratch"/>
    /// for the temporary raw-patch file.
    /// </summary>
    Task<double?> MeasureAsync(
        string inputVideoPath, VideoScratchSpace scratch, string scratchFileName,
        double atSec, int patch, CancellationToken ct);
}
