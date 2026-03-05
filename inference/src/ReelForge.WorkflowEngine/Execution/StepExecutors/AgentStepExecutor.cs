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
    private readonly ILogger<AgentStepExecutor> _logger;

    public AgentStepExecutor(IAgentRegistry agentRegistry, ILogger<AgentStepExecutor> logger)
    {
        _agentRegistry = agentRegistry;
        _logger = logger;
    }

    public StepType StepType => StepType.Agent;

    public async Task<StepExecutionResult> ExecuteAsync(StepExecutionContext context)
    {
        IReelForgeAgent? agent = _agentRegistry.GetByType(context.Step.AgentDefinition.AgentType);
        if (agent == null)
        {
            _logger.LogWarning("No agent found for type {AgentType}, skipping", context.Step.AgentDefinition.AgentType);
            return new StepExecutionResult
            {
                Output = context.AccumulatedOutput,
                NextStepIndex = context.CurrentStepIndex + 1,
                NewIterationCount = context.IterationCount,
                Status = StepStatus.Skipped,
                ErrorDetails = $"No agent registered for type {context.Step.AgentDefinition.AgentType}"
            };
        }

        string stepInput = string.IsNullOrEmpty(context.AccumulatedOutput)
            ? "Begin analysis of the project."
            : context.AccumulatedOutput;

        _logger.LogInformation("Executing agent step {StepOrder}: {AgentName}",
            context.Step.StepOrder, agent.Name);

        Stopwatch sw = Stopwatch.StartNew();
        string output = await agent.RunAsync(stepInput, context.CancellationToken);
        sw.Stop();

        return new StepExecutionResult
        {
            Output = output,
            NextStepIndex = context.CurrentStepIndex + 1,
            NewIterationCount = context.IterationCount,
            DurationMs = sw.ElapsedMilliseconds,
            Status = StepStatus.Completed
        };
    }
}
