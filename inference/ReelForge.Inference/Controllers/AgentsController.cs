using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReelForge.Inference.Controllers.Dto;
using ReelForge.Inference.Data;
using ReelForge.Inference.Data.Models;
using ReelForge.Inference.Services.Auth;

namespace ReelForge.Inference.Controllers;

/// <summary>
/// Manages agent definitions (built-in and custom).
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class AgentsController : ControllerBase
{
    private readonly ReelForgeDbContext _db;
    private readonly ICurrentUser _currentUser;

    public AgentsController(ReelForgeDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    /// <summary>Lists all agents visible to the caller (built-in + own custom).</summary>
    [HttpGet]
    public async Task<ActionResult<List<AgentDefinitionResponse>>> List(CancellationToken ct)
    {
        List<AgentDefinitionResponse> agents = await _db.AgentDefinitions
            .Where(a => a.IsBuiltIn || a.OwnerId == _currentUser.UserId)
            .OrderBy(a => a.Name)
            .Select(a => new AgentDefinitionResponse(a.Id, a.Name, a.Description, a.SystemPrompt, a.AgentType.ToString(), a.IsBuiltIn, a.OwnerId, a.ConfigJson, a.CreatedAt))
            .ToListAsync(ct);

        return Ok(agents);
    }

    /// <summary>Gets an agent definition by ID.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AgentDefinitionResponse>> Get(Guid id, CancellationToken ct)
    {
        AgentDefinition? agent = await _db.AgentDefinitions.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (agent == null) return NotFound();
        if (!agent.IsBuiltIn && agent.OwnerId != _currentUser.UserId) return Forbid();

        return Ok(new AgentDefinitionResponse(agent.Id, agent.Name, agent.Description, agent.SystemPrompt, agent.AgentType.ToString(), agent.IsBuiltIn, agent.OwnerId, agent.ConfigJson, agent.CreatedAt));
    }

    /// <summary>Creates a custom agent definition.</summary>
    [HttpPost]
    public async Task<ActionResult<AgentDefinitionResponse>> Create([FromBody] CreateAgentRequest request, CancellationToken ct)
    {
        AgentDefinition agent = new()
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            SystemPrompt = request.SystemPrompt,
            AgentType = AgentType.Custom,
            IsBuiltIn = false,
            OwnerId = _currentUser.UserId,
            ConfigJson = request.ConfigJson,
            CreatedAt = DateTime.UtcNow
        };

        _db.AgentDefinitions.Add(agent);
        await _db.SaveChangesAsync(ct);

        AgentDefinitionResponse response = new(agent.Id, agent.Name, agent.Description, agent.SystemPrompt, agent.AgentType.ToString(), agent.IsBuiltIn, agent.OwnerId, agent.ConfigJson, agent.CreatedAt);
        return CreatedAtAction(nameof(Get), new { id = agent.Id }, response);
    }

    /// <summary>Updates a custom agent definition (only owner, only non-built-in).</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AgentDefinitionResponse>> Update(Guid id, [FromBody] UpdateAgentRequest request, CancellationToken ct)
    {
        AgentDefinition? agent = await _db.AgentDefinitions.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (agent == null) return NotFound();
        if (agent.IsBuiltIn) return Forbid();
        if (agent.OwnerId != _currentUser.UserId) return Forbid();

        agent.Name = request.Name;
        agent.Description = request.Description;
        agent.SystemPrompt = request.SystemPrompt;
        agent.ConfigJson = request.ConfigJson;
        await _db.SaveChangesAsync(ct);

        return Ok(new AgentDefinitionResponse(agent.Id, agent.Name, agent.Description, agent.SystemPrompt, agent.AgentType.ToString(), agent.IsBuiltIn, agent.OwnerId, agent.ConfigJson, agent.CreatedAt));
    }

    /// <summary>Deletes a custom agent definition.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        AgentDefinition? agent = await _db.AgentDefinitions.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (agent == null) return NotFound();
        if (agent.IsBuiltIn) return Forbid();
        if (agent.OwnerId != _currentUser.UserId) return Forbid();

        _db.AgentDefinitions.Remove(agent);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }
}
