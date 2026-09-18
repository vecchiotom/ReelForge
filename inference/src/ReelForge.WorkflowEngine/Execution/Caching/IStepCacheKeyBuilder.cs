namespace ReelForge.WorkflowEngine.Execution.Caching;

/// <summary>
/// Builds the deterministic cache key for a <see cref="StepCacheKeyInputs"/> value. Pure and
/// side-effect-free by design — see <see cref="StepCacheKeyBuilder.Build"/> for the exact hashing
/// scheme.
/// </summary>
public interface IStepCacheKeyBuilder
{
    /// <summary>A lowercase SHA-256 hex digest (64 characters) of <paramref name="inputs"/>.</summary>
    string Build(StepCacheKeyInputs inputs);
}
