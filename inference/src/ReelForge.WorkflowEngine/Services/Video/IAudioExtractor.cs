namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// Extracts a 16 kHz mono signed-16-bit PCM WAV from a local source video — the fixed format both
/// <see cref="ISilenceDetector"/> and WS3's ASR pipeline expect (plan §2/§3). Both methods write
/// to <paramref name="outputWavPath"/>, never returning audio bytes in memory (R21).
/// </summary>
public interface IAudioExtractor
{
    /// <summary>Extracts the full audio track.</summary>
    Task ExtractWavAsync(string inputVideoPath, string outputWavPath, CancellationToken ct);

    /// <summary>
    /// Extracts only the half-open <c>[startSec, endSec)</c> range — the shape
    /// <c>TranscriptChunkPlanner.PlanChunks</c> returns each chunk in, so a chunk tuple can be
    /// passed straight through as <paramref name="startSec"/>/<paramref name="endSec"/> with no
    /// reshaping at the call site.
    /// </summary>
    Task ExtractWavRangeAsync(
        string inputVideoPath, string outputWavPath, double startSec, double endSec, CancellationToken ct);
}
