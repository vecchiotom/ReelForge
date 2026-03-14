using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReelForge.Inference.Api.Controllers.Dto;
using ReelForge.Inference.Api.Data;
using ReelForge.Inference.Api.Services.Workflows;
using ReelForge.Shared.Auth;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.IntegrationEvents;
using ReelForge.Shared.Workflows;
using System;

namespace ReelForge.Inference.Api.Controllers;

[ApiController]
[Route("api/v1/projects/{projectId:guid}/workflows")]
[Authorize]
public class WorkflowsController : ControllerBase
{
    private readonly InferenceApiDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly WorkflowTemplateProvisioningService _workflowTemplateProvisioningService;

    public WorkflowsController(
        InferenceApiDbContext db,
        ICurrentUser currentUser,
        IPublishEndpoint publishEndpoint,
        WorkflowTemplateProvisioningService workflowTemplateProvisioningService)
    {
        _db = db;
        _currentUser = currentUser;
        _publishEndpoint = publishEndpoint;
        _workflowTemplateProvisioningService = workflowTemplateProvisioningService;
    }

    [HttpGet("templates")]
    public async Task<ActionResult<List<WorkflowTemplateSummaryResponse>>> ListTemplates(Guid projectId, CancellationToken ct)
    {
        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound();
        if (project.OwnerId != _currentUser.UserId) return Forbid();

        IReadOnlyList<WorkflowTemplateDefinition> templates = await _workflowTemplateProvisioningService.ListAvailableTemplatesAsync(ct);

        return Ok(templates.Select(template => new WorkflowTemplateSummaryResponse(
            template.Key,
            template.Name,
            template.Description,
            template.Version,
            template.AutoCreateOnProject,
            template.RequiresUserInput,
            template.Steps.Count)).ToList());
    }

    [HttpPost("templates/{templateKey}/apply")]
    public async Task<ActionResult<WorkflowDefinitionResponse>> ApplyTemplate(
        Guid projectId,
        string templateKey,
        [FromBody] ApplyWorkflowTemplateRequest? request,
        CancellationToken ct)
    {
        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound();
        if (project.OwnerId != _currentUser.UserId) return Forbid();

        bool skipIfExists = request?.SkipIfExists ?? false;
        WorkflowDefinition? workflow;
        try
        {
            workflow = await _workflowTemplateProvisioningService.CreateFromTemplateAsync(projectId, templateKey, skipIfExists, ct);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }

        if (workflow is null)
            return Conflict(new { message = "Template could not be applied." });

        workflow = await _db.WorkflowDefinitions
            .Include(w => w.Steps)
            .FirstAsync(w => w.Id == workflow.Id, ct);

        return Ok(MapWorkflowResponse(workflow));
    }

