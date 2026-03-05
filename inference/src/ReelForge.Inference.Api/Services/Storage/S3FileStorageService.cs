using Amazon.S3;
using Amazon.S3.Model;

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

    public async Task<string> UploadAsync(Stream content, string fileName, string contentType, CancellationToken ct)
    {
        string storageKey = $"{Guid.NewGuid()}/{fileName}";
        PutObjectRequest request = new()
        {
            BucketName = _bucketName,
            Key = storageKey,
            InputStream = content,
            ContentType = contentType
        };
        await _s3Client.PutObjectAsync(request, ct);
        _logger.LogInformation("Uploaded file {FileName} as {StorageKey}", fileName, storageKey);
        return storageKey;
    }

    public async Task<Stream> DownloadAsync(string storageKey, CancellationToken ct)
    {
        GetObjectRequest request = new() { BucketName = _bucketName, Key = storageKey };
        GetObjectResponse response = await _s3Client.GetObjectAsync(request, ct);
        return response.ResponseStream;
    }

    public async Task DeleteAsync(string storageKey, CancellationToken ct)
    {
        DeleteObjectRequest request = new() { BucketName = _bucketName, Key = storageKey };
        await _s3Client.DeleteObjectAsync(request, ct);
        _logger.LogInformation("Deleted file {StorageKey}", storageKey);
    }
}
