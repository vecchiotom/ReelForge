using System.Linq;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace ReelForge.WorkflowEngine.Agents.Rooms;

/// <summary>
/// Reported to the turn-completed callback once a seat's (or the director's) turn finishes.
/// <paramref name="InputTokens"/>/<paramref name="OutputTokens"/> are null whenever the underlying
/// chat client didn't report usage for that turn (a provider that omits it, or the fallback/error
/// path) — never a guessed/estimated value.
/// </summary>
public sealed record RoomTurnResult(
    string SeatName, string Text, bool IsError, TimeSpan Duration,
    int? InputTokens = null, int? OutputTokens = null);

/// <summary>
/// Wraps one already-constructed inner <see cref="AIAgent"/> (a seat's or the room-participant
/// director's chat-client-backed agent) so it can participate in a room's group chat with:
/// (1) room-specific sampling options injected on every turn — the group chat host always passes
/// <c>options == null</c> to a participant, so this is the only way to control temperature/
/// reasoning-effort/max-tokens per turn; (2) the seat's persona appended as the LAST message in the
/// turn (after the shared room charter/view, which is identical across every seat) so every seat's
/// identical prefix keeps hitting the backend's prompt-prefix cache — do not reorder this; (3) a
/// display name override so the transcript reads with the seat's persona name, not the underlying
/// agent's; (4) containment — a single seat's turn throwing (a provider hiccup) must never abort
/// the whole room.
///
/// <para>
/// Fully room-agnostic: nothing here knows which room (edit, graphics, ...) the seat belongs to —
/// the persona directive, temperatures, and callback all arrive from the room's step executor.
/// Formerly named <c>EditRoomSeatAgent</c>; renamed unchanged when the second room was built.
/// </para>
///
/// <para>
/// Overrides BOTH <see cref="RunCoreAsync"/> and <see cref="RunCoreStreamingAsync"/> deliberately:
/// the group chat host invokes participants through the STREAMING path
/// (<c>AIAgent.RunStreamingAsync</c> → <c>RunCoreStreamingAsync</c>), so a wrapper that only
/// overrides the non-streaming path is silently bypassed — measured live against the real rc2
/// package.
/// </para>
/// </summary>
public sealed class RoomSeatAgent : DelegatingAIAgent
{
    private readonly string _seatName;
    private readonly string _turnDirective;
    private readonly float _temperature;
    private readonly float? _topP;
    private readonly int _maxOutputTokens;
    private readonly string? _reasoningEffort;
    private readonly Func<RoomTurnResult, Task> _onTurnCompleted;

    public RoomSeatAgent(
        AIAgent innerAgent,
        string seatName,
        string turnDirective,
        float temperature,
        int maxOutputTokens,
        string? reasoningEffort,
        Func<RoomTurnResult, Task> onTurnCompleted,
        float? topP = null)
        : base(innerAgent)
    {
        _seatName = seatName;
        _turnDirective = turnDirective;
        _temperature = temperature;
        _topP = topP;
        _maxOutputTokens = maxOutputTokens;
        _reasoningEffort = reasoningEffort;
        _onTurnCompleted = onTurnCompleted;
    }

