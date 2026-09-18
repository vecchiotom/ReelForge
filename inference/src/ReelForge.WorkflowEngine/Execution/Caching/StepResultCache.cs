using System.Net;
using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ReelForge.Shared.Data.Models;
using ReelForge.WorkflowEngine.Data;

namespace ReelForge.WorkflowEngine.Execution.Caching;

/// <summary>
/// <inheritdoc cref="IStepResultCache"/>
///
/// <para>
/// <b>The one invariant every method here upholds:</b> a caching failure — a bad DB connection,
/// an S3 hiccup, a serialization edge case — must never fail the workflow step it is trying to
/// speed up. Every public method wraps its entire body in a try/catch that logs a warning and
/// degrades to "no cache" (a miss, or simply not storing). Callers in
/// <see cref="WorkflowExecutorService"/> never need their own defensive try/catch around a call
/// into this class for that reason.
/// </para>
/// </summary>
public sealed class StepResultCache : IStepResultCache
{
    private readonly WorkflowEngineDbContext _db;
    private readonly IStepCacheKeyBuilder _keyBuilder;
    private readonly IStepCachePolicy _policy;
    private readonly StepCacheOptions _options;
    private readonly ILogger<StepResultCache> _logger;
    private readonly IAmazonS3? _s3Client;
    private readonly string _bucketName;

    /// <summary>
    /// <paramref name="policy"/> is accepted for API symmetry with the rest of the caching
    /// collaborators (and so a future caller with access to the originating <see cref="WorkflowStep"/>
    /// can defensively re-check it here) but is NOT invoked by <see cref="TryGetAsync"/>/
    /// <see cref="StoreAsync"/> themselves: <see cref="StepCacheKeyInputs"/> deliberately carries
    /// no <see cref="WorkflowStep"/> reference (it must remain a flat, hashable value), so the
    /// policy question "is this STEP cacheable" is answered once, by
    /// <see cref="WorkflowExecutorService"/>, before it ever calls into this class — this class
    /// only ever answers "does a matching ENTRY exist" / "persist this entry".
    /// <paramref name="s3Client"/> is optional: when null (e.g. a unit test harness with no S3
    /// wired), <see cref="StepCacheOptions.ValidateStorageArtifacts"/> is treated as a no-op
    /// rather than throwing — degrading to "trust the row" rather than failing the lookup.
    /// </summary>
    public StepResultCache(
        WorkflowEngineDbContext db,
        IStepCacheKeyBuilder keyBuilder,
        IStepCachePolicy policy,
        IOptions<StepCacheOptions> options,
        ILogger<StepResultCache> logger,
        IAmazonS3? s3Client = null,
        IConfiguration? configuration = null)
    {
        _db = db;
        _keyBuilder = keyBuilder;
        _policy = policy;
        _options = options.Value;
        _logger = logger;
        _s3Client = s3Client;
        _bucketName = configuration?["MinIO:BucketName"] ?? "reelforge";
    }

    public async Task<StepCacheLookupResult> TryGetAsync(StepCacheKeyInputs inputs, CancellationToken ct)
    {
        if (!_options.Enabled)
            return StepCacheLookupResult.Miss;

        try
        {
            string cacheKey = _keyBuilder.Build(inputs);

            WorkflowStepCacheEntry? entry = await _db.WorkflowStepCacheEntries
                .FirstOrDefaultAsync(e => e.ProjectId == inputs.ProjectId && e.CacheKey == cacheKey, ct);

            if (entry is null)
                return StepCacheLookupResult.Miss;

            if (entry.ExpiresAt.HasValue && entry.ExpiresAt.Value <= DateTime.UtcNow)
            {
                _logger.LogInformation(
                    "Step cache entry {CacheKeyPrefix}... for project {ProjectId} expired at {ExpiresAt}; evicting",
                    cacheKey[..Math.Min(12, cacheKey.Length)], inputs.ProjectId, entry.ExpiresAt);
                _db.WorkflowStepCacheEntries.Remove(entry);
                await _db.SaveChangesAsync(ct);
                return StepCacheLookupResult.Miss;
            }

            if (_options.ValidateStorageArtifacts && !await StorageArtifactsStillExistAsync(entry, ct))
            {
                _logger.LogInformation(
                    "Step cache entry {CacheKeyPrefix}... for project {ProjectId} references a storage object that no longer exists; evicting",
                    cacheKey[..Math.Min(12, cacheKey.Length)], inputs.ProjectId);
                _db.WorkflowStepCacheEntries.Remove(entry);
                await _db.SaveChangesAsync(ct);
                return StepCacheLookupResult.Miss;
            }

            entry.HitCount++;
            entry.LastHitAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Step cache HIT {CacheKeyPrefix}... for project {ProjectId} (hit #{HitCount}, originally spent {TokensUsed} tokens)",
                cacheKey[..Math.Min(12, cacheKey.Length)], inputs.ProjectId, entry.HitCount, entry.TokensUsed);

            return new StepCacheLookupResult(true, entry);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // See the class doc comment: a cache failure must never fail the step it is trying to
            // speed up, so any unexpected exception here degrades to an ordinary miss.
            _logger.LogWarning(ex, "Step cache lookup failed for project {ProjectId}; treating as a miss", inputs.ProjectId);
            return StepCacheLookupResult.Miss;
        }
    }

