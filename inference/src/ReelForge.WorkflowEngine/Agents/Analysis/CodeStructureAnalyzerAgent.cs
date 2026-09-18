using Microsoft.Extensions.AI;
using ReelForge.Shared.Inference;
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

        ## Tools

          You have tools to explore the project — use them before drawing any conclusions:
          1. Call `ListProjectFiles` to retrieve the full list of available files with their IDs and names.
          2. Call `SearchProjectFiles` for targeted semantic discovery of likely entry/config files.
          3. Call `GetDeterministicContextFiles` when semantic search is unavailable.
          4. Call `ReadProjectFile` with a file's ID or name to get raw content for key files.

          Always start by calling `ListProjectFiles`, then identify likely structure/entry files
          (package.json, tsconfig.json, app/page/root/index entrypoints) and read only the minimal
          subset required to determine architecture.

        ## Analysis Requirements

        Your analysis must include:
        - ProjectType: Identify the type of project (e.g., "React SPA", "Next.js App", "Vue Application")
        - Framework: Determine the framework and version being used (e.g., "Next.js 15", "React 18")
        - Directories: List major directories with their Path, Purpose, and FileCount
        - EntryPoints: Identify all entry point files (e.g., index.ts, main.tsx, app.tsx, _app.tsx)
        - OverallArchitecture: Describe the high-level architectural pattern (e.g., "Feature-based", "Layered", "Monolithic")

        Output a structured JSON summary matching the provided CodeStructureOutput schema.

        If at any point you determine the workflow cannot proceed due to an unrecoverable
        condition (missing data, inconsistent state, etc.), call the `FailWorkflow(reason)`
        tool with a clear human-readable explanation. This will abort the entire workflow
        immediately and surface the message to the user. Use it only for non-transient errors.
        """;

    public CodeStructureAnalyzerAgent(
        IAgentChatClientProvider chatClients,
        IConfiguration configuration,
        IAgentToolProvider toolProvider)
        // Structured extraction, not exploratory reasoning: low temperature for consistency,
        // low reasoning effort since this runs early in every pipeline and there's little for
        // deep thinking to add over a straight directory/module read.
        : base(chatClients, configuration, "CodeStructureAnalyzer",
            "Maps the overall directory/module structure of the webapp source.",
            AgentType.CodeStructureAnalyzer, DefaultPrompt,
            toolProvider.GetTools(AgentType.CodeStructureAnalyzer),
            agentId: null,
            outputSchemaType: typeof(CodeStructureOutput),
            defaultModelSettings: new AgentModelSettings(Temperature: 0.3f, ReasoningEffort: "low"))
    { }
}
