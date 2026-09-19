using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using ReelForge.Shared.Workflows;
using ReelForge.WorkflowEngine.Agents.Rooms;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Exercises the shared, room-generic <see cref="RoomGroupChatManager"/> scheduling/termination/
/// convergence logic through its SECOND binding (<see cref="GraphicsRoomGroupChatManager"/>, the
/// <c>p{n}</c> placement-id vocabulary) — the same behaviors <c>EditRoomGroupChatManagerTests</c>
/// already exercises through the first binding, so a regression in the shared base fails both
/// suites while a regression in one room's id vocabulary fails only its own. Same testing approach
/// as that suite: driven through the REAL <c>Microsoft.Agents.AI.Workflows</c> in-process engine
/// with canned-response <see cref="FakeAgent"/>s (zero network calls), because
/// <c>GroupChatManager.IterationCount</c> can only be advanced by the framework's own
/// orchestration.
/// </summary>
public class GraphicsRoomGroupChatManagerTests
{
    private static readonly HashSet<string> OfferedIds = new(StringComparer.Ordinal) { "p0", "p1", "p2" };

    private static GraphicsRoomStepConfig BuildConfig(
        int rounds = 2,
        int maxTurns = 8,
        EditRoomTerminationMode termination = EditRoomTerminationMode.FixedTurns,
        int minConvergenceRounds = 2) =>
        new(
            Seats:
            [
                new EditRoomSeat("Seat0", "first"),
                new EditRoomSeat("Seat1", "second"),
                new EditRoomSeat("Seat2", "third")
            ],
            Rounds: rounds,
            MaxTurns: maxTurns,
            Termination: termination,
            MinConvergenceRounds: minConvergenceRounds);

    private static async Task<(IReadOnlyList<ChatMessage> Transcript, GraphicsRoomGroupChatManager Manager)> RunRoomAsync(
        GraphicsRoomStepConfig config, IReadOnlyList<AIAgent> participants)
    {
        GraphicsRoomGroupChatManager? captured = null;

        Workflow workflow = AgentWorkflowBuilder
            .CreateGroupChatBuilderWith(agents =>
            {
                var manager = new GraphicsRoomGroupChatManager(agents, config, OfferedIds, "Director")
                {
                    MaximumIterationCount = config.ClampedMaxTurns
                };
                captured = manager;
                return manager;
            })
            .AddParticipants(participants)
            .Build();

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(20));

        await using StreamingRun run = await InProcessExecution.RunStreamingAsync(
            workflow, "opening message", Guid.NewGuid().ToString("D"), cts.Token);

        // GroupChatHost only BUFFERS the opening message (AutoSendTurnToken=false) — a TurnToken
        // must be sent explicitly to kick off the first turn-selection cycle. See the identical
        // comment in RoomStepExecutorBase.RunRoomAsync.
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        List<ChatMessage>? transcript = null;
        await foreach (WorkflowEvent evt in run.WatchStreamAsync(cts.Token))
        {
            if (evt is WorkflowOutputEvent outputEvent && outputEvent.Is<List<ChatMessage>>())
            {
                transcript = outputEvent.As<List<ChatMessage>>();
                break;
            }
        }

