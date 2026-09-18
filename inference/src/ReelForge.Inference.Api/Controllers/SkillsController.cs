using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReelForge.Inference.Api.Controllers.Dto;
using ReelForge.Inference.Api.Data;
using ReelForge.Shared.Auth;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Skills;
using System.Text.Json;

namespace ReelForge.Inference.Api.Controllers;

/// <summary>
/// Read-only skill catalog listing plus the admin-only per-agent skill assignment endpoint.
///
/// Skill assignment is editable ONLY for a custom (non-built-in) AgentDefinition — a built-in
/// agent's skills come exclusively from SkillCatalog.DefaultsFor(AgentType) in code and the
/// seeded prompt, never from a runtime override. This is the OPPOSITE of how the per-agent
/// inference-provider override works (AgentsController.SetInferenceProvider, deliberately
/// allowed for built-ins) — SetSkills below is modeled on AgentsController.Update's
/// built-in-forbid pattern instead.
///
/// Routed under /api/v1/skills (and /api/v1/agents/{id}/skills for the PUT), not
/// /api/v1/admin/*, for the same reason as /api/v1/inference-providers: nginx routes
/// /api/v1/admin/* to the Go API, so an Inference-API-owned admin endpoint must live elsewhere.
/// </summary>
[ApiController]
[Route("api/v1/skills")]
[Authorize]
public class SkillsController : ControllerBase
{
    private readonly InferenceApiDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IWebHostEnvironment _env;

    public SkillsController(InferenceApiDbContext db, ICurrentUser currentUser, IWebHostEnvironment env)
    {
        _db = db;
        _currentUser = currentUser;
        _env = env;
    }

    [HttpGet]
    public async Task<ActionResult<List<SkillSummaryResponse>>> List(CancellationToken ct)
    {
        Dictionary<string, int> assignedCounts = await ComputeAssignedCountsAsync(ct);

        List<SkillSummaryResponse> result = SkillCatalog.All
            .Select(s => new SkillSummaryResponse(
                s.Name,
                s.DisplayName,
                s.Description,
                s.Category.ToString(),
                s.Version,
                DefaultForAgentTypes(s.Name),
                assignedCounts.GetValueOrDefault(s.Name)))
            .ToList();

        return Ok(result);
    }

    [HttpGet("{name:regex(^[a-z0-9-]+$)}")]
    public async Task<ActionResult<SkillDetailResponse>> Get(string name, CancellationToken ct)
    {
        SkillDescriptor? descriptor = SkillCatalog.Find(name);
        if (descriptor == null) return NotFound();

        Dictionary<string, int> assignedCounts = await ComputeAssignedCountsAsync(ct);
        string? body = await TryReadSkillBodyAsync(descriptor, ct);

        return Ok(new SkillDetailResponse(
            descriptor.Name,
            descriptor.DisplayName,
            descriptor.Description,
            descriptor.Category.ToString(),
            descriptor.Version,
            DefaultForAgentTypes(descriptor.Name),
            assignedCounts.GetValueOrDefault(descriptor.Name),
            body));
    }

    /// <summary>
    /// Sets (or clears) a custom agent's skill assignment. Admin-only. Unlike
    /// AgentsController.SetInferenceProvider, this is explicitly NOT allowed for built-in
    /// agents — a built-in's skills come from code, full stop. See the class-level doc comment.
    /// </summary>
    [HttpPut("/api/v1/agents/{id:guid}/skills")]
    public async Task<ActionResult<AgentDefinitionResponse>> SetSkills(
        Guid id, [FromBody] SetAgentSkillsRequest request, CancellationToken ct)
    {
        if (!_currentUser.IsAdmin) return Forbid();

        AgentDefinition? agent = await _db.AgentDefinitions
            .Include(a => a.InferenceProvider)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
        if (agent == null) return NotFound();

        // The one thing to get right: skill assignment is editable ONLY for custom agents.
        // Mirrors AgentsController.Update's Forbid() (not SetInferenceProvider's allow-built-ins
        // pattern), since a built-in's skills come exclusively from
        // SkillCatalog.DefaultsFor(AgentType) in code — never from this column.
        if (agent.IsBuiltIn) return Forbid();

        string[] skills = request.Skills ?? [];
        string[] unknown = skills
            .Where(s => SkillCatalog.Find(s) == null)
            .ToArray();
        if (unknown.Length > 0)
        {
            return BadRequest(new { error = "Unknown skill name(s).", skills = unknown });
        }

        // Store the catalog's canonical names (deduplicated), not the caller's casing — every
        // later consumer then matches by simple equality instead of depending on each read path
        // remembering to compare case-insensitively.
        string[] canonical = skills
            .Select(s => SkillCatalog.Find(s)!.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        agent.AssignedSkillsJson = JsonSerializer.Serialize(canonical);
        await _db.SaveChangesAsync(ct);

        return Ok(AgentsController.MapToResponse(agent));
    }

    /// <summary>Reverse-indexes SkillCatalog.DefaultsFor across every AgentType for one skill name.</summary>
    private static string[] DefaultForAgentTypes(string skillName) =>
        Enum.GetValues<AgentType>()
            .Where(t => SkillCatalog.DefaultsFor(t).Contains(skillName, StringComparer.OrdinalIgnoreCase))
            .Select(t => t.ToString())
            .ToArray();

    /// <summary>How many custom AgentDefinition rows currently have this skill assigned.</summary>
    private async Task<Dictionary<string, int>> ComputeAssignedCountsAsync(CancellationToken ct)
    {
        List<string?> jsonValues = await _db.AgentDefinitions
            .Where(a => !a.IsBuiltIn && a.AssignedSkillsJson != null)
            .Select(a => a.AssignedSkillsJson)
            .ToListAsync(ct);

        Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);
        foreach (string? json in jsonValues)
        {
            if (string.IsNullOrEmpty(json)) continue;

            string[]? names;
            try
            {
                names = JsonSerializer.Deserialize<string[]>(json);
            }
            catch (JsonException)
            {
                continue;
            }

            if (names == null) continue;

            foreach (string name in names)
            {
                counts[name] = counts.GetValueOrDefault(name) + 1;
            }
        }

        return counts;
    }

    /// <summary>
    /// Reads a skill's SKILL.md body from the vendored corpus, if reachable. Returns null (never
    /// throws) when the corpus isn't present — e.g. this worktree's Dockerfile doesn't yet COPY
    /// inference/skills/ into the image, since that vendored corpus doesn't exist here yet
    /// (a sibling effort's responsibility — see SkillCatalog.cs's stub note).
    /// </summary>
    private async Task<string?> TryReadSkillBodyAsync(SkillDescriptor descriptor, CancellationToken ct)
    {
        string path = Path.Combine(_env.ContentRootPath, "skills", descriptor.RelativePath, "SKILL.md");
        try
        {
            if (!System.IO.File.Exists(path)) return null;
            return await System.IO.File.ReadAllTextAsync(path, ct);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
