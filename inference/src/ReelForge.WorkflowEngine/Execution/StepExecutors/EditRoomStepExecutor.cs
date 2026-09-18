using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Data.OutputSchemas;
using ReelForge.Shared.Inference;
using ReelForge.Shared.Workflows;
using ReelForge.WorkflowEngine.Agents;
using ReelForge.WorkflowEngine.Agents.EditRoom;
using ReelForge.WorkflowEngine.Agents.Tools;
using ReelForge.WorkflowEngine.Services.Storage;

namespace ReelForge.WorkflowEngine.Execution.StepExecutors;

/// <summary>
/// Executes <see cref="StepType.EditRoom"/> steps: several <see cref="AgentType.VideoStoryEditor"/>-
/// role seats plus an <see cref="AgentType.VideoEditDirector"/> moderator converse in a live
/// <c>Microsoft.Agents.AI.Workflows</c> group chat over a bounded <c>VideoAnalyze</c> view, then the
/// director emits ONE schema-validated <see cref="VideoEditDecisionOutput"/> in a normal structured
/// call OUTSIDE the chat loop. <see cref="StepExecutors.VideoCompileStepExecutor"/> needs ZERO
/// changes — it already consumes <see cref="VideoEditDecisionOutput"/> from whichever step a
/// workflow's <c>Decision.StepOrder</c> points at. See docs/video-editing.md "The edit room".
///
/// <para>
/// Same never-throws, always-valid-JSON discipline as <see cref="VideoAnalyzeStepExecutor"/>/
/// <see cref="VideoCompileStepExecutor"/> — every failure mode degrades to the solo-editor fallback
/// (<see cref="EditRoomStepConfig.FallbackToSoloEditor"/>) or, as an absolute last resort, a clean
/// structured failure result.
/// </para>
/// </summary>
public class EditRoomStepExecutor : IStepExecutor
{
    private static readonly JsonSerializerOptions ConfigJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    // Mirrors VideoCompileStepExecutor.DecisionJsonOptions exactly: no UnmappedMemberHandling
    // override (default Skip), which is why the additive "room" sibling object on output_json is
    // safe to include alongside keep/editRationale/suggestedTitle — VideoCompileStepExecutor's own
    // deserialization of this same type simply ignores the extra key.
    private static readonly JsonSerializerOptions DecisionJsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly JsonSerializerOptions EnvelopeJsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    /// <summary>The room-participant display name for the director — matches what its EditRoomSeatAgent wrapper reports via Name.</summary>
    private const string DirectorSeatName = "Director";

    // Shared, identical instructions for EVERY room participant (seats AND the director's
    // room-participant instance) — the room's rules/context, never the persona (that is injected
    // per-turn by EditRoomSeatAgent as the LAST message) and never the bounded view data itself
    // (that arrives as the group chat's opening message, shared identically by every turn's
    // history). Deliberately does NOT carry a "your output must be valid JSON" contract — unlike
    // ReelForgeAgentBase's auto-appended one for AgentType.VideoStoryEditor/VideoEditDirector's
    // OWN structured-output calls, room turns are always free-form prose.
    private const string RoomCharterPrompt =
        """
        You are participating in a live "edit room" discussion between several video editors and a
        director, deciding which spans of a source video to KEEP. The bounded view of the video's
        shots, silence gaps, and (when available) transcript segments — each with a short opaque id
        such as "s2", "g3", or "t7" — was given to you as the first message in this conversation.
        Every participant in this room sees the exact same view.

        ## Rules — hard constraints, not suggestions

        - Reference ONLY ids that appear in the view you were given. Never invent an id, never
          guess one.
        - NEVER mention, estimate, or output a timestamp, duration, frame number, or any other
          numeric time value, in this discussion. A separate deterministic step resolves chosen ids
          to exact times — your job here is only discussing which ids to keep, qualitatively.
        - You cannot create, request, or describe a transition, fade, dissolve, or effect of any
          kind — a separate deterministic step decides those from measurements of the footage.
        - Keep every turn SHORT — a few sentences of prose, not an essay. This is a live discussion,
          not a final report.
        - Do NOT output JSON, markdown code fences, or any structured format in this discussion —
          plain conversational prose only. A separate call, made after this discussion ends,
          produces the final structured decision.
        """;

