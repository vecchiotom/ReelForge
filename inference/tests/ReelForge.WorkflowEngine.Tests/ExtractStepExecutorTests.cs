using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using ReelForge.Shared.Data.Models;
using ReelForge.WorkflowEngine.Agents.Tools;
using ReelForge.WorkflowEngine.Execution;
using ReelForge.WorkflowEngine.Execution.StepExecutors;
using ReelForge.WorkflowEngine.Services.Messaging;
using ReelForge.WorkflowEngine.Services.Storage;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

public class ExtractStepExecutorTests
{
    private static ExtractStepExecutor CreateExecutor(IProjectFileWorkspace? workspace = null) =>
        new(workspace ?? Mock.Of<IProjectFileWorkspace>(), NullLogger<ExtractStepExecutor>.Instance);

    private static StepExecutionContext CreateContext(
        string extractConfigJson,
        IReadOnlyList<StepOutputHistoryEntry>? history = null,
        Guid? projectId = null)
    {
        var step = new WorkflowStep
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = Guid.NewGuid(),
            AgentDefinitionId = Guid.NewGuid(),
            StepOrder = 99,
            StepType = StepType.Extract,
            ExtractConfigJson = extractConfigJson
        };

        return new StepExecutionContext
        {
            Execution = new WorkflowExecution { ProjectId = projectId ?? Guid.NewGuid() },
            Step = step,
            AllSteps = [step],
            AccumulatedOutput = string.Empty,
            StepOutputHistory = history ?? [],
            CurrentStepIndex = 0,
            IterationCount = 0,
            CorrelationId = "test",
            CancellationToken = CancellationToken.None
        };
    }

    private static JsonElement ParseOutput(StepExecutionResult result) =>
        JsonDocument.Parse(result.Output).RootElement.Clone();

    // -----------------------------------------------------------------
    // project
    // -----------------------------------------------------------------

    [Fact]
    public async Task Project_on_500_item_array_with_take_10_returns_10_items_and_correct_meta()
    {
        var items = Enumerable.Range(0, 500)
            .Select(i => new { id = $"c{i}", name = $"Comp{i}", filePath = $"src/Comp{i}.tsx" });
        string sourceJson = JsonSerializer.Serialize(new { components = items });

        const string config = """
            {"version":1,"operation":"project","inputs":{"source":{"from":"previous"}},"path":"$.components","fields":["name","filePath"],"take":10}
            """;

        StepExecutionContext context = CreateContext(
            config,
            history: [new StepOutputHistoryEntry(1, "Inventory", sourceJson)]);

        StepExecutionResult result = await CreateExecutor().ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed);
        JsonElement root = ParseOutput(result);

        root.GetProperty("view").GetProperty("items").GetArrayLength().Should().Be(10);
        root.GetProperty("meta").GetProperty("totalItemCount").GetInt32().Should().Be(500);
        root.GetProperty("meta").GetProperty("itemCount").GetInt32().Should().Be(10);
        root.GetProperty("meta").GetProperty("truncated").GetBoolean().Should().BeTrue();
        root.GetProperty("meta").GetProperty("droppedItems").GetInt32().Should().Be(490);
    }

    [Fact]
    public async Task MaxOutputChars_forces_item_drops_and_output_still_parses_as_json()
    {
        var items = Enumerable.Range(0, 200)
            .Select(i => new { id = $"item-{i}", description = new string('x', 50) });
        string sourceJson = JsonSerializer.Serialize(new { components = items });

        // No take/skip — the cap alone must force items to be dropped.
        const string config = """
            {"version":1,"operation":"project","inputs":{"source":{"from":"previous"}},"path":"$.components","maxOutputChars":1000}
            """;

        StepExecutionContext context = CreateContext(
            config,
            history: [new StepOutputHistoryEntry(1, "Inventory", sourceJson)]);

        StepExecutionResult result = await CreateExecutor().ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed);

        Action parse = () => JsonDocument.Parse(result.Output);
        parse.Should().NotThrow("the executor must never emit invalid/truncated-mid-JSON output");

        JsonElement root = ParseOutput(result);
        int itemCount = root.GetProperty("meta").GetProperty("itemCount").GetInt32();
        int totalItemCount = root.GetProperty("meta").GetProperty("totalItemCount").GetInt32();

        totalItemCount.Should().Be(200);
        itemCount.Should().BeLessThan(totalItemCount);
        root.GetProperty("meta").GetProperty("truncated").GetBoolean().Should().BeTrue();
        result.Output.Length.Should().BeLessThanOrEqualTo(2000); // generous ceiling above the 1000-char view cap (meta adds overhead)
    }

    [Fact]
    public async Task Project_expect_minItems_violation_returns_Failed_with_EXPECT_FAILED()
    {
        string sourceJson = JsonSerializer.Serialize(new { components = Array.Empty<object>() });

        const string config = """
            {"version":1,"operation":"project","inputs":{"source":{"from":"previous"}},"path":"$.components","expect":{"minItems":1}}
            """;

        StepExecutionContext context = CreateContext(
            config,
            history: [new StepOutputHistoryEntry(1, "Inventory", sourceJson)]);

        StepExecutionResult result = await CreateExecutor().ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        JsonElement root = ParseOutput(result);
        root.GetProperty("view").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("error").GetProperty("code").GetString().Should().Be("EXPECT_FAILED");
    }

    // -----------------------------------------------------------------
    // resolve
    // -----------------------------------------------------------------

    [Fact]
    public async Task Resolve_with_unknown_id_and_onUnknownId_fail_returns_Failed_with_UNKNOWN_ID()
    {
        string idsJson = JsonSerializer.Serialize(new { selectedIds = new[] { "a", "does-not-exist" } });
        string recordsJson = JsonSerializer.Serialize(new
        {
            view = new { items = new[] { new { id = "a", name = "A" } } }
        });

        const string config = """
            {"version":1,"operation":"resolve","inputs":{"ids":{"from":"step","stepOrder":1},"records":{"from":"step","stepOrder":2}},"idsPath":"$.selectedIds","recordsPath":"$.view.items","onUnknownId":"fail"}
            """;

        StepExecutionContext context = CreateContext(
            config,
            history:
            [
                new StepOutputHistoryEntry(1, "Ids", idsJson),
                new StepOutputHistoryEntry(2, "Records", recordsJson)
            ]);

        StepExecutionResult result = await CreateExecutor().ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        JsonElement root = ParseOutput(result);
        root.GetProperty("error").GetProperty("code").GetString().Should().Be("UNKNOWN_ID");
    }

    [Fact]
    public async Task Resolve_preserves_order_of_ids_not_order_of_records()
    {
        string idsJson = JsonSerializer.Serialize(new { selectedIds = new[] { "c3", "c1" } });
        string recordsJson = JsonSerializer.Serialize(new
        {
            view = new
            {
                items = new[]
                {
                    new { id = "c1", name = "One" },
                    new { id = "c3", name = "Three" }
                }
            }
        });

        const string config = """
            {"version":1,"operation":"resolve","inputs":{"ids":{"from":"step","stepOrder":1},"records":{"from":"step","stepOrder":2}},"idsPath":"$.selectedIds","recordsPath":"$.view.items","onUnknownId":"skip"}
            """;

        StepExecutionContext context = CreateContext(
            config,
            history:
            [
                new StepOutputHistoryEntry(1, "Ids", idsJson),
                new StepOutputHistoryEntry(2, "Records", recordsJson)
            ]);

        StepExecutionResult result = await CreateExecutor().ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed);
        JsonElement items = ParseOutput(result).GetProperty("view").GetProperty("items");

        items.GetArrayLength().Should().Be(2);
        items[0].GetProperty("name").GetString().Should().Be("Three"); // c3 requested first
        items[1].GetProperty("name").GetString().Should().Be("One");   // c1 requested second
    }

    // -----------------------------------------------------------------
    // files
    // -----------------------------------------------------------------

    [Fact]
    public async Task Files_op_with_mocked_workspace_returns_capped_inventory()
    {
        Guid projectId = Guid.NewGuid();
        List<ProjectWorkspaceFile> files = Enumerable.Range(0, 20)
            .Select(i => new ProjectWorkspaceFile(
                Guid.NewGuid(),
                projectId,
                $"File{i}.tsx",
                $"src/File{i}.tsx",
                "sourceFiles",
                $"projects/{projectId}/sourceFiles/File{i}.tsx",
                "text/plain",
                1024,
                DateTime.UtcNow,
                $"Summary of file {i} with some descriptive text to add bulk to the payload."))
            .ToList();

        var workspaceMock = new Mock<IProjectFileWorkspace>();
        workspaceMock
            .Setup(w => w.ListFilesAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(files);

        const string config = """
            {"version":1,"operation":"files","inputs":{},"maxOutputChars":800}
            """;

        StepExecutionContext context = CreateContext(config, projectId: projectId);
        StepExecutionResult result = await CreateExecutor(workspaceMock.Object).ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed);
        JsonElement root = ParseOutput(result);

        root.GetProperty("meta").GetProperty("totalItemCount").GetInt32().Should().Be(20);
        int itemCount = root.GetProperty("meta").GetProperty("itemCount").GetInt32();
        itemCount.Should().BeLessThan(20, "the small maxOutputChars cap must force the 20-file inventory to be capped");
        root.GetProperty("view").GetProperty("items").GetArrayLength().Should().Be(itemCount);

        workspaceMock.Verify(w => w.ListFilesAsync(projectId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------
    // Purity guard
    // -----------------------------------------------------------------

    [Fact]
    public void Constructor_has_no_IChatClient_or_IAgentRegistry_dependency()
    {
        ConstructorInfo ctor = typeof(ExtractStepExecutor).GetConstructors().Single();
        ParameterInfo[] parameters = ctor.GetParameters();

        parameters.Should().NotContain(p => p.ParameterType.Name.Contains("IChatClient"));
        parameters.Should().NotContain(p => p.ParameterType.Name.Contains("IAgentRegistry"));
    }

    [Fact]
    public void StepType_is_Extract()
    {
        CreateExecutor().StepType.Should().Be(StepType.Extract);
    }

    // -----------------------------------------------------------------
    // WorkflowExecutorService integration points
    // -----------------------------------------------------------------

    [Fact]
    public void ResolveMaxRetries_returns_1_for_Extract_steps()
    {
        var service = new WorkflowExecutorService(
            scopeFactory: null!,
            eventPublisher: null!,
            logger: NullLogger<WorkflowExecutorService>.Instance,
            executors: Array.Empty<IStepExecutor>(),
            rabbitHelper: new RabbitMqHelper(new ConfigurationBuilder().Build()),
            hardeningOptions: Options.Create(new WorkflowHardeningOptions { MaxStepRetries = 3 }));

        MethodInfo method = typeof(WorkflowExecutorService).GetMethod(
            "ResolveMaxRetries", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var step = new WorkflowStep { StepType = StepType.Extract, StepOrder = 1 };
        var maxRetries = (int)method.Invoke(service, [step])!;

        maxRetries.Should().Be(1);
    }
}
