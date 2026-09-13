using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReelForge.Inference.Api.Controllers.Dto;
using ReelForge.Inference.Api.Data;
using ReelForge.Shared.Auth;
using ReelForge.Shared.Data.Models;

namespace ReelForge.Inference.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class AgentsController : ControllerBase
{
    private readonly InferenceApiDbContext _db;
    private readonly ICurrentUser _currentUser;

    public AgentsController(InferenceApiDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<List<AgentDefinitionResponse>>> List(CancellationToken ct)
    {
        List<AgentDefinition> agents = await _db.AgentDefinitions
            .Include(a => a.InferenceProvider)
            .Where(a => a.IsBuiltIn || a.OwnerId == _currentUser.UserId)
            .OrderBy(a => a.Name)
            .ToListAsync(ct);
        return Ok(agents.Select(MapToResponse));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AgentDefinitionResponse>> Get(Guid id, CancellationToken ct)
    {
        AgentDefinition? agent = await _db.AgentDefinitions
            .Include(a => a.InferenceProvider)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
        if (agent == null) return NotFound();
        if (!agent.IsBuiltIn && agent.OwnerId != _currentUser.UserId) return Forbid();
        return Ok(MapToResponse(agent));
    }

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
            Color = request.Color,
            CreatedAt = DateTime.UtcNow
        };

        _db.AgentDefinitions.Add(agent);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = agent.Id }, MapToResponse(agent));
    }

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
        agent.Color = request.Color;
        await _db.SaveChangesAsync(ct);

        return Ok(MapToResponse(agent));
    }

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

    /// <summary>
    /// Sets (or clears) the per-agent inference provider override. Admin-only, and — unlike
    /// <see cref="Update"/> — explicitly allowed for built-in agents, since overriding a
    /// built-in's provider (e.g. routing a chatty analysis agent to a cheaper local model) is
    /// the primary use case for this feature.
    /// </summary>
    [HttpPut("{id:guid}/inference-provider")]
    public async Task<ActionResult<AgentDefinitionResponse>> SetInferenceProvider(
        Guid id, [FromBody] SetAgentInferenceProviderRequest request, CancellationToken ct)
    {
        if (!_currentUser.IsAdmin) return Forbid();

        AgentDefinition? agent = await _db.AgentDefinitions
            .Include(a => a.InferenceProvider)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
        if (agent == null) return NotFound();

        if (request.InferenceProviderId.HasValue)
        {
            // This override feeds chat-completion resolution only (InferenceProviderResolver.
            // ResolveAsync) — a Transcription-capability provider would build an IChatClient
            // against an ASR endpoint. Reject it here rather than relying on the resolver's own
            // defense-in-depth check or the UI's picker filter, neither of which is a server
            // boundary (found by Copilot review).
            InferenceProviderCapability? providerCapability = await _db.InferenceProviders
                .Where(p => p.Id == request.InferenceProviderId.Value)
                .Select(p => (InferenceProviderCapability?)p.Capability)
                .FirstOrDefaultAsync(ct);
            if (providerCapability == null)
            {
                return BadRequest(new { error = "Referenced inference provider not found." });
            }
            if (providerCapability != InferenceProviderCapability.Chat)
            {
                return BadRequest(new { error = "Only a Chat-capability provider can be assigned as an agent's inference provider override." });
            }
        }

        agent.InferenceProviderId = request.InferenceProviderId;
        await _db.SaveChangesAsync(ct);

        // Reload the navigation property so the response reflects the new (or cleared) provider.
        await _db.Entry(agent).Reference(a => a.InferenceProvider).LoadAsync(ct);

        return Ok(MapToResponse(agent));
    }

    private static AgentDefinitionResponse MapToResponse(AgentDefinition a)
    {
        string[]? tools = a.AvailableToolsJson != null
            ? System.Text.Json.JsonSerializer.Deserialize<string[]>(a.AvailableToolsJson)
            : null;
        return new AgentDefinitionResponse(
            a.Id, a.Name, a.Description, a.SystemPrompt,
            a.AgentType.ToString(), a.IsBuiltIn, a.OwnerId, a.ConfigJson,
            a.CreatedAt, a.Color, a.OutputSchemaJson,
            tools, a.GeneratesOutput, a.OutputSchemaName,
            a.InferenceProviderId, a.InferenceProvider?.Name);
    }
}
