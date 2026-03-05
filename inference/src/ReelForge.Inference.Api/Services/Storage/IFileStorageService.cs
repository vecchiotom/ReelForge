namespace ReelForge.Inference.Api.Services.Storage;

/// <summary>
/// Abstraction for file storage operations (MinIO/S3).
/// </summary>
public interface IFileStorageService
{
    Task<StoredFileObject> UploadAsync(
        Guid projectId,
        Stream content,
        string fileName,
        string contentType,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken ct);
    Task<Stream> DownloadAsync(Guid projectId, string storageKey, CancellationToken ct);
    Task DeleteAsync(Guid projectId, string storageKey, CancellationToken ct);
}

public sealed record StoredFileObject(
    string StorageKey,
    string BucketName,
    string StoragePrefix,
    string? StorageMetadataJson);