    [HttpGet]
    public async Task<ActionResult<List<WorkflowDefinitionResponse>>> List(Guid projectId, CancellationToken ct)
    {
        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound();
        if (project.OwnerId != _currentUser.UserId) return Forbid();

        List<WorkflowDefinitionResponse> workflows = await _db.WorkflowDefinitions
            .Where(w => w.ProjectId == projectId)
            .Include(w => w.Steps)
            .OrderByDescending(w => w.CreatedAt)
            .Select(w => new WorkflowDefinitionResponse(
                w.Id, w.Name, w.CreatedAt, w.UpdatedAt,
                w.Steps.OrderBy(s => s.StepOrder).Select(s =>
                    new WorkflowStepResponse(s.Id, s.AgentDefinitionId, s.StepOrder, s.EdgeConditionJson, s.Label,
                        s.StepType.ToString(), s.ConditionExpression, s.LoopSourceExpression,
                        s.LoopTargetStepOrder, s.MaxIterations, s.MinScore, s.InputMappingJson,
                        s.AgentInputContextMode.HasValue ? s.AgentInputContextMode.Value.ToString() : null,
                        s.SelectedPriorStepOrdersJson,
                        s.TrueBranchStepOrder, s.FalseBranchStepOrder, s.ParallelAgentIdsJson)
                ).ToList(),
                w.RequiresUserInput))
            .ToListAsync(ct);
        return Ok(workflows);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WorkflowDefinitionResponse>> Get(Guid id, CancellationToken ct)
    {
        WorkflowDefinition? workflow = await _db.WorkflowDefinitions
            .Include(w => w.Steps)
            .FirstOrDefaultAsync(w => w.Id == id, ct);
        if (workflow == null) return NotFound();
        return Ok(MapWorkflowResponse(workflow));
    }

    [HttpPost]
    public async Task<ActionResult<WorkflowDefinitionResponse>> Create(Guid projectId, [FromBody] CreateWorkflowRequest request, CancellationToken ct)
    {
        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound();
        if (project.OwnerId != _currentUser.UserId) return Forbid();

        WorkflowDefinition workflow = new()
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            ProjectId = projectId,
            RequiresUserInput = request.RequiresUserInput,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        foreach (CreateWorkflowStepRequest stepReq in request.Steps)
        {
            workflow.Steps.Add(CreateStep(workflow.Id, stepReq));
        }

        _db.WorkflowDefinitions.Add(workflow);
        await _db.SaveChangesAsync(ct);

        return StatusCode(201, MapWorkflowResponse(workflow));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<WorkflowDefinitionResponse>> Update(Guid projectId, Guid id, [FromBody] UpdateWorkflowRequest request, CancellationToken ct)
    {
        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound();
        if (project.OwnerId != _currentUser.UserId) return Forbid();

        WorkflowDefinition? workflow = await _db.WorkflowDefinitions
            .FirstOrDefaultAsync(w => w.Id == id && w.ProjectId == projectId, ct);
        if (workflow == null) return NotFound();

        // Replace workflow steps atomically: remove all existing rows for this workflow,
        // then insert the provided set. This avoids tracked-entity delete/re-add state
        // conflicts that can surface as false DbUpdateConcurrencyException responses.
        await _db.WorkflowSteps
            .Where(s => s.WorkflowDefinitionId == workflow.Id)
            .ExecuteDeleteAsync(ct);

        if (!string.IsNullOrWhiteSpace(request.Name))
            workflow.Name = request.Name;

        if (request.RequiresUserInput.HasValue)
            workflow.RequiresUserInput = request.RequiresUserInput.Value;

        foreach (CreateWorkflowStepRequest stepReq in request.Steps)
        {
            _db.WorkflowSteps.Add(CreateStep(workflow.Id, stepReq));
        }

        workflow.UpdatedAt = DateTime.UtcNow;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // the workflow or one of its steps was removed by another user/session
            return Conflict("The workflow was modified or deleted by another process. Please reload and try again.");
        }

        return Ok(MapWorkflowResponse(workflow));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid projectId, Guid id, CancellationToken ct)
    {
        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound();
        if (project.OwnerId != _currentUser.UserId) return Forbid();

        WorkflowDefinition? workflow = await _db.WorkflowDefinitions.FirstOrDefaultAsync(w => w.Id == id && w.ProjectId == projectId, ct);
        if (workflow == null) return NotFound();

        _db.WorkflowDefinitions.Remove(workflow);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/execute")]
    public async Task<ActionResult<WorkflowExecutionResponse>> Execute(
        Guid projectId, Guid id,
        [FromBody] ExecuteWorkflowRequest? request,
        CancellationToken ct)
    {
        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound();
        if (project.OwnerId != _currentUser.UserId) return Forbid();

        WorkflowDefinition? workflow = await _db.WorkflowDefinitions.FirstOrDefaultAsync(w => w.Id == id && w.ProjectId == projectId, ct);
        if (workflow == null) return NotFound();

        string correlationId = Guid.NewGuid().ToString();

        WorkflowExecution execution = new()
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = id,
            ProjectId = projectId,
            Status = ExecutionStatus.Queued,
            IterationCount = 0,
            CorrelationId = correlationId,
            InitiatedByUserId = _currentUser.UserId,
            UserRequest = request?.UserRequest
        };

        _db.WorkflowExecutions.Add(execution);
        await _db.SaveChangesAsync(ct);

        // Publish to RabbitMQ via MassTransit
        await _publishEndpoint.Publish(new WorkflowExecutionRequested
        {
            ExecutionId = execution.Id,
            WorkflowDefinitionId = id,
            ProjectId = projectId,
            InitiatedByUserId = _currentUser.UserId,
            CorrelationId = correlationId,
            RequestedAt = DateTime.UtcNow,
            UserRequest = request?.UserRequest
        }, ct);

        return StatusCode(201, new WorkflowExecutionResponse(
            execution.Id, execution.WorkflowDefinitionId, execution.Status.ToString(),
            execution.StartedAt, execution.CompletedAt, execution.IterationCount,
            execution.ResultJson, execution.CorrelationId, execution.ErrorMessage,
            new List<StepResultResponse>(), new List<ReviewScoreResponse>(),
            execution.UserRequest));
    }

    [HttpPost("{id:guid}/executions/{executionId:guid}/stop")]
    public async Task<IActionResult> StopExecution(
        Guid projectId, Guid id,
        Guid executionId,
        CancellationToken ct)
    {
        // verify project ownership
        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound();
        if (project.OwnerId != _currentUser.UserId) return Forbid();

        WorkflowExecution? exec = await _db.WorkflowExecutions
            .FirstOrDefaultAsync(e => e.Id == executionId && e.ProjectId == projectId && e.WorkflowDefinitionId == id, ct);
        if (exec == null) return NotFound();

        await _publishEndpoint.Publish(new WorkflowExecutionStopRequested
        {
            ExecutionId = executionId,
            RequestedByUserId = _currentUser.UserId
        }, ct);

        return Accepted();
    }

    [HttpGet("{id:guid}/executions")]
    public async Task<ActionResult<List<WorkflowExecutionResponse>>> ListExecutions(Guid projectId, Guid id, CancellationToken ct)
    {
        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound();
        if (project.OwnerId != _currentUser.UserId) return Forbid();

        WorkflowDefinition? workflow = await _db.WorkflowDefinitions.FirstOrDefaultAsync(w => w.Id == id && w.ProjectId == projectId, ct);
        if (workflow == null) return NotFound();

        List<WorkflowExecution> executions = await _db.WorkflowExecutions
            .Where(e => e.WorkflowDefinitionId == id && e.ProjectId == projectId)
            .Include(e => e.StepResults)
            .Include(e => e.ReviewScores)
            .AsSplitQuery()
            .OrderByDescending(e => e.StartedAt)
            .ToListAsync(ct);

        return Ok(executions.Select(MapExecutionResponse).ToList());
    }

    [HttpGet("~/api/v1/projects/{projectId:guid}/executions/{executionId:guid}")]
    public async Task<ActionResult<WorkflowExecutionResponse>> GetExecution(Guid projectId, Guid executionId, CancellationToken ct)
    {
        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound();
        if (project.OwnerId != _currentUser.UserId) return Forbid();

        WorkflowExecution? execution = await _db.WorkflowExecutions
            .Include(e => e.StepResults)
            .Include(e => e.ReviewScores)
            .AsSplitQuery()
            .FirstOrDefaultAsync(e => e.Id == executionId && e.ProjectId == projectId, ct);
        if (execution == null) return NotFound();

        return Ok(MapExecutionResponse(execution));
    }

    private static WorkflowStep CreateStep(Guid workflowId, CreateWorkflowStepRequest req)
    {
        var step = new WorkflowStep
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = workflowId,
            AgentDefinitionId = req.AgentDefinitionId,
            StepOrder = req.StepOrder,
            EdgeConditionJson = req.EdgeConditionJson,
            Label = req.Label,
            ConditionExpression = req.ConditionExpression,
            LoopSourceExpression = req.LoopSourceExpression,
            LoopTargetStepOrder = req.LoopTargetStepOrder,
            MinScore = req.MinScore,
            InputMappingJson = req.InputMappingJson,
            SelectedPriorStepOrdersJson = req.SelectedPriorStepOrdersJson,
            TrueBranchStepOrder = req.TrueBranchStepOrder,
            FalseBranchStepOrder = req.FalseBranchStepOrder,
            ParallelAgentIdsJson = req.ParallelAgentIdsJson
        };

        if (req.StepType != null && Enum.TryParse<StepType>(req.StepType, out var stepType))
            step.StepType = stepType;

        if (req.AgentInputContextMode != null
            && Enum.TryParse<AgentInputContextMode>(req.AgentInputContextMode, out var mode))
            step.AgentInputContextMode = mode;

        if (req.MaxIterations.HasValue)
            step.MaxIterations = req.MaxIterations.Value;

        return step;
    }

