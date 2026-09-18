namespace ReelForge.WorkflowEngine.Execution.Context;

/// <summary>
/// Result of <see cref="AgentInputBudget.Apply"/>. <see cref="OriginalChars"/> is the length of
/// today's plain concatenation (before any budgeting); <see cref="FinalChars"/> is
/// <see cref="Text"/>'s actual length, which can still exceed the configured cap when even the
/// protected steps alone don't fit (see rule 6 in <see cref="AgentInputBudget"/>'s doc comment).
/// </summary>
public sealed record BudgetedConcatenation(
    string Text,
    int OriginalChars,
    int FinalChars,
    int DigestedStepCount,
    int DroppedStepCount);

/// <summary>
/// Deterministic, non-LLM budget for the text an agent step's prompt concatenates from prior
/// steps' outputs. Pure and static: given the same <paramref name="parts"/> and
/// <paramref name="options"/> it always produces the same result, with no model call, no I/O, no
/// randomness.
///
/// <para>
/// <b>Safety contract (read this before changing anything here) — this is prompt-only budgeting.</b>
/// It must NEVER change <c>StepExecutionContext.StepOutputHistory</c> itself, NEVER change what
/// <c>WorkflowExecutorService</c> persists as <c>WorkflowStepResult.OutputJson</c>, and NEVER
/// affect any deterministic by-<c>StepOrder</c> consumer (e.g. <c>VideoCompileStepExecutor</c>
/// resolving a decision/plan/artifact by StepOrder straight off the persisted step result). This
/// mirrors <see cref="Execution.StepExecutionContext.SetPromptOutputOverride"/>'s existing
/// contract — an override/budget may only ever change what a PROMPT sees, never the underlying
/// step-result data. <see cref="Execution.StepExecutionContext"/> is the only caller; it applies
/// this to the string it is ABOUT TO hand an agent, never to anything it stores.
/// </para>
///
/// <para>Additional hard rules the caller (<see cref="Execution.StepExecutionContext"/>) upholds
/// by never invoking this type for certain modes/parts, rather than this type trying to detect
/// them itself:</para>
/// <list type="bullet">
/// <item><c>AgentInputContextMode.PreviousStepOnly</c>'s single-step output is NEVER passed
/// through here — it is frequently a machine-consumed contract (a <c>{view, meta}</c> envelope, a
/// structured decision) that the next agent must see byte-for-byte intact.</item>
/// <item><c>AgentInputContextMode.CustomMappedSubset</c>'s mapped output is NEVER passed through
/// here either — the mapping already deliberately narrowed it.</item>
/// <item>Within the parts this type IS given (<c>FullWorkflow</c>/<c>SelectedPriorSteps</c>), the
/// most recent <see cref="AgentInputBudgetOptions.RecentStepsVerbatim"/> entries (by descending
/// <c>StepOrder</c>) are protected from BOTH digesting and dropping — see rule 4/5 below.</item>
/// </list>
///
/// <para><b>Algorithm</b> (only engaged once the plain concatenation actually exceeds
/// <see cref="AgentInputBudgetOptions.MaxInputChars"/>):</para>
/// <list type="number">
/// <item>Disabled, or nothing to concatenate: return today's exact format, unchanged.</item>
/// <item>Build today's format (see <c>BuildFormatted</c>) — this doubles as the fast-path result
/// AND the <see cref="BudgetedConcatenation.OriginalChars"/> measurement.</item>
/// <item>Fits already (or the cap is disabled via a value &lt;= 0): return it unchanged — this is
/// the single most important behavior here, since it means an already-small workflow is
/// byte-identical to pre-budget behavior.</item>
/// <item>Over budget: protect the last <c>RecentStepsVerbatim</c> parts (by <c>StepOrder</c>,
/// stable-sorted ascending so "last N" means "highest N StepOrders"); digest every other part
/// through <see cref="JsonOutputDigest"/> with <c>MaxOutputChars = MaxDigestedStepChars</c>;
/// re-concatenate and measure.</item>
/// <item>Still over budget: drop non-protected parts OLDEST-first (ascending StepOrder), replacing
/// each dropped part's body with a one-line marker (the <c>## Step N: Label</c> header stays, so
/// the agent still knows a step existed), re-measuring after each drop and stopping as soon as it
/// fits.</item>
/// <item>Even the protected parts alone don't fit: return the result as-is — never corrupt a
/// machine-consumed contract just to hit a character count — with the stats still reflecting what
/// was actually digested/dropped.</item>
/// </list>
/// </summary>
public static class AgentInputBudget
{
    private const string StepSeparator = "\n\n---\n\n";
    private const string EmptyFallback = "[\"Begin analysis of the project.\"]";

