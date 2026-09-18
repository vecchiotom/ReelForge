using Microsoft.EntityFrameworkCore;
using ReelForge.Inference.Api.Data;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Workflows;

namespace ReelForge.Inference.Api.Services.Workflows;

public sealed class WorkflowTemplateProvisioningService
{
    private readonly InferenceApiDbContext _db;
    private readonly ILogger<WorkflowTemplateProvisioningService> _logger;
    private readonly IConfiguration _configuration;

    public WorkflowTemplateProvisioningService(
        InferenceApiDbContext db,
        ILogger<WorkflowTemplateProvisioningService> logger,
        IConfiguration configuration)
    {
        _db = db;
        _logger = logger;
        _configuration = configuration;
    }

    public bool IsAutoStarterEnabled() =>
        _configuration.GetValue("WorkflowTemplates:EnableAutoStarter", true);

    public bool IsAdvancedTemplatesEnabled() =>
        _configuration.GetValue("WorkflowTemplates:EnableAdvancedTemplates", true);

    public async Task<IReadOnlyList<WorkflowDefinition>> EnsureAutoTemplatesAsync(Guid projectId, CancellationToken ct)
    {
        if (!IsAutoStarterEnabled())
            return [];

        List<WorkflowDefinition> created = [];
        foreach (WorkflowTemplateDefinition template in WorkflowTemplateCatalog.GetAutoCreateTemplates())
        {
            WorkflowDefinition? workflow = await CreateFromTemplateAsync(projectId, template.Key, skipIfExists: true, ct);
            if (workflow != null)
                created.Add(workflow);
        }

        return created;
    }

    public Task<WorkflowDefinition?> CreateFromTemplateAsync(Guid projectId, string templateKey, bool skipIfExists, CancellationToken ct)
    {
        WorkflowTemplateDefinition? template = WorkflowTemplateCatalog.GetByKey(templateKey);
        if (template is null)
            throw new InvalidOperationException($"Unknown workflow template '{templateKey}'.");

        if (!template.AutoCreateOnProject && !IsAdvancedTemplatesEnabled())
            throw new InvalidOperationException("Advanced workflow templates are disabled by configuration.");

        return CreateFromTemplateAsync(projectId, template, skipIfExists, ct);
    }

    public async Task<IReadOnlyList<WorkflowTemplateDefinition>> ListAvailableTemplatesAsync(CancellationToken ct)
    {
        _ = ct;
        IReadOnlyList<WorkflowTemplateDefinition> all = WorkflowTemplateCatalog.GetAll();
        if (IsAdvancedTemplatesEnabled())
            return all;

        return all.Where(t => t.AutoCreateOnProject).ToList();
    }

