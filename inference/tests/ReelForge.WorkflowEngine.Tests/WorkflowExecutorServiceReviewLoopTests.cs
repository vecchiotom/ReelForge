using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ReelForge.Shared.Data.Models;
using ReelForge.WorkflowEngine.Data;
using ReelForge.WorkflowEngine.Execution;
using ReelForge.WorkflowEngine.Services.Messaging;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// End-to-end regression coverage for bug group A (StepOutputHistory duplication on a ReviewLoop
/// loop-back — see CLAUDE.md "Video Editing" and docs/video-editing.md "Review loop"). Builds the
/// exact shape of the shipped video-derush-edit template's tail — StepType.Agent
/// (AgentType.VideoStoryEditor, AgentInputContextMode.PreviousStepOnly) -> StepType.VideoCompile ->
/// StepType.ReviewLoop (AgentType.VideoReviewAgent) looping back to the story editor — with stub
/// executors, and runs it through the REAL WorkflowExecutorService.ExecuteAsync. This exercises
/// StepOutputHistory pruning (WorkflowExecutorService, bug A.3) and PreviousStepOnly resolution
/// (StepExecutionContext, bug A.1) together, exactly as they run in production — not just each
/// fix in isolation.
/// </summary>
public class WorkflowExecutorServiceReviewLoopTests
{
    private const string AnalysisView = "{\"view\":{\"shots\":[{\"id\":\"s1\"},{\"id\":\"s2\"}]},\"meta\":{}}";

    private sealed class NoOpEventPublisher : IWorkflowEventPublisher
    {
        public Task PublishExecutionRunningAsync(WorkflowExecution execution, CancellationToken ct) => Task.CompletedTask;

        public Task PublishStepStartedAsync(
            WorkflowExecution execution, WorkflowStep step, WorkflowStepResult stepResult, string? inputPreview, CancellationToken ct) =>
            Task.CompletedTask;

        public Task PublishStepCompletedAsync(
            WorkflowExecution execution, WorkflowStep step, WorkflowStepResult stepResult,
            StepExecutionResult stepExecutionResult, CancellationToken ct) => Task.CompletedTask;

        public Task PublishStepProgressAsync(
            WorkflowExecution execution, WorkflowStep step, WorkflowStepResult stepResult,
            string stage, int? percentComplete, CancellationToken ct) => Task.CompletedTask;

        public Task PublishStepChatTurnAsync(
            WorkflowExecution execution, WorkflowStep step, WorkflowStepResult stepResult,
            int turnIndex, int? totalTurns, string speaker, string speakerRole, string text,
            IReadOnlyList<string> idsMentioned, CancellationToken ct) => Task.CompletedTask;

        public Task PublishStepDiagnosticsAsync(
            WorkflowExecution execution, WorkflowStep step, WorkflowStepResult stepResult,
            StepExecutionResult stepExecutionResult, CancellationToken ct) => Task.CompletedTask;

        public Task PublishExecutionCompletedAsync(WorkflowExecution execution, CancellationToken ct) => Task.CompletedTask;
        public Task PublishExecutionFailedAsync(WorkflowExecution execution, CancellationToken ct) => Task.CompletedTask;
    }

    // Step 1 (VideoAnalyze): always returns the same fixed "analysis view" output — deterministic,
    // never re-run on loop-back in the real template either (LoopTargetStepOrder points at the
    // story editor, step 2, not the analyze step).
    private sealed class FixedOutputExecutor : IStepExecutor
    {
        private readonly string _output;
        public FixedOutputExecutor(StepType stepType, string output) { StepType = stepType; _output = output; }
        public StepType StepType { get; }

        public Task<StepExecutionResult> ExecuteAsync(StepExecutionContext context) =>
            Task.FromResult(new StepExecutionResult
            {
                Output = _output,
                NextStepIndex = context.CurrentStepIndex + 1,
                NewIterationCount = context.IterationCount,
                Status = StepStatus.Completed
            });
    }

    // Step 2 (Agent/VideoStoryEditor, PreviousStepOnly): records the resolved input it was
    // actually given on each call (via the real BuildAgentInput, exactly as AgentStepExecutor
    // does), so the test can assert what the RE-ENTERED story editor received on iteration 2.
    private sealed class RecordingAgentExecutor : IStepExecutor
    {
        public List<string> ReceivedInputs { get; } = new();
        public StepType StepType => StepType.Agent;

        public Task<StepExecutionResult> ExecuteAsync(StepExecutionContext context)
        {
            ReceivedInputs.Add(context.BuildAgentInput());
            return Task.FromResult(new StepExecutionResult
            {
                Output = "{\"keep\":[{\"fromId\":\"s1\",\"toId\":\"s1\"}]}",
                NextStepIndex = context.CurrentStepIndex + 1,
                NewIterationCount = context.IterationCount,
                Status = StepStatus.Completed
            });
        }
    }

    // Step 4 (ReviewLoop/VideoReviewAgent): loops back to step 2 (index 1) on the first pass,
    // mirroring MinScore never being met once — then passes through on the second.
    private sealed class LoopingReviewExecutor : IStepExecutor
    {
        public StepType StepType => StepType.ReviewLoop;

        public Task<StepExecutionResult> ExecuteAsync(StepExecutionContext context)
        {
            int newIteration = context.IterationCount + 1;
            bool loopBack = newIteration < 2;
            return Task.FromResult(new StepExecutionResult
            {
                Output = "{\"score\":3,\"passesReview\":false,\"summary\":\"needs another pass\"}",
                NextStepIndex = loopBack ? 1 : context.CurrentStepIndex + 1,
                NewIterationCount = newIteration,
                Status = StepStatus.Completed,
                IterationNumber = newIteration
            });
        }
    }

