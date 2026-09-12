using ReelForge.Shared.Data.Models;

namespace ReelForge.WorkflowEngine.Services.Storage;

public sealed record ProjectWorkspaceFile(
    Guid Id,
    Guid ProjectId,
    string OriginalFileName,
    string? OriginalPath,
    string Category,
    string StorageKey,
    string MimeType,
    long SizeBytes,
    DateTime UploadedAt,
    string? AgentSummary);

public interface IProjectFileWorkspace
{
    Task<IReadOnlyList<ProjectWorkspaceFile>> ListFilesAsync(Guid projectId, CancellationToken ct);
    Task<string> ReadFileAsync(Guid projectId, string fileReference, CancellationToken ct);
    Task<ProjectWorkspaceFile> WriteTextFileAsync(
        Guid projectId,
        string fileName,
        string content,
        string contentType,
        CancellationToken ct,
        string category = "agentFiles",
        string? originalPath = null);

    /// <summary>
    /// Streams the S3 object at <paramref name="storageKey"/> straight to a local file at
    /// <paramref name="destinationPath"/> — never buffered through a <c>MemoryStream</c>/
    /// <c>byte[]</c> (R21), so this is safe for large video/audio sources. <paramref name="storageKey"/>
    /// must already lie under <c>projects/{projectId}/</c> (see <c>ValidateProjectScope</c>).
    /// </summary>
    Task DownloadStorageKeyToFileAsync(
        Guid projectId, string storageKey, string destinationPath, CancellationToken ct);

    /// <summary>
    /// Uploads a local binary file to S3 via a multipart <c>TransferUtility</c> (streamed, never a
    /// whole-file <c>byte[]</c> — R21) <b>and</b> creates a corresponding <see cref="ProjectWorkspaceFile"/>
    /// row, for artifacts meant to be reachable/re-editable as ordinary project files (e.g. the
    /// final edited video). The caller must set <paramref name="summaryStatus"/>/
    /// <paramref name="indexingStatus"/> so binary media is never queued for text summarization or
    /// vector indexing (plan §1.5 — mirrors the R22 fix, independently, for this upload path).
    /// </summary>
    Task<ProjectWorkspaceFile> UploadBinaryFileAsync(
        Guid projectId,
        string localFilePath,
        string fileName,
        string contentType,
        SummaryStatus summaryStatus,
        FileIndexingStatus indexingStatus,
        CancellationToken ct,
        string category = "outputFiles",
        string? originalPath = null);

    /// <summary>
    /// Uploads a local file to S3 via a multipart <c>TransferUtility</c> (streamed) as a bare
    /// artifact — returns only the storage key, creates <b>no</b> <see cref="ProjectWorkspaceFile"/>
    /// row. For the large analysis/EDL JSON artifacts (plan §1.5) that are referenced only via
    /// <c>WorkflowStepResult.ArtifactStorageKey</c>, never surfaced in the project files UI.
    /// </summary>
    Task<string> UploadArtifactAsync(
        Guid projectId,
        string localFilePath,
        string fileName,
        string contentType,
        CancellationToken ct,
        string category = "agentFiles");
}
