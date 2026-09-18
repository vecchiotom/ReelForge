namespace ReelForge.Shared.Data.Models;

/// <summary>
/// A cross-execution step-result cache row: the persisted output of a previously COMPLETED,
/// cacheable step, keyed by <see cref="ProjectId"/> + <see cref="CacheKey"/> so a later execution
/// of the same (or a differently-saved but structurally identical) workflow step against an
/// unchanged project can replay it instead of re-calling an agent or re-running ffmpeg. Owned by
/// the WorkflowEngine (table <c>workflow_step_cache_entries</c>).
///
/// <para>
/// <b>Deliberately no foreign key to <c>projects</c> or <c>workflow_definitions</c>.</b> A normal
/// FK (even one with <c>OnDelete(Cascade)</c>) would tie a cache row's lifetime to a specific
/// workflow DEFINITION row, so trivial, output-irrelevant edits to that definition (renaming a
/// step's label, reordering unrelated steps, editing an unrelated step's prompt) would either
/// cascade-orphan/void perfectly good cache entries or require constant migration bookkeeping to
/// keep the FK valid. The cache is keyed on <see cref="CacheKey"/> — a content hash of everything
/// that actually affects a step's output (see
/// <c>ReelForge.WorkflowEngine.Execution.Caching.StepCacheKeyBuilder</c>) — precisely so it
/// survives definition edits that don't change that content, and naturally misses (rather than
/// serving something stale) when they do. Rows are instead pruned by POLICY: TTL expiry
/// (<see cref="ExpiresAt"/>, opportunistically swept by
/// <c>ReelForge.WorkflowEngine.Execution.Caching.StepResultCache.StoreAsync</c>) is the only
/// eviction mechanism — there is no cascade delete on project or workflow-definition removal, so
/// a deleted project's cache rows simply age out like any other entry rather than requiring a
/// synchronous cleanup on the hot delete path.
/// </para>
/// </summary>
public class WorkflowStepCacheEntry
{
    public Guid Id { get; set; }

    /// <summary>The project this cached step ran against. Part of the lookup key, not a real FK — see the class doc comment.</summary>
    public Guid ProjectId { get; set; }

    /// <summary>Lowercase SHA-256 hex digest (64 chars) of everything that affects this step's output. See <c>StepCacheKeyBuilder</c>.</summary>
    public string CacheKey { get; set; } = string.Empty;

    /// <summary>
    /// The workflow definition this entry was produced under. Metadata/observability only — NOT
    /// part of <see cref="CacheKey"/> and never used to filter a lookup (a cache hit must be
    /// found by <see cref="ProjectId"/> + <see cref="CacheKey"/> alone, or the whole point of
    /// surviving definition edits is defeated). No FK — see the class doc comment.
    /// </summary>
    public Guid WorkflowDefinitionId { get; set; }

    public StepType StepType { get; set; }

    public AgentType AgentType { get; set; }

    /// <summary>The cached step's raw output text — what would have been <c>WorkflowStepResult.Output</c>.</summary>
    public string Output { get; set; } = string.Empty;

    /// <summary>Mirrors <see cref="WorkflowStepResult.OutputStorageKey"/> of the originating result, when the step produced media.</summary>
    public string? OutputStorageKey { get; set; }

    /// <summary>Mirrors <see cref="WorkflowStepResult.ArtifactStorageKey"/> of the originating result, when the step produced a large JSON artifact.</summary>
    public string? ArtifactStorageKey { get; set; }

    /// <summary>Mirrors <see cref="WorkflowStepResult.ChatTranscriptJson"/> of the originating result, for a room step.</summary>
    public string? ChatTranscriptJson { get; set; }

    /// <summary>What the ORIGINAL (cache-miss) execution actually spent — never what a hit costs, which is always 0. See <see cref="WorkflowStepResult.CachedTokensSaved"/>.</summary>
    public int TokensUsed { get; set; }

    public int? InputTokens { get; set; }

    public int? OutputTokens { get; set; }

    /// <summary>How long the ORIGINAL execution took to produce this output.</summary>
    public long DurationMs { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Set on every accepted cache hit. Null until the first hit.</summary>
    public DateTime? LastHitAt { get; set; }

    public int HitCount { get; set; }

    /// <summary>
    /// When this entry stops being served. Null means it never expires by policy (still subject
    /// to being overwritten by a fresh execution's <c>StoreAsync</c> upsert). Populated from
    /// <c>StepCacheOptions.TtlHours</c> at write time.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }
}
