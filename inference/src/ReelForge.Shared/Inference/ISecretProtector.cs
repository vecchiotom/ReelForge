namespace ReelForge.Shared.Inference;

/// <summary>
/// Encrypts/decrypts inference provider API keys at rest via ASP.NET Core Data Protection.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    /// <summary>
    /// Attempts to decrypt <paramref name="ciphertext"/>. Returns <c>false</c> — and never
    /// throws — when the value is null/empty or the Data Protection key ring cannot decrypt it
    /// (e.g. a lost or mismatched key ring, Risk R3), so callers degrade gracefully (treat the
    /// provider as having no usable key) instead of failing every inference call.
    /// </summary>
    bool TryUnprotect(string? ciphertext, out string plaintext);
}
