using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Inference;
using ReelForge.Shared.Workflows;
using ReelForge.WorkflowEngine.Agents;
using ReelForge.WorkflowEngine.Agents.Rooms;
using ReelForge.WorkflowEngine.Agents.Tools;
using ReelForge.WorkflowEngine.Services.Storage;

namespace ReelForge.WorkflowEngine.Execution.StepExecutors;

/// <summary>
/// Shared scaffolding for every multi-agent "room" step type (<see cref="StepType.EditRoom"/>,
/// <see cref="StepType.GraphicsRoom"/>, a future color-grading room, ...): several persona seats
/// plus a director-role moderator converse in a live <c>Microsoft.Agents.AI.Workflows</c> group
/// chat over a bounded view, then the director emits ONE schema-validated
/// <typeparamref name="TDecision"/> in a normal structured call OUTSIDE the chat loop.
///
/// <para>
/// A concrete room supplies, via the abstract members below: its <see cref="StepType"/>/config
/// column/config type, the id-vocabulary extraction and mention regex for its own id namespace,
/// the room charter prompt and director turn directive, which <see cref="AgentType"/>s back the
/// seats/director/solo-fallback, and the deterministic decision validation (normalize, filter to
/// offered ids, accept/reject). Everything else — deterministic turn scheduling (via a
/// <see cref="RoomGroupChatManager"/> the subclass constructs), per-turn option injection and
/// prefix-cache-preserving message ordering (<see cref="RoomSeatAgent"/>), live progress +
/// <c>WorkflowStepChatTurn</c> events, transcript persistence (both the MinIO artifact and the
/// DB-persisted <c>ChatTranscriptJson</c>), the retried standalone synthesis call, the
/// solo-fallback degrade, and the never-throws/always-valid-JSON failure envelope — is shared
/// verbatim across rooms.
/// </para>
///
/// <para>
/// Same never-throws, always-valid-JSON discipline as <see cref="VideoAnalyzeStepExecutor"/>/
/// <see cref="VideoCompileStepExecutor"/> — every failure mode degrades to the solo fallback
/// (<see cref="IRoomStepConfig.FallbackToSolo"/>) or, as an absolute last resort, a clean
/// structured failure result.
/// </para>
/// </summary>
public abstract class RoomStepExecutorBase<TDecision> : IStepExecutor where TDecision : class
{
    protected static readonly JsonSerializerOptions ConfigJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    // Mirrors VideoCompileStepExecutor.DecisionJsonOptions exactly: no UnmappedMemberHandling
    // override (default Skip), which is why the additive "room" sibling object on output_json is
    // safe to include alongside the decision's own properties — every downstream consumer of the
    // plain schema (VideoCompileStepExecutor's Decision AND GraphicsPlan resolution) simply
    // ignores the extra key.
    protected static readonly JsonSerializerOptions DecisionJsonOptions = new(JsonSerializerDefaults.Web);

    protected static readonly JsonSerializerOptions EnvelopeJsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    /// <summary>The room-participant display name for the director — matches what its RoomSeatAgent wrapper reports via Name.</summary>
    protected const string DirectorSeatName = "Director";

    /// <summary>
    /// Result of <see cref="FilterToOfferedIds"/>: how many id-bearing entries were dropped, plus
    /// an optional room-generic breakdown a concrete room wants surfaced on the output's
    /// <c>room</c> metadata object (e.g. the edit room's <c>droppedMixedSourceSpanCount</c>). Keys
    /// in <see cref="ExtraMeta"/> are merged into <c>roomMeta</c> verbatim — a room whose extra key
    /// collides with a base key (<c>seats</c>, <c>rounds</c>, ...) would silently overwrite it, so
    /// pick names that read as domain-specific.
    /// </summary>
    protected readonly record struct RoomFilterResult(int Dropped, JsonObject? ExtraMeta);

    protected readonly IAgentChatClientProvider ChatClients;
    protected readonly IAgentToolProvider ToolProvider;
    protected readonly IAgentRegistry AgentRegistry;
    protected readonly IProjectFileWorkspace Workspace;
    protected readonly IWorkflowExecutionContextAccessor ExecutionContextAccessor;
    protected readonly ILogger Logger;

    protected RoomStepExecutorBase(
        IAgentChatClientProvider chatClients,
        IAgentToolProvider toolProvider,
        IAgentRegistry agentRegistry,
        IProjectFileWorkspace workspace,
        IWorkflowExecutionContextAccessor executionContextAccessor,
        ILogger logger)
    {
        ChatClients = chatClients;
        ToolProvider = toolProvider;
        AgentRegistry = agentRegistry;
        Workspace = workspace;
        ExecutionContextAccessor = executionContextAccessor;
        Logger = logger;
    }

    // ---------------------------------------------------------------------
    // The domain seam — what a concrete room must (or may) supply.
    // ---------------------------------------------------------------------

    public abstract StepType StepType { get; }

    /// <summary>Log/name token for this room's step type, e.g. "EditRoom".</summary>
    protected abstract string RoomKind { get; }

    /// <summary>Sentence-case display name used in degrade messages, e.g. "Edit room".</summary>
    protected abstract string RoomDisplayName { get; }