    public async Task StoreAsync(StepCacheKeyInputs inputs, StepExecutionResult result, CancellationToken ct)
    {
        if (!_options.Enabled)
            return;
        if (result.Status != StepStatus.Completed)
            return;
        if (string.IsNullOrWhiteSpace(result.Output))
            return;
        if (result.Output.Length > _options.MaxEntryChars)
            return;

        try
        {
            string cacheKey = _keyBuilder.Build(inputs);
            DateTime now = DateTime.UtcNow;
            DateTime? expiresAt = _options.TtlHours > 0 ? now.AddHours(_options.TtlHours) : null;

            WorkflowStepCacheEntry? existing = await _db.WorkflowStepCacheEntries
                .FirstOrDefaultAsync(e => e.ProjectId == inputs.ProjectId && e.CacheKey == cacheKey, ct);

            if (existing is null)
            {
                _db.WorkflowStepCacheEntries.Add(new WorkflowStepCacheEntry
                {
                    Id = Guid.NewGuid(),
                    ProjectId = inputs.ProjectId,
                    CacheKey = cacheKey,
                    WorkflowDefinitionId = inputs.WorkflowDefinitionId,
                    StepType = inputs.StepType,
                    AgentType = inputs.AgentType,
                    Output = result.Output,
                    OutputStorageKey = result.OutputStorageKey,
                    ArtifactStorageKey = result.ArtifactStorageKey,
                    ChatTranscriptJson = result.ChatTranscriptJson,
                    TokensUsed = result.TokensUsed,
                    InputTokens = result.InputTokens,
                    OutputTokens = result.OutputTokens,
                    DurationMs = result.DurationMs,
                    CreatedAt = now,
                    LastHitAt = null,
                    HitCount = 0,
                    ExpiresAt = expiresAt
                });
            }
            else
            {
                // Upsert: a fresh execution's result replaces the stored one wholesale — including
                // its TTL (refreshed from `now`, so a step that keeps naturally re-running never
                // actually expires) — but HitCount/LastHitAt are left untouched, since writing a
                // fresh result is not itself a cache HIT.
                existing.WorkflowDefinitionId = inputs.WorkflowDefinitionId;
                existing.StepType = inputs.StepType;
                existing.AgentType = inputs.AgentType;
                existing.Output = result.Output;
                existing.OutputStorageKey = result.OutputStorageKey;
                existing.ArtifactStorageKey = result.ArtifactStorageKey;
                existing.ChatTranscriptJson = result.ChatTranscriptJson;
                existing.TokensUsed = result.TokensUsed;
                existing.InputTokens = result.InputTokens;
                existing.OutputTokens = result.OutputTokens;
                existing.DurationMs = result.DurationMs;
                existing.CreatedAt = now;
                existing.ExpiresAt = expiresAt;
            }

            // Opportunistic pruning: every store is a natural, cheap moment to sweep this
            // project's own expired rows, rather than running a separate background job just for
            // TTL eviction.
            List<WorkflowStepCacheEntry> stale = await _db.WorkflowStepCacheEntries
                .Where(e => e.ProjectId == inputs.ProjectId && e.ExpiresAt != null && e.ExpiresAt < now)
                .ToListAsync(ct);
            if (stale.Count > 0)
                _db.WorkflowStepCacheEntries.RemoveRange(stale);

            await _db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // See the class doc comment: never let a cache-write failure fail the step whose
            // (already-successful) result it was trying to persist for next time.
            _logger.LogWarning(ex, "Step cache store failed for project {ProjectId}; the step's own result is unaffected", inputs.ProjectId);
        }
    }

    /// <summary>
    /// HEADs <paramref name="entry"/>'s <see cref="WorkflowStepCacheEntry.OutputStorageKey"/> and
    /// <see cref="WorkflowStepCacheEntry.ArtifactStorageKey"/> (whichever are set) against S3, so
    /// a hit is never served for an object a bucket reset or manual cleanup already deleted. When
    /// no S3 client is wired (see the constructor's doc comment) this degrades to "trust the row"
    /// rather than failing the lookup.
    /// </summary>
    private async Task<bool> StorageArtifactsStillExistAsync(WorkflowStepCacheEntry entry, CancellationToken ct)
    {
        if (_s3Client is null)
            return true;

        foreach (string? key in new[] { entry.OutputStorageKey, entry.ArtifactStorageKey })
        {
            if (string.IsNullOrEmpty(key))
                continue;

            if (!await ObjectExistsAsync(key, ct))
                return false;
        }

        return true;
    }

    private async Task<bool> ObjectExistsAsync(string key, CancellationToken ct)
    {
        try
        {
            await _s3Client!.GetObjectMetadataAsync(_bucketName, key, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
