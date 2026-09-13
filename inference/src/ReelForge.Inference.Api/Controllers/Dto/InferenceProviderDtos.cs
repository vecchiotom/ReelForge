namespace ReelForge.Inference.Api.Controllers.Dto;

public record InferenceProviderResponse(Guid Id, string Name, string Kind, string Capability, string Endpoint,
    string ModelName, bool HasApiKey, string? ApiKeyLastFour, bool IsDefault, bool IsEnabled,
    int? TimeoutSeconds, DateTime CreatedAt, DateTime UpdatedAt,
    DateTime? LastTestAt, bool? LastTestOk, string? LastTestError);

/// <summary>Capability is optional and defaults to 'Chat' when omitted, for backward compatibility
/// with any existing frontend calls made before the Chat/Transcription split existed.</summary>
public record CreateInferenceProviderRequest(string Name, string Kind, string Endpoint,
    string ModelName, string? ApiKey, bool IsDefault, bool IsEnabled, int? TimeoutSeconds,
    string? Capability = null);

public record UpdateInferenceProviderRequest(string? Name, string? Kind, string? Endpoint,
    string? ModelName, string? ApiKey, bool? IsDefault, bool? IsEnabled, int? TimeoutSeconds,
    string? Capability = null);

public record TestInferenceProviderRequest(Guid? Id, string? Kind, string? Endpoint,
    string? ModelName, string? ApiKey, string? Capability = null);

public record TestInferenceProviderResponse(bool Ok, long LatencyMs, string? Error, string? ResponsePreview);

public record SetAgentInferenceProviderRequest(Guid? InferenceProviderId);
