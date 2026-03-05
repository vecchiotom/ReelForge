namespace ReelForge.WorkflowEngine.Services.Storage;

public sealed record ProjectWorkspaceFile(
    Guid Id,
    Guid ProjectId,
    string OriginalFileName,
    string StorageKey,
    string MimeType,
    long SizeBytes,
    DateTime UploadedAt,
    string? AgentSummary);

public interface IProjectFileWorkspace
{
    Task<IReadOnlyList<ProjectWorkspaceFile>> ListFilesAsync(Guid projectId, CancellationToken ct);
    Task<string> ReadFileAsync(Guid projectId, string fileReference, CancellationToken ct);
    Task<ProjectWorkspaceFile> WriteTextFileAsync(Guid projectId, string fileName, string content, string contentType, CancellationToken ct);
}
