namespace ReelForge.Shared.Workflows;

/// <summary>
/// Deterministic, non-LLM operation performed by a <c>StepType.Extract</c> step.
/// The operation set is deliberately closed at three members — see plan §A.3/§A.5 and
/// "Explicitly NOT ported" §7 for why this is not a general transform DSL.
/// </summary>
public enum ExtractOperation
{
    /// <summary>Project a whitelisted view of an array/object at a JSON path.</summary>
    Project,

    /// <summary>Resolve an ordered list of ids against a prior projected view.</summary>
    Resolve,

    /// <summary>List/summarize project files via <c>IProjectFileWorkspace</c>.</summary>
    Files
}

/// <summary>Where a named Extract input is sourced from.</summary>
public enum ExtractInputSource
{
    /// <summary>The most recent completed step output.</summary>
    Previous,

    /// <summary>A specific step, identified by <see cref="ExtractInputRef.StepOrder"/>.</summary>
    Step,

    /// <summary>The current accumulated workflow output.</summary>
    Accumulated,

    /// <summary>The project's file inventory (op=<see cref="ExtractOperation.Files"/> only).</summary>
    ProjectFiles
}

/// <summary>Behaviour of a <c>resolve</c> operation when an id is not found among the records.</summary>
public enum ExtractUnknownIdBehaviour
{
    Fail,
    Skip
}

/// <summary>A single named input consumed by an Extract step.</summary>
public sealed record ExtractInputRef(ExtractInputSource From, int? StepOrder = null);

/// <summary>
/// Cheap, in-process structural checks run against the resolved view before the step is
/// considered successful. All checks run with no model call.
/// </summary>
public sealed record ExtractExpectation(
    IReadOnlyList<string>? RequiredPaths = null,
    int? MinItems = null,
    int? MaxItems = null,
    IReadOnlyList<string>? NonEmptyStringPaths = null);

/// <summary>
/// JSON configuration for a <c>StepType.Extract</c> workflow step. Deserialized from
/// <c>WorkflowStep.ExtractConfigJson</c>. A closed, versioned, strongly-typed record set —
/// deliberately not a general transform DSL (three operations only).
/// </summary>
public sealed record ExtractStepConfig(
    int Version,
    ExtractOperation Operation,
    IReadOnlyDictionary<string, ExtractInputRef> Inputs,
    // -- project --
    string? Path = null,
    IReadOnlyList<string>? Fields = null,
    string? IdField = null,
    string IdPrefix = "i",
    string? SortBy = null,
    int? Take = null,
    int Skip = 0,
    // -- resolve --
    string? IdsPath = null,
    string? RecordsPath = null,
    ExtractUnknownIdBehaviour OnUnknownId = ExtractUnknownIdBehaviour.Fail,
    // -- files --
    IReadOnlyList<string>? Categories = null,
    IReadOnlyList<string>? IncludeExtensions = null,
    IReadOnlyList<string>? ExcludePathContains = null,
    bool IncludeSummaries = true,
    bool IncludeContent = false,
    int MaxCharsPerFile = 2000,
    // -- universal --
    int MaxOutputChars = 24_000,
    ExtractExpectation? Expect = null);
