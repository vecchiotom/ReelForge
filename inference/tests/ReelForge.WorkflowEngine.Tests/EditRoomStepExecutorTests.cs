using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Data.OutputSchemas;
using ReelForge.Shared.Inference;
using ReelForge.Shared.Workflows;
using ReelForge.WorkflowEngine.Agents;
using ReelForge.WorkflowEngine.Agents.Tools;
using ReelForge.WorkflowEngine.Execution;
using ReelForge.WorkflowEngine.Execution.StepExecutors;
using ReelForge.WorkflowEngine.Services.Storage;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Covers the parts of <see cref="EditRoomStepExecutor"/> reasonably testable without a real
/// chat-completions backend: config validation, view resolution/offered-id extraction, and the
/// solo-fallback degrade path (triggered here by making <see cref="IAgentChatClientProvider"/>
/// throw when building a room-participant agent — the cheapest reliable way to force "the room
/// failed to start" without standing up a fake <see cref="IChatClient"/>). The actual live group
/// chat is exercised separately by <c>EditRoomGroupChatManagerTests</c> (the scheduler/terminator)
/// and by <c>EditRoomSeatAgent</c>'s own containment behavior, neither of which needs a real model.
/// </summary>
public class EditRoomStepExecutorTests
{
    private static readonly JsonSerializerOptions ConfigOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly Guid ProjectId = Guid.NewGuid();

    private static EditRoomStepExecutor CreateExecutor(
        out Mock<IAgentChatClientProvider> chatClients,
        out Mock<IAgentToolProvider> toolProvider,
        out Mock<IAgentRegistry> agentRegistry,
        out Mock<IProjectFileWorkspace> workspace)
    {
        chatClients = new Mock<IAgentChatClientProvider>();
        toolProvider = new Mock<IAgentToolProvider>();
        toolProvider.Setup(t => t.GetTools(It.IsAny<AgentType>())).Returns(Array.Empty<AIFunction>());
        agentRegistry = new Mock<IAgentRegistry>();
        workspace = new Mock<IProjectFileWorkspace>();

        // The real (not mocked) accessor: it's a pure AsyncLocal wrapper with no external
        // dependencies, and using it here means these tests actually exercise BeginScope being
        // opened correctly — the exact thing a live run found missing (every tool call inside the
        // room/solo-fallback threw "No workflow execution context is available").
        IWorkflowExecutionContextAccessor executionContextAccessor = new WorkflowExecutionContextAccessor();

        return new EditRoomStepExecutor(
            chatClients.Object, toolProvider.Object, agentRegistry.Object, workspace.Object,
            executionContextAccessor,
            NullLogger<EditRoomStepExecutor>.Instance);
    }

    private static StepExecutionContext CreateContext(string? editRoomConfigJson, IReadOnlyList<StepOutputHistoryEntry>? history = null)
    {
        var step = new WorkflowStep
        {
            Id = Guid.NewGuid(),
            StepOrder = 2,
            StepType = StepType.EditRoom,
            EditRoomConfigJson = editRoomConfigJson
        };

        return new StepExecutionContext
        {
            Execution = new WorkflowExecution { Id = Guid.NewGuid(), ProjectId = ProjectId },
            Step = step,
            AllSteps = [step],
            AccumulatedOutput = string.Empty,
            StepOutputHistory = history ?? [],
            CurrentStepIndex = 1,
            IterationCount = 0,
            CorrelationId = "test",
            CancellationToken = CancellationToken.None
        };
    }

    private static string BuildViewJson(params string[] shotIds)
    {
        var shots = shotIds.Select((id, i) => new { id, startSec = i * 2.0, endSec = i * 2.0 + 2.0, durationSec = 2.0 });
        return JsonSerializer.Serialize(new
        {
            view = new { shots, silences = Array.Empty<object>(), segments = Array.Empty<object>() },
            meta = new { offeredIdCount = shotIds.Length }
        });
    }

    private static string ErrorCode(StepExecutionResult result)
    {
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        return doc.RootElement.GetProperty("error").GetProperty("code").GetString() ?? string.Empty;
    }

    [Fact]
    public async Task Missing_config_json_fails_cleanly_with_EDIT_ROOM_CONFIG_INVALID()
    {
        EditRoomStepExecutor executor = CreateExecutor(out _, out _, out _, out _);
        StepExecutionContext context = CreateContext(editRoomConfigJson: null);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        ErrorCode(result).Should().Be("EDIT_ROOM_CONFIG_INVALID");
    }

    [Fact]
    public async Task Malformed_config_json_fails_cleanly_with_EDIT_ROOM_CONFIG_INVALID()
    {
        EditRoomStepExecutor executor = CreateExecutor(out _, out _, out _, out _);
        StepExecutionContext context = CreateContext(editRoomConfigJson: "{not valid json");

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        ErrorCode(result).Should().Be("EDIT_ROOM_CONFIG_INVALID");
    }

