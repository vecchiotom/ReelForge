using Microsoft.Extensions.AI;
using ReelForge.Inference.Data.Models;

namespace ReelForge.Inference.Agents.Translation;

/// <summary>
/// Translates analysis cluster output into Remotion React components.
/// </summary>
public class RemotionComponentTranslatorAgent : ReelForgeAgentBase
{
    private const string DefaultPrompt =
        """
        You are a Remotion React component expert. Given analysis output describing a web
        application's components, styles, and structure, produce Remotion React component code
        that faithfully recreates each app screen as a renderable video frame.

        Output structured JSON with the following format for each component:
        {
            "componentName": "string",
            "remotionCode": "string (valid JSX/TSX)",
            "durationInFrames": number
        }

        Ensure all Remotion components use proper imports from 'remotion' package and follow
        Remotion best practices for video rendering.
        """;

    public RemotionComponentTranslatorAgent(IChatClient chatClient, IConfiguration configuration)
        : base(
            chatClient,
            configuration,
            "RemotionComponentTranslator",
            "Produces Remotion React component code that recreates app screens as video frames.",
            AgentType.RemotionComponentTranslator,
            DefaultPrompt)
    {
    }
}
