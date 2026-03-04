using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReelForge.Inference.Controllers.Dto;
using ReelForge.Inference.Data;
using ReelForge.Inference.Data.Models;
using ReelForge.Inference.Services.Auth;
using ReelForge.Inference.Services.Storage;
using ReelForge.Inference.Services.Background;

namespace ReelForge.Inference.Controllers;

/// <summary>
/// Manages files within a project.
/// </summary>
[ApiController]
[Route("api/v1/projects/{projectId:guid}/files")]
[Authorize]
public class ProjectFilesController : ControllerBase
{
    private readonly ReelForgeDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorageService _fileStorage;
    private readonly IBackgroundTaskQueue<FileSummarizationTask> _summarizationQueue;

    public ProjectFilesController(
        ReelForgeDbContext db,
        ICurrentUser currentUser,
        IFileStorageService fileStorage,
        IBackgroundTaskQueue<FileSummarizationTask> summarizationQueue)
    {
        _db = db;
        _currentUser = currentUser;
        _fileStorage = fileStorage;
        _summarizationQueue = summarizationQueue;
    }

    /// <summary>Lists all files for a project.</summary>
    [HttpGet]
    public async Task<ActionResult<List<ProjectFileResponse>>> List(Guid projectId, CancellationToken ct)
    {
        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound();
        if (project.OwnerId != _currentUser.UserId) return Forbid();

        List<ProjectFileResponse> files = await _db.ProjectFiles
            .Where(f => f.ProjectId == projectId)
            .OrderByDescending(f => f.UploadedAt)
            .Select(f => new ProjectFileResponse(f.Id, f.OriginalFileName, f.StorageKey, f.MimeType, f.SizeBytes, f.AgentSummary, f.SummaryStatus.ToString(), f.UploadedAt))
            .ToListAsync(ct);

        return Ok(files);
    }

    /// <summary>Uploads a file to the project. Enqueues async summarization.</summary>
    [HttpPost]
    public async Task<ActionResult<ProjectFileResponse>> Upload(Guid projectId, IFormFile file, CancellationToken ct)
    {
        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound();
        if (project.OwnerId != _currentUser.UserId) return Forbid();

        using Stream stream = file.OpenReadStream();
        string storageKey = await _fileStorage.UploadAsync(stream, file.FileName, file.ContentType, ct);

        ProjectFile projectFile = new()
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            OriginalFileName = file.FileName,
            StorageKey = storageKey,
            MimeType = file.ContentType,
            SizeBytes = file.Length,
            SummaryStatus = SummaryStatus.Pending,
            UploadedAt = DateTime.UtcNow
        };

        _db.ProjectFiles.Add(projectFile);
        await _db.SaveChangesAsync(ct);

        // Enqueue async summarization
        await _summarizationQueue.QueueAsync(new FileSummarizationTask(projectFile.Id), ct);

        ProjectFileResponse response = new(projectFile.Id, projectFile.OriginalFileName, projectFile.StorageKey, projectFile.MimeType, projectFile.SizeBytes, projectFile.AgentSummary, projectFile.SummaryStatus.ToString(), projectFile.UploadedAt);
        return StatusCode(201, response);
    }

    /// <summary>Deletes a file from the project and storage.</summary>
    [HttpDelete("{fileId:guid}")]
    public async Task<IActionResult> Delete(Guid projectId, Guid fileId, CancellationToken ct)
    {
        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound();
        if (project.OwnerId != _currentUser.UserId) return Forbid();

        ProjectFile? file = await _db.ProjectFiles.FirstOrDefaultAsync(f => f.Id == fileId && f.ProjectId == projectId, ct);
        if (file == null) return NotFound();

        await _fileStorage.DeleteAsync(file.StorageKey, ct);
        _db.ProjectFiles.Remove(file);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }
}