    [Fact]
    public async Task No_prior_step_output_to_resolve_the_view_from_fails_with_VIEW_UNRESOLVED()
    {
        EditRoomStepExecutor executor = CreateExecutor(out _, out _, out _, out _);
        string config = JsonSerializer.Serialize(new EditRoomStepConfig(View: new ExtractInputRef(ExtractInputSource.Previous)), ConfigOptions);
        StepExecutionContext context = CreateContext(config, history: []);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        ErrorCode(result).Should().Be("VIEW_UNRESOLVED");
    }

    [Fact]
    public async Task A_view_with_zero_offered_ids_fails_with_VIEW_UNRESOLVED()
    {
        EditRoomStepExecutor executor = CreateExecutor(out _, out _, out _, out _);
        string config = JsonSerializer.Serialize(new EditRoomStepConfig(View: new ExtractInputRef(ExtractInputSource.Previous)), ConfigOptions);
        string emptyView = JsonSerializer.Serialize(new { view = new { shots = Array.Empty<object>() }, meta = new { } });
        StepExecutionContext context = CreateContext(config, history: [new StepOutputHistoryEntry(1, "Analyze", emptyView)]);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        ErrorCode(result).Should().Be("VIEW_UNRESOLVED");
    }

    [Fact]
    public async Task Room_construction_failure_falls_back_to_the_solo_editor_and_still_completes()
    {
        EditRoomStepExecutor executor = CreateExecutor(
            out Mock<IAgentChatClientProvider> chatClients, out _, out Mock<IAgentRegistry> agentRegistry, out _);

        // Force "the room failed to start" the cheapest reliable way — no fake IChatClient needed.
        chatClients
            .Setup(c => c.GetAsync(It.IsAny<AgentType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no chat-capable provider configured"));

        string decisionJson = JsonSerializer.Serialize(new
        {
            keep = new[] { new { fromId = "s0", toId = "s1", reason = "solo fallback kept these" } },
            editRationale = "solo fallback",
            suggestedTitle = "Fallback Edit"
        });

        var soloAgent = new Mock<IReelForgeAgent>();
        soloAgent
            .Setup(a => a.RunAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult { Output = decisionJson, Success = true });

        agentRegistry
            .Setup(r => r.GetByType(AgentType.VideoStoryEditor, null))
            .Returns(soloAgent.Object);

        string config = JsonSerializer.Serialize(
            new EditRoomStepConfig(View: new ExtractInputRef(ExtractInputSource.Previous), FallbackToSoloEditor: true),
            ConfigOptions);
        string viewJson = BuildViewJson("s0", "s1", "s2");
        StepExecutionContext context = CreateContext(config, history: [new StepOutputHistoryEntry(1, "Analyze", viewJson)]);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed);
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        doc.RootElement.GetProperty("keep")[0].GetProperty("fromId").GetString().Should().Be("s0");
        doc.RootElement.GetProperty("room").GetProperty("degraded").GetBoolean().Should().BeTrue();

        soloAgent.Verify(a => a.RunAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Room_failure_with_fallback_disabled_fails_cleanly_with_EDIT_ROOM_FAILED()
    {
        EditRoomStepExecutor executor = CreateExecutor(
            out Mock<IAgentChatClientProvider> chatClients, out _, out _, out _);

        chatClients
            .Setup(c => c.GetAsync(It.IsAny<AgentType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no chat-capable provider configured"));

        string config = JsonSerializer.Serialize(
            new EditRoomStepConfig(View: new ExtractInputRef(ExtractInputSource.Previous), FallbackToSoloEditor: false),
            ConfigOptions);
        string viewJson = BuildViewJson("s0", "s1");
        StepExecutionContext context = CreateContext(config, history: [new StepOutputHistoryEntry(1, "Analyze", viewJson)]);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        ErrorCode(result).Should().Be("EDIT_ROOM_FAILED");
    }

    [Fact]
    public async Task Solo_fallback_drops_a_Keep_span_referencing_an_id_that_was_never_offered()
    {
        EditRoomStepExecutor executor = CreateExecutor(
            out Mock<IAgentChatClientProvider> chatClients, out _, out Mock<IAgentRegistry> agentRegistry, out _);

        chatClients
            .Setup(c => c.GetAsync(It.IsAny<AgentType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no chat-capable provider configured"));

        // s0/s1 were offered; s99 was never shown to any agent.
        string decisionJson = JsonSerializer.Serialize(new
        {
            keep = new[]
            {
                new { fromId = "s0", toId = "s0", reason = "valid" },
                new { fromId = "s99", toId = "s99", reason = "hallucinated id" }
            },
            editRationale = "solo fallback",
            suggestedTitle = "Fallback Edit"
        });

        var soloAgent = new Mock<IReelForgeAgent>();
        soloAgent
            .Setup(a => a.RunAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult { Output = decisionJson, Success = true });
        agentRegistry.Setup(r => r.GetByType(AgentType.VideoStoryEditor, null)).Returns(soloAgent.Object);

        string config = JsonSerializer.Serialize(
            new EditRoomStepConfig(View: new ExtractInputRef(ExtractInputSource.Previous)), ConfigOptions);
        string viewJson = BuildViewJson("s0", "s1");
        StepExecutionContext context = CreateContext(config, history: [new StepOutputHistoryEntry(1, "Analyze", viewJson)]);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed);
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        System.Text.Json.Nodes.JsonArray? keep = JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonArray>(doc.RootElement.GetProperty("keep").GetRawText());
        keep.Should().HaveCount(1, "the span referencing the unoffered id 's99' must be dropped, never trusted");
        keep![0]!["fromId"]!.GetValue<string>().Should().Be("s0");
        doc.RootElement.GetProperty("room").GetProperty("droppedSpanCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task A_completed_room_run_persists_ChatTranscriptJson_with_one_entry_per_turn()
    {
        EditRoomStepExecutor executor = CreateExecutor(
            out Mock<IAgentChatClientProvider> chatClients, out _, out Mock<IAgentRegistry> agentRegistry, out _);

        // Every room-participant agent (each seat and the director) resolves through this same
        // provider call — a single canned FakeChatClient stands in for a real model, mentioning
        // the offered id "s1" so ExtractOfferedIdMentions has something to find.
        chatClients
            .Setup(c => c.GetAsync(It.IsAny<AgentType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FakeChatClient("Let's keep s1 through the intro."));

        // Outside the live chat loop, the director's STANDALONE synthesis call (via IAgentRegistry,
        // not IAgentChatClientProvider) converts the room's discussion into the final decision.
        string decisionJson = JsonSerializer.Serialize(new
        {
            keep = new[] { new { fromId = "s1", toId = "s1", reason = "room agreed" } },
            editRationale = "room synthesis",
            suggestedTitle = "Room Edit"
        });
        var directorAgent = new Mock<IReelForgeAgent>();
        directorAgent
            .Setup(a => a.RunAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult { Output = decisionJson, Success = true });
        agentRegistry
            .Setup(r => r.GetByType(AgentType.VideoEditDirector, null))
            .Returns(directorAgent.Object);

        // One seat, one round, a 2-turn ceiling, FixedTurns (ignore the sentinel/convergence
        // checks) — the smallest room shape that still produces a deterministic "seat then
        // director" transcript: turn 0 is the seat, turn 1 is the director.
        var config = new EditRoomStepConfig(
            View: new ExtractInputRef(ExtractInputSource.Previous),
            Seats: [new EditRoomSeat("Seat0", "first")],
            Rounds: 1,
            MaxTurns: 2,
            Termination: EditRoomTerminationMode.FixedTurns,
            RoomTimeoutSeconds: 20,
            PersistTranscript: false);
        string configJson = JsonSerializer.Serialize(config, ConfigOptions);
        string viewJson = BuildViewJson("s0", "s1");
        StepExecutionContext context = CreateContext(configJson, history: [new StepOutputHistoryEntry(1, "Analyze", viewJson)]);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed);
        result.ChatTranscriptJson.Should().NotBeNullOrEmpty();

        using JsonDocument transcriptDoc = JsonDocument.Parse(result.ChatTranscriptJson!);
        JsonElement.ArrayEnumerator turns = transcriptDoc.RootElement.EnumerateArray();
        List<JsonElement> turnList = turns.ToList();

        turnList.Should().HaveCount(2, "one seat turn plus one director turn, per the 1-seat/1-round/2-turn-ceiling config");

        turnList[0].GetProperty("turnIndex").GetInt32().Should().Be(0);
        turnList[0].GetProperty("speaker").GetString().Should().Be("Seat0");
        turnList[0].GetProperty("speakerRole").GetString().Should().Be("editor");
        turnList[0].GetProperty("text").GetString().Should().Be("Let's keep s1 through the intro.",
            "DB persistence uses the FULL untruncated turn text, unlike the ~600-char SSE broadcast");
        turnList[0].GetProperty("truncated").GetBoolean().Should().BeFalse();
        turnList[0].GetProperty("idsMentioned").EnumerateArray().Select(e => e.GetString()).Should().Contain("s1");

        turnList[1].GetProperty("turnIndex").GetInt32().Should().Be(1);
        turnList[1].GetProperty("speaker").GetString().Should().Be("Director");
        turnList[1].GetProperty("speakerRole").GetString().Should().Be("director");
    }

    /// <summary>
    /// Trivial canned <see cref="IChatClient"/> — always returns the same fixed assistant text
    /// regardless of the messages sent to it, on both the streaming path (what the group chat host
    /// actually drives participants through — see <c>EditRoomSeatAgent</c>'s doc comment) and the
    /// non-streaming path. Zero network calls.
    /// </summary>
    private sealed class FakeChatClient : IChatClient
    {
        private readonly string _text;

        public FakeChatClient(string text) => _text = text;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _text)));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, _text);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