    public static BudgetedConcatenation Apply(
        IReadOnlyList<(int StepOrder, string StepLabel, string Output)> parts,
        AgentInputBudgetOptions options)
    {
        string legacy = BuildFormatted(parts);
        int originalChars = legacy.Length;

        if (!options.Enabled || parts.Count == 0)
            return new BudgetedConcatenation(legacy, originalChars, originalChars, DigestedStepCount: 0, DroppedStepCount: 0);

        if (options.MaxInputChars <= 0 || originalChars <= options.MaxInputChars)
            return new BudgetedConcatenation(legacy, originalChars, originalChars, DigestedStepCount: 0, DroppedStepCount: 0);

        // Ascending StepOrder throughout: "protected" is a suffix of this list (highest
        // StepOrders), "droppable, oldest first" is the prefix before it.
        List<(int StepOrder, string StepLabel, string Output)> ordered = parts.OrderBy(p => p.StepOrder).ToList();
        int recentCount = Math.Clamp(options.RecentStepsVerbatim, 0, ordered.Count);
        int protectedFromIndex = ordered.Count - recentCount;

        JsonDigestSettings digestSettings = new(
            MaxOutputChars: options.MaxDigestedStepChars,
            MaxStringChars: options.DigestMaxStringChars,
            MaxArrayItems: options.DigestMaxArrayItems,
            MaxDepth: options.DigestMaxDepth);

        List<(int StepOrder, string StepLabel, string Output)> working = new(ordered.Count);
        int digestedCount = 0;
        for (int i = 0; i < ordered.Count; i++)
        {
            (int stepOrder, string stepLabel, string output) = ordered[i];
            if (i >= protectedFromIndex)
            {
                working.Add((stepOrder, stepLabel, output));
                continue;
            }

            string digested = JsonOutputDigest.Digest(output, digestSettings);
            if (digested != output)
                digestedCount++;

            working.Add((stepOrder, stepLabel, digested));
        }

        string afterDigest = BuildFormatted(working);
        if (afterDigest.Length <= options.MaxInputChars)
            return new BudgetedConcatenation(afterDigest, originalChars, afterDigest.Length, digestedCount, DroppedStepCount: 0);

        int droppedCount = 0;
        for (int i = 0; i < protectedFromIndex; i++)
        {
            (int stepOrder, string stepLabel, string originalOutput) = ordered[i];
            string marker =
                $"_[Step {stepOrder} output omitted by context budget ({originalOutput.Length} chars) " +
                "— re-read it with the project/file tools if you need it.]_";
            working[i] = (stepOrder, stepLabel, marker);
            droppedCount++;

            string candidate = BuildFormatted(working);
            if (candidate.Length <= options.MaxInputChars)
                return new BudgetedConcatenation(candidate, originalChars, candidate.Length, digestedCount, droppedCount);
        }

        // Rule 6: every droppable part is already gone and it still doesn't fit — the remaining
        // (protected) parts are a machine-consumed contract we must not further mutilate. Return
        // as-is; the stats still tell the caller exactly what happened.
        string finalText = BuildFormatted(working);
        return new BudgetedConcatenation(finalText, originalChars, finalText.Length, digestedCount, droppedCount);
    }

    /// <summary>
    /// The one definition of "today's concatenation format" — copied character-for-character
    /// from the pre-budget <c>StepExecutionContext.BuildConcatenated</c>: a single part returns
    /// its output verbatim (no header), more than one part is joined with
    /// <c>"## Step {order}: {label}"</c> headers separated by <c>"\n\n---\n\n"</c>, and zero parts
    /// falls back to a fixed prompt-safe placeholder.
    /// </summary>
    private static string BuildFormatted(IReadOnlyList<(int StepOrder, string StepLabel, string Output)> parts)
    {
        if (parts.Count == 0)
            return EmptyFallback;
        if (parts.Count == 1)
            return parts[0].Output;

        return string.Join(StepSeparator, parts.Select(p => $"## Step {p.StepOrder}: {p.StepLabel}\n{p.Output}"));
    }
}