    /// <summary>Failure-code prefix, e.g. "EDIT_ROOM" (yields EDIT_ROOM_CONFIG_INVALID / EDIT_ROOM_FAILED).</summary>
    protected abstract string FailureCodePrefix { get; }

    /// <summary>The name of the step's config JSON property, e.g. "EditRoomConfigJson" — used in config-error messages.</summary>
    protected abstract string ConfigPropertyName { get; }

    /// <summary>Reads this room's config JSON off the step row.</summary>
    protected abstract string? GetConfigJson(WorkflowStep step);

    /// <summary>Deserializes this room's config record (may throw <see cref="JsonException"/>; the template catches it).</summary>
    protected abstract IRoomStepConfig? DeserializeConfig(string json);

    /// <summary>Extracts this room's offered id vocabulary from the resolved bounded view.</summary>
    protected abstract HashSet<string> ExtractOfferedIds(JsonNode? viewRoot);

    /// <summary>Extracts this room's offered-id mentions from free-form turn text (for chat-turn events).</summary>
    protected abstract IReadOnlyList<string> ExtractIdMentions(string? text, IReadOnlySet<string> offeredIds);

    /// <summary>The failure message when the view offers zero ids to decide over.</summary>
    protected abstract string NoOfferedIdsMessage { get; }

    /// <summary>
    /// Optional escape hatch for a room whose decision space can legitimately be empty: return a
    /// non-null decision to complete the step successfully (without running the room at all) when
    /// the resolved view offers zero ids. The default (null) fails the step with VIEW_UNRESOLVED —
    /// the edit room's behavior, where a view with nothing to keep is a genuine upstream error.
    /// </summary>
    protected virtual TDecision? BuildDecisionForEmptyView() => null;

    /// <summary>The shared, identical instructions for EVERY room participant (seats AND the director's room-participant instance).</summary>
    protected abstract string RoomCharterPrompt { get; }

    /// <summary>The AgentType every seat's inner agent resolves through (per-seat AgentDefinitionId overrides still apply).</summary>
    protected abstract AgentType SeatAgentType { get; }

    /// <summary>The AgentType the director resolves through — both as room participant and for the standalone synthesis call.</summary>
    protected abstract AgentType DirectorAgentType { get; }

    /// <summary>The AgentType the solo fallback resolves through.</summary>
    protected abstract AgentType SoloFallbackAgentType { get; }

    /// <summary>The role noun used in the default seat directive, e.g. "editor".</summary>
    protected abstract string SeatRoleNoun { get; }

    /// <summary>The director's per-turn room-participant directive (appended last, after the shared prefix).</summary>
    protected abstract string DirectorTurnDirective { get; }

    /// <summary>Instruction sentence naming the synthesis schema, e.g. "output ONLY valid JSON matching the VideoEditDecisionOutput schema."</summary>
    protected abstract string SynthesisSchemaInstruction { get; }

    /// <summary>Normalizes a freshly deserialized decision in place (e.g. null-list coalescing). Applied on both the synthesis and solo paths.</summary>
    protected abstract void NormalizeDecision(TDecision decision);

    /// <summary>
    /// Rejects a freshly synthesized (already normalized, not yet filtered) decision by returning
    /// a retry-guidance error, or null to accept. The edit room rejects an empty Keep list here;
    /// the graphics room accepts an empty plan (zero overlays is a valid outcome).
    /// </summary>
    protected abstract string? RejectFreshSynthesis(TDecision decision);

    /// <summary>
    /// Drops every id-bearing entry not in <paramref name="offeredIds"/> (and any other
    /// room-specific structural rule — e.g. the edit room additionally drops a Keep span whose two
    /// ids come from different source clips). <paramref name="viewRoot"/> is the same parsed
    /// bounded view <see cref="ExtractOfferedIds"/> was built from, re-offered here for a room that
    /// needs per-id metadata beyond plain membership (source-clip index, region metadata, ...)
    /// without the base template having to model that metadata generically.
    /// </summary>
    protected abstract RoomFilterResult FilterToOfferedIds(TDecision decision, HashSet<string> offeredIds, JsonNode? viewRoot);

    /// <summary>
    /// Rejects an already-filtered decision by returning a degrade/error message, or null to
    /// accept. <paramref name="solo"/> distinguishes the solo-fallback path's message wording.
    /// </summary>
    protected abstract string? RejectFilteredDecision(TDecision decision, bool solo);

    /// <summary>
    /// Optional soft-reject of a freshly normalized, already-<see cref="RejectFreshSynthesis"/>-
    /// accepted decision: return retry guidance to spend one more synthesis attempt (mirrors
    /// <see cref="RejectFreshSynthesis"/>'s retry), or null to accept as-is. Unlike
    /// <see cref="RejectFreshSynthesis"/>, this issue is NOT fatal on the last attempt — the
    /// template accepts the decision regardless once <c>attempt == maxAttempts</c>, trusting
    /// <see cref="FilterToOfferedIds"/> to clean up whatever the issue was (the edit room's
    /// mixed-source-span check: worth one retry with precise feedback, but a span still bridging
    /// two clips on the final attempt is dropped rather than degrading the whole room).
    /// </summary>
    protected virtual string? CheckRetryableIssue(TDecision decision, JsonNode? viewRoot) => null;

