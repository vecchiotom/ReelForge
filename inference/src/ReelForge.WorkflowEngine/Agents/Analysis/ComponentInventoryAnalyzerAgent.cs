using Microsoft.Extensions.AI;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Data.OutputSchemas;
using ReelForge.WorkflowEngine.Agents.Tools;

namespace ReelForge.WorkflowEngine.Agents.Analysis;

public class ComponentInventoryAnalyzerAgent : ReelForgeAgentBase
{
    private const string DefaultPrompt =
        """
        You are a UI component analysis expert. Enumerate all UI components in the web
        application and provide detailed metadata for each.

        For each component, extract:
        - Name: The component name
        - FilePath: Relative path to the component file
        - Props: Array of prop definitions with:
          - Name: The prop name
          - Type: TypeScript/PropType definition (e.g., "string", "number", "{ id: number }")
          - Required: Boolean indicating if the prop is required
          - DefaultValue: Default value if specified (as string)
        - Responsibility: Clear description of what the component does
        - Dependencies: List of other component names that this component imports/uses

        Also provide:
        - TotalComponents: Total count of components found
        - CommonPatterns: List of recurring design patterns (e.g., "Container/Presentational", "Compound Components", "Render Props", "Custom Hooks")

        For React apps, find all .tsx/.jsx components. For Vue, find .vue files. For Angular, find .component.ts files.
        Output a structured JSON inventory matching the provided ComponentInventoryOutput schema.
        """;

    public ComponentInventoryAnalyzerAgent(
        IChatClient chatClient,
        IConfiguration configuration,
        IAgentToolProvider toolProvider)
        : base(chatClient, configuration, "ComponentInventoryAnalyzer",
            "Enumerates all UI components, their props and basic responsibilities.",
            AgentType.ComponentInventoryAnalyzer, DefaultPrompt,
            toolProvider.GetTools(AgentType.ComponentInventoryAnalyzer),
            agentId: null,
            outputSchemaType: typeof(ComponentInventoryOutput))
    { }
}
