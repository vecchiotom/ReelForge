namespace ReelForge.Inference.Services.Storage;

/// <summary>
/// Abstraction for file storage operations (MinIO/S3).
/// </summary>
public interface IFileStorageService
{
    /// <summary>Uploads a file and returns the storage key.</summary>
    Task<string> UploadAsync(Stream content, string fileName, string contentType, CancellationToken ct);

    /// <summary>Downloads a file by its storage key.</summary>
    Task<Stream> DownloadAsync(string storageKey, CancellationToken ct);

    /// <summary>Deletes a file by its storage key.</summary>
    Task DeleteAsync(string storageKey, CancellationToken ct);
}
