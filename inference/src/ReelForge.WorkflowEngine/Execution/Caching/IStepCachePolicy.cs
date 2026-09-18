using ReelForge.Shared.Data.Models;

namespace ReelForge.WorkflowEngine.Execution.Caching;

/// <summary>
/// Decides whether a given <see cref="WorkflowStep"/>'s result is a candidate for
/// cross-execution caching at all. This is a pure, side-effect-free question about the STEP
/// DEFINITION — it says nothing about whether a matching cache entry actually exists (that is
/// <see cref="IStepResultCache.TryGetAsync"/>'s job).
/// </summary>
public interface IStepCachePolicy
{
    /// <summary>
    /// True when <paramref name="step"/> is eligible to be read from and written to the
    /// step-result cache. See <see cref="StepCachePolicy.IsCacheable"/> for the actual decision
    /// table and its rationale.
    /// </summary>
    bool IsCacheable(WorkflowStep step);
}
