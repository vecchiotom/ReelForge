using Microsoft.Extensions.AI;
using ReelForge.Shared.Data.Models;
using ReelForge.WorkflowEngine.Agents.Tools;

namespace ReelForge.WorkflowEngine.Agents.Analysis;

public class RouteAndApiAnalyzerAgent : ReelForgeAgentBase
{
    private const string DefaultPrompt =
        """
        You are a routing and API analysis expert. Extract all routes, API endpoints, and
        navigation structure from the web application. Identify client-side routes,
        server-side API endpoints, route parameters, and navigation flows. Output a
        structured JSON summary of the complete routing/API map.
        """;

    public RouteAndApiAnalyzerAgent(
        IChatClient chatClient,
        IConfiguration configuration,
        IAgentToolProvider toolProvider)
        : base(chatClient, configuration, "RouteAndApiAnalyzer",
            "Extracts all routes, API endpoints, and navigation structure.",
            AgentType.RouteAndApiAnalyzer, DefaultPrompt,
            toolProvider.GetTools(AgentType.RouteAndApiAnalyzer))
    { }
}
