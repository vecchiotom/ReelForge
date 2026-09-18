using ReelForge.Shared.Data.Models;

namespace ReelForge.WorkflowEngine.Execution.Caching;

/// <summary>
/// Everything that affects a cacheable step's output, gathered into one immutable value so
/// <see cref="IStepCacheKeyBuilder.Build"/> can hash it deterministically. Two executions that
/// produce identical <see cref="StepCacheKeyInputs"/> are, by construction, expected to produce
/// identical step output — that is the entire correctness argument for serving one from the
/// other.
///
/// <para>
/// Every property here EXCEPT <see cref="WorkflowDefinitionId"/> feeds
/// <see cref="IStepCacheKeyBuilder.Build"/>'s hash — see that method's implementation for the
/// exact field list and framing. <see cref="WorkflowDefinitionId"/> is carried only so
/// <see cref="IStepResultCache.StoreAsync"/> can populate
/// <see cref="WorkflowStepCacheEntry.WorkflowDefinitionId"/> (metadata/observability only); it is
/// deliberately NOT hashed, for the same reason <see cref="WorkflowStepCacheEntry"/> carries no
/// foreign key to <c>workflow_definitions</c> — see that entity's doc comment.
/// </para>
/// </summary>
public sealed record StepCacheKeyInputs
{
    /// <summary>
    /// Bumped whenever the hashed field set or its framing changes, so a pre-existing cache entry
    /// from an older engine version can never be mistaken for a hit against the new key shape —
    /// it simply becomes an orphaned row that ages out via <see cref="StepCacheOptions.TtlHours"/>
    /// instead of being (incorrectly) matched or (expensively) migrated.
    /// </summary>
    public const string SchemaVersion = "v1";

    public required Guid ProjectId { get; init; }
    public required StepType StepType { get; init; }
    public required AgentType AgentType { get; init; }
    public required Guid AgentDefinitionId { get; init; }
    public string? AgentSystemPrompt { get; init; }
    public string? AgentOutputSchemaName { get; init; }
    public Guid? AgentInferenceProviderId { get; init; }
    public string? AgentAssignedSkillsJson { get; init; }
    public required AgentInputContextMode AgentInputContextMode { get; init; }
    public string? SelectedPriorStepOrdersJson { get; init; }
    public string? InputMappingJson { get; init; }
    public string? ExtractConfigJson { get; init; }
    public string? VideoAnalyzeConfigJson { get; init; }
    public string? VideoCompileConfigJson { get; init; }
    public string? EditRoomConfigJson { get; init; }
    public string? GraphicsRoomConfigJson { get; init; }
    public string? ColorGradeRoomConfigJson { get; init; }
    public string? ConditionExpression { get; init; }
    public required int MaxIterations { get; init; }
    public int? MinScore { get; init; }

    /// <summary>The fully-built step input string — <c>StepExecutionContext.LastResolvedAgentInput</c> (Agent steps) or the equivalent resolved-input descriptor a deterministic executor recorded.</summary>
    public string? ResolvedInput { get; init; }

    public string? UserRequest { get; init; }

    /// <summary>
    /// A hash of the project's file inventory (see <see cref="IProjectFileFingerprintProvider"/>).
    /// Required even for a step whose config doesn't obviously reference project files: any
    /// agent granted <see cref="Shared.Agents.ToolGroup.ProjectRead"/> can read file content that
    /// never appears in its prompt, so the file inventory itself must be part of the key or a
    /// cache hit could replay an answer about files that have since changed.
    /// </summary>
    public required string ProjectFileFingerprint { get; init; }

    /// <summary>See the class doc comment — metadata only, not part of the hash.</summary>
    public Guid WorkflowDefinitionId { get; init; }
}
