using Microsoft.Extensions.AI;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Data.OutputSchemas;
using ReelForge.WorkflowEngine.Agents.Tools;

namespace ReelForge.WorkflowEngine.Agents.Translation;

public class RemotionComponentTranslatorAgent : ReelForgeAgentBase
{
    private const string DefaultPrompt =
        """
        You are a Remotion React component expert. Given analysis output describing a web
        application's components, styles, and structure, produce Remotion React component code
        that faithfully recreates each app screen as a renderable video frame.

        Output structured JSON with the following format:
        {
            "components": [
                {
                    "componentName": "string (e.g., 'LoginScreen', 'DashboardHero')",
                    "remotionCode": "string (valid JSX/TSX)",
                    "durationInFrames": number,
                    "description": "string (brief description of component's purpose)",
                    "defaultProps": { object (key-value pairs of default props) }
                }
            ],
            "remotionVersion": "string (e.g., '4.0' - target Remotion version)",
            "requiredImports": ["string (e.g., '@remotion/player', 'framer-motion')"]
        }

        Ensure:
        - All components in 'components' array are complete and properly structured
        - 'description' explains each component's role in the video
        - 'defaultProps' includes sensible defaults for component configuration
        - 'requiredImports' lists all external packages needed beyond 'remotion'
        - All Remotion components use proper imports from 'remotion' package and follow Remotion best practices
        """;

    public RemotionComponentTranslatorAgent(
        IChatClient chatClient,
        IConfiguration configuration,
        IAgentToolProvider toolProvider)
        : base(chatClient, configuration, "RemotionComponentTranslator",
            "Produces Remotion React component code that recreates app screens as video frames.",
            AgentType.RemotionComponentTranslator, DefaultPrompt,
            toolProvider.GetTools(AgentType.RemotionComponentTranslator),
            agentId: null,
            outputSchemaType: typeof(RemotionComponentOutput))
    { }
}
