using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ReelForge.Shared.Data.Models;
using ReelForge.WorkflowEngine.Agents;
using ReelForge.WorkflowEngine.Data;

namespace ReelForge.WorkflowEngine.Execution.StepExecutors;

/// <summary>
/// Executes multiple agents in parallel and merges their outputs into a structured JSON array
/// passed to the next step as: [{"agentName":"...","output":"..."}, ...]
/// 
/// The agents to run are specified in <see cref="WorkflowStep.ParallelAgentIdsJson"/> as a JSON
/// array of AgentDefinition GUIDs. The primary <see cref="WorkflowStep.AgentDefinitionId"/> is
/// always included. All agents receive the same accumulated input and run concurrently.
/// </summary>
public class ParallelStepExecutor : IStepExecutor
{
    private readonly IAgentRegistry _agentRegistry;
    private readonly IWorkflowExecutionContextAccessor _executionContextAccessor;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ParallelStepExecutor> _logger;

    public ParallelStepExecutor(
        IAgentRegistry agentRegistry,
        IWorkflowExecutionContextAccessor executionContextAccessor,
        IServiceScopeFactory scopeFactory,
        ILogger<ParallelStepExecutor> logger)
    {
        _agentRegistry = agentRegistry;
        _executionContextAccessor = executionContextAccessor;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public StepType StepType => StepType.Parallel;

    public async Task<StepExecutionResult> ExecuteAsync(StepExecutionContext context)
    {
        // Build the full list of agent definition IDs: primary + any extras from ParallelAgentIdsJson
        List<Guid> agentIds = BuildAgentIdList(context.Step);

        if (agentIds.Count == 0)
        {
            return new StepExecutionResult
            {
                Output = context.AccumulatedOutput,
                NextStepIndex = context.CurrentStepIndex + 1,
                NewIterationCount = context.IterationCount,
                Status = StepStatus.Skipped,
                ErrorDetails = "Parallel step has no agent IDs configured."
            };
        }

        // Load AgentDefinition records to map IDs → AgentType
        using IServiceScope scope = _scopeFactory.CreateScope();
        WorkflowEngineDbContext db = scope.ServiceProvider.GetRequiredService<WorkflowEngineDbContext>();
        List<AgentDefinition> agentDefs = await db.AgentDefinitions
            .Where(a => agentIds.Contains(a.Id))
            .ToListAsync(context.CancellationToken);

        // Resolve to IReelForgeAgent instances (skip any that aren't registered)
        List<(string Name, IReelForgeAgent Agent)> resolved = agentIds
            .Select(id =>
            {
                AgentDefinition? def = agentDefs.FirstOrDefault(a => a.Id == id);
                if (def == null)
                {
                    _logger.LogWarning("Parallel step {StepOrder}: no AgentDefinition found for ID {AgentId}, skipping", context.Step.StepOrder, id);
                    return default;
                }

                Guid? customId = def.AgentType == AgentType.Custom ? def.Id : null;
                IReelForgeAgent? agent = _agentRegistry.GetByType(def.AgentType, customId);
                if (agent == null)
                    _logger.LogWarning("Parallel step {StepOrder}: no agent registered for type {AgentType} (ID {AgentId}), skipping", context.Step.StepOrder, def.AgentType, id);

                return (Name: def.Name, Agent: agent!);
            })
            .Where(x => x.Agent != null)
            .ToList();

        if (resolved.Count == 0)
        {
            return new StepExecutionResult
            {
                Output = context.AccumulatedOutput,
                NextStepIndex = context.CurrentStepIndex + 1,
                NewIterationCount = context.IterationCount,
                Status = StepStatus.Skipped,
                ErrorDetails = "Parallel step: none of the configured agents could be resolved."
            };
        }

        _logger.LogInformation("Parallel step {StepOrder}: running {Count} agents in parallel: {Names}",
            context.Step.StepOrder, resolved.Count, string.Join(", ", resolved.Select(r => r.Name)));

        string stepInput = context.BuildAgentInput();

        // Execute all agents in parallel
        ParallelAgentOutput[] results = new ParallelAgentOutput[resolved.Count];
        long totalDurationMs = 0;
        int totalTokens = 0;

        using IDisposable _ = _executionContextAccessor.BeginScope(
            context.Execution.Id,
            context.Execution.ProjectId,
            context.CorrelationId);

        await Parallel.ForEachAsync(
            Enumerable.Range(0, resolved.Count),
            new ParallelOptions { CancellationToken = context.CancellationToken },
            async (index, token) =>
            {
                (string name, IReelForgeAgent agent) = resolved[index];
                Stopwatch sw = Stopwatch.StartNew();
                AgentRunResult agentResult = await agent.RunAsync(stepInput, token);
                sw.Stop();

                _logger.LogInformation(
                    "Parallel step {StepOrder}: agent {AgentName} responded with preview: {ResponsePreview}",
                    context.Step.StepOrder,
                    name,
                    CreateResponsePreview(agentResult.Output, 250));

                Interlocked.Add(ref totalDurationMs, sw.ElapsedMilliseconds);
                Interlocked.Add(ref totalTokens, agentResult.TokensUsed);

                results[index] = new ParallelAgentOutput(name, agentResult.Output);
            });

        // Serialize structured output: [{"agentName":"...","output":"..."}, ...]
        string mergedOutput = JsonSerializer.Serialize(results, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        return new StepExecutionResult
        {
            Output = mergedOutput,
            NextStepIndex = context.CurrentStepIndex + 1,
            NewIterationCount = context.IterationCount,
            DurationMs = totalDurationMs,
            TokensUsed = totalTokens,
            Status = StepStatus.Completed
        };
    }

    private static List<Guid> BuildAgentIdList(WorkflowStep step)
    {
        var ids = new List<Guid>();

        // Parse ParallelAgentIdsJson first (it should include all agents, including the primary)
        if (!string.IsNullOrWhiteSpace(step.ParallelAgentIdsJson))
        {
            try
            {
                Guid[]? parsed = JsonSerializer.Deserialize<Guid[]>(step.ParallelAgentIdsJson);
                if (parsed != null)
                    ids.AddRange(parsed);
            }
            catch (JsonException) { /* fall through to primary */ }
        }

        // Always ensure the primary AgentDefinitionId is included (deduped)
        if (step.AgentDefinitionId != Guid.Empty && !ids.Contains(step.AgentDefinitionId))
            ids.Insert(0, step.AgentDefinitionId);

        // Deduplicate while preserving order
        return ids.Distinct().ToList();
    }

    private static string CreateResponsePreview(string? response, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(response))
            return string.Empty;

        string normalized = response.Replace("\r", " ").Replace("\n", " ").Trim();
        if (normalized.Length <= maxLength)
            return normalized;

        return normalized[..maxLength];
    }
}

/// <summary>
/// Output entry for a single agent within a Parallel step.
/// Serialises as {"agentName":"...","output":"..."}.
/// </summary>
public record ParallelAgentOutput(string AgentName, string Output);
