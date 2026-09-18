using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ReelForge.Shared.Workflows;
using ReelForge.WorkflowEngine.Agents.Rooms;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Covers the color grade room's OWN contribution to the shared room scheduler — its
/// <c>s{n}</c> shot-id mention vocabulary — plus the iteration-zero safety check every binding
/// repeats. The shared <see cref="RoomGroupChatManager"/> scheduling/termination/convergence
/// machinery is already exercised twice through real engine runs
/// (<c>EditRoomGroupChatManagerTests</c>/<c>GraphicsRoomGroupChatManagerTests</c>), so this
/// third binding deliberately does not repeat those engine-driven suites — a regression in the
/// shared base fails both of them, and a regression here can only live in the vocabulary regex
/// or constructor plumbing tested below.
/// </summary>
public class ColorGradeRoomGroupChatManagerTests
{
    private static readonly HashSet<string> OfferedIds = new(StringComparer.Ordinal) { "s0", "s1", "s2" };

    [Fact]
    public void ExtractOfferedIdMentions_returns_only_shot_ids_actually_in_the_offered_set()
    {
        IReadOnlyList<string> found = ColorGradeRoomGroupChatManager.ExtractOfferedIdMentions(
            "s0 reads backlit and s2 leans cool; s99 does not exist. s2 again.", OfferedIds);

        found.Should().BeEquivalentTo(["s0", "s2"]);
    }

    [Fact]
    public void ExtractOfferedIdMentions_never_matches_other_rooms_id_kinds_even_when_offered()
    {
        // Even a (mis-)constructed offered set containing g/t/p ids must not leak through the
        // grade room's pattern — the vocabulary is the pattern, not just the offered set.
        HashSet<string> mixed = new(StringComparer.Ordinal) { "s1", "g1", "t7", "p0" };

        IReadOnlyList<string> found = ColorGradeRoomGroupChatManager.ExtractOfferedIdMentions(
            "The gap g1 and segment t7 and placement p0 must never count — only s1.", mixed);

        found.Should().BeEquivalentTo(["s1"]);
    }

    [Fact]
    public void ExtractOfferedIdMentions_of_empty_text_is_empty()
    {
        ColorGradeRoomGroupChatManager.ExtractOfferedIdMentions(null, OfferedIds).Should().BeEmpty();
        ColorGradeRoomGroupChatManager.ExtractOfferedIdMentions("", OfferedIds).Should().BeEmpty();
    }

    [Fact]
    public void ShouldTerminateAsync_does_not_throw_at_iteration_zero_with_empty_history()
    {
        var config = new ColorGradeRoomStepConfig(
            Seats: [new EditRoomSeat("Seat0", "first"), new EditRoomSeat("Seat1", "second")]);
        var manager = new TestableManager(
            [new StubAgent("Seat0"), new StubAgent("Seat1"), new StubAgent("Director")],
            config, OfferedIds, "Director");

        Func<Task> act = async () => await manager.ShouldTerminatePublic([], CancellationToken.None);

        act.Should().NotThrowAsync();
    }

    private sealed class TestableManager : ColorGradeRoomGroupChatManager
    {
        public TestableManager(IReadOnlyList<AIAgent> allParticipants, ColorGradeRoomStepConfig config, IReadOnlySet<string> offeredIds, string directorSeatName)
            : base(allParticipants, config, offeredIds, directorSeatName)
        {
        }

        public ValueTask<bool> ShouldTerminatePublic(IReadOnlyList<ChatMessage> history, CancellationToken ct) =>
            ShouldTerminateAsync(history, ct);
    }

    private sealed class StubSession : AgentSession
    {
    }

    /// <summary>Never actually run — only needs a Name for the constructor's participant list.</summary>
    private sealed class StubAgent : AIAgent
    {
        private readonly string _name;

        public StubAgent(string name) => _name = name;

        public override string Name => _name;

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<AgentSession>(new StubSession());

        protected override ValueTask<System.Text.Json.JsonElement> SerializeSessionCoreAsync(
            AgentSession session, System.Text.Json.JsonSerializerOptions? jsonSerializerOptions, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new System.Text.Json.JsonElement());

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            System.Text.Json.JsonElement serializedState, System.Text.Json.JsonSerializerOptions? jsonSerializerOptions, CancellationToken cancellationToken) =>
            ValueTask.FromResult<AgentSession>(new StubSession());

        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages, AgentSession? session, AgentRunOptions? options, CancellationToken cancellationToken) =>
            Task.FromResult(new AgentResponse(new ChatMessage(ChatRole.Assistant, "stub") { AuthorName = _name }));

        protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages, AgentSession? session, AgentRunOptions? options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return new AgentResponseUpdate(ChatRole.Assistant, "stub") { AuthorName = _name };
        }
    }
}
