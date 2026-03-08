using System.Diagnostics;
using ReelForge.Shared.Data.Models;
using ReelForge.WorkflowEngine.Agents;

namespace ReelForge.WorkflowEngine.Execution.StepExecutors;

/// <summary>
/// Executes a standard agent step - runs the agent and returns its output.
/// </summary>
public class AgentStepExecutor : IStepExecutor
{
    private readonly IAgentRegistry _agentRegistry;
    private readonly IWorkflowExecutionContextAccessor _executionContextAccessor;
    private readonly ILogger<AgentStepExecutor> _logger;

    public AgentStepExecutor(
        IAgentRegistry agentRegistry,
        IWorkflowExecutionContextAccessor executionContextAccessor,
        ILogger<AgentStepExecutor> logger)
    {
        _agentRegistry = agentRegistry;
        _executionContextAccessor = executionContextAccessor;
        _logger = logger;
    }

    public StepType StepType => StepType.Agent;

    public async Task<StepExecutionResult> ExecuteAsync(StepExecutionContext context)
    {
        Guid? customAgentId = context.Step.AgentDefinition.AgentType == AgentType.Custom
            ? context.Step.AgentDefinitionId
            : null;

        IReelForgeAgent? agent = _agentRegistry.GetByType(context.Step.AgentDefinition.AgentType, customAgentId);
        if (agent == null)
        {
            _logger.LogWarning(
                "No agent found for type {AgentType} (AgentDefinitionId: {AgentDefinitionId}), skipping",
                context.Step.AgentDefinition.AgentType,
                context.Step.AgentDefinitionId);
            return new StepExecutionResult
            {
                Output = context.AccumulatedOutput,
                NextStepIndex = context.CurrentStepIndex + 1,
                NewIterationCount = context.IterationCount,
                Status = StepStatus.Skipped,
                ErrorDetails = $"No agent registered for type {context.Step.AgentDefinition.AgentType} and agent definition id {context.Step.AgentDefinitionId}"
            };
        }

        string stepInput = context.BuildAgentInput();

        _logger.LogInformation("Executing agent step {StepOrder}: {AgentName}",
            context.Step.StepOrder, agent.Name);

        Stopwatch sw = Stopwatch.StartNew();
        using IDisposable _ = _executionContextAccessor.BeginScope(
            context.Execution.Id,
            context.Execution.ProjectId,
            context.CorrelationId);
        AgentRunResult result = await agent.RunAsync(stepInput, context.CancellationToken);
        sw.Stop();

        string? outputStorageKey = _executionContextAccessor.Current?.PendingOutputStorageKey;

        return new StepExecutionResult
        {
            Output = result.Output,
            NextStepIndex = context.CurrentStepIndex + 1,
            NewIterationCount = context.IterationCount,
            DurationMs = sw.ElapsedMilliseconds,
            TokensUsed = result.TokensUsed,
            Status = StepStatus.Completed,
            OutputStorageKey = outputStorageKey
        };
    }
}
