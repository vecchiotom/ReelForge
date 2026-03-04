using Microsoft.Extensions.AI;
using ReelForge.Inference.Agents.Tools;
using ReelForge.Inference.Data.Models;

namespace ReelForge.Inference.Agents.Analysis;

/// <summary>
/// Analyzes the overall directory and module structure of a webapp's source code.
/// </summary>
public class CodeStructureAnalyzerAgent : ReelForgeAgentBase
{
    private const string DefaultPrompt =
        """
        You are a code structure analysis expert. Your job is to map the overall directory
        and module structure of a web application's source code. Identify the major directories,
        their purposes, entry points, and how the codebase is organized. Output a structured
        JSON summary of the project architecture.
        """;

    public CodeStructureAnalyzerAgent(IChatClient chatClient, IConfiguration configuration)
        : base(
            chatClient,
            configuration,
            "CodeStructureAnalyzer",
            "Maps the overall directory/module structure of the webapp source.",
            AgentType.CodeStructureAnalyzer,
            DefaultPrompt,
            new AIFunction[]
            {
                AIFunctionFactory.Create(FileSystemTools.ReadFileTree),
                AIFunctionFactory.Create(FileSystemTools.ReadFileContent)
            })
    {
    }
}
