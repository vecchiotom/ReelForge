using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ReelForge.Shared.Data.Models;
using ReelForge.WorkflowEngine.Agents.Tools;
using ReelForge.WorkflowEngine.Execution;
using Xunit;

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
        public async Task ExecuteStepWithRetry_throwsImmediately_when_AgentWorkflowException()
        {
            var executor = new ThrowingExecutor();
            var service = new WorkflowExecutorService(
                scopeFactory: null!,
                eventPublisher: null!,
                logger: NullLogger<WorkflowExecutorService>.Instance,
                executors: new[] { executor },
                publishEndpoint: null!);

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
                publishEndpoint: null!);

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
}