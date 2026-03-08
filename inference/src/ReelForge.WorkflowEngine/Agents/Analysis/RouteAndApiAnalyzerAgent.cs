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

        ## Tools

        You have tools to locate and inspect routing and API files — use them:
        1. Call `ListProjectFiles` to get the full list of available project files.
        2. Call `ReadProjectFile` with a file's ID or name to read its content.
        3. Call `ReadFileContent` to format and present a specific file's content
           (pass the file path and content as arguments).
        4. Call `SearchPatterns` with a text or regex pattern and source content to find specific
           patterns (e.g., route definitions, API handlers) within files you have already read.

        Start with `ListProjectFiles`, then read routing configuration files, middleware, and
        page/handler files. Use `SearchPatterns` on file content to locate route declarations,
        API endpoint paths, and navigation guards.

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

        If at any point you determine the workflow cannot proceed due to an unrecoverable
        condition (missing data, inconsistent state, etc.), call the `FailWorkflow(reason)`
        tool with a clear human-readable explanation. This will abort the entire workflow
        immediately and surface the message to the user. Use it only for non-transient errors.
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
