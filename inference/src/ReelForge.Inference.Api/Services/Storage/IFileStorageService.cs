namespace ReelForge.Inference.Api.Services.Storage;

/// <summary>
/// Abstraction for file storage operations (MinIO/S3).
/// </summary>
public interface IFileStorageService
{
    Task<string> UploadAsync(Stream content, string fileName, string contentType, CancellationToken ct);
    Task<Stream> DownloadAsync(string storageKey, CancellationToken ct);
    Task DeleteAsync(string storageKey, CancellationToken ct);
}
