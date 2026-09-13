namespace ReelForge.Shared.Inference;

/// <summary>
/// Builds (and caches) <see cref="ITranscriptionClient"/> instances for a resolved transcription
/// provider. Mirrors <see cref="IChatClientFactory"/> exactly.
/// </summary>
public interface ITranscriptionClientFactory
{
    ITranscriptionClient Get(ResolvedTranscriptionProvider provider);
}
