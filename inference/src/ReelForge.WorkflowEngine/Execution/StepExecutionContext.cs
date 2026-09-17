using System.Text.Json;
using ReelForge.Shared.Data.Models;
using ReelForge.WorkflowEngine.Agents;

namespace ReelForge.WorkflowEngine.Execution;

/// <summary>
/// <paramref name="OutputStorageKey"/>/<paramref name="ArtifactStorageKey"/> mirror the same
/// fields on <see cref="WorkflowStepResult"/> for the step that produced this history entry —
/// carried here so <c>VideoSourceKind.StepOutput</c>/<c>PreviousStepOutput</c> (a prior render or
/// VideoCompile step) and cross-step artifact resolution (a prior VideoAnalyze step, referenced
/// by <c>VideoCompileStepConfig.AnalysisStepOrder</c>) can resolve within the same execution
/// without a DB round-trip. Both default to null so every pre-existing 3-arg call site
/// (Extract/Agent/etc. history entries, which never set either) keeps compiling unchanged.
/// </summary>
public record StepOutputHistoryEntry(
    int StepOrder,
    string StepLabel,
    string Output,
    string? OutputStorageKey = null,
    string? ArtifactStorageKey = null);

/// <summary>
/// Context passed to each step executor containing all needed state.
/// </summary>
public class StepExecutionContext
{
    private readonly List<string> _retryFeedback = new();
    private readonly Dictionary<int, string> _promptOutputOverrides = new();

    public required WorkflowExecution Execution { get; init; }
    public required WorkflowStep Step { get; init; }
    public required List<WorkflowStep> AllSteps { get; init; }
    public required string AccumulatedOutput { get; init; }
    public required int CurrentStepIndex { get; init; }
    public required int IterationCount { get; init; }
    public required string CorrelationId { get; init; }
    public CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// Ordered history of completed step outputs so context modes can resolve inputs
    /// without querying persisted step results.
    /// </summary>
    public required IReadOnlyList<StepOutputHistoryEntry> StepOutputHistory { get; init; }

    /// <summary>
    /// Last concrete agent input built during this context lifetime.
    /// </summary>
    public string? LastResolvedAgentInput { get; private set; }

    /// <summary>
    /// Free-text user request provided at execution time.
    /// Null when the workflow was executed without user input.
    /// </summary>
    public string? UserRequest { get; init; }

    /// <summary>
    /// The <see cref="WorkflowStepResult"/> id for this attempt — set by
    /// <see cref="WorkflowExecutorService"/> once the row is created (after this context is first
    /// constructed), so it is <see cref="Guid.Empty"/> for the brief window before that. Mutable
    /// (not <c>init</c>) for that reason.
    /// </summary>
    public Guid StepResultId { get; set; }

    /// <summary>
    /// Wired by <see cref="WorkflowExecutorService"/> to publish an ephemeral
    /// <c>WorkflowStepProgress</c> event via <see cref="IWorkflowEventPublisher"/>. Null in any
    /// context built without that wiring (e.g. a unit test constructing this directly) — callers
    /// should always go through <see cref="ReportProgressAsync"/>, which no-ops when this is null,
    /// rather than invoking the delegate directly.
    /// </summary>
    public Func<string, int?, CancellationToken, Task>? ProgressReporter { get; set; }

