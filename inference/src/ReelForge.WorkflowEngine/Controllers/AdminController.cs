using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ReelForge.WorkflowEngine.Controllers;

[ApiController]
[Route("api/v1/workflow-engine/[controller]")]
[Authorize]
public class AdminController : ControllerBase
{
    [HttpGet("status")]
    public IActionResult Status() => Ok(new
    {
        service = "workflow-engine",
        status = "running",
        timestamp = DateTime.UtcNow
    });
}
