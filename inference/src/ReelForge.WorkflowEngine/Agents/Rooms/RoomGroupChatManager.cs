using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using ReelForge.Shared.Workflows;

namespace ReelForge.WorkflowEngine.Agents.Rooms;

/// <summary>
/// Deterministic scheduler/terminator shared by every multi-agent "room" step type's
/// <c>Microsoft.Agents.AI.Workflows</c> group chat. Not LLM-driven — every seat sees the exact
/// same bounded view and can speak to any part of it, so an intelligent "who should speak next"
/// decision would only double a room's LLM-call cost for no benefit. A concrete room supplies
/// only its offered-id vocabulary (the regex that recognizes its own id namespace in free-form
/// turn text — <c>[sgt]\d+</c> for the edit room, <c>p\d+</c> for the graphics room) via a thin
/// subclass; the round-robin schedule, sentinel detection, convergence detection, turn
/// observation, and ceiling handling below are identical for every room. See
/// docs/video-editing.md "The edit room" / "The graphics room".
///
/// <para>
/// Fully unit-testable with zero network calls: it never makes an HTTP/model call itself, only
/// orchestrating which already-constructed <see cref="AIAgent"/> speaks next based on message
/// history it is handed. See <c>EditRoomGroupChatManagerTests</c>/<c>GraphicsRoomGroupChatManagerTests</c>.
/// </para>
/// </summary>
public abstract class RoomGroupChatManager : GroupChatManager
{
    /// <summary>The literal token every room's director prompt is instructed to emit once the room has converged on a decision.</summary>
    public const string SentinelToken = "ROOM_DECIDED";

    /// <summary>
    /// The director occupies the LAST <see cref="DirectorAliasCount"/> slots of <c>allParticipants</c>,
    /// not just one — see the constructor's remarks for why two identical registrations are required.
    /// </summary>
    private const int DirectorAliasCount = 2;

    private readonly IReadOnlyList<AIAgent> _allParticipants;
    private readonly IRoomStepConfig _config;
    private readonly IReadOnlySet<string> _offeredIds;
    private readonly string _directorSeatName;
    private readonly Regex _idMentionPattern;
    private readonly int _editorCount;

    private readonly List<HashSet<string>> _roundMentionHistory = [];
    private HashSet<string> _currentRoundMentions = new(StringComparer.Ordinal);
    private int _lastRoundIndexObserved = -1;

    /// <summary>
    /// Every message this manager has observed via <see cref="UpdateHistoryAsync"/>, in arrival
    /// order — a pure side-effect buffer for the executor to read back after the run completes
    /// (e.g. to build the non-authoritative transcript artifact); never mutated back into the
    /// canonical history returned to the framework.
    /// </summary>
    public IReadOnlyList<ChatMessage> ObservedTurns => _observedTurns;
    private readonly List<ChatMessage> _observedTurns = [];

    /// <summary>
    /// Why <see cref="ShouldTerminateAsync"/> last returned <c>true</c>: <c>"sentinel"</c> or
    /// <c>"converged"</c>. Null while the room is still running or if it ran to the
    /// <see cref="GroupChatManager.MaximumIterationCount"/> ceiling instead (the executor should
    /// treat a still-null value after the run ends as <c>"ceiling"</c>).
    /// </summary>
    public string? TerminationReason { get; private set; }

    /// <summary>
    /// Extracts offered-id-vocabulary mentions from free-form turn text — shared by convergence
    /// tracking here and by each executor's chat-turn event reporting. <paramref name="pattern"/>
    /// must capture the candidate id as group 1 (each room's subclass exposes a convenience
    /// wrapper bound to its own pattern).
    /// </summary>
    public static IReadOnlyList<string> ExtractIdMentions(string? text, IReadOnlySet<string> offeredIds, Regex pattern)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        List<string> found = [];
        foreach (Match m in pattern.Matches(text))
        {
            string id = m.Groups[1].Value;
            if (offeredIds.Contains(id) && !found.Contains(id))
                found.Add(id);
        }

