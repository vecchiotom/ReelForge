using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReelForge.Inference.Controllers.Dto;
using ReelForge.Inference.Data;
using ReelForge.Inference.Data.Models;
using ReelForge.Inference.Services.Auth;
using ReelForge.Inference.Services.Background;

namespace ReelForge.Inference.Controllers;

/// <summary>
/// Manages workflow definitions and executions within a project.
/// </summary>
[ApiController]
[Route("api/v1/projects/{projectId:guid}/workflows")]
[Authorize]
public class WorkflowsController : ControllerBase
{
    private readonly ReelForgeDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IBackgroundTaskQueue<WorkflowExecutionTask> _executionQueue;

    public WorkflowsController(
        ReelForgeDbContext db,
        ICurrentUser currentUser,
        IBackgroundTaskQueue<WorkflowExecutionTask> executionQueue)
    {
        _db = db;
        _currentUser = currentUser;
        _executionQueue = executionQueue;
    }

    /// <summary>Lists workflow definitions for a project.</summary>
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
                    new WorkflowStepResponse(s.Id, s.AgentDefinitionId, s.StepOrder, s.EdgeConditionJson, s.Label)
                ).ToList()))
            .ToListAsync(ct);

        return Ok(workflows);
    }

    /// <summary>Creates a workflow definition with ordered steps.</summary>
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
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        foreach (CreateWorkflowStepRequest stepReq in request.Steps)
        {
            workflow.Steps.Add(new WorkflowStep
            {
                Id = Guid.NewGuid(),
                WorkflowDefinitionId = workflow.Id,
                AgentDefinitionId = stepReq.AgentDefinitionId,
                StepOrder = stepReq.StepOrder,
                EdgeConditionJson = stepReq.EdgeConditionJson,
                Label = stepReq.Label
            });
        }

        _db.WorkflowDefinitions.Add(workflow);
        await _db.SaveChangesAsync(ct);

        WorkflowDefinitionResponse response = new(
            workflow.Id, workflow.Name, workflow.CreatedAt, workflow.UpdatedAt,
            workflow.Steps.OrderBy(s => s.StepOrder).Select(s =>
                new WorkflowStepResponse(s.Id, s.AgentDefinitionId, s.StepOrder, s.EdgeConditionJson, s.Label)
            ).ToList());

        return StatusCode(201, response);
    }

    /// <summary>Updates workflow steps.</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<WorkflowDefinitionResponse>> Update(Guid projectId, Guid id, [FromBody] UpdateWorkflowRequest request, CancellationToken ct)
    {
        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound();
        if (project.OwnerId != _currentUser.UserId) return Forbid();

        WorkflowDefinition? workflow = await _db.WorkflowDefinitions
            .Include(w => w.Steps)
            .FirstOrDefaultAsync(w => w.Id == id && w.ProjectId == projectId, ct);
        if (workflow == null) return NotFound();

        // Remove existing steps and replace
        _db.WorkflowSteps.RemoveRange(workflow.Steps);

        foreach (CreateWorkflowStepRequest stepReq in request.Steps)
        {
            workflow.Steps.Add(new WorkflowStep
            {
                Id = Guid.NewGuid(),
                WorkflowDefinitionId = workflow.Id,
                AgentDefinitionId = stepReq.AgentDefinitionId,
                StepOrder = stepReq.StepOrder,
                EdgeConditionJson = stepReq.EdgeConditionJson,
                Label = stepReq.Label
            });
        }

        workflow.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        WorkflowDefinitionResponse response = new(
            workflow.Id, workflow.Name, workflow.CreatedAt, workflow.UpdatedAt,
            workflow.Steps.OrderBy(s => s.StepOrder).Select(s =>
                new WorkflowStepResponse(s.Id, s.AgentDefinitionId, s.StepOrder, s.EdgeConditionJson, s.Label)
            ).ToList());

        return Ok(response);
    }

    /// <summary>Deletes a workflow definition.</summary>
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

    /// <summary>Triggers an asynchronous workflow execution.</summary>
    [HttpPost("{id:guid}/execute")]
    public async Task<ActionResult<WorkflowExecutionResponse>> Execute(Guid projectId, Guid id, CancellationToken ct)
    {
        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound();
        if (project.OwnerId != _currentUser.UserId) return Forbid();

        WorkflowDefinition? workflow = await _db.WorkflowDefinitions.FirstOrDefaultAsync(w => w.Id == id && w.ProjectId == projectId, ct);
        if (workflow == null) return NotFound();

        WorkflowExecution execution = new()
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = id,
            ProjectId = projectId,
            Status = ExecutionStatus.Queued,
            IterationCount = 0
        };

        _db.WorkflowExecutions.Add(execution);
        await _db.SaveChangesAsync(ct);

        await _executionQueue.QueueAsync(new WorkflowExecutionTask(execution.Id), ct);

        WorkflowExecutionResponse response = new(
            execution.Id, execution.WorkflowDefinitionId, execution.Status.ToString(),
            execution.StartedAt, execution.CompletedAt, execution.IterationCount,
            execution.ResultJson, new List<StepResultResponse>());

        return StatusCode(201, response);
    }

    /// <summary>Polls execution status and step results.</summary>
    [HttpGet("~/api/v1/projects/{projectId:guid}/executions/{executionId:guid}")]
    public async Task<ActionResult<WorkflowExecutionResponse>> GetExecution(Guid projectId, Guid executionId, CancellationToken ct)
    {
        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound();
        if (project.OwnerId != _currentUser.UserId) return Forbid();

        WorkflowExecution? execution = await _db.WorkflowExecutions
            .Include(e => e.StepResults)
            .FirstOrDefaultAsync(e => e.Id == executionId && e.ProjectId == projectId, ct);
        if (execution == null) return NotFound();

        WorkflowExecutionResponse response = new(
            execution.Id, execution.WorkflowDefinitionId, execution.Status.ToString(),
            execution.StartedAt, execution.CompletedAt, execution.IterationCount,
            execution.ResultJson,
            execution.StepResults.OrderBy(r => r.ExecutedAt).Select(r =>
                new StepResultResponse(r.Id, r.WorkflowStepId, r.Output, r.TokensUsed, r.DurationMs, r.ExecutedAt)
            ).ToList());

        return Ok(response);
    }
}
