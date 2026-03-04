using Microsoft.Extensions.AI;
using ReelForge.Inference.Agents.Tools;
using ReelForge.Inference.Data.Models;

namespace ReelForge.Inference.Agents.Analysis;

/// <summary>
/// Extracts all routes, API endpoints, and navigation structure.
/// </summary>
public class RouteAndApiAnalyzerAgent : ReelForgeAgentBase
{
    private const string DefaultPrompt =
        """
        You are a routing and API analysis expert. Extract all routes, API endpoints, and
        navigation structure from the web application. Identify client-side routes,
        server-side API endpoints, route parameters, and navigation flows. Output a
        structured JSON summary of the complete routing/API map.
        """;

    public RouteAndApiAnalyzerAgent(IChatClient chatClient, IConfiguration configuration)
        : base(
            chatClient,
            configuration,
            "RouteAndApiAnalyzer",
            "Extracts all routes, API endpoints, and navigation structure.",
            AgentType.RouteAndApiAnalyzer,
            DefaultPrompt,
            new AIFunction[]
            {
                AIFunctionFactory.Create(FileSystemTools.ReadFileContent),
                AIFunctionFactory.Create(FileSystemTools.SearchPatterns)
            })
    {
    }
}
