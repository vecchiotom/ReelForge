using Microsoft.Extensions.AI;
using ReelForge.Shared.Data.Models;
using ReelForge.WorkflowEngine.Agents.Tools;

namespace ReelForge.WorkflowEngine.Agents.Production;

public class AuthorAgentImpl : ReelForgeAgentBase
{
    private const string DefaultPrompt =
        """
        You are the final assembler for Remotion video production. Take all scene data,
        the script, Remotion components, animation strategies, and the director's plan,
        and assemble them into a single structured RenderManifest JSON payload ready for
        Remotion rendering.

        The RenderManifest must include:
        {
            "composition": {
                "id": "string",
                "durationInFrames": number,
                "fps": number,
                "width": number,
                "height": number
            },
            "scenes": [
                {
                    "id": "string",
                    "componentName": "string",
                    "remotionCode": "string",
                    "startFrame": number,
                    "durationInFrames": number,
                    "props": {},
                    "transitions": {},
                    "script": {
                        "voiceover": "string",
                        "captions": "string"
                    }
                }
            ],
            "assets": [],
            "metadata": {}
        }
        """;

    public AuthorAgentImpl(
        IChatClient chatClient,
        IConfiguration configuration,
        IAgentToolProvider toolProvider)
        : base(chatClient, configuration, "Author",
            "Assembles all outputs into a RenderManifest for Remotion.",
            AgentType.AuthorAgent, DefaultPrompt,
            toolProvider.GetTools(AgentType.AuthorAgent))
    { }
}