    /// <summary>Constructs this room's <see cref="RoomGroupChatManager"/> (the subclass binds its own id-mention pattern and config type).</summary>
    protected abstract RoomGroupChatManager CreateManager(
        IReadOnlyList<AIAgent> agents, IRoomStepConfig config, IReadOnlySet<string> offeredIds);

    /// <summary>Per-seat room-turn directive. Default mirrors the edit room's original wording via <see cref="SeatRoleNoun"/>.</summary>
    protected virtual string BuildSeatDirective(EditRoomSeat seat) =>
        $"You are now speaking as {seat.Name}, the {seat.Persona} {SeatRoleNoun} in this room.";

    /// <summary>Progress label for a completed turn. Default mirrors the edit room's original wording.</summary>
    protected virtual string BuildTurnProgressLabel(bool isDirector, string seatName, int turnNumber, int maxTurns) =>
        isDirector
            ? "Director is reviewing"
            : $"{seatName} is proposing a cut (turn {turnNumber}/{maxTurns})";

    /// <summary>The group chat's opening message carrying the bounded view — the shared prompt-prefix every turn hits the cache against.</summary>
    protected virtual string BuildOpeningMessage(string viewJson) =>
        $"Bounded analysis view for this {RoomDisplayName.ToLowerInvariant()} (every participant sees this same view):\n\n" + viewJson;

    /// <summary>
    /// Room-turn tool scope for an in-room agent (a seat's or the director's room-participant
    /// instance). Defaults to the agent type's full <see cref="IAgentToolProvider"/> grant — right
    /// for rooms whose agent types are minimal-read-only anyway (the edit room); a room whose
    /// backing agents carry wider grants (the graphics room's sandbox+render set) overrides this
    /// to restrict in-room turns.
    /// </summary>
    protected virtual IReadOnlyList<AIFunction> GetRoomTurnTools(AgentType agentType) =>
        ToolProvider.GetTools(agentType);

    /// <summary>
    /// Hook invoked before the bounded view is resolved — e.g. to install a prompt-output override
    /// enriching the view (the graphics room's inEdit annotation). Default no-op. Must never throw
    /// for a recoverable reason: a view-enrichment failure should degrade to the plain view.
    /// </summary>
    protected virtual Task PrepareViewAsync(StepExecutionContext context) => Task.CompletedTask;

    // ---------------------------------------------------------------------
    // The template.
    // ---------------------------------------------------------------------

