namespace ReelForge.Shared.Inference;

/// <summary>
/// ReelForge's own abstraction over an ASR (speech-to-text) backend. Deliberately not a
/// passthrough of <c>OpenAI.Audio.AudioClient</c>: callers (the video-analyze pipeline) must not
/// need to know whether the underlying SDK object is a plain <c>AudioClient</c> (OpenAI-compatible
/// self-hosted backends such as whisper.cpp-server/faster-whisper-server) or Azure's derived
/// <c>AzureAudioClient</c> — both are polymorphically an <c>AudioClient</c>, mirroring exactly how
/// <see cref="IChatClientFactory"/> hides the same Azure-vs-OpenAI-compatible branching for chat.
/// This also makes the analyze step executor unit-testable with a mocked implementation.
/// </summary>
public interface ITranscriptionClient
{
    /// <summary>
    /// Transcribes a single WAV audio stream. <paramref name="wav"/> is expected to already be a
    /// bounded chunk (callers are responsible for chunking long tracks — see
    /// <c>TranscriptChunkPlanner</c> — and for offsetting the returned timestamps back to
    /// absolute time); this method always returns timestamps relative to the start of
    /// <paramref name="wav"/>.
    /// </summary>
    Task<TranscriptResult> TranscribeAsync(
        Stream wav,
        string fileName,
        string? language,
        bool wordTimestamps,
        CancellationToken ct);
}