    private readonly IAgentChatClientProvider _chatClients;
    private readonly IAgentToolProvider _toolProvider;
    private readonly IAgentRegistry _agentRegistry;
    private readonly IProjectFileWorkspace _workspace;
    private readonly ILogger<EditRoomStepExecutor> _logger;

    public EditRoomStepExecutor(
        IAgentChatClientProvider chatClients,
        IAgentToolProvider toolProvider,
        IAgentRegistry agentRegistry,
        IProjectFileWorkspace workspace,
        ILogger<EditRoomStepExecutor> logger)
    {
        _chatClients = chatClients;
        _toolProvider = toolProvider;
        _agentRegistry = agentRegistry;
        _workspace = workspace;
        _logger = logger;
    }

    public StepType StepType => StepType.EditRoom;

    public async Task<StepExecutionResult> ExecuteAsync(StepExecutionContext context)
    {
        Stopwatch sw = Stopwatch.StartNew();
        WorkflowStep step = context.Step;

        try
        {
            EditRoomStepConfig? config;
            try
            {
                if (string.IsNullOrWhiteSpace(step.EditRoomConfigJson))
                    return Failure(context, sw, "EDIT_ROOM_CONFIG_INVALID", "EditRoom step has no EditRoomConfigJson configured.");

                config = JsonSerializer.Deserialize<EditRoomStepConfig>(step.EditRoomConfigJson, ConfigJsonOptions);
            }
            catch (JsonException ex)
            {
                return Failure(context, sw, "EDIT_ROOM_CONFIG_INVALID", $"EditRoomConfigJson is not valid JSON: {ex.Message}");
            }

            if (config is null)
                return Failure(context, sw, "EDIT_ROOM_CONFIG_INVALID", "EditRoomConfigJson deserialized to null.");

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
                return Failure(context, sw, "VIEW_UNRESOLVED", "The resolved view offers no shot/silence/segment ids to decide over.");

            context.RecordResolvedInput(JsonSerializer.Serialize(new
            {
                view = new { from = viewRef.From.ToString(), stepOrder = viewRef.StepOrder },
                offeredIdCount = offeredIds.Count,
                seats = config.EffectiveSeats.Select(s => s.Name).ToArray()
            }));

            string? degradeReason = null;
            VideoEditDecisionOutput? decision = null;
            int synthesisAttempts = 0;
            int droppedSpanCount = 0;
            int observedTurnCount = 0;
            string terminationReason = "room-not-run";
            string? transcriptArtifactStorageKey = null;

            try
            {
                RoomRunResult roomResult = await RunRoomAsync(context, config, viewJson, offeredIds, sw, step.StepOrder);
                observedTurnCount = roomResult.Transcript.Count;
                terminationReason = roomResult.TerminationReason;

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
                        await SynthesizeDecisionAsync(context, config, viewJson, roomResult.Transcript);

                    if (decision is null)
                    {
                        degradeReason = synthesisError ?? "Synthesis did not produce a usable decision.";
                    }
                    else
                    {
                        droppedSpanCount = FilterToOfferedIds(decision, offeredIds);
                        if (decision.Keep.Count == 0)
                            degradeReason = "The synthesized decision had no Keep spans referencing offered ids.";
                    }
                }
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "EditRoom step {StepOrder}: room run failed; falling back.", step.StepOrder);
                degradeReason = $"Edit room failed: {ex.Message}";
            }

            bool degraded = degradeReason is not null;

            if (degraded)
            {
                if (!config.FallbackToSoloEditor)
                    return Failure(context, sw, "EDIT_ROOM_FAILED", degradeReason!, transcriptArtifactStorageKey);

                (decision, int soloDropped, string? soloError) = await RunSoloFallbackAsync(context, viewJson, offeredIds);
                if (decision is null)
                    return Failure(context, sw, "EDIT_ROOM_FAILED", soloError ?? degradeReason!, transcriptArtifactStorageKey);

                droppedSpanCount = soloDropped;
            }

