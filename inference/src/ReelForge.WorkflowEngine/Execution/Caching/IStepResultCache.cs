using ReelForge.Shared.Data.Models;

namespace ReelForge.WorkflowEngine.Execution.Caching;

/// <summary>
/// Outcome of a <see cref="IStepResultCache.TryGetAsync"/> lookup. <see cref="Entry"/> is
/// non-null if and only if <see cref="Hit"/> is true.
/// </summary>
public sealed record StepCacheLookupResult(bool Hit, WorkflowStepCacheEntry? Entry)
{
    /// <summary>Shared instance for every miss path — disabled, not found, expired, or a failed storage-artifact validation.</summary>
    public static readonly StepCacheLookupResult Miss = new(false, null);
}

/// <summary>
/// Reads and writes the cross-execution step-result cache
/// (<c>workflow_step_cache_entries</c>). Every implementation must uphold ONE invariant above all
/// others: a cache failure can NEVER fail the workflow it is trying to speed up. Both methods
/// catch and log every exception internally rather than letting one propagate — see
/// <see cref="StepResultCache"/>'s implementation for where that is enforced.
/// </summary>
public interface IStepResultCache
{
    /// <summary>
    /// Looks up a prior cached result for <paramref name="inputs"/>. Returns
    /// <see cref="StepCacheLookupResult.Miss"/> when caching is disabled, no matching row exists,
    /// the matching row has expired, or (when configured) the row's referenced storage object no
    /// longer exists. Never throws.
    /// </summary>
    Task<StepCacheLookupResult> TryGetAsync(StepCacheKeyInputs inputs, CancellationToken ct);

    /// <summary>
    /// Persists <paramref name="result"/> as the cached output for <paramref name="inputs"/>,
    /// upserting on (ProjectId, CacheKey). No-ops (without throwing) when caching is disabled,
    /// <paramref name="result"/> did not complete successfully, its output is empty, or its
    /// output exceeds <see cref="StepCacheOptions.MaxEntryChars"/>. Never throws.
    /// </summary>
    Task StoreAsync(StepCacheKeyInputs inputs, StepExecutionResult result, CancellationToken ct);
}
