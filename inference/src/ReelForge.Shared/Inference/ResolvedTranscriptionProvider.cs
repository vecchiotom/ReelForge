using System.Security.Cryptography;
using System.Text;
using ReelForge.Shared.Data.Models;

namespace ReelForge.Shared.Inference;

/// <summary>
/// A fully resolved transcription provider configuration, ready to build (or reuse) an
/// <see cref="ITranscriptionClient"/> from. Built fresh on every resolve by
/// <see cref="IInferenceProviderResolver"/> — never persisted itself. Mirrors
/// <see cref="ResolvedInferenceProvider"/> exactly, kept as a separate type because a chat
/// resolution and a transcription resolution are never interchangeable (see
/// <see cref="InferenceProviderCapability"/>).
/// </summary>
public sealed record ResolvedTranscriptionProvider(
    Guid? ProviderId,
    string Name,
    InferenceProviderKind Kind,
    string Endpoint,
    string ModelName,
    string ApiKey,
    int TimeoutSeconds)
{
    /// <summary>
    /// Stable cache key over everything that affects transcription-client construction
    /// (Kind|Endpoint|ModelName|SHA256(ApiKey)|TimeoutSeconds, itself hashed).
    /// The raw API key is never included in, or recoverable from, this value.
    /// </summary>
    public string CacheKey { get; } = ComputeCacheKey(Kind, Endpoint, ModelName, ApiKey, TimeoutSeconds);

    private static string ComputeCacheKey(
        InferenceProviderKind kind,
        string endpoint,
        string modelName,
        string apiKey,
        int timeoutSeconds)
    {
        string hashedApiKey = Sha256Hex(apiKey ?? string.Empty);
        string material = string.Join('|', kind, endpoint, modelName, hashedApiKey, timeoutSeconds);
        return Sha256Hex(material);
    }

    /// <summary>
    /// Suppresses the compiler-generated record <c>ToString()</c>, which would print every
    /// positional member — including <c>ApiKey</c> in plaintext. Nothing logs a whole provider
    /// today, but this type is one `LogError("... {Provider}", provider)` away from leaking a
    /// credential into the log stream.
    /// </summary>
    public override string ToString() => $"{Name} ({Kind}/{ModelName})";

    private static string Sha256Hex(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash);
    }
}
