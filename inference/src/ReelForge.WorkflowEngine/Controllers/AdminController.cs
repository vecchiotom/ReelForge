using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ReelForge.WorkflowEngine.Controllers;

[ApiController]
[Route("api/v1/workflow-engine/[controller]")]
[Authorize]
public class AdminController : ControllerBase
{
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
}
