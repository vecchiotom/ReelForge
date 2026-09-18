using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ReelForge.Shared.Data.Models;
using ReelForge.WorkflowEngine.Data;

namespace ReelForge.WorkflowEngine.Execution.Caching;

/// <summary>
/// <inheritdoc cref="IProjectFileFingerprintProvider"/>
///
/// <para>
/// <b>Why this needs to exist at all.</b> A cacheable Agent step's prompt (<c>SystemPrompt</c> +
/// <c>ResolvedInput</c>) is already part of <see cref="StepCacheKeyInputs"/>, but any agent
/// granted <see cref="Shared.Agents.ToolGroup.ProjectRead"/> can additionally READ project file
/// content that never appears verbatim in its prompt — <c>ListProjectFiles</c>,
/// <c>ReadProjectFile</c>, <c>SearchProjectFiles</c> all resolve against whatever the project's
/// file inventory looks like AT CALL TIME. Without this fingerprint, a cache key built purely
/// from prompt/config text could not tell "the project's files are unchanged since the cached run"
/// from "the project's files were completely replaced" — a hit would then silently replay an
/// answer about files that no longer exist (or don't yet reflect newly added ones).
/// </para>
///
/// <para>
/// <b>Fields hashed per file, in <see cref="ProjectFile.Id"/> order (so the fingerprint is
/// insertion-order-independent):</b> <c>Id|StorageKey|SizeBytes|UploadedAt(ticks)|SummaryStatus|
/// IndexingStatus</c>. <c>SizeBytes</c>/<c>UploadedAt</c> catch a replaced-in-place upload under
/// the same row; <c>SummaryStatus</c>/<c>IndexingStatus</c> catch a file whose AI-generated
/// summary or vector index — both of which <c>ProjectRead</c> tools can surface — has since
/// changed even though the underlying bytes have not.
/// </para>
///
/// <para>
/// <b>Scoped, memoized per scope.</b> Registered Scoped so it shares its <see cref="_db"/>
/// instance (and therefore transaction/consistency semantics) with the rest of a single step's
/// execution; the in-memory cache means a workflow with many cacheable steps against the same
/// project pays for this query at most once per execution, not once per step.
/// </para>
/// </summary>
public sealed class ProjectFileFingerprintProvider : IProjectFileFingerprintProvider
{
    private readonly WorkflowEngineDbContext _db;
    private readonly Dictionary<Guid, string> _memoized = new();

    public ProjectFileFingerprintProvider(WorkflowEngineDbContext db)
    {
        _db = db;
    }

    public async Task<string> GetFingerprintAsync(Guid projectId, CancellationToken ct)
    {
        if (_memoized.TryGetValue(projectId, out string? cached))
            return cached;

        List<ProjectFile> files = await _db.ProjectFiles
            .AsNoTracking()
            .Where(f => f.ProjectId == projectId)
            .OrderBy(f => f.Id)
            .ToListAsync(ct);

        StringBuilder sb = new();
        foreach (ProjectFile file in files)
        {
            sb.Append(file.Id).Append('|')
              .Append(file.StorageKey).Append('|')
              .Append(file.SizeBytes).Append('|')
              .Append(file.UploadedAt.Ticks).Append('|')
              .Append(file.SummaryStatus).Append('|')
              .Append(file.IndexingStatus)
              .Append('\n');
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        string fingerprint = Convert.ToHexString(hash).ToLowerInvariant();

        _memoized[projectId] = fingerprint;
        return fingerprint;
    }
}
