namespace ReelForge.Shared.Inference;

/// <summary>
/// A single transcribed word with its absolute (never chunk-relative) timestamp bounds, in
/// seconds from the start of the original audio track.
/// </summary>
public sealed record TranscriptWord(string Text, double StartSec, double EndSec);

/// <summary>
/// A single transcribed segment (sentence/phrase granularity) with its absolute timestamp
/// bounds, in seconds from the start of the original audio track.
/// </summary>
public sealed record TranscriptSegment(string Text, double StartSec, double EndSec);

/// <summary>
/// The full result of transcribing one audio track. When the source audio was chunked (see
/// <c>TranscriptChunkPlanner</c>), <see cref="Segments"/> and <see cref="Words"/> have already
/// been offset back to absolute time and concatenated across chunks — callers never see
/// chunk-relative timestamps.
/// </summary>
public sealed record TranscriptResult(
    string Text,
    IReadOnlyList<TranscriptSegment> Segments,
    IReadOnlyList<TranscriptWord> Words,
    string? Language);
