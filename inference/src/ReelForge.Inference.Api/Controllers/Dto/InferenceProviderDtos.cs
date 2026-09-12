namespace ReelForge.Inference.Api.Controllers.Dto;

public record InferenceProviderResponse(Guid Id, string Name, string Kind, string Endpoint,
    string ModelName, bool HasApiKey, string? ApiKeyLastFour, bool IsDefault, bool IsEnabled,
    int? TimeoutSeconds, DateTime CreatedAt, DateTime UpdatedAt,
    DateTime? LastTestAt, bool? LastTestOk, string? LastTestError);

public record CreateInferenceProviderRequest(string Name, string Kind, string Endpoint,
    string ModelName, string? ApiKey, bool IsDefault, bool IsEnabled, int? TimeoutSeconds);

public record UpdateInferenceProviderRequest(string? Name, string? Kind, string? Endpoint,
    string? ModelName, string? ApiKey, bool? IsDefault, bool? IsEnabled, int? TimeoutSeconds);

public record TestInferenceProviderRequest(Guid? Id, string? Kind, string? Endpoint,
    string? ModelName, string? ApiKey);

public record TestInferenceProviderResponse(bool Ok, long LatencyMs, string? Error, string? ResponsePreview);

public record SetAgentInferenceProviderRequest(Guid? InferenceProviderId);
