namespace ReelForge.Inference.Api.Services.Storage;

/// <summary>
/// Abstraction for file storage operations (MinIO/S3).
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Upload a file into project storage.
    /// </summary>
    /// <param name="projectId">Target project.</param>
    /// <param name="content">File stream.</param>
    /// <param name="fileName">Original file name (basename).</param>
    /// <param name="contentType">MIME type.</param>
    /// <param name="metadata">Additional metadata to attach.</param>
    /// <param name="category">
    /// Which subfolder under projects/{id} to place the object. Supported values:
    /// "userFiles" (default), "agentFiles", "outputFiles".
    /// </param>
    /// <param name="originalPath">
    /// Optional original relative path (including any folders) supplied by the client.
    /// Stored in metadata for UI rendering.
    /// </param>
    Task<StoredFileObject> UploadAsync(
        Guid projectId,
        Stream content,
        string fileName,
        string contentType,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken ct,
        string category = "userFiles",
        string? originalPath = null);
    Task<Stream> DownloadAsync(Guid projectId, string storageKey, CancellationToken ct);
    Task DeleteAsync(Guid projectId, string storageKey, CancellationToken ct);
}

public sealed record StoredFileObject(
    string StorageKey,
    string BucketName,
    string StoragePrefix,
    string? StorageMetadataJson,
    string Category,
    string? OriginalPath);
