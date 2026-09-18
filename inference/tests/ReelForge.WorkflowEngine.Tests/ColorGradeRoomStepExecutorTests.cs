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
/// Covers the parts of <see cref="ColorGradeRoomStepExecutor"/> reasonably testable without a
/// real chat-completions backend, mirroring <c>EditRoomStepExecutorTests</c>/
/// <c>GraphicsRoomStepExecutorTests</c>' approach exactly: config validation, view
/// resolution/offered-shot-id extraction, the grade room's distinctive validation (the decision
/// carries no ids, a "None" look is VALID, a view with zero shots completes with a "None" plan
/// instead of failing), the solo-fallback degrade path, and ChatTranscriptJson persistence
/// through a full canned-<see cref="IChatClient"/> room run. The live group chat's
/// scheduling/termination is exercised separately (and generically) by the manager suites.
/// </summary>
public class ColorGradeRoomStepExecutorTests
{
    private static readonly JsonSerializerOptions ConfigOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly Guid ProjectId = Guid.NewGuid();

    private static ColorGradeRoomStepExecutor CreateExecutor(
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

        // The real (not mocked) accessor — same rationale as the other room suites: it is a pure
        // AsyncLocal wrapper, and using it means these tests exercise BeginScope actually opening.
        IWorkflowExecutionContextAccessor executionContextAccessor = new WorkflowExecutionContextAccessor();

        return new ColorGradeRoomStepExecutor(
            chatClients.Object, toolProvider.Object, agentRegistry.Object, workspace.Object,
            executionContextAccessor, NullLogger<ColorGradeRoomStepExecutor>.Instance);
    }

