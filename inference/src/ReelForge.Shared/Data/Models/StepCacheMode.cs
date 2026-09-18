namespace ReelForge.Shared.Data.Models;

/// <summary>
/// Per-step override for cross-execution step-result caching (see
/// <c>ReelForge.WorkflowEngine.Execution.Caching.IStepCachePolicy</c> for the actual policy
/// decision this participates in). Null on <see cref="WorkflowStep.CacheMode"/> means
/// <see cref="Default"/> — the built-in, StepType/AgentType-driven decision — not "disabled".
/// </summary>
public enum StepCacheMode
{
    /// <summary>
    /// Defer to the built-in policy: cacheable for deterministic step types (Extract,
    /// VideoAnalyze, VideoCompile, EditRoom, ColorGradeRoom) and for Agent steps whose granted
    /// tools carry no state-mutating side effect outside their own output (no ProjectWrite, no
    /// SandboxAuthoring, no SandboxRender). Never cacheable for GraphicsRoom, ReviewLoop,
    /// Conditional, ForEach, or Parallel.
    /// </summary>
    Default,

    /// <summary>
    /// Force this step to be treated as cacheable regardless of the built-in policy. Use with
    /// care on a step type/agent the default policy excludes — the workflow author is asserting
    /// that, for this specific step, replaying a prior output has no side effect worth
    /// re-running for.
    /// </summary>
    Always,

    /// <summary>
    /// Force this step to never be served from or written to the cache, regardless of the
    /// built-in policy. Use for a step whose output must always be freshly computed even when it
    /// would otherwise qualify (e.g. a step deliberately kept non-deterministic for testing).
    /// </summary>
    Never
}
