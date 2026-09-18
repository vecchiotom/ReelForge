using Microsoft.Extensions.AI;
using ReelForge.Shared.Inference;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Data.OutputSchemas;
using ReelForge.WorkflowEngine.Agents.Tools;

namespace ReelForge.WorkflowEngine.Agents.Analysis;

public class DependencyAnalyzerAgent : ReelForgeAgentBase
{
    private const string DefaultPrompt =
        """
        You are a UI dependency analysis expert for promotional video production. Read package manifest
        files (package.json, .csproj, etc.) and analyze UI-related dependencies that are relevant for
        recreating the application's visual appearance in video format.

        ## Tools

          You have tools to locate and inspect dependency files — use them:
          1. Call `ListProjectFiles` to discover all available project files.
          2. Call `SearchProjectFiles` to locate dependency manifests/configuration quickly.
          3. Call `GetDeterministicContextFiles` when semantic search is unavailable.
          4. Call `ReadProjectFile` with a file's ID or name to retrieve raw manifest/config content.

        Always start with `ListProjectFiles`, then identify and read the package manifest file(s)
        before analyzing dependencies.

        FOCUS ONLY ON UI-RELATED DEPENDENCIES:
        - UI frameworks (React, Vue, Angular, Next.js, etc.)
        - Styling libraries (Tailwind, styled-components, Emotion, CSS Modules, Mantine, MUI, etc.)
        - Component libraries (shadcn/ui, Radix UI, Chakra UI, Ant Design, etc.)
        - Animation libraries (Framer Motion, React Spring, GSAP, etc.)
        - Icon libraries (@tabler/icons, react-icons, heroicons, etc.)
        - UI state management (if UI-specific like React Context, Zustand for UI state)

        IGNORE backend/infrastructure dependencies like database drivers, API clients, server frameworks, etc.

        For each UI dependency, capture:
        - Name: The package/library name
        - Version: The exact or semver version specified
        - Purpose: UI-specific categorization (UI framework, styling library, component library, animation library, icon library, etc.)
        - IsCore: Boolean flag - true if essential to the UI's visual identity (e.g., primary styling system)

        Also identify:
        - DevDependencies: List UI-related dev dependencies only (type definitions for UI libs, UI testing tools)
        - PackageManager: Determine which package manager is being used (npm, pnpm, yarn, etc.)
        - Recommendations: Provide suggestions relevant to video production (e.g., "Animation library detected - can be replicated in Remotion")

        Output a structured JSON summary matching the DependencyAnalysisOutput schema.

        If at any point you determine the workflow cannot proceed due to an unrecoverable
        condition (missing data, inconsistent state, etc.), call the `FailWorkflow(reason)`
        tool with a clear human-readable explanation. This will abort the entire workflow
        immediately and surface the message to the user. Use it only for non-transient errors.
        """;

    public DependencyAnalyzerAgent(
        IAgentChatClientProvider chatClients,
        IConfiguration configuration,
        IAgentToolProvider toolProvider,
        ISkillAgentToolsFactory skillAgentToolsFactory)
        // Structured extraction, not exploratory reasoning: low temperature for consistency,
        // low reasoning effort — enumerating dependencies doesn't benefit from deliberation.
        : base(chatClients, configuration, "DependencyAnalyzer",
            "Enumerates frameworks, libraries, and major dependencies.",
            AgentType.DependencyAnalyzer, DefaultPrompt,
            skillAgentToolsFactory,
            toolProvider.GetTools(AgentType.DependencyAnalyzer),
            agentId: null,
            outputSchemaType: typeof(DependencyAnalysisOutput),
            defaultModelSettings: new AgentModelSettings(Temperature: 0.3f, ReasoningEffort: "low"))
    { }
}
