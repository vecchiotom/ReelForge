using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ReelForge.Shared.Data.Models;
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
                hardeningOptions: Options.Create(new WorkflowHardeningOptions()));

            await service.CancelExecutionAsync(id, Guid.NewGuid());
            fakeHelper.Called.Should().BeTrue();
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
                hardeningOptions: Options.Create(new WorkflowHardeningOptions()));

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
            var service = new WorkflowExecutorService(
                scopeFactory: null!,
                eventPublisher: null!,
                logger: NullLogger<WorkflowExecutorService>.Instance,
                executors: new[] { new ThrowingExecutor() },
                rabbitHelper: new RabbitMqHelper(new ConfigurationBuilder().Build()),
                hardeningOptions: Options.Create(new WorkflowHardeningOptions()));

            // inject a fake cancellation token source
            var field = typeof(WorkflowExecutorService).GetField("_executionCts",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var dict = (System.Collections.Concurrent.ConcurrentDictionary<System.Guid, CancellationTokenSource>)field.GetValue(service);
            var id = Guid.NewGuid();
            var cts = new CancellationTokenSource();
            dict[id] = cts;

            await service.CancelExecutionAsync(id, Guid.NewGuid());
            cts.IsCancellationRequested.Should().BeTrue();
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
                hardeningOptions: Options.Create(new WorkflowHardeningOptions { MaxStepRetries = 3 }));

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
                hardeningOptions: Options.Create(new WorkflowHardeningOptions()));

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
