## ✅ Plan: Agent Workflow Abort Tool

**TL;DR** –
Introduce a new agent-accessible tool (`FailWorkflow`) that throws a custom exception carrying a human-readable reason. Workflow execution logic will treat that exception as an immediate failure (no retries). Update every built-in agent’s default system prompt and docs to explain when to use it.

---

### 🛠 Steps

1. **Define exception & agent tool**
   - Create `AgentWorkflowException` with a `Reason` property.  
   - Add `WorkflowControlAgentTools` containing `Task FailWorkflow(string reason)` that throws the exception.  
   - Register `WorkflowControlAgentTools` in DI (`WorkflowEngine/Program.cs`).

2. **Register the tool for all agents**
   - Modify `AgentToolProvider` constructor to accept the new tools class.  
   - For every branch in the `GetTools` switch (including `_` default) append `AIFunctionFactory.Create(_workflowControlTools.FailWorkflow)`.  
   - Optionally refactor common tool list creation to reduce duplication (not required but cleaner).

3. **Adjust execution logic**
   - In `WorkflowExecutorService.ExecuteStepWithRetryAsync`, update the catch filter to exclude `AgentWorkflowException` (similar to `InvalidOperationException`) so it bypasses retries.  
   - Optionally log a dedicated message when such an exception bubbles up.

4. **Educate built‑in agents**
   - Edit `DatabaseSeeder.BuiltInAgents` prompt strings: add a concluding paragraph to each default prompt describing the new `FailWorkflow` tool and when to call it (unrecoverable scenarios, missing data, inconsistent state, etc.).  
   - Update `docs/builtin-agents.md` with a global note about the tool and optionally insert a small example snippet in each agent description.

5. **Tests & verification**
   - Add a new test project (e.g. `inference/tests/ReelForge.WorkflowEngine.Tests`) with xUnit references; if the repo already has a test project use that.
   - Write unit tests:
     * `WorkflowControlAgentToolsTests.FailWorkflow_throws_AgentWorkflowException`.
     * `WorkflowExecutorServiceTests.ExecuteStepWithRetry_throwsImmediately_when_AgentWorkflowException` (use a fake `IStepExecutor` that throws).
   - Consider an integration/functional test that simulates a workflow with a dummy agent that invokes the tool and assert the whole execution ends as failed with the given message.

6. **Manual validation**
   - Start the stack locally, create a simple workflow with a custom agent step that immediately calls the new tool (tool invocation may require editing the prompt or using a test agent). Run the workflow and confirm the UI shows failure and the error message is propagated.
   - Inspect database row for workflow execution and step result (no step result should be created, or error message should appear in execution record).

**Relevant files**
- `inference/src/ReelForge.WorkflowEngine/Agents/Tools/AgentWorkflowException.cs` (new)
- `inference/src/ReelForge.WorkflowEngine/Agents/Tools/WorkflowControlAgentTools.cs` (new)
- `inference/src/ReelForge.WorkflowEngine/Program.cs` (register service)
- `inference/src/ReelForge.WorkflowEngine/Agents/Tools/AgentToolProvider.cs` (add tool and DI)
- `inference/src/ReelForge.WorkflowEngine/Execution/WorkflowExecutorService.cs` (catch filter update)
- `inference/src/ReelForge.Inference.Api/Data/DatabaseSeeder.cs` (prompt updates)
- `docs/builtin-agents.md` (documentation)
- Optional test files under new/existing test project

**Verification**
1. Unit tests pass: the tool throws correctly and retry logic bypasses when triggered.
2. Manual workflow test as described above confirms execution is aborted with reason.
3. Inspect system prompts stored in DB after seeding to ensure they include the new instruction.
4. Possibly run `dotnet run` on WorkflowEngine with a simple payload and inspect logs for `AgentWorkflowException` being logged.

**Decisions**
- Chose to signal workflow abort by throwing a custom exception rather than returning a special `StepExecutionResult`. This leverages existing executor behaviour and keeps step result storage unchanged.
- Registered the tool for all agent types (including custom) to keep API simple.
- Added prompt instructions to `DatabaseSeeder` so newly created agents (and seeded ones) carry the guidance; existing prompts in DB will not update automatically, but documentation will advise manual override if necessary.

**Further Considerations**
1. Should the new exception set `StackTrace` or additional metadata? A simple message suffices, but we might later add properties for structured data.
2. Determine how UI surfaces the reason (execution.ErrorMessage is already shown in workflow detail page) and adjust if necessary.
3. We could offer a dedicated API endpoint for aborting running executions from outside agents; outside of scope for now.

---

This plan sets a clear path for implementing and verifying the abort tool with minimal invasive changes and full documentation.
