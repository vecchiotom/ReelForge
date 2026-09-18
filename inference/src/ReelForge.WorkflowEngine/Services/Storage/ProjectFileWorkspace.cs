using System.Text;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
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

        // An agent tool can be pointed at any project file by name, including audio/video/image
        // uploads (e.g. a candidate background-music track offered to MusicSupervisor). Without
        // this check, a binary file was decoded as UTF-8 text and returned whole — observed in
        // production as a single MP3 producing ~2.9M "chars" of mojibake that blew the model's
        // context and 400'd the whole request. Mirrors ProjectFilesController's IsLikelyText/
        // ResolveUploadMimeType (R22) allowlist-not-blocklist approach, so an unrecognized binary
        // format is refused by default rather than decoded.
        if (!IsLikelyTextFile(file.MimeType, file.OriginalFileName))
        {
            throw new InvalidOperationException(
                $"File '{file.OriginalFileName}' ({file.MimeType ?? "unknown type"}, {file.SizeBytes:N0} bytes) " +
                "is a binary file and cannot be read as text. Use its file name and any metadata you were " +
                "already given instead of reading its content.");
        }

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

    public async Task DownloadStorageKeyToFileAsync(
        Guid projectId, string storageKey, string destinationPath, CancellationToken ct)
    {
        ValidateProjectScope(projectId, storageKey);

        _logger.LogInformation(
            "Downloading storage key {StorageKey} to {DestinationPath} for project {ProjectId}",
            storageKey, destinationPath, projectId);

        string? destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        using GetObjectResponse response = await _s3Client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = _bucketName,
            Key = storageKey
        }, ct);

        // Stream straight to disk — never buffer the object in a MemoryStream/byte[] (R21), which
        // matters here because this method exists specifically to bring large source video files
        // from S3 into local scratch space for ffmpeg to operate on.
        await using Stream responseStream = response.ResponseStream;
        await using FileStream fileStream = new(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await responseStream.CopyToAsync(fileStream, ct);

        _logger.LogInformation(
            "Downloaded storage key {StorageKey} to {DestinationPath} for project {ProjectId}",
            storageKey, destinationPath, projectId);
    }

    public async Task<ProjectWorkspaceFile> UploadBinaryFileAsync(
        Guid projectId,
        string localFilePath,
        string fileName,
        string contentType,
        SummaryStatus summaryStatus,
        FileIndexingStatus indexingStatus,
        CancellationToken ct,
        string category = "outputFiles",
        string? originalPath = null)
    {
        _logger.LogInformation(
            "Uploading binary project file for project {ProjectId}: fileName={FileName}, category={Category}",
            projectId, fileName, category);

        if (category != "userFiles" && category != "agentFiles" && category != "outputFiles")
            category = "outputFiles";

        if (!File.Exists(localFilePath))
        {
            throw new FileNotFoundException($"Local file '{localFilePath}' does not exist.", localFilePath);
        }

        Guid fileId = Guid.NewGuid();
        string normalizedOriginalPath = ProjectFilePath.NormalizeRelativePath(
            string.IsNullOrWhiteSpace(originalPath) ? fileName : originalPath);
        string normalizedFileName = ProjectFilePath.GetFileName(normalizedOriginalPath);
        string? directoryPath = ProjectFilePath.GetDirectoryPath(normalizedOriginalPath);
        string storageFileName = ProjectFilePath.BuildStorageFileName(fileId, normalizedFileName);
        string storageKey = ProjectFilePath.BuildStorageKey(projectId, category, directoryPath, storageFileName);
        string storagePrefix = ProjectFilePath.BuildStoragePrefix(projectId, category);

        ValidateProjectScope(projectId, storageKey);

        Dictionary<string, string> metadata = new()
        {
            ["project-id"] = projectId.ToString(),
            ["project-file-id"] = fileId.ToString(),
            ["generated-by"] = "workflow-engine",
            ["category"] = category,
            ["original-path"] = normalizedOriginalPath,
            ["original-file-name"] = normalizedFileName,
            ["storage-file-name"] = storageFileName
        };

        long sizeBytes = new FileInfo(localFilePath).Length;

        // TransferUtility streams from the FileStream in multipart chunks — the whole file is
        // never materialized as a single in-memory buffer (R21), which matters here because this
        // path is exactly how a compiled edited video (potentially hundreds of MB) reaches S3.
        TransferUtility transferUtility = new(_s3Client);
        await using (FileStream fileStream = new(localFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            TransferUtilityUploadRequest uploadRequest = new()
            {
                BucketName = _bucketName,
                Key = storageKey,
                InputStream = fileStream,
                ContentType = contentType,
                AutoCloseStream = false
            };
            foreach (KeyValuePair<string, string> kv in metadata)
                uploadRequest.Metadata[kv.Key] = kv.Value;

            await transferUtility.UploadAsync(uploadRequest, ct);
        }

        string metadataJson = JsonSerializer.Serialize(metadata);

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
            SizeBytes = sizeBytes,
            // Caller-supplied, never the Pending/NotIndexed defaults: a binary media file uploaded
            // through this path must never be picked up by the text summarizer or vector chunker
            // (plan §1.5 — same trap R22 fixes on the general upload controller, independently
            // avoided here since this path never goes through that controller).
            SummaryStatus = summaryStatus,
            IndexingStatus = indexingStatus,
            UploadedAt = DateTime.UtcNow
        };

        using IServiceScope scope = _scopeFactory.CreateScope();
        WorkflowEngineDbContext db = scope.ServiceProvider.GetRequiredService<WorkflowEngineDbContext>();
        db.ProjectFiles.Add(projectFile);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Saved binary project file metadata and object for project {ProjectId}: fileId={FileId}, storageKey={StorageKey}",
            projectId, projectFile.Id, projectFile.StorageKey);

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

    public async Task<string> UploadArtifactAsync(
        Guid projectId,
        string localFilePath,
        string fileName,
        string contentType,
        CancellationToken ct,
        string category = "agentFiles")
    {
        _logger.LogInformation(
            "Uploading bare artifact for project {ProjectId}: fileName={FileName}, category={Category}",
            projectId, fileName, category);

        if (!File.Exists(localFilePath))
        {
            throw new FileNotFoundException($"Local file '{localFilePath}' does not exist.", localFilePath);
        }

        // Unlike WriteTextFileAsync/UploadBinaryFileAsync, the storage file name is NOT
        // guid-prefixed: this is a bare artifact with no ProjectFile row to dedupe against, so the
        // caller's own key layout (e.g. "video-analysis/{executionId}/step-1-analysis.json", per
        // plan §1.5) is preserved verbatim under the category prefix. NormalizeRelativePath still
        // rejects ".." traversal in a caller-supplied name.
        string normalizedFileName = ProjectFilePath.NormalizeRelativePath(fileName);
        string storageKey = ProjectFilePath.BuildStorageKey(
            projectId, category, directoryPath: null, storageFileName: normalizedFileName);

        ValidateProjectScope(projectId, storageKey);

        TransferUtility transferUtility = new(_s3Client);
        await using (FileStream fileStream = new(localFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            TransferUtilityUploadRequest uploadRequest = new()
            {
                BucketName = _bucketName,
                Key = storageKey,
                InputStream = fileStream,
                ContentType = contentType,
                AutoCloseStream = false
            };
            uploadRequest.Metadata["project-id"] = projectId.ToString();
            uploadRequest.Metadata["generated-by"] = "workflow-engine";
            uploadRequest.Metadata["category"] = category;

            await transferUtility.UploadAsync(uploadRequest, ct);
        }

        _logger.LogInformation(
            "Uploaded bare artifact for project {ProjectId}: storageKey={StorageKey}", projectId, storageKey);

        return storageKey;
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

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ts", ".tsx", ".js", ".jsx", ".json", ".md", ".txt", ".css", ".html", ".cs",
        ".yml", ".yaml", ".csv", ".xml"
    };

    internal static bool IsLikelyTextFile(string? mimeType, string fileName)
    {
        if (!string.IsNullOrEmpty(mimeType))
        {
            if (mimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
                return true;
            if (mimeType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
                mimeType.Contains("xml", StringComparison.OrdinalIgnoreCase) ||
                mimeType.Contains("javascript", StringComparison.OrdinalIgnoreCase))
                return true;
            if (mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) ||
                mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ||
                mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return TextExtensions.Contains(Path.GetExtension(fileName));
    }
}
