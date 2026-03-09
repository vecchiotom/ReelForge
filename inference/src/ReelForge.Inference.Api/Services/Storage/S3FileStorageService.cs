using Amazon.S3;
using Amazon.S3.Model;
using System.Text.Json;

namespace ReelForge.Inference.Api.Services.Storage;

/// <summary>
/// S3-compatible file storage service targeting MinIO.
/// </summary>
public class S3FileStorageService : IFileStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly ILogger<S3FileStorageService> _logger;

    public S3FileStorageService(IAmazonS3 s3Client, IConfiguration configuration, ILogger<S3FileStorageService> logger)
    {
        _s3Client = s3Client;
        _bucketName = configuration["MinIO:BucketName"] ?? "reelforge";
        _logger = logger;
    }

    public async Task<StoredFileObject> UploadAsync(
        Guid projectId,
        Stream content,
        string fileName,
        string contentType,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken ct,
        string category = "userFiles",
        string? originalPath = null)
    {
        // Ensure category is one of the known values to prevent escaping the prefix
        if (category != "userFiles" && category != "agentFiles" && category != "outputFiles")
            category = "userFiles";

        string storagePrefix = $"projects/{projectId}/{category}/";
        // use generated GUID directory to avoid collisions, then append either
        // the provided relative path or the base file name.
        string nameSegment = string.IsNullOrEmpty(originalPath)
            ? fileName
            : originalPath.Replace("\\", "/"); // normalize any backslashes

        string storageKey = $"{storagePrefix}{Guid.NewGuid()}/{nameSegment}";
        PutObjectRequest request = new()
        {
            BucketName = _bucketName,
            Key = storageKey,
            InputStream = content,
            ContentType = contentType
        };
        // standard metadata
        request.Metadata["project-id"] = projectId.ToString();
        request.Metadata["category"] = category;
        if (!string.IsNullOrEmpty(originalPath))
            request.Metadata["original-path"] = originalPath;

        if (metadata != null)
        {
            foreach (var (key, value) in metadata)
            {
                request.Metadata[key] = value;
            }
        }

        await _s3Client.PutObjectAsync(request, ct);
        _logger.LogInformation("Uploaded file {FileName} as {StorageKey}", fileName, storageKey);
        string metadataJson = JsonSerializer.Serialize(request.Metadata.Keys
            .ToDictionary(k => k, k => request.Metadata[k]));
        return new StoredFileObject(storageKey, _bucketName, storagePrefix, metadataJson, category, originalPath);
    }

    public async Task<Stream> DownloadAsync(Guid projectId, string storageKey, CancellationToken ct)
    {
        if (!storageKey.StartsWith($"projects/{projectId}/", StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Requested file is not in the project's storage scope.");

        GetObjectRequest request = new() { BucketName = _bucketName, Key = storageKey };
        GetObjectResponse response = await _s3Client.GetObjectAsync(request, ct);
        return response.ResponseStream;
    }

    public async Task DeleteAsync(Guid projectId, string storageKey, CancellationToken ct)
    {
        if (!storageKey.StartsWith($"projects/{projectId}/", StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Requested file is not in the project's storage scope.");

        DeleteObjectRequest request = new() { BucketName = _bucketName, Key = storageKey };
        await _s3Client.DeleteObjectAsync(request, ct);
        _logger.LogInformation("Deleted file {StorageKey}", storageKey);
    }
}
