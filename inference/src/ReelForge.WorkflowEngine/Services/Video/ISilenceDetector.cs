namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// Detects silent audio spans in a local media file via ffmpeg's <c>silencedetect</c> filter
/// (plan §2 / WS2). Spans are half-open <c>[StartSec, EndSec)</c>, sorted ascending.
/// </summary>
public interface ISilenceDetector
{
    /// <summary>
    /// Runs <c>-af silencedetect=noise={thresholdDb}dB:d={minSilenceSeconds}</c> over
    /// <paramref name="localFilePath"/> and returns the detected silence spans.
    /// </summary>
    /// <param name="totalDurationSec">
    /// The file's probed duration (from <see cref="IMediaProbe"/>), used only to close a trailing
    /// silence span that runs to end-of-stream (ffmpeg logs <c>silence_start</c> for it but never
    /// a matching <c>silence_end</c> since the stream simply ends). When <c>null</c>, such a
    /// trailing span is dropped rather than guessed at.
    /// </param>
    Task<IReadOnlyList<(double StartSec, double EndSec)>> DetectAsync(
        string localFilePath,
        double thresholdDb,
        double minSilenceSeconds,
        double? totalDurationSec,
        CancellationToken ct);
}
