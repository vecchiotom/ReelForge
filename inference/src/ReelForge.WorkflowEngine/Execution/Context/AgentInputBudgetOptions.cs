namespace ReelForge.WorkflowEngine.Execution.Context;

/// <summary>
/// Configuration for the deterministic, non-LLM prompt-context budget applied to
/// <c>AgentInputContextMode.FullWorkflow</c>/<c>SelectedPriorSteps</c> agent inputs — see
/// <see cref="AgentInputBudget"/>'s doc comment for the full safety contract this budget must
/// respect. Bound at <see cref="SectionName"/>, alongside <c>WorkflowHardeningOptions</c>'s own
/// <c>"WorkflowHardening"</c> section.
/// </summary>
public sealed class AgentInputBudgetOptions
{
    public const string SectionName = "WorkflowEngine:ContextBudget";

    /// <summary>
    /// Master on/off switch. When false, <see cref="AgentInputBudget.Apply"/> always returns the
    /// legacy full concatenation, byte-identical to today's behavior — the same fast path taken
    /// when the composed input already fits under <see cref="MaxInputChars"/>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Overall cap, in characters, on the composed prior-steps input before an agent call. A
    /// value &lt;= 0 disables the cap entirely (equivalent to leaving <see cref="Enabled"/> false
    /// for budgeting purposes, though the flag itself stays whatever it was set to).
    /// </summary>
    public int MaxInputChars { get; set; } = 160_000;

    /// <summary>
    /// Per-step cap, in characters, applied to an individual OLDER step's output once digesting
    /// kicks in (i.e. once <see cref="MaxInputChars"/> is exceeded by the plain concatenation).
    /// </summary>
    public int MaxDigestedStepChars { get; set; } = 12_000;

    /// <summary>
    /// How many of the most recent steps (by descending <c>StepOrder</c>) are always kept
    /// verbatim — never digested, never dropped — regardless of the overall budget.
    /// </summary>
    public int RecentStepsVerbatim { get; set; } = 2;

    /// <summary>String-value truncation length used by <see cref="JsonOutputDigest"/>.</summary>
    public int DigestMaxStringChars { get; set; } = 600;

    /// <summary>Array-length cap used by <see cref="JsonOutputDigest"/>.</summary>
    public int DigestMaxArrayItems { get; set; } = 25;

    /// <summary>Nesting-depth cap used by <see cref="JsonOutputDigest"/>.</summary>
    public int DigestMaxDepth { get; set; } = 12;
}
