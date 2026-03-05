using Microsoft.Extensions.AI;
using ReelForge.Shared.Data.Models;
using ReelForge.WorkflowEngine.Agents.Tools;

namespace ReelForge.WorkflowEngine.Agents.Analysis;

public class ComponentInventoryAnalyzerAgent : ReelForgeAgentBase
{
    private const string DefaultPrompt =
        """
        You are a UI component analysis expert. Enumerate all UI components in the web
        application, identifying their names, props/parameters, basic responsibilities,
        and relationships. For React apps, find all .tsx/.jsx components. For Vue, find
        .vue files. Output a structured JSON inventory of all components.
        """;

    public ComponentInventoryAnalyzerAgent(IChatClient chatClient, IConfiguration configuration)
        : base(chatClient, configuration, "ComponentInventoryAnalyzer",
            "Enumerates all UI components, their props and basic responsibilities.",
            AgentType.ComponentInventoryAnalyzer, DefaultPrompt,
            new AIFunction[] { AIFunctionFactory.Create(FileSystemTools.ReadFileContent), AIFunctionFactory.Create(FileSystemTools.ListFilesByExtension) })
    { }
}