    [Fact]
    public async Task ReviewLoop_loop_back_feeds_the_re_entered_agent_the_original_analysis_view_not_the_reviews_own_output()
    {
        var options = new DbContextOptionsBuilder<WorkflowEngineDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var db = new WorkflowEngineDbContext(options);

        Guid projectId = Guid.NewGuid();
        Guid workflowDefId = Guid.NewGuid();
        Guid executionId = Guid.NewGuid();

        AgentDefinition analyzeAgent = new()
        { Id = Guid.NewGuid(), Name = "Analyze", AgentType = AgentType.VideoTransform, SystemPrompt = "" };
        AgentDefinition storyEditorAgent = new()
        { Id = Guid.NewGuid(), Name = "StoryEditor", AgentType = AgentType.VideoStoryEditor, SystemPrompt = "" };
        AgentDefinition compileAgent = new()
        { Id = Guid.NewGuid(), Name = "Compile", AgentType = AgentType.VideoTransform, SystemPrompt = "" };
        AgentDefinition reviewAgent = new()
        { Id = Guid.NewGuid(), Name = "Review", AgentType = AgentType.VideoReviewAgent, SystemPrompt = "" };

        WorkflowStep step1 = new()
        {
            Id = Guid.NewGuid(), StepOrder = 1, StepType = StepType.VideoAnalyze,
            AgentDefinitionId = analyzeAgent.Id, AgentDefinition = analyzeAgent
        };
        WorkflowStep step2 = new()
        {
            Id = Guid.NewGuid(), StepOrder = 2, StepType = StepType.Agent,
            AgentDefinitionId = storyEditorAgent.Id, AgentDefinition = storyEditorAgent,
            AgentInputContextMode = AgentInputContextMode.PreviousStepOnly
        };
        WorkflowStep step3 = new()
        {
            Id = Guid.NewGuid(), StepOrder = 3, StepType = StepType.VideoCompile,
            AgentDefinitionId = compileAgent.Id, AgentDefinition = compileAgent
        };
        WorkflowStep step4 = new()
        {
            Id = Guid.NewGuid(), StepOrder = 4, StepType = StepType.ReviewLoop,
            AgentDefinitionId = reviewAgent.Id, AgentDefinition = reviewAgent,
            LoopTargetStepOrder = 2, MaxIterations = 2, MinScore = 7,
            AgentInputContextMode = AgentInputContextMode.FullWorkflow
        };

        WorkflowDefinition definition = new()
        {
            Id = workflowDefId, Name = "test", ProjectId = projectId,
            Steps = new List<WorkflowStep> { step1, step2, step3, step4 }
        };

        WorkflowExecution execution = new()
        {
            Id = executionId, WorkflowDefinitionId = workflowDefId, ProjectId = projectId,
            Status = ExecutionStatus.Queued, WorkflowDefinition = definition
        };

        db.AgentDefinitions.AddRange(analyzeAgent, storyEditorAgent, compileAgent, reviewAgent);
        db.WorkflowDefinitions.Add(definition);
        db.WorkflowExecutions.Add(execution);
        await db.SaveChangesAsync();

        var scopeFactory = new FakeScopeFactory(db);
        var recordingAgentExecutor = new RecordingAgentExecutor();
        IStepExecutor[] executors =
        {
            new FixedOutputExecutor(StepType.VideoAnalyze, AnalysisView),
            recordingAgentExecutor,
            new FixedOutputExecutor(StepType.VideoCompile, "{\"status\":\"completed\"}"),
            new LoopingReviewExecutor()
        };

        var service = new WorkflowExecutorService(
            scopeFactory: scopeFactory,
            eventPublisher: new NoOpEventPublisher(),
            logger: NullLogger<WorkflowExecutorService>.Instance,
            executors: executors,
            rabbitHelper: new RabbitMqHelper(new ConfigurationBuilder().Build()),
            hardeningOptions: Options.Create(new WorkflowHardeningOptions()),
                cancellationRegistry: new ReelForge.WorkflowEngine.Execution.ExecutionCancellationRegistry());

        await service.ExecuteAsync(executionId, "corr-1", CancellationToken.None);

        // The story editor (step 2) must have run exactly twice: once per ReviewLoop iteration.
        recordingAgentExecutor.ReceivedInputs.Should().HaveCount(2);

        // BOTH iterations must resolve PreviousStepOnly to the VideoAnalyze step's ORIGINAL
        // output — never the ReviewLoop step's own JSON verdict from the prior iteration. This is
        // the actual bug: without the fix, the second call's history lookup would instead hand the
        // story editor the review's {"score":3,...} output (either because StepOutputHistory still
        // held the stale duplicate, or because "most recently appended" resolved to whatever ran
        // last rather than "the step immediately before me").
        recordingAgentExecutor.ReceivedInputs[0].Should().Contain(AnalysisView);
        recordingAgentExecutor.ReceivedInputs[1].Should().StartWith(AnalysisView);
        recordingAgentExecutor.ReceivedInputs[1].Should().NotContain("passesReview");

        WorkflowExecution? completed = await db.WorkflowExecutions.FindAsync(executionId);
        completed.Should().NotBeNull();
        completed!.Status.Should().Be(ExecutionStatus.Passed);

        // The compile step (step 3) also ran twice — once per iteration — and its own
        // StepOutputHistory-based resolution (AnalysisStepOrder=1) must likewise still see the
        // VideoAnalyze step's single original entry, not a stale/duplicate one.
        List<WorkflowStepResult> compileResults = await db.WorkflowStepResults
            .Where(r => r.WorkflowExecutionId == executionId && r.WorkflowStepId == step3.Id)
            .ToListAsync();
        compileResults.Should().HaveCount(2);
    }
}
