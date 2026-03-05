namespace ReelForge.Inference.Api.Controllers.Dto;

public record CreateProjectRequest(string Name, string? Description);
public record UpdateProjectRequest(string Name, string? Description);

public record ProjectResponse(
    Guid Id, string Name, string? Description, string Status,
    DateTime CreatedAt, DateTime UpdatedAt);

public record ProjectFileResponse(
    Guid Id, string OriginalFileName, string StorageKey, string MimeType,
    long SizeBytes, string? AgentSummary, string SummaryStatus, DateTime UploadedAt);

public record AgentDefinitionResponse(
    Guid Id, string Name, string Description, string SystemPrompt,
    string AgentType, bool IsBuiltIn, Guid? OwnerId, string? ConfigJson,
    DateTime CreatedAt, string? Color);

public record CreateAgentRequest(string Name, string Description, string SystemPrompt, string? ConfigJson, string? Color);
public record UpdateAgentRequest(string Name, string Description, string SystemPrompt, string? ConfigJson, string? Color);

public record WorkflowDefinitionResponse(
    Guid Id, string Name, DateTime CreatedAt, DateTime UpdatedAt,
    List<WorkflowStepResponse> Steps);

public record WorkflowStepResponse(
    Guid Id, Guid AgentDefinitionId, int StepOrder,
    string? EdgeConditionJson, string? Label,
    string StepType = "Agent",
    string? ConditionExpression = null,
    string? LoopSourceExpression = null,
    int? LoopTargetStepOrder = null,
    int MaxIterations = 3,
    int? MinScore = null,
    string? InputMappingJson = null,
    string? TrueBranchStepOrder = null,
    string? FalseBranchStepOrder = null);

public record CreateWorkflowRequest(string Name, List<CreateWorkflowStepRequest> Steps);
public record CreateWorkflowStepRequest(
    Guid AgentDefinitionId, int StepOrder, string? EdgeConditionJson, string? Label,
    string? StepType = null,
    string? ConditionExpression = null,
    string? LoopSourceExpression = null,
    int? LoopTargetStepOrder = null,
    int? MaxIterations = null,
    int? MinScore = null,
    string? InputMappingJson = null,
    string? TrueBranchStepOrder = null,
    string? FalseBranchStepOrder = null);
public record UpdateWorkflowRequest(string? Name, List<CreateWorkflowStepRequest> Steps);

public record WorkflowExecutionResponse(
    Guid Id, Guid WorkflowDefinitionId, string Status,
    DateTime? StartedAt, DateTime? CompletedAt, int IterationCount,
    string? ResultJson, string? CorrelationId, string? ErrorMessage,
    List<StepResultResponse> StepResults,
    List<ReviewScoreResponse> ReviewScores);

public record StepResultResponse(
    Guid Id, Guid WorkflowStepId, string Output, int TokensUsed,
    long DurationMs, DateTime ExecutedAt,
    string? InputJson = null, string? OutputJson = null,
    string? Status = null, string? ErrorDetails = null,
    int? IterationNumber = null, DateTime? CompletedAt = null);

public record ReviewScoreResponse(
    Guid Id, int IterationNumber, int Score, string Comments, DateTime CreatedAt);
