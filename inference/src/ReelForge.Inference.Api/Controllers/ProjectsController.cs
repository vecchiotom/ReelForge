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
public class ProjectsController : ControllerBase
{
    private readonly InferenceApiDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ProjectsController(InferenceApiDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<List<ProjectResponse>>> List(CancellationToken ct)
    {
        List<ProjectResponse> projects = await _db.Projects
            .Where(p => p.OwnerId == _currentUser.UserId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new ProjectResponse(p.Id, p.Name, p.Description, p.Status.ToString(), p.CreatedAt, p.UpdatedAt))
            .ToListAsync(ct);
        return Ok(projects);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProjectResponse>> Get(Guid id, CancellationToken ct)
    {
        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project == null) return NotFound();
        if (project.OwnerId != _currentUser.UserId) return Forbid();
        return Ok(new ProjectResponse(project.Id, project.Name, project.Description, project.Status.ToString(), project.CreatedAt, project.UpdatedAt));
    }

    [HttpPost]
    public async Task<ActionResult<ProjectResponse>> Create([FromBody] CreateProjectRequest request, CancellationToken ct)
    {
        ApplicationUser? user = await _db.ApplicationUsers.FirstOrDefaultAsync(u => u.Id == _currentUser.UserId, ct);
        if (user == null)
        {
            user = new ApplicationUser
            {
                Id = _currentUser.UserId,
                Email = _currentUser.Email,
                DisplayName = _currentUser.Email,
                CreatedAt = DateTime.UtcNow
            };
            _db.ApplicationUsers.Add(user);
        }

        Project project = new()
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            OwnerId = _currentUser.UserId,
            Status = ProjectStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Projects.Add(project);
        await _db.SaveChangesAsync(ct);

        ProjectResponse response = new(project.Id, project.Name, project.Description, project.Status.ToString(), project.CreatedAt, project.UpdatedAt);
        return CreatedAtAction(nameof(Get), new { id = project.Id }, response);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ProjectResponse>> Update(Guid id, [FromBody] UpdateProjectRequest request, CancellationToken ct)
    {
        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project == null) return NotFound();
        if (project.OwnerId != _currentUser.UserId) return Forbid();

        project.Name = request.Name;
        project.Description = request.Description;
        project.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(new ProjectResponse(project.Id, project.Name, project.Description, project.Status.ToString(), project.CreatedAt, project.UpdatedAt));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project == null) return NotFound();
        if (project.OwnerId != _currentUser.UserId) return Forbid();

        _db.Projects.Remove(project);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