    private static WorkflowDefinitionResponse MapWorkflowResponse(WorkflowDefinition workflow) =>
        new(workflow.Id, workflow.Name, workflow.CreatedAt, workflow.UpdatedAt,
            workflow.Steps.OrderBy(s => s.StepOrder).Select(s =>
                new WorkflowStepResponse(s.Id, s.AgentDefinitionId, s.StepOrder, s.EdgeConditionJson, s.Label,
                    s.StepType.ToString(), s.ConditionExpression, s.LoopSourceExpression,
                    s.LoopTargetStepOrder, s.MaxIterations, s.MinScore, s.InputMappingJson,
                    s.AgentInputContextMode?.ToString(), s.SelectedPriorStepOrdersJson,
                    s.TrueBranchStepOrder, s.FalseBranchStepOrder, s.ParallelAgentIdsJson)
            ).ToList(),
            workflow.RequiresUserInput);

    private static WorkflowExecutionResponse MapExecutionResponse(WorkflowExecution execution) =>
        new(execution.Id, execution.WorkflowDefinitionId, execution.Status.ToString(),
            execution.StartedAt, execution.CompletedAt, execution.IterationCount,
            execution.ResultJson, execution.CorrelationId, execution.ErrorMessage,
            (execution.StepResults ?? Enumerable.Empty<WorkflowStepResult>()).OrderBy(r => r.ExecutedAt).Select(r =>
                new StepResultResponse(r.Id, r.WorkflowStepId, r.Output, r.TokensUsed, r.DurationMs, r.ExecutedAt,
                    r.InputJson, r.OutputJson, r.Status.ToString(), r.ErrorDetails, r.IterationNumber, r.CompletedAt,
                    r.OutputStorageKey)
            ).ToList(),
            (execution.ReviewScores ?? Enumerable.Empty<ReviewScore>()).OrderBy(rs => rs.IterationNumber).Select(rs =>
                new ReviewScoreResponse(rs.Id, rs.IterationNumber, rs.Score, rs.Comments, rs.CreatedAt)
            ).ToList(),
            execution.UserRequest);
}
