using ReelForge.Inference.Data.Models;

namespace ReelForge.Inference.Controllers.Dto;

public record CreateProjectRequest(string Name, string? Description);
public record UpdateProjectRequest(string Name, string? Description);

public record ProjectResponse(
    Guid Id,
    string Name,
    string? Description,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record ProjectFileResponse(
    Guid Id,
    string OriginalFileName,
    string StorageKey,
    string MimeType,
    long SizeBytes,
    string? AgentSummary,
    string SummaryStatus,
    DateTime UploadedAt);

public record AgentDefinitionResponse(
    Guid Id,
    string Name,
    string Description,
    string SystemPrompt,
    string AgentType,
    bool IsBuiltIn,
    Guid? OwnerId,
    string? ConfigJson,
    DateTime CreatedAt);

public record CreateAgentRequest(string Name, string Description, string SystemPrompt, string? ConfigJson);
public record UpdateAgentRequest(string Name, string Description, string SystemPrompt, string? ConfigJson);

public record WorkflowDefinitionResponse(
    Guid Id,
    string Name,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    List<WorkflowStepResponse> Steps);

public record WorkflowStepResponse(
    Guid Id,
    Guid AgentDefinitionId,
    int StepOrder,
    string? EdgeConditionJson,
    string? Label);

public record CreateWorkflowRequest(string Name, List<CreateWorkflowStepRequest> Steps);
public record CreateWorkflowStepRequest(Guid AgentDefinitionId, int StepOrder, string? EdgeConditionJson, string? Label);
public record UpdateWorkflowRequest(List<CreateWorkflowStepRequest> Steps);

public record WorkflowExecutionResponse(
    Guid Id,
    Guid WorkflowDefinitionId,
    string Status,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    int IterationCount,
    string? ResultJson,
    List<StepResultResponse> StepResults);

public record StepResultResponse(
    Guid Id,
    Guid WorkflowStepId,
    string Output,
    int TokensUsed,
    long DurationMs,
    DateTime ExecutedAt);
