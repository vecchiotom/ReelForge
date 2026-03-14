using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MassTransit;
using ReelForge.Inference.Api.Controllers.Dto;
using ReelForge.Inference.Api.Data;
using ReelForge.Inference.Api.Services.Background;
using ReelForge.Inference.Api.Services.Storage;
using ReelForge.Inference.Api.Services.VectorSearch;
using ReelForge.Shared;
using ReelForge.Shared.Auth;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.IntegrationEvents;

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
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly VectorSearchQueryService _vectorSearchQueryService;

    public ProjectFilesController(
        InferenceApiDbContext db, ICurrentUser currentUser,
        IFileStorageService fileStorage,
        IBackgroundTaskQueue<FileSummarizationTask> summarizationQueue,
        IPublishEndpoint publishEndpoint,
        VectorSearchQueryService vectorSearchQueryService)
    {
        _db = db;
        _currentUser = currentUser;
        _fileStorage = fileStorage;
        _summarizationQueue = summarizationQueue;
        _publishEndpoint = publishEndpoint;
        _vectorSearchQueryService = vectorSearchQueryService;
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
            .Select(f => new ProjectFileResponse(
                f.Id,
                f.OriginalFileName,
                f.OriginalPath,
                f.DirectoryPath,
                f.Category,
                f.StorageKey,
                f.StorageFileName,
                f.MimeType,
                f.SizeBytes,
                f.AgentSummary,
                f.SummaryStatus.ToString(),
                f.UploadedAt))
            .ToListAsync(ct);
        return Ok(files);
    }

    [HttpPost]
    public async Task<ActionResult<ProjectFileResponse>> Upload(Guid projectId, IFormFile file, CancellationToken ct)
    {
        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound();
        if (project.OwnerId != _currentUser.UserId) return Forbid();

        // catch optional relative path that the client may send when uploading a folder
        string? relativePath = null;
        if (Request.Form.TryGetValue("relativePath", out var values))
        {
            relativePath = values.FirstOrDefault();
        }

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
            fileId,
            stream,
            Path.GetFileName(file.FileName),
            file.ContentType,
            storageMetadata,
            ct,
            category: "userFiles",
            originalPath: relativePath ?? file.FileName);

        ProjectFile projectFile = new()
        {
            Id = fileId,
            ProjectId = projectId,
            OriginalFileName = storedObject.OriginalFileName,
            OriginalPath = storedObject.OriginalPath,
            DirectoryPath = storedObject.DirectoryPath,
            Category = "userFiles",
            StorageKey = storedObject.StorageKey,
            StorageFileName = storedObject.StorageFileName,
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

        await _publishEndpoint.Publish(new ProjectFileIndexingRequested
        {
            ProjectId = projectId,
            FileId = projectFile.Id,
            Operation = "Upsert",
            RequestedAt = DateTime.UtcNow
        }, ct);

        await _summarizationQueue.QueueAsync(new FileSummarizationTask(projectFile.Id), ct);

        return StatusCode(201, new ProjectFileResponse(
            projectFile.Id,
            projectFile.OriginalFileName,
            projectFile.OriginalPath,
            projectFile.DirectoryPath,
            projectFile.Category,
            projectFile.StorageKey,
            projectFile.StorageFileName,
            projectFile.MimeType,
            projectFile.SizeBytes,
            projectFile.AgentSummary,
            projectFile.SummaryStatus.ToString(),
            projectFile.UploadedAt));
    }

    [HttpGet("{fileId:guid}/download")]
    public async Task<IActionResult> Download(Guid projectId, Guid fileId, CancellationToken ct)
    {
        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound();
        if (project.OwnerId != _currentUser.UserId) return Forbid();

        ProjectFile? file = await _db.ProjectFiles.FirstOrDefaultAsync(f => f.Id == fileId && f.ProjectId == projectId, ct);
        if (file == null) return NotFound();

        Stream stream = await _fileStorage.DownloadAsync(projectId, file.StorageKey, ct);
        return File(stream, string.IsNullOrWhiteSpace(file.MimeType) ? "application/octet-stream" : file.MimeType, file.OriginalFileName);
    }

    [HttpGet("{fileId:guid}/content")]
    public async Task<ActionResult<ProjectFileContentResponse>> GetContent(Guid projectId, Guid fileId, CancellationToken ct)
    {
        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound();
        if (project.OwnerId != _currentUser.UserId) return Forbid();

        ProjectFile? file = await _db.ProjectFiles.FirstOrDefaultAsync(f => f.Id == fileId && f.ProjectId == projectId, ct);
        if (file == null) return NotFound();

        if (!IsLikelyText(file.MimeType, file.OriginalFileName))
            return Conflict(new { message = "File is not a text-editable asset." });

        await using Stream stream = await _fileStorage.DownloadAsync(projectId, file.StorageKey, ct);
        using StreamReader reader = new(stream);
        string content = await reader.ReadToEndAsync(ct);

        return Ok(new ProjectFileContentResponse(
            file.Id,
            file.OriginalFileName,
            file.OriginalPath,
            file.MimeType,
            content,
            file.UploadedAt));
    }

    [HttpPut("{fileId:guid}/content")]
    public async Task<ActionResult<ProjectFileResponse>> UpdateContent(
        Guid projectId,
        Guid fileId,
        [FromBody] UpdateProjectFileContentRequest request,
        CancellationToken ct)
    {
        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound();
        if (project.OwnerId != _currentUser.UserId) return Forbid();

        ProjectFile? file = await _db.ProjectFiles.FirstOrDefaultAsync(f => f.Id == fileId && f.ProjectId == projectId, ct);
        if (file == null) return NotFound();

        if (!IsLikelyText(file.MimeType, file.OriginalFileName))
            return Conflict(new { message = "File is not a text-editable asset." });

        string normalizedPath = ProjectFilePath.NormalizeRelativePath(file.OriginalPath ?? file.OriginalFileName);
        string contentType = string.IsNullOrWhiteSpace(request.ContentType) ? file.MimeType : request.ContentType;

        Dictionary<string, string> metadata = new()
        {
            ["project-id"] = projectId.ToString(),
            ["owner-id"] = project.OwnerId.ToString(),
            ["uploaded-by"] = _currentUser.UserId.ToString(),
            ["project-file-id"] = fileId.ToString(),
            ["updated-by"] = _currentUser.UserId.ToString()
        };

        await using MemoryStream stream = new(System.Text.Encoding.UTF8.GetBytes(request.Content));
        StoredFileObject storedObject = await _fileStorage.UploadAsync(
            projectId,
            fileId,
            stream,
            file.OriginalFileName,
            contentType,
            metadata,
            ct,
            category: file.Category,
            originalPath: normalizedPath);

        if (!string.Equals(file.StorageKey, storedObject.StorageKey, StringComparison.Ordinal))
            await _fileStorage.DeleteAsync(projectId, file.StorageKey, ct);

        file.OriginalFileName = storedObject.OriginalFileName;
        file.OriginalPath = storedObject.OriginalPath;
        file.DirectoryPath = storedObject.DirectoryPath;
        file.StorageKey = storedObject.StorageKey;
        file.StorageFileName = storedObject.StorageFileName;
        file.StorageBucket = storedObject.BucketName;
        file.StoragePrefix = storedObject.StoragePrefix;
        file.StorageMetadataJson = storedObject.StorageMetadataJson;
        file.MimeType = contentType;
        file.SizeBytes = System.Text.Encoding.UTF8.GetByteCount(request.Content);
        file.SummaryStatus = SummaryStatus.Pending;
        file.UploadedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        await _publishEndpoint.Publish(new ProjectFileIndexingRequested
        {
            ProjectId = projectId,
            FileId = file.Id,
            Operation = "Upsert",
            RequestedAt = DateTime.UtcNow
        }, ct);

        await _summarizationQueue.QueueAsync(new FileSummarizationTask(file.Id), ct);

        return Ok(new ProjectFileResponse(
            file.Id,
            file.OriginalFileName,
            file.OriginalPath,
            file.DirectoryPath,
            file.Category,
            file.StorageKey,
            file.StorageFileName,
            file.MimeType,
            file.SizeBytes,
            file.AgentSummary,
            file.SummaryStatus.ToString(),
            file.UploadedAt));
    }

    [HttpPost("move")]
    public async Task<ActionResult<List<ProjectFileResponse>>> Move(
        Guid projectId,
        [FromBody] MoveProjectFilesRequest request,
        CancellationToken ct)
    {
        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound();
        if (project.OwnerId != _currentUser.UserId) return Forbid();

        if (request.FileIds == null || request.FileIds.Count == 0)
            return BadRequest(new { message = "At least one fileId is required." });

        string? targetDirectoryPath = ProjectFilePath.NormalizeDirectoryPath(request.TargetDirectoryPath);
        List<ProjectFile> files = await _db.ProjectFiles
            .Where(f => f.ProjectId == projectId && request.FileIds.Contains(f.Id))
            .ToListAsync(ct);

        if (files.Count != request.FileIds.Count)
            return NotFound(new { message = "One or more files were not found in project scope." });

        foreach (ProjectFile file in files)
        {
            string targetCategory = string.IsNullOrWhiteSpace(request.TargetCategory) ? file.Category : request.TargetCategory;
            if (targetCategory != "userFiles" && targetCategory != "agentFiles" && targetCategory != "outputFiles")
                return BadRequest(new { message = "TargetCategory must be userFiles, agentFiles, or outputFiles." });

            string storageFileName = string.IsNullOrWhiteSpace(file.StorageFileName)
                ? ProjectFilePath.BuildStorageFileName(file.Id, file.OriginalFileName)
                : file.StorageFileName;

            string nextOriginalPath = ProjectFilePath.CombineDirectoryAndFileName(targetDirectoryPath, file.OriginalFileName);
            string nextStorageKey = ProjectFilePath.BuildStorageKey(projectId, targetCategory, targetDirectoryPath, storageFileName);

            await _fileStorage.MoveAsync(projectId, file.StorageKey, nextStorageKey, ct);

            file.Category = targetCategory;
            file.DirectoryPath = targetDirectoryPath;
            file.OriginalPath = nextOriginalPath;
            file.StorageFileName = storageFileName;
            file.StorageKey = nextStorageKey;
            file.StoragePrefix = ProjectFilePath.BuildStoragePrefix(projectId, targetCategory);
            file.UploadedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        return Ok(files.Select(f => new ProjectFileResponse(
            f.Id,
            f.OriginalFileName,
            f.OriginalPath,
            f.DirectoryPath,
            f.Category,
            f.StorageKey,
            f.StorageFileName,
            f.MimeType,
            f.SizeBytes,
            f.AgentSummary,
            f.SummaryStatus.ToString(),
            f.UploadedAt)).ToList());
    }

    [HttpPost("folders")]
    public async Task<IActionResult> CreateFolder(Guid projectId, [FromBody] FolderPathRequest request, CancellationToken ct)
    {
        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound();
        if (project.OwnerId != _currentUser.UserId) return Forbid();

        string normalizedPath = ProjectFilePath.NormalizeRelativePath(request.Path);
        return StatusCode(201, new
        {
            path = normalizedPath,
            materialized = false,
            message = "Folder paths are logical and become materialized when files are uploaded or moved into them."
        });
    }

    [HttpPatch("folders")]
    public async Task<IActionResult> RenameFolder(Guid projectId, [FromBody] RenameFolderRequest request, CancellationToken ct)
    {
        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound();
        if (project.OwnerId != _currentUser.UserId) return Forbid();

        string sourcePath = ProjectFilePath.NormalizeRelativePath(request.SourcePath);
        string targetPath = ProjectFilePath.NormalizeRelativePath(request.TargetPath);

        List<ProjectFile> files = await _db.ProjectFiles
            .Where(f => f.ProjectId == projectId &&
                        f.DirectoryPath != null &&
                        (f.DirectoryPath == sourcePath || f.DirectoryPath.StartsWith(sourcePath + "/")))
            .ToListAsync(ct);

        if (files.Count == 0)
            return NotFound(new { message = "No files found in source folder." });

        foreach (ProjectFile file in files)
        {
            string oldDirectory = file.DirectoryPath!;
            string suffix = oldDirectory.Length == sourcePath.Length ? string.Empty : oldDirectory[sourcePath.Length..];
            string nextDirectory = string.IsNullOrEmpty(suffix)
                ? targetPath
                : $"{targetPath}{suffix}";

            string storageFileName = string.IsNullOrWhiteSpace(file.StorageFileName)
                ? ProjectFilePath.BuildStorageFileName(file.Id, file.OriginalFileName)
                : file.StorageFileName;

            string nextStorageKey = ProjectFilePath.BuildStorageKey(projectId, file.Category, nextDirectory, storageFileName);
            await _fileStorage.MoveAsync(projectId, file.StorageKey, nextStorageKey, ct);

            file.DirectoryPath = nextDirectory;
            file.OriginalPath = ProjectFilePath.CombineDirectoryAndFileName(nextDirectory, file.OriginalFileName);
            file.StorageFileName = storageFileName;
            file.StorageKey = nextStorageKey;
            file.StoragePrefix = ProjectFilePath.BuildStoragePrefix(projectId, file.Category);
            file.UploadedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { sourcePath, targetPath, movedFiles = files.Count });
    }

    [HttpDelete("folders")]
    public async Task<IActionResult> DeleteFolder(Guid projectId, [FromBody] FolderDeleteRequest request, CancellationToken ct)
    {
        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound();
        if (project.OwnerId != _currentUser.UserId) return Forbid();

        string normalizedPath = ProjectFilePath.NormalizeRelativePath(request.Path);

        List<ProjectFile> files = await _db.ProjectFiles
            .Where(f => f.ProjectId == projectId &&
                        f.DirectoryPath != null &&
                        (f.DirectoryPath == normalizedPath || f.DirectoryPath.StartsWith(normalizedPath + "/")))
            .ToListAsync(ct);

        if (files.Count == 0)
            return NotFound(new { message = "No files found in folder." });

        if (!request.Recursive && files.Any(f => !string.Equals(f.DirectoryPath, normalizedPath, StringComparison.Ordinal)))
            return Conflict(new { message = "Folder is not empty. Retry with recursive=true." });

        List<Guid> deletedFileIds = files.Select(f => f.Id).ToList();

        foreach (ProjectFile file in files)
            await _fileStorage.DeleteAsync(projectId, file.StorageKey, ct);

        _db.ProjectFiles.RemoveRange(files);
        await _db.SaveChangesAsync(ct);

        foreach (Guid deletedFileId in deletedFileIds)
        {
            await _publishEndpoint.Publish(new ProjectFileIndexingRequested
            {
                ProjectId = projectId,
                FileId = deletedFileId,
                Operation = "Delete",
                RequestedAt = DateTime.UtcNow
            }, ct);
        }

        return Ok(new { deletedFiles = files.Count, path = normalizedPath });
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

        await _publishEndpoint.Publish(new ProjectFileIndexingRequested
        {
            ProjectId = projectId,
            FileId = fileId,
            Operation = "Delete",
            RequestedAt = DateTime.UtcNow
        }, ct);

        return NoContent();
    }

    [HttpPost("search")]
    public async Task<ActionResult<SearchProjectFilesResponse>> Search(
        Guid projectId,
        [FromBody] SearchProjectFilesRequest request,
        CancellationToken ct)
    {
        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound();
        if (project.OwnerId != _currentUser.UserId) return Forbid();

        int limit = request.Limit <= 0 ? 5 : request.Limit;
        if (string.IsNullOrWhiteSpace(request.Query))
            return Ok(new SearchProjectFilesResponse([], false));

        try
        {
            IReadOnlyList<VectorSearchChunkResult> results = await _vectorSearchQueryService
                .SearchProjectFilesAsync(projectId, request.Query, limit, ct);

            List<SearchProjectFileChunkResult> mapped = results
                .Select(r => new SearchProjectFileChunkResult(
                    r.FileId,
                    r.FilePath,
                    r.FileName,
                    r.ChunkIndex,
                    r.TotalChunks,
                    r.Language,
                    r.Content,
                    r.Score))
                .ToList();

            return Ok(new SearchProjectFilesResponse(mapped, false));
        }
        catch (IndexNotReadyException)
        {
            return Ok(new SearchProjectFilesResponse([], true));
        }
    }

    private static bool IsLikelyText(string mimeType, string fileName)
    {
        if (mimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            return true;

        if (mimeType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
            mimeType.Contains("xml", StringComparison.OrdinalIgnoreCase) ||
            mimeType.Contains("javascript", StringComparison.OrdinalIgnoreCase))
            return true;

        string extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension is ".ts" or ".tsx" or ".js" or ".jsx" or ".json" or ".md" or ".txt" or ".css" or ".html" or ".cs";
    }
}
