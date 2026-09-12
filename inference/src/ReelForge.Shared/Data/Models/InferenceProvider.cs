namespace ReelForge.Shared.Data.Models;

/// <summary>
/// A configured chat-completion inference backend (Azure OpenAI or an OpenAI-compatible
/// endpoint such as vLLM, LM Studio, llama.cpp server, or LiteLLM).
/// Exactly one row is expected to have <see cref="IsDefault"/> set to true.
/// </summary>
public class InferenceProvider
{
    public Guid Id { get; set; }

    /// <summary>Unique display name.</summary>
    public string Name { get; set; } = string.Empty;

    public InferenceProviderKind Kind { get; set; }

    /// <summary>Azure resource URL, or OpenAI-compatible base URL.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Azure deployment name, or model id.</summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>Data-Protection ciphertext; never leaves the backend.</summary>
    public string? ApiKeyEncrypted { get; set; }

    /// <summary>Display-only hint, e.g. the last four characters of the key.</summary>
    public string? ApiKeyLastFour { get; set; }

    public bool IsDefault { get; set; }

    public bool IsEnabled { get; set; } = true;

    public int? TimeoutSeconds { get; set; }

    /// <summary>jsonb; some self-hosted gateways need a custom auth header.</summary>
    public string? ExtraHeadersJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastTestAt { get; set; }

    public bool? LastTestOk { get; set; }

    public string? LastTestError { get; set; }

    public ICollection<AgentDefinition> AgentDefinitions { get; set; } = new List<AgentDefinition>();
}