        transcript.Should().NotBeNull("the workflow must produce a WorkflowOutputEvent<List<ChatMessage>>");
        return (transcript!, captured!);
    }

    // The director is registered TWICE, deliberately — see RoomGroupChatManager's constructor
    // remarks. Microsoft.Agents.AI.Workflows 1.22.0's group-chat host refuses to re-invoke the SAME
    // registered participant on two consecutive turns, so the round-robin-then-always-the-director
    // schedule needs two functionally-identical director registrations to alternate between once
    // the round-robin phase ends. Both must mirror RoomStepExecutorBase.RunRoomAsync's own
    // participant list shape (seats, then the director twice) for these tests to exercise the same
    // contract production code actually builds.
    private static IReadOnlyList<AIAgent> BuildParticipants(string directorText = "moderating") =>
    [
        new FakeAgent("Seat0", "The lower third at p0 works."),
        new FakeAgent("Seat1", "Agreed, and p1 for the title."),
        new FakeAgent("Seat2", "Sounds good to me."),
        new FakeAgent("Director", directorText),
        new FakeAgent("Director", directorText)
    ];

    [Fact]
    public async Task Round_robins_three_seats_for_two_rounds_then_always_the_director_until_the_ceiling()
    {
        GraphicsRoomStepConfig config = BuildConfig(rounds: 2, maxTurns: 8, termination: EditRoomTerminationMode.FixedTurns);
        (IReadOnlyList<ChatMessage> transcript, GraphicsRoomGroupChatManager manager) =
            await RunRoomAsync(config, BuildParticipants());

        // transcript[0] is the opening message; the rest are the 8 turns (ceiling-enforced, since
        // FixedTurns disables the sentinel/convergence checks entirely).
        List<string?> speakers = transcript.Skip(1).Select(m => m.AuthorName).ToList();
        speakers.Should().Equal("Seat0", "Seat1", "Seat2", "Seat0", "Seat1", "Seat2", "Director", "Director");
        manager.TerminationReason.Should().BeNull("FixedTurns never sets a termination reason — only the ceiling ends the room");
    }

    [Fact]
    public async Task Sentinel_in_the_directors_turn_ends_the_room_early()
    {
        GraphicsRoomStepConfig config = BuildConfig(rounds: 1, maxTurns: 8, termination: EditRoomTerminationMode.SentinelOnly);
        (IReadOnlyList<ChatMessage> transcript, GraphicsRoomGroupChatManager manager) =
            await RunRoomAsync(config, BuildParticipants(directorText: "Great work, ROOM_DECIDED — lower third at p0, title at p1."));

        manager.TerminationReason.Should().Be("sentinel");
        // 3 seat turns (one round) + exactly 1 director turn carrying the sentinel; must stop
        // well short of the 8-turn ceiling.
        (transcript.Count - 1).Should().Be(4);
        transcript[^1].AuthorName.Should().Be("Director");
    }

    [Fact]
    public async Task Identical_offered_placement_id_mentions_across_consecutive_rounds_converge_before_the_ceiling()
    {
        GraphicsRoomStepConfig config = BuildConfig(rounds: 4, maxTurns: 20, termination: EditRoomTerminationMode.Converged, minConvergenceRounds: 2);
        IReadOnlyList<AIAgent> participants =
        [
            new FakeAgent("Seat0", "Keep the lower third at p0."),
            new FakeAgent("Seat1", "And p1 for the closing title."),
            new FakeAgent("Seat2", "Agreed on p0 and p1."),
            new FakeAgent("Director", "Still discussing."),
            new FakeAgent("Director", "Still discussing.")
        ];

        (IReadOnlyList<ChatMessage> transcript, GraphicsRoomGroupChatManager manager) = await RunRoomAsync(config, participants);

        manager.TerminationReason.Should().Be("converged");
        // Every seat mentions the exact same offered ids every single turn, so convergence should
        // fire well before 4 full rounds (12 seat turns) let alone the 20-turn ceiling.
        (transcript.Count - 1).Should().BeLessThan(config.EffectiveSeats.Count * config.Rounds);
    }

    [Fact]
    public async Task FixedTurns_ignores_a_sentinel_and_runs_to_the_ceiling()
    {
        GraphicsRoomStepConfig config = BuildConfig(rounds: 1, maxTurns: 5, termination: EditRoomTerminationMode.FixedTurns);
        (IReadOnlyList<ChatMessage> transcript, GraphicsRoomGroupChatManager manager) =
            await RunRoomAsync(config, BuildParticipants(directorText: "ROOM_DECIDED right away."));

        manager.TerminationReason.Should().BeNull();
        (transcript.Count - 1).Should().Be(5, "FixedTurns must ignore the sentinel and run to the MaxTurns ceiling");
    }

    [Fact]
    public void ShouldTerminateAsync_does_not_throw_at_iteration_zero_with_empty_history()
    {
        GraphicsRoomStepConfig config = BuildConfig();
        IReadOnlyList<AIAgent> participants = BuildParticipants();
        var manager = new TestableManager(participants, config, OfferedIds, "Director");

        Func<Task> act = async () => await manager.ShouldTerminatePublic([], CancellationToken.None);

        act.Should().NotThrowAsync();
    }

    [Fact]
    public void ExtractOfferedIdMentions_returns_only_placement_ids_actually_in_the_offered_set()
    {
        IReadOnlyList<string> found = GraphicsRoomGroupChatManager.ExtractOfferedIdMentions(
            "Use p0 and p1, definitely not p99... and the shot s2 or gap g1 must never count. p1 again.", OfferedIds);

        found.Should().BeEquivalentTo(["p0", "p1"]);
    }

    [Fact]
    public void ExtractOfferedIdMentions_never_matches_edit_room_id_kinds_even_when_offered()
    {
        // Even a (mis-)constructed offered set containing s/g/t ids must not leak through the
        // graphics room's pattern — the vocabulary is the pattern, not just the offered set.
        HashSet<string> mixed = new(StringComparer.Ordinal) { "p0", "s2", "g1", "t7" };

        IReadOnlyList<string> found = GraphicsRoomGroupChatManager.ExtractOfferedIdMentions(
            "Keep s2 and g1 and t7 — and the overlay at p0.", mixed);

        found.Should().BeEquivalentTo(["p0"]);
    }

    /// <summary>
    /// Exposes the shared base's protected <c>ShouldTerminateAsync</c> as a public method callable
    /// directly from a test — the one case where a real engine run isn't needed, since a
    /// freshly-constructed manager already starts at iteration 0 with no engine involvement.
    /// </summary>
    private sealed class TestableManager : GraphicsRoomGroupChatManager
    {
        public TestableManager(IReadOnlyList<AIAgent> allParticipants, GraphicsRoomStepConfig config, IReadOnlySet<string> offeredIds, string directorSeatName)
            : base(allParticipants, config, offeredIds, directorSeatName)
        {
        }

        public ValueTask<bool> ShouldTerminatePublic(IReadOnlyList<ChatMessage> history, CancellationToken ct) =>
            ShouldTerminateAsync(history, ct);
    }

    /// <summary>Minimal concrete <see cref="AgentSession"/> — never actually inspected by these tests, only needs to exist to satisfy <see cref="AIAgent"/>'s abstract session methods.</summary>
    private sealed class FakeSession : AgentSession
    {
    }

    /// <summary>Trivial concrete <see cref="AIAgent"/> that returns fixed, instant text — zero network/model calls.</summary>
    private sealed class FakeAgent : AIAgent
    {
        private readonly string _name;
        private readonly string _text;

        public FakeAgent(string name, string text)
        {
            _name = name;
            _text = text;
        }

        public override string Name => _name;

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<AgentSession>(new FakeSession());

        protected override ValueTask<System.Text.Json.JsonElement> SerializeSessionCoreAsync(
            AgentSession session, System.Text.Json.JsonSerializerOptions? jsonSerializerOptions, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new System.Text.Json.JsonElement());

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            System.Text.Json.JsonElement serializedState, System.Text.Json.JsonSerializerOptions? jsonSerializerOptions, CancellationToken cancellationToken) =>
            ValueTask.FromResult<AgentSession>(new FakeSession());

        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages, AgentSession? session, AgentRunOptions? options, CancellationToken cancellationToken) =>
            Task.FromResult(new AgentResponse(new ChatMessage(ChatRole.Assistant, _text) { AuthorName = _name }));

        protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages, AgentSession? session, AgentRunOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return new AgentResponseUpdate(ChatRole.Assistant, _text) { AuthorName = _name };
        }
    }
}
