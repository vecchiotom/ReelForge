using Microsoft.Extensions.AI;
using ReelForge.Inference.Agents.Tools;
using ReelForge.Inference.Data.Models;

namespace ReelForge.Inference.Agents.Analysis;

/// <summary>
/// Analyzes package manifests to enumerate frameworks, libraries, and major dependencies.
/// </summary>
public class DependencyAnalyzerAgent : ReelForgeAgentBase
{
    private const string DefaultPrompt =
        """
        You are a dependency analysis expert. Read package manifest files (package.json,
        .csproj, pom.xml, requirements.txt, etc.) and enumerate all frameworks, libraries,
        and major dependencies. Categorize them by purpose (UI framework, state management,
        testing, build tools, etc.). Output a structured JSON summary.
        """;

    public DependencyAnalyzerAgent(IChatClient chatClient, IConfiguration configuration)
        : base(
            chatClient,
            configuration,
            "DependencyAnalyzer",
            "Enumerates frameworks, libraries, and major dependencies.",
            AgentType.DependencyAnalyzer,
            DefaultPrompt,
            new AIFunction[]
            {
                AIFunctionFactory.Create(FileSystemTools.ReadPackageManifest),
                AIFunctionFactory.Create(FileSystemTools.ReadFileContent)
            })
    {
    }
}
