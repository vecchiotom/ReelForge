using System;
using System.Threading.Tasks;
using ReelForge.WorkflowEngine.Agents.Tools;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests
{
    public class WorkflowControlAgentToolsTests
    {
        [Fact]
        public async Task FailWorkflow_throws_AgentWorkflowException()
        {
            var tools = new WorkflowControlAgentTools();
            const string reason = "critical data missing";

            Task act() => tools.FailWorkflow(reason);

            var ex = await Assert.ThrowsAsync<AgentWorkflowException>(act);
            Assert.Equal(reason, ex.Reason);
            Assert.Equal(reason, ex.Message);
        }
    }
}