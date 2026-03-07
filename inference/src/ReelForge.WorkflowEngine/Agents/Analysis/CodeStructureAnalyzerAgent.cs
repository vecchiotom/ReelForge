using Microsoft.Extensions.AI;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Data.OutputSchemas;
using ReelForge.WorkflowEngine.Agents.Tools;

namespace ReelForge.WorkflowEngine.Agents.Analysis;

public class CodeStructureAnalyzerAgent : ReelForgeAgentBase
{
    private const string DefaultPrompt =
        """
        You are a code structure analysis expert. Your job is to map the overall directory
        and module structure of a web application's source code.

        Your analysis must include:
        - ProjectType: Identify the type of project (e.g., "React SPA", "Next.js App", "Vue Application")
        - Framework: Determine the framework and version being used (e.g., "Next.js 15", "React 18")
        - Directories: List major directories with their Path, Purpose, and FileCount
        - EntryPoints: Identify all entry point files (e.g., index.ts, main.tsx, app.tsx, _app.tsx)
        - OverallArchitecture: Describe the high-level architectural pattern (e.g., "Feature-based", "Layered", "Monolithic")

        Output a structured JSON summary matching the provided CodeStructureOutput schema.
        """;

    public CodeStructureAnalyzerAgent(
        IChatClient chatClient,
        IConfiguration configuration,
        IAgentToolProvider toolProvider)
        : base(chatClient, configuration, "CodeStructureAnalyzer",
            "Maps the overall directory/module structure of the webapp source.",
            AgentType.CodeStructureAnalyzer, DefaultPrompt,
            toolProvider.GetTools(AgentType.CodeStructureAnalyzer),
            agentId: null,
            outputSchemaType: typeof(CodeStructureOutput))
    { }
}