        return found;
    }

    /// <param name="allParticipants">
    /// Seats in the same order as the config's <c>EffectiveSeats</c>, with the director appended
    /// LAST <em>TWICE</em> — two separate <see cref="RoomSeatAgent"/> registrations, each wrapping
    /// its OWN inner director agent instance (same resolved chat client/instructions/tools; see the
    /// remarks below for why sharing one inner instance across both wrappers does not work).
    /// </param>
    /// <param name="config">The step's resolved room configuration (schedule/termination knobs only are read here).</param>
    /// <param name="offeredIds">The offered id vocabulary extracted from the bounded view, used for convergence detection only.</param>
    /// <param name="directorSeatName">The <see cref="AIAgent.Name"/> the director's <see cref="RoomSeatAgent"/> wrapper reports — used to recognise the director's own turns in history.</param>
    /// <param name="idMentionPattern">The compiled regex recognizing this room's offered-id namespace in free-form turn text (candidate id captured as group 1).</param>
    /// <remarks>
    /// <c>Microsoft.Agents.AI.Workflows</c> 1.22.0's internal <c>GroupChatHost.TakeTurnAsync</c>
    /// (decompiled and confirmed against the shipped 1.22.0 assembly — absent from 1.0.0-rc2)
    /// added a guard that was not there before: if <c>SelectNextAgentAsync</c> returns the SAME
    /// registered participant that took the immediately preceding turn, the host treats the room
    /// as having nothing left to say and ends it right there, without ever invoking that agent
    /// again — silently short-circuiting the turn instead of sending it. That collides head-on
    /// with this scheduler's own design once the round-robin phase ends: every room wants the
    /// SAME director to keep speaking for every remaining turn up to the ceiling (see
    /// <c>SelectNextAgentAsync</c> below), which under 1.22.0 now ends the room one turn early the
    /// very first time the director would otherwise speak twice in a row. Two distinct director
    /// registrations — alternated, never the same object on consecutive turns — sidestep the new
    /// guard without changing what "the director" means: both receive the identical growing history
    /// each turn (the group chat broadcasts <c>_history</c> to every registered participant, not a
    /// per-participant slice), so which alias answers a given turn is a scheduling-identity detail
    /// only, invisible to the transcript (both report <see cref="AIAgent.Name"/> as
    /// <paramref name="directorSeatName"/>) and to the model itself.
    /// <para>
    /// The two aliases must be built from SEPARATE inner agent instances, not one inner agent
    /// wrapped twice: <c>AIAgent.Id</c> defaults to a fresh random GUID per instance, but
    /// <c>DelegatingAIAgent</c> (what <see cref="RoomSeatAgent"/> is) forwards its <c>IdCore</c> to
    /// <c>InnerAgent.Id</c>. <c>AgentWorkflowBuilder</c> derives each participant's internal
    /// executor-binding id from Name+Id, and both aliases must keep the SAME Name — so a shared
    /// inner agent gives both aliases the identical Id too, and
    /// <c>GroupChatWorkflowBuilder.Build()</c> throws ("Cannot bind executor with ID '...' because
    /// an executor with the same ID but different instance is already bound.") the moment the
    /// workflow is built. Confirmed live against the real 1.22.0 assembly. See
    /// <c>RoomStepExecutorBase.RunRoomAsync</c>, which builds each alias's inner agent independently
    /// for exactly this reason.
    /// </para>
    /// </remarks>
    protected RoomGroupChatManager(
        IReadOnlyList<AIAgent> allParticipants,
        IRoomStepConfig config,
        IReadOnlySet<string> offeredIds,
        string directorSeatName,
        Regex idMentionPattern)
    {
        if (allParticipants.Count < 1 + DirectorAliasCount)
            throw new ArgumentException("A room needs at least one seat plus two director registrations.", nameof(allParticipants));

        _allParticipants = allParticipants;
        _config = config;
        _offeredIds = offeredIds;
        _directorSeatName = directorSeatName;
        _idMentionPattern = idMentionPattern;
        _editorCount = allParticipants.Count - DirectorAliasCount;
    }

    /// <summary>
    /// Round-robins the seats (indices <c>0..seatCount-1</c> of <c>allParticipants</c>)
    /// for the config's <c>Rounds</c> full passes, then alternates between the director's two
    /// alias registrations (the last <see cref="DirectorAliasCount"/> participants) for every turn
    /// after that — see the constructor's remarks for why a single director registration can no
    /// longer produce more than one consecutive turn under Microsoft.Agents.AI.Workflows 1.22.0.
    /// </summary>
    protected override ValueTask<AIAgent> SelectNextAgentAsync(
        IReadOnlyList<ChatMessage> history, CancellationToken cancellationToken)
    {
        int i = IterationCount;
        if (i < _editorCount * _config.Rounds)
            return ValueTask.FromResult(_allParticipants[i % _editorCount]);

        int directorTurn = i - (_editorCount * _config.Rounds);
        AIAgent next = _allParticipants[^(DirectorAliasCount - directorTurn % DirectorAliasCount)];
        return ValueTask.FromResult(next);
    }

    /// <summary>
    /// Pure pass-through: records every NEW message (beyond what was already observed) into
    /// <see cref="ObservedTurns"/> for the executor's own transcript/progress bookkeeping, then
    /// returns <paramref name="history"/> completely unchanged. Deliberately never filters/trims —
    /// the framework treats this return value as the new canonical transcript, not a per-turn view,
    /// so returning anything other than the untouched input would corrupt it.
    /// </summary>
    protected override ValueTask<IEnumerable<ChatMessage>> UpdateHistoryAsync(
        IReadOnlyList<ChatMessage> history, CancellationToken cancellationToken)
    {
        for (int i = _observedTurns.Count; i < history.Count; i++)
            _observedTurns.Add(history[i]);

        TrackConvergence(history);

        return ValueTask.FromResult<IEnumerable<ChatMessage>>(history);
    }

    /// <summary>
    /// Ceiling check first (mirrors <see cref="GroupChatManager"/>'s own default
    /// <c>ShouldTerminateAsync</c> implementation — <c>IterationCount &gt;= MaximumIterationCount</c>
    /// — which overriding this method entirely SHADOWS rather than composes with; the ceiling is
    /// NOT enforced automatically just because <c>MaximumIterationCount</c> is set, contrary to an
    /// earlier assumption here corrected after decompiling the real rc2 assembly. This method must
    /// therefore check it explicitly, unconditionally, before any mode-specific logic), then the
    /// sentinel check (director's most recent turn contains <see cref="SentinelToken"/>) and/or
    /// offered-id-mention convergence check across consecutive seat rounds, gated by the config's
    /// <c>Termination</c>. <see cref="EditRoomTerminationMode.FixedTurns"/> skips both
    /// mode-specific checks so only the ceiling can end the room. Safe to call at iteration 0
    /// (empty/opening-only history).
    /// </summary>
    protected override ValueTask<bool> ShouldTerminateAsync(
        IReadOnlyList<ChatMessage> history, CancellationToken cancellationToken)
    {
        if (IterationCount >= MaximumIterationCount)
            return ValueTask.FromResult(true);

        if (_config.Termination == EditRoomTerminationMode.FixedTurns)
            return ValueTask.FromResult(false);

        if (history.Count > 0 &&
            _config.Termination is EditRoomTerminationMode.SentinelOnly or EditRoomTerminationMode.SentinelOrConverged)
        {
            ChatMessage last = history[^1];
            if (string.Equals(last.AuthorName, _directorSeatName, StringComparison.Ordinal) &&
                (last.Text?.Contains(SentinelToken, StringComparison.Ordinal) ?? false))
            {
                TerminationReason = "sentinel";
                return ValueTask.FromResult(true);
            }
        }

        if (_config.Termination is EditRoomTerminationMode.Converged or EditRoomTerminationMode.SentinelOrConverged)
        {
            int rounds = _roundMentionHistory.Count;
            int minRounds = Math.Max(1, _config.MinConvergenceRounds);
            if (rounds >= minRounds)
            {
                HashSet<string> reference = _roundMentionHistory[^1];
                bool allEqual = true;
                for (int r = rounds - minRounds; r < rounds - 1; r++)
                {
                    if (!_roundMentionHistory[r].SetEquals(reference))
                    {
                        allEqual = false;
                        break;
                    }
                }

                if (allEqual)
                {
                    TerminationReason = "converged";
                    return ValueTask.FromResult(true);
                }
            }
        }

        return ValueTask.FromResult(false);
    }

    /// <summary>Clears every piece of internal state so this manager instance could, in principle, drive a second independent run.</summary>
    protected override void Reset()
    {
        _observedTurns.Clear();
        _roundMentionHistory.Clear();
        _currentRoundMentions = new HashSet<string>(StringComparer.Ordinal);
        _lastRoundIndexObserved = -1;
        TerminationReason = null;
    }

    /// <summary>
    /// Extracts offered-id mentions from each newly observed SEAT turn (never the director's —
    /// the director doesn't propose ids, it moderates) and accumulates them per round; a round's
    /// mention set is finalized (appended to <see cref="_roundMentionHistory"/>) once every seat
    /// in that round has spoken.
    /// </summary>
    private void TrackConvergence(IReadOnlyList<ChatMessage> history)
    {
        if (_editorCount <= 0)
            return;

        // IterationCount is the number of turns ALREADY COMPLETED when this callback fires (see
        // the class's "called BEFORE any agent has spoken" contract for iteration 0) — walk only
        // the seat-authored messages in history that arrived since the last call.
        for (int i = 0; i < history.Count; i++)
        {
            ChatMessage msg = history[i];
            if (string.Equals(msg.AuthorName, _directorSeatName, StringComparison.Ordinal))
                continue;

            bool isEditorSeat = false;
            for (int s = 0; s < _editorCount; s++)
            {
                if (string.Equals(msg.AuthorName, _allParticipants[s].Name, StringComparison.Ordinal))
                {
                    isEditorSeat = true;
                    break;
                }
            }

            if (!isEditorSeat)
                continue;

            int globalTurnIndex = i; // 0-based position among ALL messages including the opening one
            if (globalTurnIndex <= _lastRoundIndexObserved)
                continue;

            foreach (Match m in _idMentionPattern.Matches(msg.Text ?? string.Empty))
            {
                if (_offeredIds.Contains(m.Groups[1].Value))
                    _currentRoundMentions.Add(m.Groups[1].Value);
            }

            _lastRoundIndexObserved = globalTurnIndex;
        }

        // A round is complete once IterationCount is a positive multiple of _editorCount (i.e.
        // every seat has just taken its Nth turn) and we haven't already recorded that round.
        // Bounded to the round-robin phase only (IterationCount <= editorCount*Rounds) — once the
        // director starts speaking, IterationCount can coincidentally be a multiple of
        // _editorCount again with no new seat turns since the last recorded round, which must
        // never be mistaken for a freshly completed seat round.
        if (IterationCount > 0 && IterationCount <= _editorCount * _config.Rounds && IterationCount % _editorCount == 0)
        {
            int roundNumber = IterationCount / _editorCount;
            if (_roundMentionHistory.Count < roundNumber)
            {
                _roundMentionHistory.Add(new HashSet<string>(_currentRoundMentions, StringComparer.Ordinal));
                _currentRoundMentions = new HashSet<string>(StringComparer.Ordinal);
            }
        }
    }
}
