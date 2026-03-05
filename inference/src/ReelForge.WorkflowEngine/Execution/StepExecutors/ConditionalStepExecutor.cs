using ReelForge.Shared.Data.Models;

namespace ReelForge.WorkflowEngine.Execution.StepExecutors;

/// <summary>
/// Evaluates a condition expression and branches to true/false step order.
/// </summary>
public class ConditionalStepExecutor : IStepExecutor
{
    private readonly ILogger<ConditionalStepExecutor> _logger;

    public ConditionalStepExecutor(ILogger<ConditionalStepExecutor> logger) => _logger = logger;

    public StepType StepType => StepType.Conditional;

    public Task<StepExecutionResult> ExecuteAsync(StepExecutionContext context)
    {
        bool result = ExpressionEvaluator.Evaluate(
            context.Step.ConditionExpression ?? "",
            context.AccumulatedOutput);

        _logger.LogInformation("Conditional step {StepOrder}: expression '{Expression}' evaluated to {Result}",
            context.Step.StepOrder, context.Step.ConditionExpression, result);

        int nextIndex;
        if (result && context.Step.TrueBranchStepOrder != null
            && int.TryParse(context.Step.TrueBranchStepOrder, out int trueOrder))
        {
            nextIndex = context.AllSteps.FindIndex(s => s.StepOrder == trueOrder);
        }
        else if (!result && context.Step.FalseBranchStepOrder != null
            && int.TryParse(context.Step.FalseBranchStepOrder, out int falseOrder))
        {
            nextIndex = context.AllSteps.FindIndex(s => s.StepOrder == falseOrder);
        }
        else
        {
            nextIndex = context.CurrentStepIndex + 1;
        }

        if (nextIndex < 0) nextIndex = context.CurrentStepIndex + 1;

        return Task.FromResult(new StepExecutionResult
        {
            Output = context.AccumulatedOutput,
            NextStepIndex = nextIndex,
            NewIterationCount = context.IterationCount,
            Status = StepStatus.Completed
        });
    }
}