    /// <summary>
    /// Reports a lightweight, best-effort progress checkpoint for a long-running step (e.g.
    /// <c>VideoAnalyze</c>/<c>VideoCompile</c>) — a short stage label, and a 0-100 percent when one
    /// is naturally available (e.g. real ffmpeg encode progress). Never persisted, never throws
    /// (progress reporting must never be able to fail the step it is reporting on); a no-op when
    /// <see cref="ProgressReporter"/> was never wired.
    /// </summary>
    public async Task ReportProgressAsync(string stage, int? percentComplete = null)
    {
        if (ProgressReporter is null)
            return;

        try
        {
            await ProgressReporter(stage, percentComplete, CancellationToken);
        }
        catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested)
        {
            // Execution is winding down anyway — swallow, same as any other best-effort signal.
        }
        catch
        {
            // Progress reporting is pure UI signal (see WorkflowStepProgress's doc comment) — a
            // publish failure (e.g. a transient RabbitMQ hiccup) must never fail the step itself.
        }
    }

    /// <summary>
    /// Stores retry feedback (for example schema validation errors) so the next
    /// agent attempt can self-correct based on the previous failure.
    /// </summary>
    public IReadOnlyList<string> RetryFeedback => _retryFeedback.AsReadOnly();

    /// <summary>
    /// Prompt-ready retry guidance text built from previous failed attempts.
    /// Empty when no retry feedback has been recorded.
    /// </summary>
    public string RetryGuidance => BuildRetryGuidance();

    /// <summary>
    /// Builds the final string input for an agent step, applying the effective
    /// step-level mode and appending the optional user request.
    /// </summary>
    public string BuildAgentInput()
    {
        AgentInputContextMode effectiveMode = AgentInputContextResolver.ResolveEffectiveMode(Step);
        string baseInput = effectiveMode switch
        {
            AgentInputContextMode.FullWorkflow => BuildFullWorkflowInput(),
            AgentInputContextMode.PreviousStepOnly => BuildPreviousStepInput(),
            AgentInputContextMode.SelectedPriorSteps => BuildSelectedPriorStepsInput(),
            AgentInputContextMode.CustomMappedSubset => BuildCustomMappedSubsetInput(),
            _ => BuildFullWorkflowInput()
        };

        string composedInput = baseInput;

        if (!string.IsNullOrWhiteSpace(UserRequest))
            composedInput = $"{composedInput}\n\n---\nUser Request:\n{UserRequest}";

        string retryGuidance = BuildRetryGuidance();
        if (!string.IsNullOrWhiteSpace(retryGuidance))
            composedInput = $"{composedInput}\n\n---\nRetry Guidance:\n{retryGuidance}";

        LastResolvedAgentInput = composedInput;
        return composedInput;
    }

    /// <summary>
    /// Records what a deterministic (non-agent) step, such as an Extract step, actually
    /// consumed as its input — a compact descriptor, not the raw upstream blob — so
    /// <c>ResolveInputJsonForPersistence</c> persists something meaningful instead of falling
    /// back to the full accumulated output.
    /// </summary>
    public void RecordResolvedInput(string? value) => LastResolvedAgentInput = value;

    /// <summary>
    /// Substitutes an enriched rendering of an already-completed step's output for PROMPT-BUILDING
    /// purposes only — <see cref="StepOutputHistory"/> itself, the persisted
    /// <c>WorkflowStepResult.OutputJson</c> of the step being overridden, and every deterministic
    /// consumer that resolves a step's output by <c>StepOrder</c> (e.g.
    /// <c>VideoCompileStepExecutor</c>'s decision/plan/artifact resolution) all keep seeing the
    /// original, unmodified output.
    ///
    /// <para>
    /// Added for exactly one caller: <see cref="StepExecutors.MotionGraphicsPlacementAnnotator"/>,
    /// which marks each <c>view.placements</c> candidate with whether it survives the story
    /// editor's cut — information that only exists once BOTH the analyze step and the story-editor
    /// step have completed, and therefore cannot be baked into the analyze step's own output. An
    /// override must only ever ADD derived, server-computed context to a step's output; it is not
    /// a mechanism for rewriting what a step actually produced.
    /// </para>
    /// </summary>
    public void SetPromptOutputOverride(int stepOrder, string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return;

        _promptOutputOverrides[stepOrder] = output;
    }

    /// <summary>
    /// The prompt-facing output for <paramref name="entry"/> — its override if one was installed
    /// by <see cref="SetPromptOutputOverride"/>, otherwise its own output verbatim.
    /// </summary>
    private StepOutputHistoryEntry ForPrompt(StepOutputHistoryEntry entry) =>
        _promptOutputOverrides.TryGetValue(entry.StepOrder, out string? overridden)
            ? entry with { Output = overridden }
            : entry;

    public void RecordRetryFeedback(int attemptNumber, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return;

        string normalized = reason.Trim();
        if (normalized.Length > 2000)
            normalized = normalized[..2000];

        _retryFeedback.Add($"Attempt {attemptNumber}: {normalized}");

        // Keep only the latest few feedback entries to avoid runaway prompt growth.
        while (_retryFeedback.Count > 3)
            _retryFeedback.RemoveAt(0);
    }

    private string BuildFullWorkflowInput()
    {
        List<StepOutputHistoryEntry> history = StepOutputHistory
            .Where(h => h.StepOrder < Step.StepOrder && !string.IsNullOrWhiteSpace(h.Output))
            .OrderBy(h => h.StepOrder)
            .Select(ForPrompt)
            .ToList();

        return BuildConcatenated(history);
    }

    private string BuildPreviousStepInput()
    {
        // "The previous step" means the entry with the greatest StepOrder strictly less than
        // this step's own StepOrder — NOT "the most recently appended entry". Those two
        // coincide as long as StepOutputHistory is append-only, but a ReviewLoop loop-back
        // re-executes earlier steps, appending a second entry for a StepOrder that already
        // exists in history; "most recently appended" would then resolve to whichever step
        // last completed (which can be the ReviewLoop step's own JSON verdict), not the step
        // immediately before this one. See WorkflowExecutorService's loop-back pruning for the
        // complementary belt-and-braces fix.
        StepOutputHistoryEntry? latest = StepOutputHistory
            .Where(h => h.StepOrder < Step.StepOrder && !string.IsNullOrWhiteSpace(h.Output))
            .OrderByDescending(h => h.StepOrder)
            .FirstOrDefault();
        return latest is null ? "[\"Begin analysis of the project.\"]" : ForPrompt(latest).Output;
    }

    private string BuildSelectedPriorStepsInput()
    {
        IReadOnlyList<int> selectedOrders = ParseSelectedStepOrders();
        if (selectedOrders.Count == 0)
            return "[\"Begin analysis of the project.\"]";

        HashSet<int> selectedSet = selectedOrders.ToHashSet();
        List<StepOutputHistoryEntry> selected = StepOutputHistory
            .Where(h => selectedSet.Contains(h.StepOrder) && !string.IsNullOrWhiteSpace(h.Output))
            .OrderBy(h => h.StepOrder)
            .Select(ForPrompt)
            .ToList();

        return BuildConcatenated(selected);
    }

    private string BuildCustomMappedSubsetInput()
    {
        if (string.IsNullOrWhiteSpace(Step.InputMappingJson))
            return BuildFullWorkflowInput();

        string? mapped = ExpressionEvaluator.ApplyInputMapping(AccumulatedOutput, Step.InputMappingJson);
        if (string.IsNullOrWhiteSpace(mapped))
            return BuildFullWorkflowInput();

        return mapped;
    }

    private IReadOnlyList<int> ParseSelectedStepOrders()
    {
        if (string.IsNullOrWhiteSpace(Step.SelectedPriorStepOrdersJson))
            return [];

        try
        {
            List<int>? parsed = JsonSerializer.Deserialize<List<int>>(Step.SelectedPriorStepOrdersJson);
            if (parsed is null)
                return [];

            return parsed
                .Where(stepOrder => stepOrder > 0 && stepOrder < Step.StepOrder)
                .Distinct()
                .OrderBy(stepOrder => stepOrder)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string BuildConcatenated(IEnumerable<StepOutputHistoryEntry> history)
    {
        List<StepOutputHistoryEntry> parts = history.Where(h => !string.IsNullOrEmpty(h.Output)).ToList();
        if (parts.Count == 0)
            return "[\"Begin analysis of the project.\"]";
        if (parts.Count == 1)
            return parts[0].Output;

        return string.Join("\n\n---\n\n", parts.Select(p => $"## Step {p.StepOrder}: {p.StepLabel}\n{p.Output}"));
    }

    private string BuildRetryGuidance()
    {
        if (_retryFeedback.Count == 0)
            return string.Empty;

        return string.Join(
            "\n",
            _retryFeedback.Select(message => $"- {message}"))
            + "\nReturn output that strictly matches the expected JSON schema and is valid JSON.";
    }
}

/// <summary>
/// Result from a step execution determining what happens next.
/// </summary>
public class StepExecutionResult
{
    public required string Output { get; init; }
    public required int NextStepIndex { get; init; }
    public int NewIterationCount { get; init; }
    public int TokensUsed { get; init; }
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public long DurationMs { get; init; }
    public int AttemptCount { get; init; } = 1;
    public int RetryCount { get; init; }
    public IReadOnlyList<AgentToolCallTrace> ToolCalls { get; init; } = [];
    public IReadOnlyList<string> Reasoning { get; init; } = [];
    public StepStatus Status { get; init; } = StepStatus.Completed;
    public string? ErrorDetails { get; init; }
    public int? IterationNumber { get; init; }

    /// <summary>
    /// S3/MinIO storage key for a video or image artifact produced during this step.
    /// Set by the RenderVideoAndUploadToStorage sandbox tool; keys now follow
    /// "projects/{projectId}/outputFiles/{executionId}/..." rather than the old
    /// "outputs/" prefix.
    /// </summary>
    public string? OutputStorageKey { get; init; }

    /// <summary>
    /// S3/MinIO storage key for a large, non-playable JSON artifact produced during this step
    /// (a VideoAnalyze step's full analysis document, or a VideoCompile step's EDL audit trail).
    /// Mirrors <see cref="WorkflowStepResult.ArtifactStorageKey"/> — kept as a SEPARATE field
    /// from <see cref="OutputStorageKey"/> for the same reason that column is separate: an
    /// outputFiles-prefixed OutputStorageKey is treated as a playable video by OutputsController,
    /// so a JSON artifact must never be confused with one.
    /// </summary>
    public string? ArtifactStorageKey { get; init; }
}
