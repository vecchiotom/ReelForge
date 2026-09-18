namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// Probed characteristics of a media file. Fps is carried as an exact rational — the numerator
/// and denominator ffprobe reports in <c>r_frame_rate</c> (e.g. <c>"30000/1001"</c>) — rather
/// than collapsed to a <c>double</c>, so downstream frame-exact arithmetic (plan §4.3) never
/// accumulates floating-point drift (R9).
/// </summary>
public sealed record MediaProbeResult(
    double DurationSec,
    int FpsNum,
    int FpsDen,
    int Width,
    int Height,
    string? VideoCodec,
    string? AudioCodec,
    int? AudioSampleRate,
    string? PixFmt = null);

public interface IMediaProbe
{
    Task<MediaProbeResult> ProbeAsync(string localFilePath, CancellationToken ct);
}
