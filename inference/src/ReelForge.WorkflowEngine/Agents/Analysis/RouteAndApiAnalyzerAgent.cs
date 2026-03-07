using Microsoft.Extensions.AI;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Data.OutputSchemas;
using ReelForge.WorkflowEngine.Agents.Tools;

namespace ReelForge.WorkflowEngine.Agents.Analysis;

public class RouteAndApiAnalyzerAgent : ReelForgeAgentBase
{
    private const string DefaultPrompt =
        """
        You are a routing and API analysis expert. Extract all routes, API endpoints, and
        navigation structure from the web application.

        For ClientRoutes, identify:
        - Path: The route path pattern (e.g., "/users/:id", "/products/[slug]", "/app/*")
        - ComponentName: The component or page that handles this route
        - Parameters: Array of dynamic route parameters (e.g., ["id"], ["slug"])
        - RequiresAuth: Boolean indicating if authentication is required (based on middleware, guards, or wrappers)

        For ApiEndpoints, identify:
        - Method: HTTP method (GET, POST, PUT, DELETE, PATCH, etc.)
        - Path: The API endpoint path
        - Purpose: Brief description of what this endpoint does
        - Parameters: Query params, path params, or body parameters

        Also determine:
        - RoutingStrategy: The routing approach being used (e.g., "File-based (Next.js App Router)", "React Router v6", "Vue Router", "Express.js")

        Output a structured JSON summary matching the provided RouteAndApiOutput schema.
        """;

    public RouteAndApiAnalyzerAgent(
        IChatClient chatClient,
        IConfiguration configuration,
        IAgentToolProvider toolProvider)
        : base(chatClient, configuration, "RouteAndApiAnalyzer",
            "Extracts all routes, API endpoints, and navigation structure.",
            AgentType.RouteAndApiAnalyzer, DefaultPrompt,
            toolProvider.GetTools(AgentType.RouteAndApiAnalyzer),
            agentId: null,
            outputSchemaType: typeof(RouteAndApiOutput))
    { }
}
