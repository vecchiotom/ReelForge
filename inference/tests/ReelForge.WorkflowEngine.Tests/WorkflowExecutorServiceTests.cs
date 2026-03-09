using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ReelForge.Shared.Data.Models;
using ReelForge.WorkflowEngine.Agents.Tools;
using ReelForge.WorkflowEngine.Services.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.InMemory;
using ReelForge.WorkflowEngine.Data;
using ReelForge.WorkflowEngine.Execution;
using Xunit;
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

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
                publishEndpoint: null!,
                rabbitHelper: fakeHelper);

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
                publishEndpoint: null!,
                rabbitHelper: new RabbitMqHelper(new ConfigurationBuilder().Build()));

            var step = new WorkflowStep { StepOrder = 1, StepType = StepType.Agent };
            var context = new StepExecutionContext
            {
                Execution = new WorkflowExecution(),
                Step = step,
                AllSteps = new System.Collections.Generic.List<WorkflowStep> { step },
                AccumulatedOutput = string.Empty,
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
                publishEndpoint: null!,
                rabbitHelper: new RabbitMqHelper(new ConfigurationBuilder().Build()));

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
