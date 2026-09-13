namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// Detects scene-change (shot) boundaries in a local video file via ffmpeg's
/// <c>select='gt(scene,threshold)',showinfo</c> filter chain (plan §2 / WS2), and derives shot
/// spans from them. <see cref="DetectShotsAsync"/> guarantees the returned spans are half-open
/// <c>[StartSec, EndSec)</c>, sorted ascending, contiguous, and cover <c>[0, totalDurationSec)</c>
/// in full — the first shot always starts at 0 and the last always ends at
/// <c>totalDurationSec</c>, regardless of how many (if any) scene changes ffmpeg detected.
/// </summary>
public interface IShotDetector
{
    Task<IReadOnlyList<(double StartSec, double EndSec)>> DetectShotsAsync(
        string localFilePath,
        double sceneThreshold,
        double totalDurationSec,
        CancellationToken ct);
}
