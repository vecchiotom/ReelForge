using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ReelForge.Shared.Data.Models;
using ReelForge.WorkflowEngine.Agents;
using ReelForge.WorkflowEngine.Agents.Tools;
using ReelForge.WorkflowEngine.Services.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.InMemory;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.Sqlite;
using ReelForge.WorkflowEngine.Data;
using ReelForge.WorkflowEngine.Execution;
using Xunit;
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace ReelForge.WorkflowEngine.Tests
{
    public class WorkflowExecutorServiceTests
    {
        private class ThrowingExecutor : IStepExecutor
        {
            public int CallCount { get; private set; }
            public StepType StepType => StepType.Agent;

            public Task<StepExecutionResult> ExecuteAsync(StepExecutionContext context)
            {
                CallCount++;
                throw new AgentWorkflowException("fatal error");
            }
        }

        [Fact]
        public async Task CancelExecutionAsync_removes_message_when_queued()
        {
            // prepare in-memory db with a queued execution
            var options = new DbContextOptionsBuilder<WorkflowEngineDbContext>()
                .UseInMemoryDatabase("testdb").Options;
            var db = new WorkflowEngineDbContext(options);
            var id = Guid.NewGuid();
            db.WorkflowExecutions.Add(new ReelForge.Shared.Data.Models.WorkflowExecution
            {
                Id = id,
                Status = ReelForge.Shared.Data.Models.ExecutionStatus.Queued
            });
            await db.SaveChangesAsync();

            // simple scope factory that always returns our in-memory context
            var scopeFactory = new FakeScopeFactory(db);

            var fakeHelper = new TestRabbitHelper();
            var service = new WorkflowExecutorService(
                scopeFactory: scopeFactory,
                eventPublisher: null!,
                logger: NullLogger<WorkflowExecutorService>.Instance,
                executors: new[] { new ThrowingExecutor() },
                rabbitHelper: fakeHelper,
                hardeningOptions: Options.Create(new WorkflowHardeningOptions()),
                cancellationRegistry: new ReelForge.WorkflowEngine.Execution.ExecutionCancellationRegistry());

            await service.CancelExecutionAsync(id, Guid.NewGuid());
            fakeHelper.Called.Should().BeTrue();
        }

        private sealed class TimingOutExecutor : IStepExecutor
        {
            public int CallCount { get; private set; }
            public StepType StepType => StepType.Agent;

            public Task<StepExecutionResult> ExecuteAsync(StepExecutionContext context)
            {
                CallCount++;
                throw new TimeoutException("Agent 'RemotionComponentTranslator' timed out after 10800 seconds.");
            }
        }

        [Fact]
        public async Task ExecuteStepWithRetry_does_not_retry_a_timed_out_agent_run()
        {
            // No partial progress carries over between attempts, so a retry restarts the identical
            // workload under the identical budget and times out again. With per-agent budgets in
            // hours, retrying turned one translator timeout into a multi-hour window of repeats.
            var executor = new TimingOutExecutor();
            var service = new WorkflowExecutorService(
                scopeFactory: null!,
                eventPublisher: null!,
                logger: NullLogger<WorkflowExecutorService>.Instance,
                executors: new[] { new ThrowingExecutor() },
                rabbitHelper: new RabbitMqHelper(new ConfigurationBuilder().Build()),
                hardeningOptions: Options.Create(new WorkflowHardeningOptions { MaxStepRetries = 3 }),
                cancellationRegistry: new ReelForge.WorkflowEngine.Execution.ExecutionCancellationRegistry());

            var step = new WorkflowStep { StepOrder = 1, StepType = StepType.Agent };
            var context = new StepExecutionContext
            {
                Execution = new WorkflowExecution(),
                Step = step,
                AllSteps = new System.Collections.Generic.List<WorkflowStep> { step },
                AccumulatedOutput = string.Empty,
                StepOutputHistory = new System.Collections.Generic.List<StepOutputHistoryEntry>(),
                CurrentStepIndex = 0,
                IterationCount = 0,
                CorrelationId = "",
                CancellationToken = CancellationToken.None
            };

            // The real timeout message must survive, not be flattened into a generic
            // "failed after N attempts" that hides why it actually stopped.
            TimeoutException thrown = await Assert.ThrowsAsync<TimeoutException>(
                () => service.ExecuteStepWithRetryAsync(executor, context, step, CancellationToken.None));

            thrown.Message.Should().Contain("timed out");
            executor.CallCount.Should().Be(1);
        }

        [Fact]
        public async Task ExecuteStepWithRetry_throwsImmediately_when_AgentWorkflowException()
        {
            var executor = new ThrowingExecutor();
            var service = new WorkflowExecutorService(
                scopeFactory: null!,
                eventPublisher: null!,
                logger: NullLogger<WorkflowExecutorService>.Instance,
                executors: new[] { executor },
                rabbitHelper: new RabbitMqHelper(new ConfigurationBuilder().Build()),
                hardeningOptions: Options.Create(new WorkflowHardeningOptions()),
                cancellationRegistry: new ReelForge.WorkflowEngine.Execution.ExecutionCancellationRegistry());

            var step = new WorkflowStep { StepOrder = 1, StepType = StepType.Agent };
            var context = new StepExecutionContext
            {
                Execution = new WorkflowExecution(),
                Step = step,
                AllSteps = new System.Collections.Generic.List<WorkflowStep> { step },
                AccumulatedOutput = string.Empty,
                StepOutputHistory = new System.Collections.Generic.List<StepOutputHistoryEntry>(),
                CurrentStepIndex = 0,
                IterationCount = 0,
                CorrelationId = "",
                CancellationToken = CancellationToken.None
            };

            var ct = CancellationToken.None;

            await Assert.ThrowsAsync<AgentWorkflowException>(
                () => service.ExecuteStepWithRetryAsync(executor, context, step, ct));

            // ensure the executor was invoked only once (no retry)
            executor.CallCount.Should().Be(1);
        }

        [Fact]
        public async Task CancelExecutionAsync_cancels_token_when_present()
        {
            var registry = new ReelForge.WorkflowEngine.Execution.ExecutionCancellationRegistry();
            var service = new WorkflowExecutorService(
                scopeFactory: null!,
                eventPublisher: null!,
                logger: NullLogger<WorkflowExecutorService>.Instance,
                executors: new[] { new ThrowingExecutor() },
                rabbitHelper: new RabbitMqHelper(new ConfigurationBuilder().Build()),
                hardeningOptions: Options.Create(new WorkflowHardeningOptions()),
                cancellationRegistry: registry);

            var id = Guid.NewGuid();
            var cts = new CancellationTokenSource();
            registry.Register(id, cts);

            await service.CancelExecutionAsync(id, Guid.NewGuid());
            cts.IsCancellationRequested.Should().BeTrue();
        }

        [Fact]
        public async Task CancelExecutionAsync_finds_nothing_to_cancel_when_the_registry_never_saw_the_execution()
        {
            // The exact shape of the real bug this whole registry exists to fix: a second,
            // unrelated WorkflowExecutorService instance (e.g. one constructed for a different
            // consumed message) has never seen this execution id at all if it's given its OWN
            // registry rather than the shared one — TryCancel must report that honestly (false),
            // not throw or silently succeed.
            var service = new WorkflowExecutorService(
                scopeFactory: null!,
                eventPublisher: null!,
                logger: NullLogger<WorkflowExecutorService>.Instance,
                executors: new[] { new ThrowingExecutor() },
                rabbitHelper: new RabbitMqHelper(new ConfigurationBuilder().Build()),
                hardeningOptions: Options.Create(new WorkflowHardeningOptions()),
                cancellationRegistry: new ReelForge.WorkflowEngine.Execution.ExecutionCancellationRegistry());

            Func<Task> act = () => service.CancelExecutionAsync(Guid.NewGuid(), Guid.NewGuid());

            await act.Should().NotThrowAsync();
        }

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
                string stage, int? percentComplete, CancellationToken ct,
                int? tokensUsedSoFar = null, int? inputTokensSoFar = null, int? outputTokensSoFar = null) => Task.CompletedTask;
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

        // Simulates the real-run regression as faithfully as an in-process test can: a SEPARATE
        // WorkflowExecutorService instance (standing in for the separate DI-scoped
        // WorkflowExecutionStopRequestedConsumer instance that handles a stop request in
        // production) calls CancelExecutionAsync WHILE the step is "in flight" — modeled here as a
        // call made from inside the step executor itself, since a real second thread isn't needed
        // to prove the fix. The step then reports success normally (no OperationCanceledException
        // ever thrown or observed — exactly what was seen live: a long-running chat completion ran
        // to a natural, successful conclusion without honoring the token in time), reproducing
        // "cancel requested mid-step, step finishes successfully anyway" against two instances that
        // must still agree because they share one ExecutionCancellationRegistry.
        private sealed class SucceedsAfterExternalCancellationExecutor : IStepExecutor
        {
            private readonly Guid _executionId;
            private readonly WorkflowExecutorService _canceller;
            public StepType StepType => StepType.Agent;

            public SucceedsAfterExternalCancellationExecutor(Guid executionId, WorkflowExecutorService canceller)
            {
                _executionId = executionId;
                _canceller = canceller;
            }

            public async Task<StepExecutionResult> ExecuteAsync(StepExecutionContext context)
            {
                await _canceller.CancelExecutionAsync(_executionId, Guid.NewGuid());

                // The step itself never observes the cancellation (its own awaited HTTP call ran to
                // completion first, matching the SDK-internal-timeout behavior observed live) — it
                // reports success exactly as if nothing had happened.
                return new StepExecutionResult
                {
                    Output = "{}",
                    NextStepIndex = context.CurrentStepIndex + 1,
                    NewIterationCount = context.IterationCount,
                    Status = StepStatus.Completed
                };
            }
        }

        [Fact]
        public async Task ExecuteAsync_does_not_overwrite_a_mid_run_cancellation_with_Passed()
        {
            var options = new DbContextOptionsBuilder<WorkflowEngineDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            using var db = new WorkflowEngineDbContext(options);

            Guid projectId = Guid.NewGuid();
            Guid workflowDefId = Guid.NewGuid();
            Guid executionId = Guid.NewGuid();

            AgentDefinition agent = new() { Id = Guid.NewGuid(), Name = "Agent", AgentType = AgentType.VideoStoryEditor, SystemPrompt = "" };
            WorkflowStep step1 = new()
            {
                Id = Guid.NewGuid(),
                StepOrder = 1,
                StepType = StepType.Agent,
                AgentDefinitionId = agent.Id,
                AgentDefinition = agent
            };
            WorkflowDefinition definition = new()
            {
                Id = workflowDefId,
                Name = "test",
                ProjectId = projectId,
                Steps = new List<WorkflowStep> { step1 }
            };
            WorkflowExecution execution = new()
            {
                Id = executionId,
                WorkflowDefinitionId = workflowDefId,
                ProjectId = projectId,
                Status = ExecutionStatus.Queued,
                WorkflowDefinition = definition
            };

            db.AgentDefinitions.Add(agent);
            db.WorkflowDefinitions.Add(definition);
            db.WorkflowExecutions.Add(execution);
            await db.SaveChangesAsync();

            var scopeFactory = new FakeScopeFactory(db);
            // The one piece that must be SHARED for the fix to matter — exactly the singleton
            // registration in Program.cs, standing in for two DI scopes that would otherwise never
            // see each other's cancellation tokens.
            var sharedRegistry = new ReelForge.WorkflowEngine.Execution.ExecutionCancellationRegistry();

            var canceller = new WorkflowExecutorService(
                scopeFactory: scopeFactory,
                eventPublisher: new NoOpEventPublisher(),
                logger: NullLogger<WorkflowExecutorService>.Instance,
                executors: Array.Empty<IStepExecutor>(),
                rabbitHelper: new RabbitMqHelper(new ConfigurationBuilder().Build()),
                hardeningOptions: Options.Create(new WorkflowHardeningOptions()),
                cancellationRegistry: sharedRegistry);

            var runner = new WorkflowExecutorService(
                scopeFactory: scopeFactory,
                eventPublisher: new NoOpEventPublisher(),
                logger: NullLogger<WorkflowExecutorService>.Instance,
                executors: new IStepExecutor[] { new SucceedsAfterExternalCancellationExecutor(executionId, canceller) },
                rabbitHelper: new RabbitMqHelper(new ConfigurationBuilder().Build()),
                hardeningOptions: Options.Create(new WorkflowHardeningOptions()),
                cancellationRegistry: sharedRegistry);

            await runner.ExecuteAsync(executionId, "corr-1", CancellationToken.None);

            WorkflowExecution? final = await db.WorkflowExecutions.FindAsync(executionId);
            final.Should().NotBeNull();
            final!.Status.Should().Be(ExecutionStatus.Cancelled,
                "a cancellation requested by a separate consumer instance while the last step was " +
                "in flight must win even though that step itself completed successfully afterward, " +
                "and even though the two instances never talk to each other directly");
        }

        [Fact(Skip = "Depends on reference tables excluded from test harness migrations and provider-specific DDL.")]
        public async Task DeletingProject_cascades_WorkflowExecutions()
        {
            // build a fresh SQLite database from the model
            var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
            connection.Open();
            var options = new DbContextOptionsBuilder<WorkflowEngineDbContext>()
                .UseSqlite(connection)
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
                .Options;

            using var db = new WorkflowEngineDbContext(options);
            db.Database.EnsureCreated();

            var projectId = Guid.NewGuid();
            db.Database.ExecuteSqlRaw(
                "INSERT INTO projects (id,name,owner_id,status,created_at,updated_at) VALUES ({0},{1},{2},{3},{4},{5})",
                projectId.ToString(), "foo", Guid.Empty.ToString(), "Draft", DateTime.UtcNow, DateTime.UtcNow);

            var execId = Guid.NewGuid();
            db.Database.ExecuteSqlRaw(
                "INSERT INTO workflow_executions (id,project_id,workflow_definition_id,status,iteration_count,correlation_id) VALUES ({0},{1},{2},{3},{4},{5})",
                execId.ToString(), projectId.ToString(), Guid.Empty.ToString(), "Queued", 0, Guid.NewGuid().ToString());

            db.Database.ExecuteSqlRaw("DELETE FROM projects WHERE id = {0}", projectId.ToString());

            var remaining = await db.WorkflowExecutions.CountAsync();
            remaining.Should().Be(0);
        }

        [Fact]
        public void ResolveInputJsonForPersistence_uses_resolved_input_when_available()
        {
            const string resolved = "resolved-agent-input";
            const string accumulated = "previous";

            string inputJson = WorkflowExecutorService.ResolveInputJsonForPersistence(resolved, accumulated);

            inputJson.Should().Be(resolved);
        }

        [Fact]
        public void ResolveInputJsonForPersistence_falls_back_to_accumulated_when_no_resolved_input()
        {
            const string accumulated = "previous";

            string inputJson = WorkflowExecutorService.ResolveInputJsonForPersistence(null, accumulated);

            inputJson.Should().Be(accumulated);
        }

        // -----------------------------------------------------------------
        // WS5: VideoAnalyze/VideoCompile retry policy + genuine dispatch (not falling through
        // to the Agent executor when unregistered — mirrors how ExtractStepExecutorTests
        // verifies the same for StepType.Extract).
        // -----------------------------------------------------------------

        [Theory]
        [InlineData(StepType.VideoAnalyze)]
        [InlineData(StepType.VideoCompile)]
        public void ResolveMaxRetries_returns_1_for_deterministic_video_steps(StepType stepType)
        {
            var service = new WorkflowExecutorService(
                scopeFactory: null!,
                eventPublisher: null!,
                logger: NullLogger<WorkflowExecutorService>.Instance,
                executors: Array.Empty<IStepExecutor>(),
                rabbitHelper: new RabbitMqHelper(new ConfigurationBuilder().Build()),
                hardeningOptions: Options.Create(new WorkflowHardeningOptions { MaxStepRetries = 3 }),
                cancellationRegistry: new ReelForge.WorkflowEngine.Execution.ExecutionCancellationRegistry());

            System.Reflection.MethodInfo method = typeof(WorkflowExecutorService).GetMethod(
                "ResolveMaxRetries", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

            var step = new WorkflowStep { StepType = stepType, StepOrder = 1 };
            var maxRetries = (int)method.Invoke(service, [step])!;

            maxRetries.Should().Be(1, because: "both are deterministic — retrying the whole step reproduces the same failure");
        }

        private sealed class StubExecutor : IStepExecutor
        {
            public StubExecutor(StepType stepType) => StepType = stepType;
            public StepType StepType { get; }
            public Task<StepExecutionResult> ExecuteAsync(StepExecutionContext context) =>
                throw new NotImplementedException("Not invoked by this test — only dispatch resolution is under test.");
        }

        private sealed class CleanFailureExecutor : IStepExecutor
        {
            public CleanFailureExecutor(StepType stepType) => StepType = stepType;
            public StepType StepType { get; }

            public Task<StepExecutionResult> ExecuteAsync(StepExecutionContext context) =>
                Task.FromResult(new StepExecutionResult
                {
                    // Mirrors ExtractStepExecutor/VideoAnalyzeStepExecutor/VideoCompileStepExecutor's
                    // own Failure() helpers: Status=Failed with an already-valid-JSON Output,
                    // exactly as R15 requires of every deterministic executor.
                    Output = "{\"status\":\"failed\",\"error\":{\"code\":\"TEST\",\"message\":\"boom\"}}",
                    NextStepIndex = context.CurrentStepIndex + 1,
                    NewIterationCount = context.IterationCount,
                    Status = StepStatus.Failed,
                    ErrorDetails = "boom"
                });
        }

        /// <summary>
        /// Regression test for a live-testing-only-discoverable bug: a deterministic step type
        /// (Extract/VideoAnalyze/VideoCompile all have ResolveMaxRetries == 1, see the theory
        /// above) that fails via its own clean, already-valid-JSON Failure() envelope was, on its
        /// very FIRST attempt, thrown as an InvalidOperationException by ExecuteStepWithRetryAsync
        /// (attemptNumber never being &lt; maxRetries==1) — discarding that valid JSON. The outer
        /// catch in ExecuteAsync then persisted BuildFailureStepResult's Output (plain text from
        /// BuildRetryDiagnosticMessage, not JSON) directly into WorkflowStepResult.OutputJson, a
        /// jsonb column — crashing the whole execution's SaveChangesAsync with Postgres error
        /// 22P02 and leaving the execution stuck in "Running" forever (observed live against a
        /// real Postgres instance; EFCore.InMemory does not enforce column types so this was
        /// invisible to every other test in this suite). BuildFailureStepResult must always
        /// produce a valid JSON Output, regardless of what the underlying exception's message
        /// looks like.
        /// </summary>
        [Fact]
        public async Task BuildFailureStepResult_output_is_always_valid_json_even_from_a_deterministic_steps_own_clean_failure()
        {
            var executor = new CleanFailureExecutor(StepType.VideoCompile);
            var service = new WorkflowExecutorService(
                scopeFactory: null!,
                eventPublisher: null!,
                logger: NullLogger<WorkflowExecutorService>.Instance,
                executors: new[] { (IStepExecutor)executor },
                rabbitHelper: new RabbitMqHelper(new ConfigurationBuilder().Build()),
                hardeningOptions: Options.Create(new WorkflowHardeningOptions()),
                cancellationRegistry: new ReelForge.WorkflowEngine.Execution.ExecutionCancellationRegistry());

            var step = new WorkflowStep { StepOrder = 3, StepType = StepType.VideoCompile };
            var context = new StepExecutionContext
            {
                Execution = new WorkflowExecution(),
                Step = step,
                AllSteps = new List<WorkflowStep> { step },
                AccumulatedOutput = string.Empty,
                StepOutputHistory = new List<StepOutputHistoryEntry>(),
                CurrentStepIndex = 0,
                IterationCount = 0,
                CorrelationId = "",
                CancellationToken = CancellationToken.None
            };

            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ExecuteStepWithRetryAsync(executor, context, step, CancellationToken.None));

            System.Reflection.MethodInfo buildFailure = typeof(WorkflowExecutorService).GetMethod(
                "BuildFailureStepResult", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            var result = (StepExecutionResult)buildFailure.Invoke(null, [step, context, ex])!;

            result.Status.Should().Be(StepStatus.Failed);
            Action parse = () => System.Text.Json.JsonDocument.Parse(result.Output);
            parse.Should().NotThrow(because: "this Output is written verbatim into a jsonb column");
        }

        [Fact]
        public void VideoAnalyze_and_VideoCompile_step_types_dispatch_to_their_own_executors_not_the_Agent_executor()
        {
            var agentStub = new StubExecutor(StepType.Agent);
            var analyzeStub = new StubExecutor(StepType.VideoAnalyze);
            var compileStub = new StubExecutor(StepType.VideoCompile);

            var service = new WorkflowExecutorService(
                scopeFactory: null!,
                eventPublisher: null!,
                logger: NullLogger<WorkflowExecutorService>.Instance,
                executors: new IStepExecutor[] { agentStub, analyzeStub, compileStub },
                rabbitHelper: new RabbitMqHelper(new ConfigurationBuilder().Build()),
                hardeningOptions: Options.Create(new WorkflowHardeningOptions()),
                cancellationRegistry: new ReelForge.WorkflowEngine.Execution.ExecutionCancellationRegistry());

            System.Reflection.FieldInfo field = typeof(WorkflowExecutorService).GetField(
                "_executors", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var executors = (Dictionary<StepType, IStepExecutor>)field.GetValue(service)!;

            // The real bug this guards: WorkflowExecutorService.ExecuteAsync silently falls back
            // to _executors[StepType.Agent] whenever TryGetValue fails for the step's own type
            // (e.g. because DI registration in Program.cs was forgotten) — a step would then run
            // as if it were a plain Agent step with no diagnostic at all. Asserting the dictionary
            // resolves each new StepType to its OWN registered executor instance (not agentStub)
            // is what actually proves the registration is load-bearing.
            executors.Should().ContainKey(StepType.VideoAnalyze);
            executors[StepType.VideoAnalyze].Should().BeSameAs(analyzeStub);
            executors[StepType.VideoAnalyze].Should().NotBeSameAs(agentStub);

            executors.Should().ContainKey(StepType.VideoCompile);
            executors[StepType.VideoCompile].Should().BeSameAs(compileStub);
            executors[StepType.VideoCompile].Should().NotBeSameAs(agentStub);
        }

        [Fact]
        public void WithAttemptMetadata_copies_ArtifactStorageKey_onto_the_retried_result()
        {
            // R3: WithAttemptMetadata hand-copies every field of a StepExecutionResult between
            // attempts. ArtifactStorageKey is easy to miss here — anything added to
            // StepExecutionResult but not copied in this method is silently dropped on any step
            // that goes through a retry attempt (even though VideoAnalyze/VideoCompile themselves
            // never retry, this method is shared code every step type's result flows through).
            const string expectedArtifactKey = "projects/p/agentFiles/video-analysis/e/step-1-analysis.json";
            var original = new StepExecutionResult
            {
                Output = "{}",
                NextStepIndex = 1,
                Status = StepStatus.Completed,
                ArtifactStorageKey = expectedArtifactKey,
                OutputStorageKey = "projects/p/outputFiles/e/video.mp4"
            };

            System.Reflection.MethodInfo method = typeof(WorkflowExecutorService).GetMethod(
                "WithAttemptMetadata", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

            var copied = (StepExecutionResult)method.Invoke(null, [original, 1])!;

            copied.ArtifactStorageKey.Should().Be(expectedArtifactKey);
            copied.OutputStorageKey.Should().Be(original.OutputStorageKey);
        }

        [Fact]
        public void WithAttemptMetadata_copies_ToolCalls_Reasoning_and_ChatTranscriptJson_onto_the_retried_result()
        {
            // Same R3 hand-copy hazard as the ArtifactStorageKey test above, now covering the three
            // diagnostics-persistence fields: ToolCalls/Reasoning feed ToolCallsJson/ReasoningJson,
            // and ChatTranscriptJson is copied directly — none of these were copied here before this
            // change, so a step that retried at least once would silently lose all three before they
            // ever reached WorkflowStepResult.
            var original = new StepExecutionResult
            {
                Output = "{}",
                NextStepIndex = 1,
                Status = StepStatus.Completed,
                ToolCalls = new List<AgentToolCallTrace>
                {
                    new() { ToolName = "ReadProjectFile", Arguments = "{}", Result = "ok" }
                },
                Reasoning = new List<string> { "considered the offered ids" },
                ChatTranscriptJson = "[{\"turnIndex\":0,\"speaker\":\"Seat0\",\"speakerRole\":\"editor\",\"text\":\"keep s1\"}]"
            };

            System.Reflection.MethodInfo method = typeof(WorkflowExecutorService).GetMethod(
                "WithAttemptMetadata", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

            var copied = (StepExecutionResult)method.Invoke(null, [original, 2])!;

            copied.ToolCalls.Should().BeEquivalentTo(original.ToolCalls);
            copied.Reasoning.Should().BeEquivalentTo(original.Reasoning);
            copied.ChatTranscriptJson.Should().Be(original.ChatTranscriptJson);
        }

        [Fact]
        public async Task ExecuteAsync_persists_ToolCallsJson_and_ReasoningJson_when_the_step_reports_them()
        {
            var options = new DbContextOptionsBuilder<WorkflowEngineDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            using var db = new WorkflowEngineDbContext(options);

            Guid projectId = Guid.NewGuid();
            Guid workflowDefId = Guid.NewGuid();
            Guid executionId = Guid.NewGuid();

            AgentDefinition agent = new() { Id = Guid.NewGuid(), Name = "Agent", AgentType = AgentType.VideoStoryEditor, SystemPrompt = "" };
            WorkflowStep step1 = new()
            {
                Id = Guid.NewGuid(),
                StepOrder = 1,
                StepType = StepType.Agent,
                AgentDefinitionId = agent.Id,
                AgentDefinition = agent
            };
            WorkflowDefinition definition = new()
            {
                Id = workflowDefId,
                Name = "test",
                ProjectId = projectId,
                Steps = new List<WorkflowStep> { step1 }
            };
            WorkflowExecution execution = new()
            {
                Id = executionId,
                WorkflowDefinitionId = workflowDefId,
                ProjectId = projectId,
                Status = ExecutionStatus.Queued,
                WorkflowDefinition = definition
            };

            db.AgentDefinitions.Add(agent);
            db.WorkflowDefinitions.Add(definition);
            db.WorkflowExecutions.Add(execution);
            await db.SaveChangesAsync();

            var scopeFactory = new FakeScopeFactory(db);
            var toolCallsReasoningExecutor = new ToolCallsAndReasoningExecutor(
                toolCalls: new List<AgentToolCallTrace> { new() { ToolName = "ReadProjectFile", Arguments = "{\"path\":\"a.txt\"}", Result = "contents" } },
                reasoning: new List<string> { "step one", "step two" });

            var runner = new WorkflowExecutorService(
                scopeFactory: scopeFactory,
                eventPublisher: new NoOpEventPublisher(),
                logger: NullLogger<WorkflowExecutorService>.Instance,
                executors: new IStepExecutor[] { toolCallsReasoningExecutor },
                rabbitHelper: new RabbitMqHelper(new ConfigurationBuilder().Build()),
                hardeningOptions: Options.Create(new WorkflowHardeningOptions()),
                cancellationRegistry: new ReelForge.WorkflowEngine.Execution.ExecutionCancellationRegistry());

            await runner.ExecuteAsync(executionId, "corr-1", CancellationToken.None);

            WorkflowStepResult? stepResult = await db.WorkflowStepResults
                .FirstOrDefaultAsync(r => r.WorkflowExecutionId == executionId);
            stepResult.Should().NotBeNull();
            stepResult!.ToolCallsJson.Should().NotBeNullOrEmpty();
            stepResult.ToolCallsJson.Should().Contain("ReadProjectFile");
            stepResult.ReasoningJson.Should().NotBeNullOrEmpty();
            stepResult.ReasoningJson.Should().Contain("step one").And.Contain("step two");
        }

        [Fact]
        public async Task ExecuteAsync_leaves_ToolCallsJson_and_ReasoningJson_null_when_the_step_reports_none()
        {
            var options = new DbContextOptionsBuilder<WorkflowEngineDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            using var db = new WorkflowEngineDbContext(options);

            Guid projectId = Guid.NewGuid();
            Guid workflowDefId = Guid.NewGuid();
            Guid executionId = Guid.NewGuid();

            AgentDefinition agent = new() { Id = Guid.NewGuid(), Name = "Agent", AgentType = AgentType.VideoStoryEditor, SystemPrompt = "" };
            WorkflowStep step1 = new()
            {
                Id = Guid.NewGuid(),
                StepOrder = 1,
                StepType = StepType.Agent,
                AgentDefinitionId = agent.Id,
                AgentDefinition = agent
            };
            WorkflowDefinition definition = new()
            {
                Id = workflowDefId,
                Name = "test",
                ProjectId = projectId,
                Steps = new List<WorkflowStep> { step1 }
            };
            WorkflowExecution execution = new()
            {
                Id = executionId,
                WorkflowDefinitionId = workflowDefId,
                ProjectId = projectId,
                Status = ExecutionStatus.Queued,
                WorkflowDefinition = definition
            };

            db.AgentDefinitions.Add(agent);
            db.WorkflowDefinitions.Add(definition);
            db.WorkflowExecutions.Add(execution);
            await db.SaveChangesAsync();

            var scopeFactory = new FakeScopeFactory(db);
            var emptyExecutor = new ToolCallsAndReasoningExecutor(
                toolCalls: new List<AgentToolCallTrace>(), reasoning: new List<string>());

            var runner = new WorkflowExecutorService(
                scopeFactory: scopeFactory,
                eventPublisher: new NoOpEventPublisher(),
                logger: NullLogger<WorkflowExecutorService>.Instance,
                executors: new IStepExecutor[] { emptyExecutor },
                rabbitHelper: new RabbitMqHelper(new ConfigurationBuilder().Build()),
                hardeningOptions: Options.Create(new WorkflowHardeningOptions()),
                cancellationRegistry: new ReelForge.WorkflowEngine.Execution.ExecutionCancellationRegistry());

            await runner.ExecuteAsync(executionId, "corr-1", CancellationToken.None);

            WorkflowStepResult? stepResult = await db.WorkflowStepResults
                .FirstOrDefaultAsync(r => r.WorkflowExecutionId == executionId);
            stepResult.Should().NotBeNull();
            stepResult!.ToolCallsJson.Should().BeNull();
            stepResult.ReasoningJson.Should().BeNull();
        }

        private sealed class ToolCallsAndReasoningExecutor : IStepExecutor
        {
            private readonly IReadOnlyList<AgentToolCallTrace> _toolCalls;
            private readonly IReadOnlyList<string> _reasoning;

            public ToolCallsAndReasoningExecutor(IReadOnlyList<AgentToolCallTrace> toolCalls, IReadOnlyList<string> reasoning)
            {
                _toolCalls = toolCalls;
                _reasoning = reasoning;
            }

            public StepType StepType => StepType.Agent;

            public Task<StepExecutionResult> ExecuteAsync(StepExecutionContext context) =>
                Task.FromResult(new StepExecutionResult
                {
                    Output = "{}",
                    NextStepIndex = context.CurrentStepIndex + 1,
                    NewIterationCount = context.IterationCount,
                    Status = StepStatus.Completed,
                    ToolCalls = _toolCalls,
                    Reasoning = _reasoning
                });
        }

        // Regression coverage for a bug found by e2e QA: writing arbitrary non-JSON text (e.g. a
        // chat completion that didn't conform to its requested output schema) straight into
        // WorkflowStepResult.OutputJson/WorkflowExecution.ResultJson (both jsonb columns) fails
        // SaveChangesAsync with Postgres 22P02, discarding the step/execution's Status update
        // along with it and leaving the execution stuck "Running" forever.
        private static string? InvokeEnsureJsonForJsonbColumn(string? value)
        {
            var method = typeof(WorkflowExecutorService).GetMethod(
                "EnsureJsonForJsonbColumn",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            method.Should().NotBeNull("WorkflowExecutorService must expose a jsonb-safety helper");
            return (string?)method!.Invoke(null, [value]);
        }

        [Fact]
        public void EnsureJsonForJsonbColumn_passes_through_valid_json_unchanged()
        {
            string valid = "{\"status\":\"ok\",\"items\":[1,2,3]}";
            InvokeEnsureJsonForJsonbColumn(valid).Should().Be(valid);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void EnsureJsonForJsonbColumn_maps_null_or_empty_to_null(string? input)
        {
            InvokeEnsureJsonForJsonbColumn(input).Should().BeNull();
        }

        [Fact]
        public void EnsureJsonForJsonbColumn_wraps_non_json_text_as_a_json_string_instead_of_crashing()
        {
            // exactly the shape of a non-conforming chat completion: plain prose, not JSON
            string raw = "I'm sorry, I cannot keep any segments because the request was unclear.";

            string? result = InvokeEnsureJsonForJsonbColumn(raw);

            result.Should().NotBeNull();
            // must itself be valid JSON (a jsonb column would reject anything else)
            System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(result!);
            doc.RootElement.ValueKind.Should().Be(System.Text.Json.JsonValueKind.String);
            doc.RootElement.GetString().Should().Be(raw);
        }

        [Fact]
        public void EnsureJsonForJsonbColumn_wraps_whitespace_only_text_rather_than_passing_it_through()
        {
            // whitespace-only is not a valid standalone JSON token even though some callers'
            // permissive IsValidJson helper treats it as "fine" for logging purposes
            string? result = InvokeEnsureJsonForJsonbColumn("   ");

            result.Should().NotBeNull();
            System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(result!);
            doc.RootElement.ValueKind.Should().Be(System.Text.Json.JsonValueKind.String);
        }

        // -----------------------------------------------------------------
        // Review loop feedback window: whether a ReviewLoop step's loop-back feedback should be
        // seeded onto a given step's context (see docs/video-editing.md "Review loop" — this is
        // the generic mechanism, not video-specific, so it benefits every ReviewLoop pipeline).
        // -----------------------------------------------------------------

        [Theory]
        [InlineData(2, 2, 4, true)]   // loop target itself (inclusive)
        [InlineData(3, 2, 4, true)]   // a step between the loop target and the review step
        [InlineData(4, 2, 4, false)]  // the ReviewLoop step itself (exclusive)
        [InlineData(1, 2, 4, false)]  // before the loop target
        [InlineData(5, 2, 4, false)]  // after the ReviewLoop step
        public void IsWithinReviewFeedbackWindow_matches_the_half_open_loop_back_window(
            int stepOrder, int minInclusive, int maxExclusive, bool expected)
        {
            WorkflowExecutorService.IsWithinReviewFeedbackWindow(stepOrder, minInclusive, maxExclusive)
                .Should().Be(expected);
        }
    }

    // helper classes for tests
    internal class FakeScopeFactory : IServiceScopeFactory
    {
        private readonly WorkflowEngineDbContext _db;
        public FakeScopeFactory(WorkflowEngineDbContext db) => _db = db;
        public IServiceScope CreateScope() => new FakeScope(_db);
        private class FakeScope : IServiceScope
        {
            public IServiceProvider ServiceProvider { get; }
            public FakeScope(WorkflowEngineDbContext db) => ServiceProvider = new SimpleProvider(db);
            public void Dispose() { }
        }
        private class SimpleProvider : IServiceProvider
        {
            private readonly WorkflowEngineDbContext _db;
            public SimpleProvider(WorkflowEngineDbContext db) => _db = db;
            public object GetService(Type serviceType)
                => serviceType == typeof(WorkflowEngineDbContext) ? _db : null!;
        }
    }

    internal class TestRabbitHelper : RabbitMqHelper
    {
        public bool Called { get; private set; }
        public TestRabbitHelper() : base(new ConfigurationBuilder().Build()) { }
        public override Task<bool> RemoveExecutionMessageAsync(Guid executionId)
        {
            Called = true;
            return Task.FromResult(true);
        }
    }
}