            var roomMeta = new JsonObject
            {
                ["seats"] = new JsonArray(config.EffectiveSeats.Select(s => (JsonNode)JsonValue.Create(s.Name)!).ToArray()),
                ["rounds"] = config.Rounds,
                ["maxTurns"] = config.ClampedMaxTurns,
                ["turnCount"] = observedTurnCount,
                ["terminationReason"] = terminationReason,
                ["converged"] = terminationReason == "converged",
                ["synthesisAttempts"] = synthesisAttempts,
                ["droppedSpanCount"] = droppedSpanCount,
                ["degraded"] = degraded,
                ["degradeReason"] = degradeReason,
                ["transcriptArtifactStorageKey"] = transcriptArtifactStorageKey
            };

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
                ArtifactStorageKey = transcriptArtifactStorageKey
            };
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EditRoom step {StepOrder} failed unexpectedly", step.StepOrder);
            return Failure(context, sw, "UNEXPECTED_ERROR", $"Unexpected error: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------------
    // The group chat run
    // ---------------------------------------------------------------------

    private sealed record RoomRunResult(IReadOnlyList<ChatMessage> Transcript, string TerminationReason);

    private async Task<RoomRunResult> RunRoomAsync(
        StepExecutionContext context, EditRoomStepConfig config, string viewJson, HashSet<string> offeredIds,
        Stopwatch sw, int stepOrder)
    {
        CancellationToken ct = context.CancellationToken;

        List<EditRoomSeat> seats = config.EffectiveSeats.ToList();
        int turnCounter = 0;

        async Task OnTurnCompleted(EditRoomTurnResult result)
        {
            int idx = turnCounter++;
            bool isDirector = string.Equals(result.SeatName, DirectorSeatName, StringComparison.Ordinal);
            string speakerRole = isDirector ? "director" : "editor";
            string label = isDirector
                ? "Director is reviewing"
                : $"{result.SeatName} is proposing a cut (turn {idx + 1}/{config.ClampedMaxTurns})";
            int percent = (int)Math.Clamp(Math.Round(100.0 * (idx + 1) / Math.Max(1, config.ClampedMaxTurns)), 0, 100);

            await context.ReportProgressAsync(label, percent);

            if (config.StreamTurns)
            {
                IReadOnlyList<string> ids = EditRoomGroupChatManager.ExtractOfferedIdMentions(result.Text, offeredIds);
                await context.ReportChatTurnAsync(idx, config.ClampedMaxTurns, result.SeatName, speakerRole, result.Text, ids);
            }
        }

        List<AIAgent> allParticipants = new(seats.Count + 1);
        foreach (EditRoomSeat seat in seats)
        {
            AIAgent inner = await BuildInnerAgentAsync(AgentType.VideoStoryEditor, seat.AgentDefinitionId, ct);
            string directive = $"You are now speaking as {seat.Name}, the {seat.Persona} editor in this room.";
            allParticipants.Add(new EditRoomSeatAgent(
                inner, seat.Name, directive, config.Temperature, Math.Max(16, config.MaxTurnTokens),
                config.ReasoningEffort, OnTurnCompleted));
        }

        AIAgent directorInner = await BuildInnerAgentAsync(AgentType.VideoEditDirector, config.DirectorAgentDefinitionId, ct);
        const string directorDirective =
            "You are now speaking as Director, moderating this edit room. Review the discussion " +
            "so far. If the room has converged on a good, coherent cut, end this turn with the " +
            "literal token ROOM_DECIDED followed by a one- or two-sentence summary of what was " +
            "agreed. Otherwise, give brief guidance to help the editors converge. Keep this turn short.";
        allParticipants.Add(new EditRoomSeatAgent(
            directorInner, DirectorSeatName, directorDirective, config.DirectorTemperature,
            Math.Max(16, config.MaxTurnTokens), config.ReasoningEffort, OnTurnCompleted));

        HashSet<string> offeredIdsReadOnly = offeredIds;
        EditRoomGroupChatManager? capturedManager = null;
        Workflow workflow = AgentWorkflowBuilder
            .CreateGroupChatBuilderWith(agents =>
            {
                var manager = new EditRoomGroupChatManager(agents, config, offeredIdsReadOnly, DirectorSeatName)
                {
                    MaximumIterationCount = config.ClampedMaxTurns
                };
                capturedManager = manager;
                return manager;
            })
            .AddParticipants(allParticipants)
            .Build();

        string openingMessage = BuildOpeningMessage(viewJson);

        using CancellationTokenSource roomCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        roomCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, config.RoomTimeoutSeconds)));
        CancellationToken roomCt = roomCts.Token;

        await context.ReportProgressAsync("Starting edit room", 0);

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

        // EditRoomGroupChatManager.TerminationReason is set only for an early "sentinel"/"converged"
        // stop; a still-null value here means the room ran to the MaxTurns ceiling instead (the
        // "ceiling" default already assigned above), unless the timeout branch above already
        // overrode it.
        if (terminationReason == "ceiling" && capturedManager?.TerminationReason is { } managerReason)
            terminationReason = managerReason;

        return new RoomRunResult(transcript ?? [], terminationReason);
    }

    private async Task<AIAgent> BuildInnerAgentAsync(AgentType agentType, Guid? agentDefinitionId, CancellationToken ct)
    {
        IChatClient chatClient = await _chatClients.GetAsync(agentType, agentDefinitionId, ct);
        IReadOnlyList<Microsoft.Extensions.AI.AIFunction> tools = _toolProvider.GetTools(agentType);
        return chatClient.AsAIAgent(
            instructions: RoomCharterPrompt,
            name: agentType.ToString(),
            tools: tools.Cast<AITool>().ToList());
    }

    private static string BuildOpeningMessage(string viewJson) =>
        "Bounded analysis view for this edit room (every participant sees this same view):\n\n" + viewJson;

    // ---------------------------------------------------------------------
    // Synthesis — a normal ReelForgeAgentBase-style structured-output call, OUTSIDE the group chat
    // ---------------------------------------------------------------------

    private async Task<(VideoEditDecisionOutput? Decision, int Attempts, string? Error)> SynthesizeDecisionAsync(
        StepExecutionContext context, EditRoomStepConfig config, string viewJson, IReadOnlyList<ChatMessage> transcript)
    {
        IReelForgeAgent? director = _agentRegistry.GetByType(AgentType.VideoEditDirector, config.DirectorAgentDefinitionId);
        if (director is null)
            return (null, 0, "AgentType.VideoEditDirector is not registered.");

        string transcriptText = RenderTranscript(transcript, config.MaxHistoryChars);
        string basePrompt =
            "The edit room discussion has ended. Here is the bounded analysis view again, followed " +
            "by the full room transcript. Synthesize the room's discussion into the FINAL decision " +
            "now — output ONLY valid JSON matching the VideoEditDecisionOutput schema.\n\n" +
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

            VideoEditDecisionOutput? decision;
            try
            {
                decision = JsonSerializer.Deserialize<VideoEditDecisionOutput>(json, DecisionJsonOptions);
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

            decision.Keep ??= [];
            if (decision.Keep.Count == 0)
            {
                lastError = "Decision had an empty Keep list.";
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
    // Solo fallback — today's existing single-editor pipeline, unchanged
    // ---------------------------------------------------------------------

    private async Task<(VideoEditDecisionOutput? Decision, int DroppedSpanCount, string? Error)> RunSoloFallbackAsync(
        StepExecutionContext context, string viewJson, HashSet<string> offeredIds)
    {
        IReelForgeAgent? soloEditor = _agentRegistry.GetByType(AgentType.VideoStoryEditor, null);
        if (soloEditor is null)
            return (null, 0, "AgentType.VideoStoryEditor is not registered for the solo fallback.");

        AgentRunResult result;
        try
        {
            result = await soloEditor.RunAsync(viewJson, null, context.CancellationToken);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (null, 0, $"Solo fallback failed: {ex.Message}");
        }

        if (!result.Success)
            return (null, 0, result.FailureReason ?? "The solo fallback editor invoked FailWorkflow.");

        string? json = RobustJsonExtractor.ExtractJsonObject(result.Output);
        if (json is null)
            return (null, 0, "Solo fallback output had no recognizable JSON object.");

        VideoEditDecisionOutput? decision;
        try
        {
            decision = JsonSerializer.Deserialize<VideoEditDecisionOutput>(json, DecisionJsonOptions);
        }
        catch (JsonException ex)
        {
            return (null, 0, $"Solo fallback output was not valid JSON: {ex.Message}");
        }

        if (decision is null)
            return (null, 0, "Solo fallback output deserialized to null.");

        decision.Keep ??= [];
        int dropped = FilterToOfferedIds(decision, offeredIds);
        if (decision.Keep.Count == 0)
            return (null, dropped, "Solo fallback produced no Keep spans referencing offered ids.");

        return (decision, dropped, null);
    }

    // ---------------------------------------------------------------------
    // Deterministic validation — never trust the model, same discipline VideoCompileStepExecutor
    // already applies to VideoStoryEditor's output.
    // ---------------------------------------------------------------------

    /// <summary>Drops any Keep span whose FromId/ToId isn't in <paramref name="offeredIds"/>. Returns how many spans were dropped.</summary>
    private static int FilterToOfferedIds(VideoEditDecisionOutput decision, HashSet<string> offeredIds)
    {
        decision.Keep ??= [];
        int before = decision.Keep.Count;
        decision.Keep = decision.Keep
            .Where(k => offeredIds.Contains(k.FromId) && offeredIds.Contains(k.ToId))
            .ToList();
        return before - decision.Keep.Count;
    }

    private static HashSet<string> ExtractOfferedIds(JsonNode? root)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        JsonNode? view = root?["view"];
        if (view is null)
            return ids;

        CollectIds(view["shots"], ids);
        CollectIds(view["silences"], ids);
        CollectIds(view["segments"], ids);
        return ids;
    }

    private static void CollectIds(JsonNode? array, HashSet<string> ids)
    {
        if (array is not JsonArray arr)
            return;

        foreach (JsonNode? item in arr)
        {
            string? id = item?["id"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(id))
                ids.Add(id);
        }
    }

    /// <summary>
    /// Resolves an <see cref="ExtractInputRef"/> (<c>Previous</c>/<c>Step</c> only) to the raw JSON
    /// output of that step — the same pattern <c>VideoCompileStepExecutor.ResolveDecisionJson</c>
    /// already established for <c>Decision</c>/<c>GraphicsPlan</c>/<c>MusicPlan</c>, duplicated here
    /// rather than shared since that method is private to its own class.
    /// </summary>
    private static (string? Json, string? Error) ResolveViewJson(StepExecutionContext context, ExtractInputRef viewRef)
    {
        string? content = viewRef.From switch
        {
            ExtractInputSource.Previous => context.StepOutputHistory
                .LastOrDefault(h => !string.IsNullOrWhiteSpace(h.Output))?.Output,
            ExtractInputSource.Step => viewRef.StepOrder.HasValue
                ? context.StepOutputHistory.LastOrDefault(h => h.StepOrder == viewRef.StepOrder.Value)?.Output
                : null,
            _ => null
        };

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
                note = "Free-form room discussion, for audit/UI display only. Never parse this back into a decision — the synthesized VideoEditDecisionOutput on this step's output_json is the only authoritative result.",
                turns
            }, EnvelopeJsonOptions);

            string tempPath = Path.Combine(Path.GetTempPath(), $"edit-room-transcript-{Guid.NewGuid():N}.json");
            await File.WriteAllTextAsync(tempPath, json, context.CancellationToken);
            try
            {
                string fileName = $"video-analysis/{context.Execution.Id:D}/step-{stepOrder}-room-transcript.json";
                return await _workspace.UploadArtifactAsync(
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
            _logger.LogWarning(ex, "EditRoom step {StepOrder}: persisting transcript artifact failed; continuing without it.", stepOrder);
            return null;
        }
    }

    private StepExecutionResult Failure(
        StepExecutionContext context, Stopwatch sw, string code, string message, string? artifactStorageKey = null)
    {
        _logger.LogWarning("EditRoom step {StepOrder} failed: [{Code}] {Message}", context.Step.StepOrder, code, message);

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
            ArtifactStorageKey = artifactStorageKey
        };
    }
}
