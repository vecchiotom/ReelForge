using ReelForge.Shared.Data.Models;

namespace ReelForge.WorkflowEngine.Execution;

public static class AgentInputContextResolver
{
    public static AgentInputContextMode ResolveEffectiveMode(WorkflowStep step)
    {
        if (step.AgentInputContextMode.HasValue)
            return step.AgentInputContextMode.Value;

        return GetDefaultMode(step.AgentDefinition.AgentType);
    }

    public static AgentInputContextMode GetDefaultMode(AgentType agentType)
    {
        return agentType switch
        {
            AgentType.CodeStructureAnalyzer => AgentInputContextMode.FullWorkflow,
            AgentType.DependencyAnalyzer => AgentInputContextMode.PreviousStepOnly,
            AgentType.ComponentInventoryAnalyzer => AgentInputContextMode.PreviousStepOnly,
            AgentType.RouteAndApiAnalyzer => AgentInputContextMode.PreviousStepOnly,
            AgentType.StyleAndThemeExtractor => AgentInputContextMode.PreviousStepOnly,
            AgentType.RemotionComponentTranslator => AgentInputContextMode.FullWorkflow,
            AgentType.AnimationStrategyAgent => AgentInputContextMode.PreviousStepOnly,
            AgentType.DirectorAgent => AgentInputContextMode.FullWorkflow,
            AgentType.ScriptwriterAgent => AgentInputContextMode.PreviousStepOnly,
            AgentType.AuthorAgent => AgentInputContextMode.PreviousStepOnly,
            AgentType.ReviewAgent => AgentInputContextMode.PreviousStepOnly,
            _ => AgentInputContextMode.FullWorkflow
        };
    }
}
