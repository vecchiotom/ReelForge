using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using ReelForge.Shared.Workflows;

namespace ReelForge.WorkflowEngine.Agents.Rooms;

/// <summary>
/// The color grade room's binding of the shared, room-generic <see cref="RoomGroupChatManager"/>
/// scheduler/terminator: all it contributes is the grade room's offered-id vocabulary — the SHOT
/// namespace (<c>s{n}</c>) and nothing else. The grade room's final decision carries no ids at
/// all (one whole-program grade in enum words — see <c>ColorGradePlanOutput</c>), but the
/// DELIBERATION is anchored to shots ("s2 reads backlit", "s0 and s4 disagree on temperature"),
/// so shot-id mentions are the convergence signal, exactly as placement-id mentions are for the
/// graphics room. Deliberately narrower than the edit room's <c>[sgt]</c> pattern: a colorist
/// mentioning a silence gap or transcript segment in passing prose must never count toward
/// convergence — gaps and segments have no colour to argue about. Everything else (scheduling,
/// sentinel, convergence, ceiling) is the base class, unchanged. See docs/video-editing.md
/// "The color grade room".
/// </summary>
public class ColorGradeRoomGroupChatManager : RoomGroupChatManager
{
    private static readonly Regex OfferedIdMentionPattern = new(@"\b(s\d{1,4})\b", RegexOptions.Compiled);

    /// <summary>Extracts grade-room offered-shot-id mentions from free-form turn text — shared by convergence tracking and by the executor's chat-turn event reporting.</summary>
    public static IReadOnlyList<string> ExtractOfferedIdMentions(string? text, IReadOnlySet<string> offeredIds) =>
        ExtractIdMentions(text, offeredIds, OfferedIdMentionPattern);

    /// <param name="allParticipants">Seats in the same order as <see cref="ColorGradeRoomStepConfig.EffectiveSeats"/>, with the director appended last.</param>
    /// <param name="config">The step's resolved configuration.</param>
    /// <param name="offeredIds">The offered shot-id vocabulary extracted from the bounded view's <c>view.shots</c>, used for convergence detection only.</param>
    /// <param name="directorSeatName">The <see cref="AIAgent.Name"/> the director's <see cref="RoomSeatAgent"/> wrapper reports — used to recognise the director's own turns in history.</param>
    public ColorGradeRoomGroupChatManager(
        IReadOnlyList<AIAgent> allParticipants,
        ColorGradeRoomStepConfig config,
        IReadOnlySet<string> offeredIds,
        string directorSeatName)
        : base(allParticipants, config, offeredIds, directorSeatName, OfferedIdMentionPattern)
    {
    }
}
