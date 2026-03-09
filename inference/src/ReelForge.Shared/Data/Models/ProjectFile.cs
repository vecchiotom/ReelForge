namespace ReelForge.Shared.Data.Models;

/// <summary>
/// A file uploaded to a project, stored in MinIO/S3.
/// </summary>
public class ProjectFile
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }

    // Path-aware metadata for user/agent uploads. When users select a folder, the
    // browser will send the path in the form field and we save it here so the UI can
    // reconstruct the original directory structure. The simple file name is kept in
    // OriginalFileName (basename).
    public string? OriginalPath { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;

    // Category determines where in S3 the object lives (userFiles/outputFiles/agentFiles).
    // It defaults to "userFiles" for files uploaded via the API, and agents write to
    // "agentFiles" by default; render outputs go to "outputFiles".
    public string Category { get; set; } = "userFiles";

    public string StorageKey { get; set; } = string.Empty;
    public string StorageBucket { get; set; } = string.Empty;
    public string? StoragePrefix { get; set; }
    public string? StorageMetadataJson { get; set; }
    public string MimeType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? AgentSummary { get; set; }
    public SummaryStatus SummaryStatus { get; set; } = SummaryStatus.Pending;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public Project Project { get; set; } = null!;
}
