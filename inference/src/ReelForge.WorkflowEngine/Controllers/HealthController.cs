using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ReelForge.WorkflowEngine.Controllers;

[ApiController]
[Route("api/v1/workflow-engine/[controller]")]
[AllowAnonymous]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "healthy", service = "workflow-engine", timestamp = DateTime.UtcNow });
}
