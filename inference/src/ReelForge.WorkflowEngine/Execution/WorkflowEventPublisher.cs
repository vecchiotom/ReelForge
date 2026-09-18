using MassTransit;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.IntegrationEvents;
using ReelForge.WorkflowEngine.Agents;

namespace ReelForge.WorkflowEngine.Execution;

public class WorkflowEventPublisher : IWorkflowEventPublisher
{
    private const int MaxPreviewLength = 1500;

    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<WorkflowEventPublisher> _logger;

    public WorkflowEventPublisher(IPublishEndpoint publishEndpoint, ILogger<WorkflowEventPublisher> logger)
    {
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public Task PublishExecutionRunningAsync(WorkflowExecution execution, CancellationToken ct)
    {
        _logger.LogInformation(
            "Publishing execution running event: ExecutionId={ExecutionId}",
            execution.Id);

        return _publishEndpoint.Publish(new WorkflowExecutionRunning
        {
            ExecutionId = execution.Id,
            ProjectId = execution.ProjectId,
            WorkflowDefinitionId = execution.WorkflowDefinitionId,
            InitiatedByUserId = execution.InitiatedByUserId,
            CorrelationId = execution.CorrelationId,
            StartedAt = execution.StartedAt ?? DateTime.UtcNow
        }, ct);
    }

    public Task PublishStepStartedAsync(
        WorkflowExecution execution,
        WorkflowStep step,
        WorkflowStepResult stepResult,
        string? inputPreview,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Publishing step started event: ExecutionId={ExecutionId}, StepId={StepId}, StepResultId={StepResultId}",
            execution.Id,
            step.Id,
            stepResult.Id);

        return _publishEndpoint.Publish(new WorkflowStepStarted
        {
            ExecutionId = execution.Id,
            StepId = step.Id,
            StepResultId = stepResult.Id,
            ProjectId = execution.ProjectId,
            WorkflowDefinitionId = execution.WorkflowDefinitionId,
            StepOrder = step.StepOrder,
            StepLabel = step.Label,
            StepType = step.StepType.ToString(),
            IterationNumber = stepResult.IterationNumber,
            AgentType = step.AgentDefinition?.AgentType.ToString(),
            AgentName = step.AgentDefinition?.Name,
            CorrelationId = execution.CorrelationId,
            InputPreview = inputPreview,
            StartedAt = stepResult.ExecutedAt
        }, ct);
    }

    public Task PublishStepCompletedAsync(
        WorkflowExecution execution,
        WorkflowStep step,
        WorkflowStepResult stepResult,
        StepExecutionResult stepExecutionResult,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Publishing step completed event: ExecutionId={ExecutionId}, StepId={StepId}, Status={Status}",
            execution.Id,
            step.Id,
            stepResult.Status);

