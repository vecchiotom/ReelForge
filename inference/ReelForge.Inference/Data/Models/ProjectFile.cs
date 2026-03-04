namespace ReelForge.Inference.Data.Models;

/// <summary>
/// A file uploaded to a project, stored in MinIO/S3.
/// </summary>
public class ProjectFile
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? AgentSummary { get; set; }
    public SummaryStatus SummaryStatus { get; set; } = SummaryStatus.Pending;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public Project Project { get; set; } = null!;
}
