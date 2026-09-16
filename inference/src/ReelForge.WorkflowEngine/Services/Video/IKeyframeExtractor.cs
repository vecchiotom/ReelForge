namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// Extracts a single representative frame from a local source video as a JPEG — the Phase 2
/// vision-captioning building block (see docs/video-editing.md "Vision captioning"). Mirrors
/// <see cref="IAudioExtractor"/>/<see cref="IFrameGridSampler"/>'s calling convention exactly (a
/// thin seam over <see cref="IVideoToolRunner"/> built from a dedicated
/// <see cref="FfmpegArgvBuilder"/> method) so a future phase adding its own new ffmpeg operation
/// (e.g. Phase 3's compile-time overlay rendering) has a single established pattern to follow.
/// </summary>
public interface IKeyframeExtractor
{
    /// <summary>
    /// Writes a single JPEG frame sampled at <paramref name="atSec"/> to
    /// <paramref name="outputJpgPath"/>, downscaled to at most <paramref name="maxWidth"/> pixels
    /// wide (never upscaled, aspect ratio preserved). Never returns image bytes in memory — same
    /// "write to a scratch path" discipline as <see cref="IAudioExtractor"/>.
    /// </summary>
    Task ExtractKeyframeAsync(
        string inputVideoPath, string outputJpgPath, double atSec, int maxWidth, CancellationToken ct);

    /// <summary>
    /// Phase 4 (<c>KeyframesPerShot</c> &gt; 1) — writes ONE contact-sheet JPEG hstacking a frame
    /// from each of <paramref name="atSecs"/> (left to right). A separate method, not an overload
    /// of <see cref="ExtractKeyframeAsync"/>, so <c>KeyframesPerShot == 1</c> keeps calling the
    /// exact original single-frame path byte-for-byte.
    /// </summary>
    Task ExtractContactSheetAsync(
        string inputVideoPath, string outputJpgPath, IReadOnlyList<double> atSecs, int maxWidth, CancellationToken ct);
}