        return
        _publishEndpoint.Publish(new WorkflowStepCompleted
        {
            ExecutionId = execution.Id,
            StepId = step.Id,
            StepResultId = stepResult.Id,
            ProjectId = execution.ProjectId,
            WorkflowDefinitionId = execution.WorkflowDefinitionId,
            StepOrder = step.StepOrder,
            StepLabel = step.Label,
            StepType = step.StepType.ToString(),
            IterationNumber = stepResult.IterationNumber,
            AgentType = step.AgentDefinition?.AgentType.ToString(),
            AgentName = step.AgentDefinition?.Name,
            CorrelationId = execution.CorrelationId,
            StepStatus = stepResult.Status.ToString(),
            TokensUsed = stepResult.TokensUsed,
            InputTokens = stepExecutionResult.InputTokens,
            OutputTokens = stepExecutionResult.OutputTokens,
            AttemptCount = stepExecutionResult.AttemptCount,
            RetryCount = stepExecutionResult.RetryCount,
            DurationMs = stepResult.DurationMs,
            ToolCallCount = stepExecutionResult.ToolCalls.Count,
            ReasoningCount = stepExecutionResult.Reasoning.Count,
            ErrorDetails = stepResult.ErrorDetails,
            OutputStorageKey = stepResult.OutputStorageKey,
            CompletedAt = stepResult.CompletedAt ?? DateTime.UtcNow
        }, ct);
    }

    public Task PublishStepProgressAsync(
        WorkflowExecution execution,
        WorkflowStep step,
        WorkflowStepResult stepResult,
        string stage,
        int? percentComplete,
        CancellationToken ct,
        int? tokensUsedSoFar = null,
        int? inputTokensSoFar = null,
        int? outputTokensSoFar = null)
    {
        _logger.LogDebug(
            "Publishing step progress event: ExecutionId={ExecutionId}, StepId={StepId}, Stage={Stage}, Percent={Percent}",
            execution.Id,
            step.Id,
            stage,
            percentComplete);

        return _publishEndpoint.Publish(new WorkflowStepProgress
        {
            ExecutionId = execution.Id,
            StepId = step.Id,
            StepResultId = stepResult.Id,
            ProjectId = execution.ProjectId,
            WorkflowDefinitionId = execution.WorkflowDefinitionId,
            StepOrder = step.StepOrder,
            StepLabel = step.Label,
            StepType = step.StepType.ToString(),
            CorrelationId = execution.CorrelationId,
            Stage = stage,
            PercentComplete = percentComplete,
            TokensUsedSoFar = tokensUsedSoFar,
            InputTokensSoFar = inputTokensSoFar,
            OutputTokensSoFar = outputTokensSoFar,
            OccurredAt = DateTime.UtcNow
        }, ct);
    }

    private const int MaxChatTurnLength = 600;

    public Task PublishStepChatTurnAsync(
        WorkflowExecution execution,
        WorkflowStep step,
        WorkflowStepResult stepResult,
        int turnIndex,
        int? totalTurns,
        string speaker,
        string speakerRole,
        string text,
        IReadOnlyList<string> idsMentioned,
        CancellationToken ct)
    {
        (string clipped, bool truncated) = ClipChatTurn(text);

        _logger.LogDebug(
            "Publishing step chat turn event: ExecutionId={ExecutionId}, StepId={StepId}, Turn={TurnIndex}, Speaker={Speaker}",
            execution.Id,
            step.Id,
            turnIndex,
            speaker);

        return _publishEndpoint.Publish(new WorkflowStepChatTurn
        {
            ExecutionId = execution.Id,
            StepId = step.Id,
            StepResultId = stepResult.Id,
            ProjectId = execution.ProjectId,
            WorkflowDefinitionId = execution.WorkflowDefinitionId,
            StepOrder = step.StepOrder,
            StepLabel = step.Label,
            CorrelationId = execution.CorrelationId,
            TurnIndex = turnIndex,
            TotalTurns = totalTurns,
            Speaker = speaker,
            SpeakerRole = speakerRole,
            Text = clipped,
            Truncated = truncated,
            IdsMentioned = idsMentioned,
            OccurredAt = DateTime.UtcNow
        }, ct);
    }

    /// <summary>Truncates to <see cref="MaxChatTurnLength"/> characters at the last whitespace boundary at or before the limit, so a turn is never cut mid-word.</summary>
    private static (string Text, bool Truncated) ClipChatTurn(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return (string.Empty, false);

        string trimmed = value.Trim();
        if (trimmed.Length <= MaxChatTurnLength)
            return (trimmed, false);

        int cut = trimmed.LastIndexOf(' ', MaxChatTurnLength - 1);
        string clipped = cut > 0 ? trimmed[..cut] : trimmed[..MaxChatTurnLength];
        return (clipped.TrimEnd() + "…", true);
    }

    public async Task PublishStepDiagnosticsAsync(
        WorkflowExecution execution,
        WorkflowStep step,
        WorkflowStepResult stepResult,
        StepExecutionResult stepExecutionResult,
        CancellationToken ct)
    {
        if (stepExecutionResult.ToolCalls.Count == 0 && stepExecutionResult.Reasoning.Count == 0)
            return;

        for (int index = 0; index < stepExecutionResult.ToolCalls.Count; index++)
        {
            AgentToolCallTrace toolCall = stepExecutionResult.ToolCalls[index];
            await _publishEndpoint.Publish(new WorkflowStepToolCalled
            {
                ExecutionId = execution.Id,
                StepId = step.Id,
                StepResultId = stepResult.Id,
                ProjectId = execution.ProjectId,
                WorkflowDefinitionId = execution.WorkflowDefinitionId,
                StepOrder = step.StepOrder,
                StepLabel = step.Label,
                AgentType = step.AgentDefinition?.AgentType.ToString(),
                AgentName = step.AgentDefinition?.Name,
                CorrelationId = execution.CorrelationId,
                Sequence = index + 1,
                ToolName = toolCall.ToolName,
                ArgumentsPreview = Clip(toolCall.Arguments),
                ResultPreview = Clip(toolCall.Result),
                OccurredAt = DateTime.UtcNow
            }, ct);
        }

        for (int index = 0; index < stepExecutionResult.Reasoning.Count; index++)
        {
            string reasoning = stepExecutionResult.Reasoning[index];
            if (string.IsNullOrWhiteSpace(reasoning))
                continue;

            await _publishEndpoint.Publish(new WorkflowStepReasoningCaptured
            {
                ExecutionId = execution.Id,
                StepId = step.Id,
                StepResultId = stepResult.Id,
                ProjectId = execution.ProjectId,
                WorkflowDefinitionId = execution.WorkflowDefinitionId,
                StepOrder = step.StepOrder,
                StepLabel = step.Label,
                AgentType = step.AgentDefinition?.AgentType.ToString(),
                AgentName = step.AgentDefinition?.Name,
                CorrelationId = execution.CorrelationId,
                Sequence = index + 1,
                Content = Clip(reasoning) ?? string.Empty,
                OccurredAt = DateTime.UtcNow
            }, ct);
        }
    }

    public Task PublishExecutionCompletedAsync(WorkflowExecution execution, CancellationToken ct)
    {
        _logger.LogInformation(
            "Publishing execution completed event: ExecutionId={ExecutionId}, FinalStatus={FinalStatus}",
            execution.Id,
            execution.Status);

        return
        _publishEndpoint.Publish(new WorkflowExecutionCompleted
        {
            ExecutionId = execution.Id,
            ProjectId = execution.ProjectId,
            WorkflowDefinitionId = execution.WorkflowDefinitionId,
            InitiatedByUserId = execution.InitiatedByUserId,
            CorrelationId = execution.CorrelationId,
            FinalStatus = execution.Status.ToString(),
            ResultJson = execution.ResultJson,
            CompletedAt = execution.CompletedAt ?? DateTime.UtcNow
        }, ct);
    }

    public Task PublishExecutionFailedAsync(WorkflowExecution execution, CancellationToken ct)
    {
        _logger.LogInformation(
            "Publishing execution failed event: ExecutionId={ExecutionId}, ErrorMessage={ErrorMessage}",
            execution.Id,
            execution.ErrorMessage ?? "Unknown workflow execution failure.");

        return
        _publishEndpoint.Publish(new WorkflowExecutionFailed
        {
            ExecutionId = execution.Id,
            ProjectId = execution.ProjectId,
            WorkflowDefinitionId = execution.WorkflowDefinitionId,
            InitiatedByUserId = execution.InitiatedByUserId,
            CorrelationId = execution.CorrelationId,
            ErrorMessage = execution.ErrorMessage ?? "Unknown workflow execution failure.",
            FailedAt = execution.CompletedAt ?? DateTime.UtcNow
        }, ct);
    }

    private static string? Clip(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string trimmed = value.Trim();
        return trimmed.Length <= MaxPreviewLength ? trimmed : trimmed[..MaxPreviewLength];
    }
}
