using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using ReelForge.Shared.Workflows;

namespace ReelForge.WorkflowEngine.Agents.Rooms;

/// <summary>
/// The graphics room's binding of the shared, room-generic <see cref="RoomGroupChatManager"/>
/// scheduler/terminator: all it contributes is the graphics room's offered-id vocabulary — the
/// overlay-placement namespace (<c>p{n}</c>) and nothing else. Deliberately NOT the edit room's
/// <c>[sgt]</c> pattern: a graphics-room seat mentioning a shot id (<c>s2</c>) in passing prose
/// ("the shot p3 sits on") must never count toward placement-id convergence, exactly as a Keep
/// span can never reference a placement id in the edit room. Everything else (scheduling,
/// sentinel, convergence, ceiling) is the base class, unchanged. See docs/video-editing.md
/// "The graphics room".
/// </summary>
public class GraphicsRoomGroupChatManager : RoomGroupChatManager
{
    private static readonly Regex OfferedIdMentionPattern = new(@"\b(p\d{1,4})\b", RegexOptions.Compiled);

    /// <summary>Extracts graphics-room offered-placement-id mentions from free-form turn text — shared by convergence tracking and by the executor's chat-turn event reporting.</summary>
    public static IReadOnlyList<string> ExtractOfferedIdMentions(string? text, IReadOnlySet<string> offeredIds) =>
        ExtractIdMentions(text, offeredIds, OfferedIdMentionPattern);

    /// <param name="allParticipants">Seats in the same order as <see cref="GraphicsRoomStepConfig.EffectiveSeats"/>, each participant registered twice and the director last (see the base constructor\'s remarks).</param>
    /// <param name="config">The step's resolved configuration.</param>
    /// <param name="offeredIds">The offered placement-id vocabulary extracted from the bounded view's <c>view.placements</c>, used for convergence detection only.</param>
    /// <param name="directorSeatName">The <see cref="AIAgent.Name"/> the director's <see cref="RoomSeatAgent"/> wrapper reports — used to recognise the director's own turns in history.</param>
    public GraphicsRoomGroupChatManager(
        IReadOnlyList<AIAgent> allParticipants,
        GraphicsRoomStepConfig config,
        IReadOnlySet<string> offeredIds,
        string directorSeatName)
        : base(allParticipants, config, offeredIds, directorSeatName, OfferedIdMentionPattern)
    {
    }
}
