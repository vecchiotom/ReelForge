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
/// Covers the parts of <see cref="GraphicsRoomStepExecutor"/> reasonably testable without a real
/// chat-completions backend, mirroring <c>EditRoomStepExecutorTests</c>'s approach exactly:
/// config validation, view resolution/offered-placement-id extraction, the graphics room's
/// distinctive validation (an empty overlay plan is VALID; a view with zero placements completes
/// with an empty plan instead of failing), the solo-fallback degrade path, offered-id filtering,
/// and ChatTranscriptJson persistence through a full canned-<see cref="IChatClient"/> room run.
/// The live group chat's scheduling/termination is exercised separately by
/// <c>GraphicsRoomGroupChatManagerTests</c> (and generically by <c>EditRoomGroupChatManagerTests</c>).
/// </summary>
public class GraphicsRoomStepExecutorTests
{
    private static readonly JsonSerializerOptions ConfigOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly Guid ProjectId = Guid.NewGuid();

    private static GraphicsRoomStepExecutor CreateExecutor(
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

        // The annotator soft-fails to a no-op by contract; a canned no-op stands in for it here
        // (its actual inEdit math is covered by MotionGraphicsPlacementAnnotatorTests).
        var annotator = new Mock<IMotionGraphicsPlacementAnnotator>();
        annotator
            .Setup(a => a.AnnotateAsync(It.IsAny<StepExecutionContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // The real (not mocked) accessor — same rationale as EditRoomStepExecutorTests: it is a
        // pure AsyncLocal wrapper, and using it means these tests exercise BeginScope actually
        // being opened.
        IWorkflowExecutionContextAccessor executionContextAccessor = new WorkflowExecutionContextAccessor();

        return new GraphicsRoomStepExecutor(
            chatClients.Object, toolProvider.Object, agentRegistry.Object, workspace.Object,
            executionContextAccessor, annotator.Object,
            NullLogger<GraphicsRoomStepExecutor>.Instance);
    }

    private static StepExecutionContext CreateContext(string? graphicsRoomConfigJson, IReadOnlyList<StepOutputHistoryEntry>? history = null)
    {
        var step = new WorkflowStep
        {
            Id = Guid.NewGuid(),
            StepOrder = 3,
            StepType = StepType.GraphicsRoom,
            GraphicsRoomConfigJson = graphicsRoomConfigJson
        };

        return new StepExecutionContext
        {
            Execution = new WorkflowExecution { Id = Guid.NewGuid(), ProjectId = ProjectId },
            Step = step,
            AllSteps = [step],
            AccumulatedOutput = string.Empty,
            StepOutputHistory = history ?? [],
            CurrentStepIndex = 2,
            IterationCount = 0,
            CorrelationId = "test",
            CancellationToken = CancellationToken.None
        };
    }

    private static string BuildViewJson(params string[] placementIds)
    {
        var placements = placementIds.Select((id, i) => new
        {
            id,
            region = "LowerThird",
            startSec = i * 2.0,
            endSec = i * 2.0 + 2.0,
            fit = 80,
            text = "Light"
        });
        return JsonSerializer.Serialize(new
        {
            view = new { shots = new[] { new { id = "s0" } }, placements },
            meta = new { offeredPlacementIdCount = placementIds.Length }
        });
    }

    private static string ErrorCode(StepExecutionResult result)
    {
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        return doc.RootElement.GetProperty("error").GetProperty("code").GetString() ?? string.Empty;
    }

    [Fact]
    public async Task Missing_config_json_fails_cleanly_with_GRAPHICS_ROOM_CONFIG_INVALID()
    {
        GraphicsRoomStepExecutor executor = CreateExecutor(out _, out _, out _, out _);
        StepExecutionContext context = CreateContext(graphicsRoomConfigJson: null);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        ErrorCode(result).Should().Be("GRAPHICS_ROOM_CONFIG_INVALID");
    }

    [Fact]
    public async Task Malformed_config_json_fails_cleanly_with_GRAPHICS_ROOM_CONFIG_INVALID()
    {
        GraphicsRoomStepExecutor executor = CreateExecutor(out _, out _, out _, out _);
        StepExecutionContext context = CreateContext(graphicsRoomConfigJson: "{not valid json");

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        ErrorCode(result).Should().Be("GRAPHICS_ROOM_CONFIG_INVALID");
    }

    [Fact]
    public async Task No_prior_step_output_to_resolve_the_view_from_fails_with_VIEW_UNRESOLVED()
    {
        GraphicsRoomStepExecutor executor = CreateExecutor(out _, out _, out _, out _);
        string config = JsonSerializer.Serialize(new GraphicsRoomStepConfig(View: new ExtractInputRef(ExtractInputSource.Previous)), ConfigOptions);
        StepExecutionContext context = CreateContext(config, history: []);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        ErrorCode(result).Should().Be("VIEW_UNRESOLVED");
    }

    [Fact]
    public async Task A_view_with_zero_placements_completes_with_an_empty_plan_instead_of_failing()
    {
        // The distinctive graphics-room behavior: a video whose analysis offered no placement
        // candidates legitimately warrants zero overlays — unlike the edit room, where a view
        // with nothing to keep is an upstream error and fails VIEW_UNRESOLVED.
        GraphicsRoomStepExecutor executor = CreateExecutor(out _, out _, out _, out _);
        string config = JsonSerializer.Serialize(new GraphicsRoomStepConfig(View: new ExtractInputRef(ExtractInputSource.Previous)), ConfigOptions);
        string viewWithoutPlacements = JsonSerializer.Serialize(new
        {
            view = new { shots = new[] { new { id = "s0" } } },
            meta = new { }
        });
        StepExecutionContext context = CreateContext(config, history: [new StepOutputHistoryEntry(1, "Analyze", viewWithoutPlacements)]);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed);
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        doc.RootElement.GetProperty("overlays").GetArrayLength().Should().Be(0);
        doc.RootElement.GetProperty("room").GetProperty("terminationReason").GetString().Should().Be("empty-view");
        doc.RootElement.GetProperty("room").GetProperty("degraded").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Room_construction_failure_falls_back_to_the_solo_planner_and_still_completes()
    {
        GraphicsRoomStepExecutor executor = CreateExecutor(
            out Mock<IAgentChatClientProvider> chatClients, out _, out Mock<IAgentRegistry> agentRegistry, out _);

        // Force "the room failed to start" the cheapest reliable way — no fake IChatClient needed.
        chatClients
            .Setup(c => c.GetAsync(It.IsAny<AgentType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no chat-capable provider configured"));

        string planJson = JsonSerializer.Serialize(new
        {
            overlays = new[]
            {
                new { placementId = "p0", kind = "LowerThird", text = "Hello", subtext = "", duration = "Short", emphasis = "Normal", renderedAssetStorageKey = "", reason = "solo fallback" }
            },
            planRationale = "solo fallback"
        });

        var soloAgent = new Mock<IReelForgeAgent>();
        soloAgent
            .Setup(a => a.RunAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult { Output = planJson, Success = true });

        agentRegistry
            .Setup(r => r.GetByType(AgentType.MotionGraphicsPlanner, null))
            .Returns(soloAgent.Object);

        string config = JsonSerializer.Serialize(
            new GraphicsRoomStepConfig(View: new ExtractInputRef(ExtractInputSource.Previous), FallbackToSoloPlanner: true),
            ConfigOptions);
        string viewJson = BuildViewJson("p0", "p1");
        StepExecutionContext context = CreateContext(config, history: [new StepOutputHistoryEntry(1, "Analyze", viewJson)]);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed);
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        doc.RootElement.GetProperty("overlays")[0].GetProperty("placementId").GetString().Should().Be("p0");
        doc.RootElement.GetProperty("room").GetProperty("degraded").GetBoolean().Should().BeTrue();

        soloAgent.Verify(a => a.RunAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Room_failure_with_fallback_disabled_fails_cleanly_with_GRAPHICS_ROOM_FAILED()
    {
        GraphicsRoomStepExecutor executor = CreateExecutor(
            out Mock<IAgentChatClientProvider> chatClients, out _, out _, out _);

        chatClients
            .Setup(c => c.GetAsync(It.IsAny<AgentType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no chat-capable provider configured"));

        string config = JsonSerializer.Serialize(
            new GraphicsRoomStepConfig(View: new ExtractInputRef(ExtractInputSource.Previous), FallbackToSoloPlanner: false),
            ConfigOptions);
        string viewJson = BuildViewJson("p0");
        StepExecutionContext context = CreateContext(config, history: [new StepOutputHistoryEntry(1, "Analyze", viewJson)]);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        ErrorCode(result).Should().Be("GRAPHICS_ROOM_FAILED");
    }

    [Fact]
    public async Task Solo_fallback_drops_an_overlay_referencing_a_placement_id_that_was_never_offered()
    {
        GraphicsRoomStepExecutor executor = CreateExecutor(
            out Mock<IAgentChatClientProvider> chatClients, out _, out Mock<IAgentRegistry> agentRegistry, out _);

        chatClients
            .Setup(c => c.GetAsync(It.IsAny<AgentType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no chat-capable provider configured"));

        // p0 was offered; p99 was never shown to any agent.
        string planJson = JsonSerializer.Serialize(new
        {
            overlays = new[]
            {
                new { placementId = "p0", kind = "LowerThird", text = "valid", subtext = "", duration = "Short", emphasis = "Normal", renderedAssetStorageKey = "", reason = "ok" },
                new { placementId = "p99", kind = "Title", text = "hallucinated", subtext = "", duration = "Short", emphasis = "Normal", renderedAssetStorageKey = "", reason = "bad" }
            },
            planRationale = "solo fallback"
        });

        var soloAgent = new Mock<IReelForgeAgent>();
        soloAgent
            .Setup(a => a.RunAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult { Output = planJson, Success = true });
        agentRegistry.Setup(r => r.GetByType(AgentType.MotionGraphicsPlanner, null)).Returns(soloAgent.Object);

        string config = JsonSerializer.Serialize(
            new GraphicsRoomStepConfig(View: new ExtractInputRef(ExtractInputSource.Previous)), ConfigOptions);
        string viewJson = BuildViewJson("p0", "p1");
        StepExecutionContext context = CreateContext(config, history: [new StepOutputHistoryEntry(1, "Analyze", viewJson)]);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed);
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        doc.RootElement.GetProperty("overlays").GetArrayLength().Should().Be(1,
            "the overlay referencing the unoffered id 'p99' must be dropped, never trusted");
        doc.RootElement.GetProperty("overlays")[0].GetProperty("placementId").GetString().Should().Be("p0");
        doc.RootElement.GetProperty("room").GetProperty("droppedSpanCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Solo_fallback_producing_an_empty_plan_is_still_a_valid_completed_result()
    {
        // The second distinctive graphics-room behavior: unlike the edit room (where a solo
        // fallback with no usable Keep spans is an error), a plan with zero overlays is valid.
        GraphicsRoomStepExecutor executor = CreateExecutor(
            out Mock<IAgentChatClientProvider> chatClients, out _, out Mock<IAgentRegistry> agentRegistry, out _);

        chatClients
            .Setup(c => c.GetAsync(It.IsAny<AgentType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no chat-capable provider configured"));

        string planJson = JsonSerializer.Serialize(new
        {
            overlays = Array.Empty<object>(),
            planRationale = "nothing warrants an overlay"
        });

        var soloAgent = new Mock<IReelForgeAgent>();
        soloAgent
            .Setup(a => a.RunAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult { Output = planJson, Success = true });
        agentRegistry.Setup(r => r.GetByType(AgentType.MotionGraphicsPlanner, null)).Returns(soloAgent.Object);

        string config = JsonSerializer.Serialize(
            new GraphicsRoomStepConfig(View: new ExtractInputRef(ExtractInputSource.Previous)), ConfigOptions);
        string viewJson = BuildViewJson("p0");
        StepExecutionContext context = CreateContext(config, history: [new StepOutputHistoryEntry(1, "Analyze", viewJson)]);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed);
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        doc.RootElement.GetProperty("overlays").GetArrayLength().Should().Be(0);
        doc.RootElement.GetProperty("planRationale").GetString().Should().Be("nothing warrants an overlay");
    }

    [Fact]
    public async Task A_completed_room_run_persists_ChatTranscriptJson_with_one_entry_per_turn()
    {
        GraphicsRoomStepExecutor executor = CreateExecutor(
            out Mock<IAgentChatClientProvider> chatClients, out _, out Mock<IAgentRegistry> agentRegistry, out _);

        // Every room-participant agent (each seat and the director) resolves through this same
        // provider call — a single canned FakeChatClient stands in for a real model, mentioning
        // the offered id "p1" so ExtractOfferedIdMentions has something to find.
        chatClients
            .Setup(c => c.GetAsync(It.IsAny<AgentType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FakeChatClient("A lower third at p1 works best."));

        // Outside the live chat loop, the director's STANDALONE synthesis call (via IAgentRegistry,
        // not IAgentChatClientProvider) converts the room's discussion into the final plan.
        string planJson = JsonSerializer.Serialize(new
        {
            overlays = new[]
            {
                new { placementId = "p1", kind = "LowerThird", text = "Hello", subtext = "", duration = "Short", emphasis = "Normal", renderedAssetStorageKey = "", reason = "room agreed" }
            },
            planRationale = "room synthesis"
        });
        var directorAgent = new Mock<IReelForgeAgent>();
        directorAgent
            .Setup(a => a.RunAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult { Output = planJson, Success = true });
        agentRegistry
            .Setup(r => r.GetByType(AgentType.MotionGraphicsDirector, null))
            .Returns(directorAgent.Object);

        // One seat, one round, a 2-turn ceiling, FixedTurns — the smallest room shape that still
        // produces a deterministic "seat then director" transcript, same as the edit room's test.
        var config = new GraphicsRoomStepConfig(
            View: new ExtractInputRef(ExtractInputSource.Previous),
            Seats: [new EditRoomSeat("Seat0", "first")],
            Rounds: 1,
            MaxTurns: 2,
            Termination: EditRoomTerminationMode.FixedTurns,
            RoomTimeoutSeconds: 20,
            PersistTranscript: false);
        string configJson = JsonSerializer.Serialize(config, ConfigOptions);
        string viewJson = BuildViewJson("p0", "p1");
        StepExecutionContext context = CreateContext(configJson, history: [new StepOutputHistoryEntry(1, "Analyze", viewJson)]);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed);
        result.ChatTranscriptJson.Should().NotBeNullOrEmpty();

        using JsonDocument transcriptDoc = JsonDocument.Parse(result.ChatTranscriptJson!);
        List<JsonElement> turnList = transcriptDoc.RootElement.EnumerateArray().ToList();

        turnList.Should().HaveCount(2, "one seat turn plus one director turn, per the 1-seat/1-round/2-turn-ceiling config");

        turnList[0].GetProperty("turnIndex").GetInt32().Should().Be(0);
        turnList[0].GetProperty("speaker").GetString().Should().Be("Seat0");
        turnList[0].GetProperty("speakerRole").GetString().Should().Be("editor");
        turnList[0].GetProperty("text").GetString().Should().Be("A lower third at p1 works best.");
        turnList[0].GetProperty("idsMentioned").EnumerateArray().Select(e => e.GetString()).Should().Contain("p1");

        turnList[1].GetProperty("speaker").GetString().Should().Be("Director");
        turnList[1].GetProperty("speakerRole").GetString().Should().Be("director");

        using JsonDocument doc = JsonDocument.Parse(result.Output);
        doc.RootElement.GetProperty("overlays")[0].GetProperty("placementId").GetString().Should().Be("p1");
        doc.RootElement.GetProperty("room").GetProperty("degraded").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void Room_turn_tools_are_restricted_to_the_read_only_allowlist()
    {
        // The seats'/director's backing agent types carry sandbox+render tools for the standalone
        // synthesis call — an in-room prose turn must only ever see the ProjectRead +
        // WorkflowControl subset.
        var chatClients = new Mock<IAgentChatClientProvider>();
        var toolProvider = new Mock<IAgentToolProvider>();
        toolProvider
            .Setup(t => t.GetTools(AgentType.MotionGraphicsDirector))
            .Returns(
            [
                AIFunctionFactory.Create(() => "x", "ListProjectFiles"),
                AIFunctionFactory.Create(() => "x", "ReadProjectFile"),
                AIFunctionFactory.Create(() => "x", "EnsureSandbox"),
                AIFunctionFactory.Create(() => "x", "RenderVideoAndUploadToStorage"),
                AIFunctionFactory.Create(() => "x", "FailWorkflow")
            ]);
        var annotator = new Mock<IMotionGraphicsPlacementAnnotator>();
        annotator
            .Setup(a => a.AnnotateAsync(It.IsAny<StepExecutionContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var executor = new TestableGraphicsRoomStepExecutor(
            chatClients.Object, toolProvider.Object, new Mock<IAgentRegistry>().Object,
            new Mock<IProjectFileWorkspace>().Object, new WorkflowExecutionContextAccessor(),
            annotator.Object, NullLogger<GraphicsRoomStepExecutor>.Instance);

        IReadOnlyList<AIFunction> roomTurnTools = executor.GetRoomTurnToolsPublic(AgentType.MotionGraphicsDirector);

        roomTurnTools.Select(t => t.Name).Should().BeEquivalentTo(
            ["ListProjectFiles", "ReadProjectFile", "FailWorkflow"],
            "sandbox/render tools must never be reachable from a room-participant turn");
    }

    /// <summary>Exposes the protected room-turn tool restriction for direct assertion.</summary>
    private sealed class TestableGraphicsRoomStepExecutor : GraphicsRoomStepExecutor
    {
        public TestableGraphicsRoomStepExecutor(
            IAgentChatClientProvider chatClients,
            IAgentToolProvider toolProvider,
            IAgentRegistry agentRegistry,
            IProjectFileWorkspace workspace,
            IWorkflowExecutionContextAccessor executionContextAccessor,
            IMotionGraphicsPlacementAnnotator placementAnnotator,
            Microsoft.Extensions.Logging.ILogger<GraphicsRoomStepExecutor> logger)
            : base(chatClients, toolProvider, agentRegistry, workspace, executionContextAccessor, placementAnnotator, logger)
        {
        }

        public IReadOnlyList<AIFunction> GetRoomTurnToolsPublic(AgentType agentType) => GetRoomTurnTools(agentType);
    }

    /// <summary>
    /// Trivial canned <see cref="IChatClient"/> — always returns the same fixed assistant text on
    /// both paths (the group chat host drives participants through the streaming one). Zero
    /// network calls. Same shape as EditRoomStepExecutorTests' FakeChatClient.
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