    private static StepExecutionContext CreateContext(string? colorGradeRoomConfigJson, IReadOnlyList<StepOutputHistoryEntry>? history = null)
    {
        var step = new WorkflowStep
        {
            Id = Guid.NewGuid(),
            StepOrder = 3,
            StepType = StepType.ColorGradeRoom,
            ColorGradeRoomConfigJson = colorGradeRoomConfigJson
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

    private static string BuildViewJson(params string[] shotIds)
    {
        var shots = shotIds.Select((id, i) => new
        {
            id,
            startSec = i * 2.0,
            endSec = i * 2.0 + 2.0,
            v = new { temp = "Cool", tone = "Balanced", sat = "Natural" }
        });
        return JsonSerializer.Serialize(new
        {
            view = new { shots },
            meta = new { offeredIdCount = shotIds.Length }
        });
    }

    private static string BuildPlanJson(
        string look = "Warm", string strength = "Subtle", string shadowTone = "Neutral",
        string highlightTone = "Neutral", string rationale = "test") =>
        JsonSerializer.Serialize(new
        {
            look,
            strength,
            shadowTone,
            highlightTone,
            reason = "test reason",
            planRationale = rationale
        });

    private static string ErrorCode(StepExecutionResult result)
    {
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        return doc.RootElement.GetProperty("error").GetProperty("code").GetString() ?? string.Empty;
    }

    [Fact]
    public async Task Missing_config_json_fails_cleanly_with_COLOR_GRADE_ROOM_CONFIG_INVALID()
    {
        ColorGradeRoomStepExecutor executor = CreateExecutor(out _, out _, out _, out _);
        StepExecutionContext context = CreateContext(colorGradeRoomConfigJson: null);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        ErrorCode(result).Should().Be("COLOR_GRADE_ROOM_CONFIG_INVALID");
    }

    [Fact]
    public async Task Malformed_config_json_fails_cleanly_with_COLOR_GRADE_ROOM_CONFIG_INVALID()
    {
        ColorGradeRoomStepExecutor executor = CreateExecutor(out _, out _, out _, out _);
        StepExecutionContext context = CreateContext(colorGradeRoomConfigJson: "{not valid json");

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        ErrorCode(result).Should().Be("COLOR_GRADE_ROOM_CONFIG_INVALID");
    }

    [Fact]
    public async Task No_prior_step_output_to_resolve_the_view_from_fails_with_VIEW_UNRESOLVED()
    {
        ColorGradeRoomStepExecutor executor = CreateExecutor(out _, out _, out _, out _);
        string config = JsonSerializer.Serialize(new ColorGradeRoomStepConfig(View: new ExtractInputRef(ExtractInputSource.Previous)), ConfigOptions);
        StepExecutionContext context = CreateContext(config, history: []);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        ErrorCode(result).Should().Be("VIEW_UNRESOLVED");
    }

    [Fact]
    public async Task A_view_with_zero_shots_completes_with_a_None_plan_instead_of_failing()
    {
        // The distinctive grade-room empty-view behavior: nothing to grade legitimately means a
        // "None" plan — unlike the edit room, where a view with nothing to keep is an upstream
        // error, and mirroring the graphics room's empty-plan grace.
        ColorGradeRoomStepExecutor executor = CreateExecutor(out _, out _, out _, out _);
        string config = JsonSerializer.Serialize(new ColorGradeRoomStepConfig(View: new ExtractInputRef(ExtractInputSource.Previous)), ConfigOptions);
        string viewWithoutShots = JsonSerializer.Serialize(new
        {
            view = new { placements = new[] { new { id = "p0" } } },
            meta = new { }
        });
        StepExecutionContext context = CreateContext(config, history: [new StepOutputHistoryEntry(1, "Analyze", viewWithoutShots)]);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed);
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        doc.RootElement.GetProperty("look").GetString().Should().Be("None");
        doc.RootElement.GetProperty("room").GetProperty("terminationReason").GetString().Should().Be("empty-view");
        doc.RootElement.GetProperty("room").GetProperty("degraded").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Room_construction_failure_falls_back_to_the_solo_colorist_and_still_completes()
    {
        ColorGradeRoomStepExecutor executor = CreateExecutor(
            out Mock<IAgentChatClientProvider> chatClients, out _, out Mock<IAgentRegistry> agentRegistry, out _);

        // Force "the room failed to start" the cheapest reliable way — no fake IChatClient needed.
        chatClients
            .Setup(c => c.GetAsync(It.IsAny<AgentType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no chat-capable provider configured"));

        var soloAgent = new Mock<IReelForgeAgent>();
        soloAgent
            .Setup(a => a.RunAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult { Output = BuildPlanJson(rationale: "solo fallback"), Success = true });

        agentRegistry
            .Setup(r => r.GetByType(AgentType.Colorist, null))
            .Returns(soloAgent.Object);

        string config = JsonSerializer.Serialize(
            new ColorGradeRoomStepConfig(View: new ExtractInputRef(ExtractInputSource.Previous), FallbackToSoloColorist: true),
            ConfigOptions);
        StepExecutionContext context = CreateContext(config, history: [new StepOutputHistoryEntry(1, "Analyze", BuildViewJson("s0", "s1"))]);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed);
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        doc.RootElement.GetProperty("look").GetString().Should().Be("Warm");
        doc.RootElement.GetProperty("room").GetProperty("degraded").GetBoolean().Should().BeTrue();

        soloAgent.Verify(a => a.RunAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Room_failure_with_fallback_disabled_fails_cleanly_with_COLOR_GRADE_ROOM_FAILED()
    {
        ColorGradeRoomStepExecutor executor = CreateExecutor(
            out Mock<IAgentChatClientProvider> chatClients, out _, out _, out _);

        chatClients
            .Setup(c => c.GetAsync(It.IsAny<AgentType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no chat-capable provider configured"));

        string config = JsonSerializer.Serialize(
            new ColorGradeRoomStepConfig(View: new ExtractInputRef(ExtractInputSource.Previous), FallbackToSoloColorist: false),
            ConfigOptions);
        StepExecutionContext context = CreateContext(config, history: [new StepOutputHistoryEntry(1, "Analyze", BuildViewJson("s0"))]);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        ErrorCode(result).Should().Be("COLOR_GRADE_ROOM_FAILED");
    }

    [Fact]
    public async Task Solo_fallback_producing_a_None_plan_is_a_valid_completed_result()
    {
        // The grade room's analogue of the graphics room's "empty plan is valid": "None" is the
        // schema's first-class no-grade word, never a degrade.
        ColorGradeRoomStepExecutor executor = CreateExecutor(
            out Mock<IAgentChatClientProvider> chatClients, out _, out Mock<IAgentRegistry> agentRegistry, out _);

        chatClients
            .Setup(c => c.GetAsync(It.IsAny<AgentType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no chat-capable provider configured"));

        var soloAgent = new Mock<IReelForgeAgent>();
        soloAgent
            .Setup(a => a.RunAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult
            {
                Output = BuildPlanJson(look: "None", rationale: "footage needs no grade"),
                Success = true
            });
        agentRegistry.Setup(r => r.GetByType(AgentType.Colorist, null)).Returns(soloAgent.Object);

        string config = JsonSerializer.Serialize(
            new ColorGradeRoomStepConfig(View: new ExtractInputRef(ExtractInputSource.Previous)), ConfigOptions);
        StepExecutionContext context = CreateContext(config, history: [new StepOutputHistoryEntry(1, "Analyze", BuildViewJson("s0"))]);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed);
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        doc.RootElement.GetProperty("look").GetString().Should().Be("None");
        doc.RootElement.GetProperty("planRationale").GetString().Should().Be("footage needs no grade");
        doc.RootElement.GetProperty("room").GetProperty("droppedSpanCount").GetInt32().Should().Be(0,
            "the decision carries no ids, so nothing can ever be dropped by offered-id filtering");
    }

    [Fact]
    public async Task A_completed_room_run_persists_ChatTranscriptJson_with_one_entry_per_turn()
    {
        ColorGradeRoomStepExecutor executor = CreateExecutor(
            out Mock<IAgentChatClientProvider> chatClients, out _, out Mock<IAgentRegistry> agentRegistry, out _);

        // Every room-participant agent (each seat and the director) resolves through this same
        // provider call — a single canned FakeChatClient stands in for a real model, mentioning
        // the offered id "s1" so ExtractOfferedIdMentions has something to find.
        chatClients
            .Setup(c => c.GetAsync(It.IsAny<AgentType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FakeChatClient("s1 leans cool; a subtle warm lift suits it."));

        // Outside the live chat loop, the director's STANDALONE synthesis call (via
        // IAgentRegistry, not IAgentChatClientProvider) converts the discussion into the plan.
        var directorAgent = new Mock<IReelForgeAgent>();
        directorAgent
            .Setup(a => a.RunAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult { Output = BuildPlanJson(rationale: "room synthesis"), Success = true });
        agentRegistry
            .Setup(r => r.GetByType(AgentType.ColorGradeDirector, null))
            .Returns(directorAgent.Object);

        // One seat, one round, a 2-turn ceiling, FixedTurns — the smallest room shape that still
        // produces a deterministic "seat then director" transcript, same as the other rooms.
        var config = new ColorGradeRoomStepConfig(
            View: new ExtractInputRef(ExtractInputSource.Previous),
            Seats: [new EditRoomSeat("Seat0", "first")],
            Rounds: 1,
            MaxTurns: 2,
            Termination: EditRoomTerminationMode.FixedTurns,
            RoomTimeoutSeconds: 20,
            PersistTranscript: false);
        string configJson = JsonSerializer.Serialize(config, ConfigOptions);
        StepExecutionContext context = CreateContext(configJson, history: [new StepOutputHistoryEntry(1, "Analyze", BuildViewJson("s0", "s1"))]);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed);
        result.ChatTranscriptJson.Should().NotBeNullOrEmpty();

        using JsonDocument transcriptDoc = JsonDocument.Parse(result.ChatTranscriptJson!);
        List<JsonElement> turnList = transcriptDoc.RootElement.EnumerateArray().ToList();

        turnList.Should().HaveCount(2, "one seat turn plus one director turn, per the 1-seat/1-round/2-turn-ceiling config");

        turnList[0].GetProperty("turnIndex").GetInt32().Should().Be(0);
        turnList[0].GetProperty("speaker").GetString().Should().Be("Seat0");
        turnList[0].GetProperty("speakerRole").GetString().Should().Be("editor");
        turnList[0].GetProperty("idsMentioned").EnumerateArray().Select(e => e.GetString()).Should().Contain("s1");

        turnList[1].GetProperty("speaker").GetString().Should().Be("Director");
        turnList[1].GetProperty("speakerRole").GetString().Should().Be("director");

        using JsonDocument doc = JsonDocument.Parse(result.Output);
        doc.RootElement.GetProperty("look").GetString().Should().Be("Warm");
        doc.RootElement.GetProperty("room").GetProperty("degraded").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Synthesis_with_an_empty_look_is_retried_and_an_unknown_look_spends_one_retry_then_passes_through()
    {
        ColorGradeRoomStepExecutor executor = CreateExecutor(
            out Mock<IAgentChatClientProvider> chatClients, out _, out Mock<IAgentRegistry> agentRegistry, out _);

        chatClients
            .Setup(c => c.GetAsync(It.IsAny<AgentType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FakeChatClient("s0 is fine as-is."));

        // Attempt 1 returns an EMPTY look (hard-rejected, retried); attempt 2 returns an unknown
        // word ("Sepia") on the FINAL attempt — accepted as-is per the base template's
        // CheckRetryableIssue contract, and left to the compile step's own unknown_look_word
        // degrade downstream.
        int calls = 0;
        var directorAgent = new Mock<IReelForgeAgent>();
        directorAgent
            .Setup(a => a.RunAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AgentRunResult
            {
                Output = ++calls == 1 ? BuildPlanJson(look: "") : BuildPlanJson(look: "Sepia"),
                Success = true
            });
        agentRegistry.Setup(r => r.GetByType(AgentType.ColorGradeDirector, null)).Returns(directorAgent.Object);

        var config = new ColorGradeRoomStepConfig(
            View: new ExtractInputRef(ExtractInputSource.Previous),
            Seats: [new EditRoomSeat("Seat0", "first")],
            Rounds: 1,
            MaxTurns: 2,
            Termination: EditRoomTerminationMode.FixedTurns,
            RoomTimeoutSeconds: 20,
            PersistTranscript: false,
            MaxSynthesisAttempts: 2);
        string configJson = JsonSerializer.Serialize(config, ConfigOptions);
        StepExecutionContext context = CreateContext(configJson, history: [new StepOutputHistoryEntry(1, "Analyze", BuildViewJson("s0"))]);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        calls.Should().Be(2);
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        doc.RootElement.GetProperty("look").GetString().Should().Be("Sepia");
        doc.RootElement.GetProperty("room").GetProperty("synthesisAttempts").GetInt32().Should().Be(2);
        doc.RootElement.GetProperty("room").GetProperty("degraded").GetBoolean().Should().BeFalse();
    }

    /// <summary>
    /// Trivial canned <see cref="IChatClient"/> — always returns the same fixed assistant text on
    /// both paths (the group chat host drives participants through the streaming one). Zero
    /// network calls. Same shape as the other room suites' FakeChatClient.
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
