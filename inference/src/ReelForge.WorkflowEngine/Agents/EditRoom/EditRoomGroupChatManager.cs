using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using ReelForge.Shared.Workflows;

namespace ReelForge.WorkflowEngine.Agents.EditRoom;

/// <summary>
/// Deterministic scheduler/terminator for the edit room's <c>Microsoft.Agents.AI.Workflows</c>
/// group chat. Not LLM-driven — every seat sees the exact same bounded view and can speak to any
/// part of it, so an intelligent "who should speak next" decision would only double the room's
/// LLM-call cost for no benefit. See docs/video-editing.md "The edit room".
///
/// <para>
/// Fully unit-testable with zero network calls: it never makes an HTTP/model call itself, only
/// orchestrates which already-constructed <see cref="AIAgent"/> speaks next based on message
/// history it is handed. See <c>EditRoomGroupChatManagerTests</c>.
/// </para>
/// </summary>
// Not sealed: EditRoomGroupChatManagerTests uses a minimal test-only subclass
// (TestableManager) to call ShouldTerminateAsync directly at iteration 0, the one case that
// doesn't need a full engine-driven run.
public class EditRoomGroupChatManager : GroupChatManager
{
    /// <summary>The literal token the director's prompt is instructed to emit once the room has converged on a decision.</summary>
    public const string SentinelToken = "ROOM_DECIDED";

    // Matches the exact three id-kinds VideoStoryEditor/VideoEditDirector ever choose among
    // (shots "s", silence gaps "g", transcript segments "t") — never placement ("p") or music
    // ("m") ids, which a Keep span can never reference in the first place.
    private static readonly Regex OfferedIdMentionPattern = new(@"\b([sgt]\d{1,4})\b", RegexOptions.Compiled);

    private readonly IReadOnlyList<AIAgent> _allParticipants;
    private readonly EditRoomStepConfig _config;
    private readonly IReadOnlySet<string> _offeredIds;
    private readonly string _directorSeatName;
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

    /// <summary>Extracts offered-id-vocabulary mentions from free-form turn text — shared by convergence tracking here and by the executor's chat-turn event reporting.</summary>
    public static IReadOnlyList<string> ExtractOfferedIdMentions(string? text, IReadOnlySet<string> offeredIds)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        List<string> found = [];
        foreach (Match m in OfferedIdMentionPattern.Matches(text))
        {
            string id = m.Groups[1].Value;
            if (offeredIds.Contains(id) && !found.Contains(id))
                found.Add(id);
        }

        return found;
    }

    /// <param name="allParticipants">Seats in the same order as <see cref="EditRoomStepConfig.EffectiveSeats"/>, with the director appended last.</param>
    /// <param name="config">The step's resolved configuration.</param>
    /// <param name="offeredIds">The offered shot/silence/segment id vocabulary extracted from the bounded view, used for convergence detection only.</param>
    /// <param name="directorSeatName">The <see cref="AIAgent.Name"/> the director's <see cref="EditRoomSeatAgent"/> wrapper reports — used to recognise the director's own turns in history.</param>
    public EditRoomGroupChatManager(
        IReadOnlyList<AIAgent> allParticipants,
        EditRoomStepConfig config,
        IReadOnlySet<string> offeredIds,
        string directorSeatName)
    {
        if (allParticipants.Count < 2)
            throw new ArgumentException("An edit room needs at least one editor seat plus the director.", nameof(allParticipants));

        _allParticipants = allParticipants;
        _config = config;
        _offeredIds = offeredIds;
        _directorSeatName = directorSeatName;
        _editorCount = allParticipants.Count - 1;
    }

    /// <summary>
    /// Round-robins the editor seats (indices <c>0..editorCount-1</c> of <c>allParticipants</c>)
    /// for <see cref="EditRoomStepConfig.Rounds"/> full passes, then always returns the director
    /// (the last participant) for every turn after that.
    /// </summary>
    protected override ValueTask<AIAgent> SelectNextAgentAsync(
        IReadOnlyList<ChatMessage> history, CancellationToken cancellationToken)
    {
        int i = IterationCount;
        AIAgent next = i < _editorCount * _config.Rounds
            ? _allParticipants[i % _editorCount]
            : _allParticipants[^1];
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
    /// offered-id-mention convergence check across consecutive editor rounds, gated by
    /// <see cref="EditRoomStepConfig.Termination"/>. <see cref="EditRoomTerminationMode.FixedTurns"/>
    /// skips both mode-specific checks so only the ceiling can end the room. Safe to call at
    /// iteration 0 (empty/opening-only history).
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
    /// Extracts offered-id mentions from each newly observed EDITOR turn (never the director's —
    /// the director doesn't propose ids, it moderates) and accumulates them per round; a round's
    /// mention set is finalized (appended to <see cref="_roundMentionHistory"/>) once every editor
    /// seat in that round has spoken.
    /// </summary>
    private void TrackConvergence(IReadOnlyList<ChatMessage> history)
    {
        if (_editorCount <= 0)
            return;

        // IterationCount is the number of turns ALREADY COMPLETED when this callback fires (see
        // the class's "called BEFORE any agent has spoken" contract for iteration 0) — walk only
        // the editor-authored messages in history that arrived since the last call.
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

            foreach (Match m in OfferedIdMentionPattern.Matches(msg.Text ?? string.Empty))
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
        // _editorCount again with no new editor turns since the last recorded round, which must
        // never be mistaken for a freshly completed editor round.
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
