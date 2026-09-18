namespace ReelForge.Inference.Api.Controllers.Dto;

public record SkillSummaryResponse(
    string Name,
    string DisplayName,
    string Description,
    string Category,
    string? Version,
    string[] DefaultForAgentTypes,
    int AssignedAgentCount,
    string? SourceUrl,
    string? SourceCommit);

public record SkillDetailResponse(
    string Name,
    string DisplayName,
    string Description,
    string Category,
    string? Version,
    string[] DefaultForAgentTypes,
    int AssignedAgentCount,
    string? Body,
    string? SourceUrl,
    string? SourceCommit);

/// <summary>
/// Body for PUT /api/v1/agents/{id}/skills. Skills is never null — for a custom agent, an empty
/// array is a valid "no skills" state (there is no "revert to default" concept, since custom
/// agents have no default to revert to).
/// </summary>
public record SetAgentSkillsRequest(string[] Skills);