    private async Task<WorkflowDefinition?> CreateFromTemplateAsync(Guid projectId, WorkflowTemplateDefinition template, bool skipIfExists, CancellationToken ct)
    {
        string workflowName = WorkflowTemplateCatalog.BuildVersionedWorkflowName(template);

        WorkflowDefinition? existing = await _db.WorkflowDefinitions
            .Include(workflow => workflow.Steps)
            .FirstOrDefaultAsync(workflow => workflow.ProjectId == projectId && workflow.Name == workflowName, ct);

        if (existing is not null)
        {
            if (skipIfExists)
                return existing;

            throw new InvalidOperationException($"Workflow template '{template.Key}' has already been applied to this project.");
        }

        IReadOnlyDictionary<AgentType, AgentDefinition> agentsByType = await ResolveBuiltInAgentsByTypeAsync(template, ct);

        WorkflowDefinition workflow = new()
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Name = workflowName,
            RequiresUserInput = template.RequiresUserInput,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        for (int i = 0; i < template.Steps.Count; i++)
        {
            WorkflowTemplateStepDefinition stepDefinition = template.Steps[i];
            AgentDefinition primaryAgent = agentsByType[stepDefinition.AgentType];

            WorkflowStep step = new()
            {
                Id = Guid.NewGuid(),
                WorkflowDefinitionId = workflow.Id,
                AgentDefinitionId = primaryAgent.Id,
                StepOrder = i + 1,
                Label = stepDefinition.Label,
                StepType = stepDefinition.StepType,
                ConditionExpression = stepDefinition.ConditionExpression,
                LoopSourceExpression = stepDefinition.LoopSourceExpression,
                LoopTargetStepOrder = stepDefinition.LoopTargetStepOrder,
                MaxIterations = stepDefinition.MaxIterations,
                MinScore = stepDefinition.MinScore,
                InputMappingJson = stepDefinition.InputMappingJson,
                AgentInputContextMode = stepDefinition.AgentInputContextMode,
                SelectedPriorStepOrdersJson = stepDefinition.SelectedPriorStepOrders is { Count: > 0 }
                    ? System.Text.Json.JsonSerializer.Serialize(stepDefinition.SelectedPriorStepOrders)
                    : null,
                TrueBranchStepOrder = stepDefinition.TrueBranchStepOrder,
                FalseBranchStepOrder = stepDefinition.FalseBranchStepOrder,
                ParallelAgentIdsJson = BuildParallelAgentIdsJson(stepDefinition, agentsByType),
                ExtractConfigJson = stepDefinition.ExtractConfigJson,
                VideoAnalyzeConfigJson = stepDefinition.VideoAnalyzeConfigJson,
                VideoCompileConfigJson = stepDefinition.VideoCompileConfigJson,
                EditRoomConfigJson = stepDefinition.EditRoomConfigJson,
                GraphicsRoomConfigJson = stepDefinition.GraphicsRoomConfigJson,
                ColorGradeRoomConfigJson = stepDefinition.ColorGradeRoomConfigJson
            };

            workflow.Steps.Add(step);
        }

        _db.WorkflowDefinitions.Add(workflow);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Applied workflow template {TemplateKey} to project {ProjectId} as workflow {WorkflowId}",
            template.Key,
            projectId,
            workflow.Id);

        return workflow;
    }

    private async Task<IReadOnlyDictionary<AgentType, AgentDefinition>> ResolveBuiltInAgentsByTypeAsync(
        WorkflowTemplateDefinition template,
        CancellationToken ct)
    {
        HashSet<AgentType> requiredAgentTypes = template.Steps
            .Select(step => step.AgentType)
            .Concat(template.Steps
                .Where(step => step.ParallelAgentTypes is { Count: > 0 })
                .SelectMany(step => step.ParallelAgentTypes!))
            .ToHashSet();

        List<AgentDefinition> builtIns = await _db.AgentDefinitions
            .Where(agent => agent.IsBuiltIn && requiredAgentTypes.Contains(agent.AgentType))
            .ToListAsync(ct);

        Dictionary<AgentType, AgentDefinition> result = builtIns
            .GroupBy(agent => agent.AgentType)
            .ToDictionary(group => group.Key, group => group.OrderBy(agent => agent.CreatedAt).First());

        AgentType[] missing = requiredAgentTypes
            .Where(type => !result.ContainsKey(type))
            .ToArray();

        if (missing.Length > 0)
            throw new InvalidOperationException($"Missing built-in agent definitions: {string.Join(", ", missing)}");

        return result;
    }

    private static string? BuildParallelAgentIdsJson(
        WorkflowTemplateStepDefinition stepDefinition,
        IReadOnlyDictionary<AgentType, AgentDefinition> agentsByType)
    {
        if (stepDefinition.ParallelAgentTypes is not { Count: > 0 })
            return null;

        List<Guid> ids = stepDefinition.ParallelAgentTypes
            .Distinct()
            .Where(type => agentsByType.ContainsKey(type))
            .Select(type => agentsByType[type].Id)
            .ToList();

        return ids.Count == 0
            ? null
            : System.Text.Json.JsonSerializer.Serialize(ids);
    }
}
