using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReelForge.WorkflowEngine.Services.Skills;

namespace ReelForge.WorkflowEngine.Controllers;

[ApiController]
[Route("api/v1/workflow-engine/[controller]")]
[Authorize]
public class AdminController : ControllerBase
{
    private readonly ISkillCorpusService _skillCorpusService;

    public AdminController(ISkillCorpusService skillCorpusService)
    {
        _skillCorpusService = skillCorpusService;
    }

    [HttpGet("status")]
    public IActionResult Status()
    {
        // Admin-only: check isAdmin claim from JWT
        string? isAdminClaim = User.FindFirstValue("isAdmin");
        if (!bool.TryParse(isAdminClaim, out bool isAdmin) || !isAdmin)
        {
            return Forbid();
        }

        return Ok(new
        {
            service = "workflow-engine",
            status = "running",
            timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Diagnostic endpoint for the vendored skill corpus (inference/skills/, Skills:CorpusPath) —
    /// this is the endpoint that would have immediately caught the original RemotionSkillsService
    /// bug (path-prefix filter excluding every skill directory, and a cache guard that hot-looped
    /// re-fetching an empty index): how many skills loaded successfully, their names/versions, and
    /// any that failed validation with the specific reason, so a bad vendor/refresh is visible
    /// without having to run an agent and watch it fail to find a skill.
    /// </summary>
    [HttpGet("/api/v1/workflow-engine/skills/status")]
    public async Task<IActionResult> SkillsStatus(CancellationToken ct)
    {
        string? isAdminClaim = User.FindFirstValue("isAdmin");
        if (!bool.TryParse(isAdminClaim, out bool isAdmin) || !isAdmin)
        {
            return Forbid();
        }

        SkillCorpusStatus status = await _skillCorpusService.GetStatusAsync(ct);

        return Ok(new
        {
            loadedCount = status.Loaded.Count,
            failedCount = status.Failed.Count,
            loaded = status.Loaded.Select(s => new
            {
                s.Name,
                s.DisplayName,
                s.Version,
                s.BodyLength
            }),
            failed = status.Failed.Select(f => new
            {
                f.Name,
                f.Reason
            })
        });
    }
}
