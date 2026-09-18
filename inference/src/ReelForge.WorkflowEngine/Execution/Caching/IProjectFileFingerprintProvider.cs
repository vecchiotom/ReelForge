namespace ReelForge.WorkflowEngine.Execution.Caching;

/// <summary>
/// Computes a content fingerprint of a project's file inventory, for inclusion in a cacheable
/// step's <see cref="StepCacheKeyInputs.ProjectFileFingerprint"/>. See
/// <see cref="ProjectFileFingerprintProvider"/> for the rationale and exact hashed fields.
/// </summary>
public interface IProjectFileFingerprintProvider
{
    /// <summary>A lowercase SHA-256 hex digest of <paramref name="projectId"/>'s current <c>ProjectFile</c> rows.</summary>
    Task<string> GetFingerprintAsync(Guid projectId, CancellationToken ct);
}
