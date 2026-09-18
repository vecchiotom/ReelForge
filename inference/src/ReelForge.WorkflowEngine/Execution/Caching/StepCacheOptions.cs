namespace ReelForge.WorkflowEngine.Execution.Caching;

/// <summary>
/// Configuration for cross-execution step-result caching (<see cref="IStepResultCache"/>). Bound
/// from the <see cref="SectionName"/> configuration section.
/// </summary>
public sealed class StepCacheOptions
{
    public const string SectionName = "WorkflowEngine:StepCache";

    /// <summary>
    /// Master on/off switch. When false, <see cref="IStepResultCache.TryGetAsync"/> always
    /// returns a miss and <see cref="IStepResultCache.StoreAsync"/> never writes — the cache
    /// behaves as if it were never wired in, without needing to remove the DI registration or
    /// touch <see cref="WorkflowExecutorService"/>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How long a stored entry stays eligible to be served, in hours, from the moment it was
    /// written. A step re-run after this window recomputes and overwrites the entry with a fresh
    /// TTL. Default one week.
    /// </summary>
    public int TtlHours { get; set; } = 168;

    /// <summary>
    /// The largest <c>Output</c> (character count) <see cref="IStepResultCache.StoreAsync"/> will
    /// persist. A larger output is not cached at all — silently, not as an error — since this
    /// cache's purpose is to skip re-running agents/ffmpeg, not to become an unbounded blob store;
    /// the step's own <c>OutputStorageKey</c>/<c>ArtifactStorageKey</c> mechanism already exists
    /// for genuinely large payloads and those keys ARE still cached (see
    /// <see cref="ValidateStorageArtifacts"/>).
    /// </summary>
    public int MaxEntryChars { get; set; } = 2_000_000;

    /// <summary>
    /// When true, a cache hit that references an <c>OutputStorageKey</c>/<c>ArtifactStorageKey</c>
    /// is verified against S3/MinIO (a HEAD request) before being served, and the entry is evicted
    /// on a miss (the object was deleted or the bucket was reset) rather than handing downstream
    /// steps a dangling key. Adds one S3 round-trip per such hit; disable only if that latency
    /// matters more than the (rare) risk of serving a dangling storage key.
    /// </summary>
    public bool ValidateStorageArtifacts { get; set; } = true;
}