    public async Task<StepExecutionResult> ExecuteAsync(StepExecutionContext context)
    {
        Stopwatch sw = Stopwatch.StartNew();
        WorkflowStep step = context.Step;

        // Every agent tool invoked during the room (each seat's ProjectFileAgentTools calls) and
        // during the solo fallback resolves its WorkflowExecutionContext via this AsyncLocal-backed
        // accessor — confirmed live to be load-bearing (a room run whose scope was missing failed
        // its solo fallback with "No workflow execution context is available for project file
        // tools."). AsyncLocal flows correctly through the awaited
        // InProcessExecution.RunStreamingAsync call chain once this scope is actually opened.
        using IDisposable _ = ExecutionContextAccessor.BeginScope(
            context.Execution.Id,
            context.Execution.ProjectId,
            context.CorrelationId);

        try
        {
            IRoomStepConfig? config;
            try
            {
                string? configJson = GetConfigJson(step);
                if (string.IsNullOrWhiteSpace(configJson))
                    return Failure(context, sw, $"{FailureCodePrefix}_CONFIG_INVALID", $"{RoomKind} step has no {ConfigPropertyName} configured.");

                config = DeserializeConfig(configJson);
            }
            catch (JsonException ex)
            {
                return Failure(context, sw, $"{FailureCodePrefix}_CONFIG_INVALID", $"{ConfigPropertyName} is not valid JSON: {ex.Message}");
            }

            if (config is null)
                return Failure(context, sw, $"{FailureCodePrefix}_CONFIG_INVALID", $"{ConfigPropertyName} deserialized to null.");

            await PrepareViewAsync(context);

            ExtractInputRef viewRef = config.View ?? new ExtractInputRef(ExtractInputSource.Previous);
            (string? viewJson, string? viewError) = ResolveViewJson(context, viewRef);
            if (viewJson is null)
                return Failure(context, sw, "VIEW_UNRESOLVED", viewError ?? "Could not resolve the bounded analysis view input.");

            JsonNode? viewRoot;
            try
            {
                viewRoot = JsonNode.Parse(viewJson);
            }
            catch (JsonException ex)
            {
                return Failure(context, sw, "VIEW_UNRESOLVED", $"View input is not valid JSON: {ex.Message}");
            }

            HashSet<string> offeredIds = ExtractOfferedIds(viewRoot);
            if (offeredIds.Count == 0)
            {
                TDecision? emptyViewDecision = BuildDecisionForEmptyView();
                if (emptyViewDecision is null)
                    return Failure(context, sw, "VIEW_UNRESOLVED", NoOfferedIdsMessage);

                context.RecordResolvedInput(JsonSerializer.Serialize(new
                {
                    view = new { from = viewRef.From.ToString(), stepOrder = viewRef.StepOrder },
                    offeredIdCount = 0,
                    seats = config.EffectiveSeats.Select(s => s.Name).ToArray()
                }));

                return Success(context, sw, config, emptyViewDecision,
                    observedTurnCount: 0, terminationReason: "empty-view",
                    synthesisAttempts: 0, droppedCount: 0, extraRoomMeta: null,
                    degraded: false, degradeReason: null,
                    transcriptArtifactStorageKey: null, chatTranscriptJson: null);
            }

            context.RecordResolvedInput(JsonSerializer.Serialize(new
            {
                view = new { from = viewRef.From.ToString(), stepOrder = viewRef.StepOrder },
                offeredIdCount = offeredIds.Count,
                seats = config.EffectiveSeats.Select(s => s.Name).ToArray()
            }));

            string? degradeReason = null;
            TDecision? decision = null;
            int synthesisAttempts = 0;
            int droppedCount = 0;
            JsonObject? extraRoomMeta = null;
            int observedTurnCount = 0;
            string terminationReason = "room-not-run";
            string? transcriptArtifactStorageKey = null;
            string? chatTranscriptJson = null;

            try
            {
                RoomRunResult roomResult = await RunRoomAsync(context, config, viewJson, offeredIds);
                observedTurnCount = roomResult.Transcript.Count;
                terminationReason = roomResult.TerminationReason;

                // Built unconditionally (unlike the MinIO artifact below, which is gated behind
                // PersistTranscript) — this is DB persistence for the execution detail page, not an
                // opt-in audit artifact, so it must be available even when PersistTranscript is off.
                if (roomResult.Turns.Count > 0)
                {
                    chatTranscriptJson = BuildChatTranscriptJson(roomResult.Turns, offeredIds, config.ClampedMaxTurns);
                }

                if (config.PersistTranscript && roomResult.Transcript.Count > 0)
                {
                    transcriptArtifactStorageKey = await TryPersistTranscriptAsync(
                        context, roomResult.Transcript, step.StepOrder);
                }

                if (roomResult.Transcript.Count == 0)
                {
                    degradeReason = "The room produced zero usable turns.";
                }
                else
                {
                    (decision, synthesisAttempts, string? synthesisError) =
                        await SynthesizeDecisionAsync(context, config, viewJson, roomResult.Transcript, viewRoot);

                    if (decision is null)
                    {
                        degradeReason = synthesisError ?? "Synthesis did not produce a usable decision.";
                    }
                    else
                    {
                        RoomFilterResult filterResult = FilterToOfferedIds(decision, offeredIds, viewRoot);
                        droppedCount = filterResult.Dropped;
                        extraRoomMeta = filterResult.ExtraMeta;
                        degradeReason = RejectFilteredDecision(decision, solo: false);
                    }
                }
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "{RoomKind} step {StepOrder}: room run failed; falling back.", RoomKind, step.StepOrder);
                degradeReason = $"{RoomDisplayName} failed: {ex.Message}";
            }

            bool degraded = degradeReason is not null;

            if (degraded)
            {
                if (!config.FallbackToSolo)
                    return Failure(context, sw, $"{FailureCodePrefix}_FAILED", degradeReason!, transcriptArtifactStorageKey, chatTranscriptJson);

                (decision, RoomFilterResult soloFilter, string? soloError) =
                    await RunSoloFallbackAsync(context, viewJson, offeredIds, viewRoot);
                if (decision is null)
                    return Failure(context, sw, $"{FailureCodePrefix}_FAILED", soloError ?? degradeReason!, transcriptArtifactStorageKey, chatTranscriptJson);

                droppedCount = soloFilter.Dropped;
                extraRoomMeta = soloFilter.ExtraMeta;
            }

            return Success(context, sw, config, decision!,
                observedTurnCount, terminationReason, synthesisAttempts, droppedCount, extraRoomMeta,
                degraded, degradeReason, transcriptArtifactStorageKey, chatTranscriptJson);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "{RoomKind} step {StepOrder} failed unexpectedly", RoomKind, step.StepOrder);
            return Failure(context, sw, "UNEXPECTED_ERROR", $"Unexpected error: {ex.Message}");
        }
    }

    private StepExecutionResult Success(
        StepExecutionContext context, Stopwatch sw, IRoomStepConfig config, TDecision decision,
        int observedTurnCount, string terminationReason, int synthesisAttempts, int droppedCount, JsonObject? extraRoomMeta,
        bool degraded, string? degradeReason, string? transcriptArtifactStorageKey, string? chatTranscriptJson)
    {
        var roomMeta = new JsonObject
        {
            ["seats"] = new JsonArray(config.EffectiveSeats.Select(s => (JsonNode)JsonValue.Create(s.Name)!).ToArray()),
            ["rounds"] = config.Rounds,
            ["maxTurns"] = config.ClampedMaxTurns,
            ["turnCount"] = observedTurnCount,
            ["terminationReason"] = terminationReason,
            ["converged"] = terminationReason == "converged",
            ["synthesisAttempts"] = synthesisAttempts,
            ["droppedSpanCount"] = droppedCount,
            ["degraded"] = degraded,
            ["degradeReason"] = degradeReason,
            ["transcriptArtifactStorageKey"] = transcriptArtifactStorageKey
        };

        if (extraRoomMeta is not null)
        {
            foreach (string key in extraRoomMeta.Select(kv => kv.Key).ToList())
            {
                JsonNode? value = extraRoomMeta[key];
                extraRoomMeta.Remove(key);
                roomMeta[key] = value;
            }
        }

        JsonNode decisionNode = JsonSerializer.SerializeToNode(decision, DecisionJsonOptions)!;
        decisionNode["room"] = roomMeta;

        return new StepExecutionResult
        {
            Output = decisionNode.ToJsonString(EnvelopeJsonOptions),
            NextStepIndex = context.CurrentStepIndex + 1,
            NewIterationCount = context.IterationCount,
            DurationMs = sw.ElapsedMilliseconds,
            TokensUsed = 0,
            Status = StepStatus.Completed,
            ArtifactStorageKey = transcriptArtifactStorageKey,
            ChatTranscriptJson = chatTranscriptJson
        };
    }

    // ---------------------------------------------------------------------
    // The group chat run
    // ---------------------------------------------------------------------

    private sealed record RoomRunResult(
        IReadOnlyList<ChatMessage> Transcript, string TerminationReason, IReadOnlyList<RoomTurnResult> Turns);

    private async Task<RoomRunResult> RunRoomAsync(
        StepExecutionContext context, IRoomStepConfig config, string viewJson, HashSet<string> offeredIds)
    {
        CancellationToken ct = context.CancellationToken;

        List<EditRoomSeat> seats = config.EffectiveSeats.ToList();
        int turnCounter = 0;

        // Running cumulative token tally across every turn observed so far in this room — real
        // mid-run visibility (see RoomSeatAgent.ExtractUsage), never estimated. Stays null
        // (reported as such on WorkflowStepProgress) until at least one turn actually reports usage,
        // since a provider that never reports usage must not be misrepresented as "0 tokens used".
        int cumulativeInputTokens = 0;
        int cumulativeOutputTokens = 0;
        bool anyUsageObserved = false;

        // Captured for EVERY turn (regardless of config.StreamTurns, which only gates the live SSE
        // broadcast) so the DB-persisted ChatTranscriptJson can be built from this — the seat's/
        // director's ACTUAL persona name, not the raw group-chat transcript's ChatMessage.AuthorName,
        // which carries the underlying model agent's name (e.g. "VideoStoryEditor") rather than the
        // seat's display name, since RoomSeatAgent only overrides AuthorName on its fallback
        // path, not on a normal successful turn. Using this list instead of the raw transcript keeps
        // the persisted history attributed exactly the way a live SSE-connected tab already saw it.
        var capturedTurns = new List<RoomTurnResult>();

        async Task OnTurnCompleted(RoomTurnResult result)
        {
            int idx = turnCounter++;
            bool isDirector = string.Equals(result.SeatName, DirectorSeatName, StringComparison.Ordinal);
            string speakerRole = isDirector ? "director" : "editor";
            string label = BuildTurnProgressLabel(isDirector, result.SeatName, idx + 1, config.ClampedMaxTurns);
            int percent = (int)Math.Clamp(Math.Round(100.0 * (idx + 1) / Math.Max(1, config.ClampedMaxTurns)), 0, 100);

            capturedTurns.Add(result);

            if (result.InputTokens.HasValue || result.OutputTokens.HasValue)
            {
                anyUsageObserved = true;
                cumulativeInputTokens += result.InputTokens ?? 0;
                cumulativeOutputTokens += result.OutputTokens ?? 0;
            }

            await context.ReportProgressAsync(
                label,
                percent,
                tokensUsedSoFar: anyUsageObserved ? cumulativeInputTokens + cumulativeOutputTokens : null,
                inputTokensSoFar: anyUsageObserved ? cumulativeInputTokens : null,
                outputTokensSoFar: anyUsageObserved ? cumulativeOutputTokens : null);

            if (config.StreamTurns)
            {
                IReadOnlyList<string> ids = ExtractIdMentions(result.Text, offeredIds);
                await context.ReportChatTurnAsync(idx, config.ClampedMaxTurns, result.SeatName, speakerRole, result.Text, ids);
            }
        }

        List<AIAgent> allParticipants = new(seats.Count + 1);
        foreach (EditRoomSeat seat in seats)
        {
            AIAgent inner = await BuildInnerAgentAsync(SeatAgentType, seat.AgentDefinitionId, ct);
            allParticipants.Add(new RoomSeatAgent(
                inner, seat.Name, BuildSeatDirective(seat), config.Temperature, Math.Max(16, config.MaxTurnTokens),
                config.ReasoningEffort, OnTurnCompleted));
        }

        // The director is registered TWICE, deliberately, as two SEPARATE inner-agent instances —
        // see RoomGroupChatManager's constructor remarks for why one registration is no longer
        // enough under Microsoft.Agents.AI.Workflows 1.22.0 (the group-chat host now refuses to
        // re-invoke the SAME registered participant on two consecutive turns, which the ceiling
        // phase — the director speaks every remaining turn — would otherwise hit the very first
        // time the director spoke twice in a row).
        //
        // Building two INDEPENDENT inner agents here (rather than one inner agent wrapped by two
        // RoomSeatAgent instances) is load-bearing, not cosmetic: AIAgent.Id defaults to a fresh
        // random GUID per instance, but DelegatingAIAgent (what RoomSeatAgent is) forwards IdCore
        // to InnerAgent.Id, so two wrappers around the SAME inner agent would carry the IDENTICAL
        // Id. AgentWorkflowBuilder derives each participant's internal executor-binding id from
        // Name + Id, and both aliases must keep the SAME Name ("Director" — the transcript/sentinel
        // detection depend on it), so a shared inner agent collides and
        // GroupChatWorkflowBuilder.Build() throws "Cannot bind executor with ID '...' because an
        // executor with the same ID but different instance is already bound." (confirmed by
        // decompiling and exercising the real 1.22.0 assembly). Two separate BuildInnerAgentAsync
        // calls give each alias its own random Id while resolving through the identical chat
        // client/instructions/tools — functionally still "the director", just two object instances.
        for (int alias = 0; alias < 2; alias++)
        {
            AIAgent directorInner = await BuildInnerAgentAsync(DirectorAgentType, config.DirectorAgentDefinitionId, ct);
            allParticipants.Add(new RoomSeatAgent(
                directorInner, DirectorSeatName, DirectorTurnDirective, config.DirectorTemperature,
                Math.Max(16, config.MaxTurnTokens), config.ReasoningEffort, OnTurnCompleted));
        }

        HashSet<string> offeredIdsReadOnly = offeredIds;
        RoomGroupChatManager? capturedManager = null;
        Workflow workflow = AgentWorkflowBuilder
            .CreateGroupChatBuilderWith(agents =>
            {
                RoomGroupChatManager manager = CreateManager(agents, config, offeredIdsReadOnly);
                manager.MaximumIterationCount = config.ClampedMaxTurns;
                capturedManager = manager;
                return manager;
            })
            .AddParticipants(allParticipants)
            .Build();

        string openingMessage = BuildOpeningMessage(viewJson);

        using CancellationTokenSource roomCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        roomCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, config.RoomTimeoutSeconds)));
        CancellationToken roomCt = roomCts.Token;

        await context.ReportProgressAsync($"Starting {RoomDisplayName.ToLowerInvariant()}", 0);

        List<ChatMessage>? transcript = null;
        string terminationReason = "ceiling";

        try
        {
            await using StreamingRun run = await InProcessExecution.RunStreamingAsync(
                workflow, openingMessage, context.StepResultId.ToString("D"), roomCt);

            // The opening message is only BUFFERED by GroupChatHost (a ChatProtocolExecutor with
            // AutoSendTurnToken=false, confirmed by decompiling the real rc2 assembly) — a TurnToken
            // must be sent explicitly to actually kick off the first turn-selection cycle. Without
            // this the run produces zero agent turns and never emits a WorkflowOutputEvent.
            await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

            await foreach (WorkflowEvent evt in run.WatchStreamAsync(roomCt))
            {
                if (evt is WorkflowOutputEvent outputEvent && outputEvent.Is<List<ChatMessage>>())
                {
                    transcript = outputEvent.As<List<ChatMessage>>();
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // RoomTimeoutSeconds fired, not the caller's own token — degrade to whatever turns
            // already happened rather than propagating.
            terminationReason = "timeout";
        }

        // RoomGroupChatManager.TerminationReason is set only for an early "sentinel"/"converged"
        // stop; a still-null value here means the room ran to the MaxTurns ceiling instead (the
        // "ceiling" default already assigned above), unless the timeout branch above already
        // overrode it.
        if (terminationReason == "ceiling" && capturedManager?.TerminationReason is { } managerReason)
            terminationReason = managerReason;

        return new RoomRunResult(transcript ?? [], terminationReason, capturedTurns);
    }

    private async Task<AIAgent> BuildInnerAgentAsync(AgentType agentType, Guid? agentDefinitionId, CancellationToken ct)
    {
        IChatClient chatClient = await ChatClients.GetAsync(agentType, agentDefinitionId, ct);
        IReadOnlyList<AIFunction> tools = GetRoomTurnTools(agentType);
        return chatClient.AsAIAgent(
            instructions: RoomCharterPrompt,
            name: agentType.ToString(),
            tools: tools.Cast<AITool>().ToList());
    }

    // ---------------------------------------------------------------------
    // Synthesis — a normal ReelForgeAgentBase-style structured-output call, OUTSIDE the group chat
    // ---------------------------------------------------------------------

    private async Task<(TDecision? Decision, int Attempts, string? Error)> SynthesizeDecisionAsync(
        StepExecutionContext context, IRoomStepConfig config, string viewJson, IReadOnlyList<ChatMessage> transcript,
        JsonNode? viewRoot)
    {
        IReelForgeAgent? director = AgentRegistry.GetByType(DirectorAgentType, config.DirectorAgentDefinitionId);
        if (director is null)
            return (null, 0, $"AgentType.{DirectorAgentType} is not registered.");

        string transcriptText = RenderTranscript(transcript, config.MaxHistoryChars);
        string basePrompt =
            $"The {RoomDisplayName.ToLowerInvariant()} discussion has ended. Here is the bounded analysis view again, followed " +
            "by the full room transcript. Synthesize the room's discussion into the FINAL decision " +
            $"now — {SynthesisSchemaInstruction}\n\n" +
            "## Bounded view\n\n" + viewJson + "\n\n## Room transcript\n\n" + transcriptText;

        int maxAttempts = Math.Max(1, config.MaxSynthesisAttempts);
        string? lastError = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            string prompt = attempt == 1
                ? basePrompt
                : $"{basePrompt}\n\n---\nRetry guidance (attempt {attempt}): {lastError}\nReturn output that strictly matches the expected JSON schema and is valid JSON.";

            AgentRunResult result;
            try
            {
                result = await director.RunAsync(prompt, config.DirectorAgentDefinitionId, context.CancellationToken);
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                continue;
            }

            if (!result.Success)
            {
                lastError = result.FailureReason ?? "The director invoked FailWorkflow.";
                continue;
            }

            string? json = RobustJsonExtractor.ExtractJsonObject(result.Output);
            if (json is null)
            {
                lastError = "No recognizable JSON object in the director's output.";
                continue;
            }

            TDecision? decision;
            try
            {
                decision = JsonSerializer.Deserialize<TDecision>(json, DecisionJsonOptions);
            }
            catch (JsonException ex)
            {
                lastError = ex.Message;
                continue;
            }

            if (decision is null)
            {
                lastError = "Decision deserialized to null.";
                continue;
            }

            NormalizeDecision(decision);
            string? rejection = RejectFreshSynthesis(decision);
            if (rejection is not null)
            {
                lastError = rejection;
                continue;
            }

            string? retryIssue = CheckRetryableIssue(decision, viewRoot);
            if (retryIssue is not null && attempt < maxAttempts)
            {
                lastError = retryIssue;
                continue;
            }

            return (decision, attempt, null);
        }

        return (null, maxAttempts, lastError ?? "Synthesis failed for an unknown reason.");
    }

    private static string RenderTranscript(IReadOnlyList<ChatMessage> transcript, int maxChars)
    {
        var sb = new StringBuilder();
        foreach (ChatMessage m in transcript)
        {
            string speaker = string.IsNullOrEmpty(m.AuthorName) ? m.Role.ToString() : m.AuthorName;
            sb.Append(speaker).Append(": ").Append(m.Text ?? string.Empty).Append('\n');
        }

        string full = sb.ToString();
        if (maxChars <= 0 || full.Length <= maxChars)
            return full;

        // Keep the TAIL (most recent turns) — synthesis cares most about where the room actually
        // ended up, not how it opened.
        return "...[earlier turns omitted]...\n" + full[^Math.Max(0, maxChars - 32)..];
    }

    // ---------------------------------------------------------------------
    // Solo fallback — the pre-room single-agent pipeline, unchanged
    // ---------------------------------------------------------------------

    private async Task<(TDecision? Decision, RoomFilterResult Filter, string? Error)> RunSoloFallbackAsync(
        StepExecutionContext context, string viewJson, HashSet<string> offeredIds, JsonNode? viewRoot)
    {
        IReelForgeAgent? soloAgent = AgentRegistry.GetByType(SoloFallbackAgentType, null);
        if (soloAgent is null)
            return (null, default, $"AgentType.{SoloFallbackAgentType} is not registered for the solo fallback.");

        AgentRunResult result;
        try
        {
            result = await soloAgent.RunAsync(viewJson, null, context.CancellationToken);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (null, default, $"Solo fallback failed: {ex.Message}");
        }

        if (!result.Success)
            return (null, default, result.FailureReason ?? "The solo fallback agent invoked FailWorkflow.");

        string? json = RobustJsonExtractor.ExtractJsonObject(result.Output);
        if (json is null)
            return (null, default, "Solo fallback output had no recognizable JSON object.");

        TDecision? decision;
        try
        {
            decision = JsonSerializer.Deserialize<TDecision>(json, DecisionJsonOptions);
        }
        catch (JsonException ex)
        {
            return (null, default, $"Solo fallback output was not valid JSON: {ex.Message}");
        }

        if (decision is null)
            return (null, default, "Solo fallback output deserialized to null.");

        NormalizeDecision(decision);
        RoomFilterResult filterResult = FilterToOfferedIds(decision, offeredIds, viewRoot);
        string? rejection = RejectFilteredDecision(decision, solo: true);
        if (rejection is not null)
            return (null, filterResult, rejection);

        return (decision, filterResult, null);
    }

    // ---------------------------------------------------------------------
    // View resolution — the same Previous/Step pattern VideoCompileStepExecutor.ResolveDecisionJson
    // established for Decision/GraphicsPlan/MusicPlan.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Resolves an <see cref="ExtractInputRef"/> (<c>Previous</c>/<c>Step</c> only) to the raw JSON
    /// output of that step. Reads through <see cref="StepExecutionContext.OutputForPrompt"/> so a
    /// prompt-output override installed by <see cref="PrepareViewAsync"/> (the graphics room's
    /// inEdit annotation) is honored; for rooms that install none, this is byte-identical to
    /// reading the entry's own output.
    /// </summary>
    private static (string? Json, string? Error) ResolveViewJson(StepExecutionContext context, ExtractInputRef viewRef)
    {
        StepOutputHistoryEntry? entry = viewRef.From switch
        {
            ExtractInputSource.Previous => context.StepOutputHistory
                .LastOrDefault(h => !string.IsNullOrWhiteSpace(h.Output)),
            ExtractInputSource.Step => viewRef.StepOrder.HasValue
                ? context.StepOutputHistory.LastOrDefault(h => h.StepOrder == viewRef.StepOrder.Value)
                : null,
            _ => null
        };

        string? content = entry is null ? null : context.OutputForPrompt(entry);

        if (string.IsNullOrWhiteSpace(content))
            return (null, "View input resolved to empty content.");

        string? extracted = RobustJsonExtractor.ExtractJsonObject(content);
        return extracted is null ? (null, "View input did not contain a recognizable JSON object.") : (extracted, null);
    }

    // ---------------------------------------------------------------------
    // Transcript artifact — NON-AUTHORITATIVE. Free-form turn text may contain a model-invented
    // timestamp as harmless prose; nothing must ever parse it back into a decision. See
    // docs/video-editing.md "The edit room".
    // ---------------------------------------------------------------------

    private async Task<string?> TryPersistTranscriptAsync(
        StepExecutionContext context, IReadOnlyList<ChatMessage> transcript, int stepOrder)
    {
        try
        {
            var turns = transcript.Select(m => new
            {
                speaker = string.IsNullOrEmpty(m.AuthorName) ? m.Role.ToString() : m.AuthorName,
                role = string.Equals(m.AuthorName, DirectorSeatName, StringComparison.Ordinal) ? "director" : "editor",
                text = m.Text ?? string.Empty
            }).ToList();

            string json = JsonSerializer.Serialize(new
            {
                nonAuthoritative = true,
                note = "Free-form room discussion, for audit/UI display only. Never parse this back into a decision — the synthesized, deterministically-validated decision on this step's output_json is the only authoritative result.",
                turns
            }, EnvelopeJsonOptions);

            string tempPath = Path.Combine(Path.GetTempPath(), $"room-transcript-{Guid.NewGuid():N}.json");
            await File.WriteAllTextAsync(tempPath, json, context.CancellationToken);
            try
            {
                string fileName = $"video-analysis/{context.Execution.Id:D}/step-{stepOrder}-room-transcript.json";
                return await Workspace.UploadArtifactAsync(
                    context.Execution.ProjectId, tempPath, fileName, "application/json", context.CancellationToken);
            }
            finally
            {
                try { File.Delete(tempPath); } catch { /* best-effort scratch cleanup */ }
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "{RoomKind} step {StepOrder}: persisting transcript artifact failed; continuing without it.", RoomKind, stepOrder);
            return null;
        }
    }

    private StepExecutionResult Failure(
        StepExecutionContext context, Stopwatch sw, string code, string message,
        string? artifactStorageKey = null, string? chatTranscriptJson = null)
    {
        Logger.LogWarning("{RoomKind} step {StepOrder} failed: [{Code}] {Message}", RoomKind, context.Step.StepOrder, code, message);

        var envelope = new JsonObject
        {
            ["status"] = "failed",
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
        };

        return new StepExecutionResult
        {
            Output = envelope.ToJsonString(EnvelopeJsonOptions),
            NextStepIndex = context.CurrentStepIndex + 1,
            NewIterationCount = context.IterationCount,
            DurationMs = sw.ElapsedMilliseconds,
            TokensUsed = 0,
            Status = StepStatus.Failed,
            ErrorDetails = message,
            ArtifactStorageKey = artifactStorageKey,
            ChatTranscriptJson = chatTranscriptJson
        };
    }

    /// <summary>
    /// Builds the DB-persisted transcript array for <see cref="StepExecutionResult.ChatTranscriptJson"/>
    /// — per-turn shape mirrors <see cref="Shared.IntegrationEvents.WorkflowStepChatTurn"/> (turnIndex,
    /// speaker, speakerRole, text, truncated, idsMentioned, totalTurns) but always carries the FULL
    /// untruncated turn text, since this is DB persistence rather than the ~600-char-truncated SSE
    /// broadcast <see cref="WorkflowEventPublisher.ClipChatTurn"/> produces — so <c>truncated</c> is
    /// always <c>false</c> here. Built from the SAME <see cref="RoomTurnResult"/> list the live
    /// <c>WorkflowStepChatTurn</c> SSE events are reported from (<c>RunRoomAsync</c>'s captured
    /// turns), NOT the raw group-chat <c>ChatMessage</c> transcript — that transcript's
    /// <c>AuthorName</c> carries the underlying model agent's name (e.g. "VideoStoryEditor"), not the
    /// seat's persona display name, on a normal successful turn. Using the same source as the live
    /// events keeps the persisted history attributed exactly the way a live SSE-connected tab saw it.
    /// The <c>speakerRole</c> values stay the room-generic pair "editor"/"director" for every room —
    /// they are a seat/moderator dichotomy the UI styles by, not a job title. Returns <c>null</c>
    /// for zero turns.
    /// </summary>
    private string? BuildChatTranscriptJson(
        IReadOnlyList<RoomTurnResult> turns, HashSet<string> offeredIds, int totalTurns)
    {
        if (turns.Count == 0)
            return null;

        var persisted = turns.Select((t, index) => new
        {
            turnIndex = index,
            totalTurns,
            speaker = t.SeatName,
            speakerRole = string.Equals(t.SeatName, DirectorSeatName, StringComparison.Ordinal) ? "director" : "editor",
            text = t.Text,
            truncated = false,
            idsMentioned = ExtractIdMentions(t.Text, offeredIds)
        }).ToList();

        return JsonSerializer.Serialize(persisted, EnvelopeJsonOptions);
    }
}
