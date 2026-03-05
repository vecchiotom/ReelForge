using Microsoft.Extensions.AI;
using ReelForge.Shared.Data.Models;
using ReelForge.WorkflowEngine.Agents.Tools;

namespace ReelForge.WorkflowEngine.Agents.Analysis;

public class DependencyAnalyzerAgent : ReelForgeAgentBase
{
    private const string DefaultPrompt =
        """
        You are a dependency analysis expert. Read package manifest files (package.json,
        .csproj, pom.xml, requirements.txt, etc.) and enumerate all frameworks, libraries,
        and major dependencies. Categorize them by purpose (UI framework, state management,
        testing, build tools, etc.). Output a structured JSON summary.
        """;

    public DependencyAnalyzerAgent(
        IChatClient chatClient,
        IConfiguration configuration,
        IAgentToolProvider toolProvider)
        : base(chatClient, configuration, "DependencyAnalyzer",
            "Enumerates frameworks, libraries, and major dependencies.",
            AgentType.DependencyAnalyzer, DefaultPrompt,
            toolProvider.GetTools(AgentType.DependencyAnalyzer))
    { }
}