    /// <summary>The transcript's <c>AuthorName</c> for this seat's turns — e.g. "PacingEditor", not the underlying "VideoStoryEditor" agent name.</summary>
    public override string Name => _seatName;

    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        CancellationToken cancellationToken)
    {
        DateTime startedAt = DateTime.UtcNow;
        List<ChatMessage> turnMessages = AppendTurnDirective(messages);
        ChatClientAgentRunOptions runOptions = BuildRunOptions();

        try
        {
            AgentResponse response = await InnerAgent.RunAsync(turnMessages, session, runOptions, cancellationToken);
            string text = response.Text ?? string.Empty;
            (int? inputTokens, int? outputTokens) = ExtractUsage(response.Usage);
            await ReportAsync(new RoomTurnResult(_seatName, text, IsError: false, DateTime.UtcNow - startedAt, inputTokens, outputTokens));
            return response;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return await FallbackAsync(startedAt);
        }
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        DateTime startedAt = DateTime.UtcNow;
        List<ChatMessage> turnMessages = AppendTurnDirective(messages);
        ChatClientAgentRunOptions runOptions = BuildRunOptions();

        IAsyncEnumerator<AgentResponseUpdate>? enumerator = null;
        var accumulated = new System.Text.StringBuilder();
        bool failed = false;
        UsageDetails? usage = null;

        try
        {
            enumerator = InnerAgent.RunStreamingAsync(turnMessages, session, runOptions, cancellationToken).GetAsyncEnumerator(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            failed = true;
        }

        if (!failed && enumerator is not null)
        {
            // try/finally (not a bare DisposeAsync after the loop) so the inner enumerator is
            // also disposed when the CONSUMER abandons this stream early — the compiler-generated
            // async-iterator DisposeAsync resumes here and runs this finally block.
            try
            {
                while (true)
                {
                    AgentResponseUpdate? update = null;
                    try
                    {
                        if (!await enumerator.MoveNextAsync())
                            break;
                        update = enumerator.Current;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception)
                    {
                        failed = true;
                    }

                    if (failed || update is null)
                        break;

                    if (!string.IsNullOrEmpty(update.Text))
                        accumulated.Append(update.Text);

                    // A streaming provider surfaces usage as a UsageContent item, typically on the
                    // final update — the exact convention Microsoft.Agents.AI's own
                    // AgentResponse.ToAgentResponseUpdates() uses in reverse (usage -> a synthesized
                    // UsageContent update). Keep the LAST one seen in case a provider emits more than
                    // one running total across updates.
                    UsageContent? usageContent = update.Contents?.OfType<UsageContent>().LastOrDefault();
                    if (usageContent is not null)
                        usage = usageContent.Details;

                    yield return update;
                }
            }
            finally
            {
                await enumerator.DisposeAsync();
            }
        }

        if (failed)
        {
            RoomTurnResult fallback = BuildFallbackResult(startedAt);
            await ReportAsync(fallback);
            yield return new AgentResponseUpdate(ChatRole.Assistant, fallback.Text) { AuthorName = _seatName };
            yield break;
        }

        (int? inputTokens, int? outputTokens) = ExtractUsage(usage);
        await ReportAsync(new RoomTurnResult(_seatName, accumulated.ToString(), IsError: false, DateTime.UtcNow - startedAt, inputTokens, outputTokens));
    }

    /// <summary>
    /// Same extraction pattern <c>ReelForgeAgentBase</c> already applies to a solo agent call's
    /// <c>ChatResponse.Usage</c> — <c>null</c> propagates as "no usage reported", never coerced to 0.
    /// </summary>
    private static (int? InputTokens, int? OutputTokens) ExtractUsage(UsageDetails? usage) =>
        usage is null
            ? (null, null)
            : ((int?)(usage.InputTokenCount ?? 0), (int?)(usage.OutputTokenCount ?? 0));

    /// <summary>
    /// Appends the seat's persona directive as the LAST message — after the shared room
    /// charter/bounded-view messages every seat receives identically — so the identical
    /// [system][shared analysis view] prefix keeps hitting the backend's prompt-prefix cache.
    /// Measured live: putting per-seat identity earlier defeats that cache (~2-3x latency cost).
    /// Do not reorder.
    /// </summary>
    private List<ChatMessage> AppendTurnDirective(IEnumerable<ChatMessage> messages)
    {
        List<ChatMessage> list = messages.ToList();
        list.Add(new ChatMessage(ChatRole.User, _turnDirective));
        return list;
    }

    private ChatClientAgentRunOptions BuildRunOptions()
    {
        ChatOptions chatOptions = new()
        {
            Temperature = _temperature,
            TopP = _topP,
            MaxOutputTokens = _maxOutputTokens
        };

        if (!string.IsNullOrEmpty(_reasoningEffort))
        {
            // Same OPENAI001 RawRepresentationFactory mechanism ReelForgeAgentBase.BuildChatOptions
            // already uses to reach the wire-level `reasoning_effort` field — duplicated here
            // (rather than extracted) since that method is private and this wrapper's injection
            // point (a per-turn options object, not a per-agent constructor-time one) doesn't share
            // enough shape with it to be worth a broader refactor.
            string effort = _reasoningEffort;
            chatOptions.RawRepresentationFactory = _ =>
            {
#pragma warning disable OPENAI001
                return new OpenAI.Chat.ChatCompletionOptions { ReasoningEffortLevel = effort };
#pragma warning restore OPENAI001
            };
        }

        return new ChatClientAgentRunOptions(chatOptions);
    }

    private async Task<AgentResponse> FallbackAsync(DateTime startedAt)
    {
        RoomTurnResult fallback = BuildFallbackResult(startedAt);
        await ReportAsync(fallback);
        return new AgentResponse(new ChatMessage(ChatRole.Assistant, fallback.Text) { AuthorName = _seatName });
    }

    private RoomTurnResult BuildFallbackResult(DateTime startedAt) =>
        new(_seatName, $"[{_seatName} had no input this round]", IsError: true, DateTime.UtcNow - startedAt);

    private async Task ReportAsync(RoomTurnResult result)
    {
        try
        {
            await _onTurnCompleted(result);
        }
        catch
        {
            // The turn-completed callback drives progress/transcript reporting only — it must
            // never be able to fail (or mask the outcome of) the seat's actual turn.
        }
    }
}
