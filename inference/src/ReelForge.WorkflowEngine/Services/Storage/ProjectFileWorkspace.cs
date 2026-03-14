using System.Text;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.EntityFrameworkCore;
using ReelForge.Shared;
using ReelForge.Shared.Data.Models;
using ReelForge.WorkflowEngine.Data;

namespace ReelForge.WorkflowEngine.Services.Storage;

public class ProjectFileWorkspace : IProjectFileWorkspace
{
    private readonly IAmazonS3 _s3Client;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _bucketName;
    private readonly ILogger<ProjectFileWorkspace> _logger;

    public ProjectFileWorkspace(
        IAmazonS3 s3Client,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<ProjectFileWorkspace> logger)
    {
        _s3Client = s3Client;
        _scopeFactory = scopeFactory;
        _bucketName = configuration["MinIO:BucketName"] ?? "reelforge";
        _logger = logger;
    }

    public async Task<IReadOnlyList<ProjectWorkspaceFile>> ListFilesAsync(Guid projectId, CancellationToken ct)
    {
        _logger.LogInformation("Listing project files for project {ProjectId}", projectId);

        using IServiceScope scope = _scopeFactory.CreateScope();
        WorkflowEngineDbContext db = scope.ServiceProvider.GetRequiredService<WorkflowEngineDbContext>();
        List<ProjectWorkspaceFile> files = await db.ProjectFiles
            .Where(f => f.ProjectId == projectId)
            .OrderByDescending(f => f.UploadedAt)
            .Select(f => new ProjectWorkspaceFile(
                f.Id,
                f.ProjectId,
                f.OriginalFileName,
                f.OriginalPath,
                f.Category,
                f.StorageKey,
                f.MimeType,
                f.SizeBytes,
                f.UploadedAt,
                f.AgentSummary))
            .ToListAsync(ct);

        _logger.LogInformation("Found {FileCount} files for project {ProjectId}", files.Count, projectId);
        return files;
    }

    public async Task<string> ReadFileAsync(Guid projectId, string fileReference, CancellationToken ct)
    {
        _logger.LogInformation(
            "Reading project file for project {ProjectId} using reference {FileReference}",
            projectId,
            fileReference);

        ProjectFile file = await ResolveFileAsync(projectId, fileReference, ct);
        ValidateProjectScope(projectId, file.StorageKey);

        GetObjectResponse response = await _s3Client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = _bucketName,
            Key = file.StorageKey
        }, ct);

        await using Stream stream = response.ResponseStream;
        using StreamReader reader = new(stream, Encoding.UTF8, leaveOpen: false);
        string content = await reader.ReadToEndAsync(ct);

        _logger.LogInformation(
            "Read project file {FileId} ({StorageKey}) for project {ProjectId}",
            file.Id,
            file.StorageKey,
            projectId);

        return content;
    }

    public async Task<ProjectWorkspaceFile> WriteTextFileAsync(
        Guid projectId,
        string fileName,
        string content,
        string contentType,
        CancellationToken ct,
        string category = "agentFiles",
        string? originalPath = null)
    {
        _logger.LogInformation(
            "Writing project text file for project {ProjectId}: fileName={FileName}, category={Category}",
            projectId,
            fileName,
            category);

        Guid fileId = Guid.NewGuid();

        if (category != "userFiles" && category != "agentFiles" && category != "outputFiles")
            category = "agentFiles";

        string normalizedOriginalPath = ProjectFilePath.NormalizeRelativePath(
            string.IsNullOrWhiteSpace(originalPath) ? fileName : originalPath);
        string normalizedFileName = ProjectFilePath.GetFileName(normalizedOriginalPath);
        string? directoryPath = ProjectFilePath.GetDirectoryPath(normalizedOriginalPath);
        string storageFileName = ProjectFilePath.BuildStorageFileName(fileId, normalizedFileName);

        string storagePrefix = $"projects/{projectId}/{category}/";
        string storageKey = string.IsNullOrWhiteSpace(directoryPath)
            ? $"{storagePrefix}{storageFileName}"
            : $"{storagePrefix}{directoryPath}/{storageFileName}";

        byte[] bytes = Encoding.UTF8.GetBytes(content);
        await using MemoryStream stream = new(bytes);
        PutObjectRequest request = new()
        {
            BucketName = _bucketName,
            Key = storageKey,
            InputStream = stream,
            ContentType = contentType
        };
        request.Metadata["project-id"] = projectId.ToString();
        request.Metadata["project-file-id"] = fileId.ToString();
        request.Metadata["generated-by"] = "workflow-engine";
        request.Metadata["category"] = category;
        request.Metadata["original-path"] = normalizedOriginalPath;
        request.Metadata["original-file-name"] = normalizedFileName;
        request.Metadata["storage-file-name"] = storageFileName;

        await _s3Client.PutObjectAsync(request, ct);

        string metadataJson = JsonSerializer.Serialize(request.Metadata.Keys
            .ToDictionary(k => k, k => request.Metadata[k]));

        ProjectFile projectFile = new()
        {
            Id = fileId,
            ProjectId = projectId,
            OriginalFileName = normalizedFileName,
            OriginalPath = normalizedOriginalPath,
            DirectoryPath = directoryPath,
            Category = category,
            StorageKey = storageKey,
            StorageFileName = storageFileName,
            StorageBucket = _bucketName,
            StoragePrefix = storagePrefix,
            StorageMetadataJson = metadataJson,
            MimeType = contentType,
            SizeBytes = bytes.LongLength,
            SummaryStatus = SummaryStatus.Pending,
            UploadedAt = DateTime.UtcNow
        };

        using IServiceScope scope = _scopeFactory.CreateScope();
        WorkflowEngineDbContext db = scope.ServiceProvider.GetRequiredService<WorkflowEngineDbContext>();
        db.ProjectFiles.Add(projectFile);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Saved project file metadata and object for project {ProjectId}: fileId={FileId}, storageKey={StorageKey}",
            projectId,
            projectFile.Id,
            projectFile.StorageKey);

        return new ProjectWorkspaceFile(
            projectFile.Id,
            projectFile.ProjectId,
            projectFile.OriginalFileName,
            projectFile.OriginalPath,
            projectFile.Category,
            projectFile.StorageKey,
            projectFile.MimeType,
            projectFile.SizeBytes,
            projectFile.UploadedAt,
            projectFile.AgentSummary);
    }

    private async Task<ProjectFile> ResolveFileAsync(Guid projectId, string fileReference, CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        WorkflowEngineDbContext db = scope.ServiceProvider.GetRequiredService<WorkflowEngineDbContext>();

        IQueryable<ProjectFile> query = db.ProjectFiles.Where(f => f.ProjectId == projectId);

        if (Guid.TryParse(fileReference, out Guid fileId))
        {
            ProjectFile? byId = await query.FirstOrDefaultAsync(f => f.Id == fileId, ct);
            if (byId != null) return byId;
        }

        ProjectFile? byKey = await query.FirstOrDefaultAsync(f => f.StorageKey == fileReference, ct);
        if (byKey != null) return byKey;

        ProjectFile? byName = await query
            .Where(f => f.OriginalFileName == fileReference || f.OriginalPath == fileReference)
            .OrderByDescending(f => f.UploadedAt)
            .FirstOrDefaultAsync(ct);

        return byName ?? throw new KeyNotFoundException($"Project file '{fileReference}' not found in project scope.");
    }

    private static void ValidateProjectScope(Guid projectId, string storageKey)
    {
        if (!storageKey.StartsWith($"projects/{projectId}/", StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Requested file is not in the project's storage scope.");
    }
}
