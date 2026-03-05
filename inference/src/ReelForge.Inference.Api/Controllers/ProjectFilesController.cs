using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReelForge.Inference.Api.Controllers.Dto;
using ReelForge.Inference.Api.Data;
using ReelForge.Inference.Api.Services.Background;
using ReelForge.Inference.Api.Services.Storage;
using ReelForge.Shared.Auth;
using ReelForge.Shared.Data.Models;

namespace ReelForge.Inference.Api.Controllers;

[ApiController]
[Route("api/v1/projects/{projectId:guid}/files")]
[Authorize]
public class ProjectFilesController : ControllerBase
{
    private readonly InferenceApiDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorageService _fileStorage;
    private readonly IBackgroundTaskQueue<FileSummarizationTask> _summarizationQueue;

    public ProjectFilesController(
        InferenceApiDbContext db, ICurrentUser currentUser,
        IFileStorageService fileStorage,
        IBackgroundTaskQueue<FileSummarizationTask> summarizationQueue)
    {
        _db = db;
        _currentUser = currentUser;
        _fileStorage = fileStorage;
        _summarizationQueue = summarizationQueue;
    }

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

    [HttpPost]
    public async Task<ActionResult<ProjectFileResponse>> Upload(Guid projectId, IFormFile file, CancellationToken ct)
    {
        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound();
        if (project.OwnerId != _currentUser.UserId) return Forbid();

        Guid fileId = Guid.NewGuid();
        Dictionary<string, string> storageMetadata = new()
        {
            ["project-id"] = projectId.ToString(),
            ["owner-id"] = project.OwnerId.ToString(),
            ["uploaded-by"] = _currentUser.UserId.ToString(),
            ["project-file-id"] = fileId.ToString()
        };

        using Stream stream = file.OpenReadStream();
        StoredFileObject storedObject = await _fileStorage.UploadAsync(
            projectId,
            stream,
            file.FileName,
            file.ContentType,
            storageMetadata,
            ct);

        ProjectFile projectFile = new()
        {
            Id = fileId,
            ProjectId = projectId,
            OriginalFileName = file.FileName,
            StorageKey = storedObject.StorageKey,
            StorageBucket = storedObject.BucketName,
            StoragePrefix = storedObject.StoragePrefix,
            StorageMetadataJson = storedObject.StorageMetadataJson,
            MimeType = file.ContentType,
            SizeBytes = file.Length,
            SummaryStatus = SummaryStatus.Pending,
            UploadedAt = DateTime.UtcNow
        };

        _db.ProjectFiles.Add(projectFile);
        await _db.SaveChangesAsync(ct);

        await _summarizationQueue.QueueAsync(new FileSummarizationTask(projectFile.Id), ct);

        return StatusCode(201, new ProjectFileResponse(projectFile.Id, projectFile.OriginalFileName, projectFile.StorageKey, projectFile.MimeType, projectFile.SizeBytes, projectFile.AgentSummary, projectFile.SummaryStatus.ToString(), projectFile.UploadedAt));
    }

    [HttpDelete("{fileId:guid}")]
    public async Task<IActionResult> Delete(Guid projectId, Guid fileId, CancellationToken ct)
    {
        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound();
        if (project.OwnerId != _currentUser.UserId) return Forbid();

        ProjectFile? file = await _db.ProjectFiles.FirstOrDefaultAsync(f => f.Id == fileId && f.ProjectId == projectId, ct);
        if (file == null) return NotFound();

        await _fileStorage.DeleteAsync(projectId, file.StorageKey, ct);
        _db.ProjectFiles.Remove(file);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
