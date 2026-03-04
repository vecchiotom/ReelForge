using Microsoft.Extensions.AI;
using ReelForge.Inference.Data.Models;

namespace ReelForge.Inference.Agents.Production;

/// <summary>
/// Composes the overall video narrative structure — scene order, timing, pacing.
/// </summary>
public class DirectorAgentImpl : ReelForgeAgentBase
{
    private const string DefaultPrompt =
        """
        You are a video director specializing in promotional app videos. Compose the overall
        video narrative structure from available Remotion components, animation strategies,
        and script content. Define:

        - Scene order and narrative flow
        - Timing and pacing for each scene
        - How components should be presented (hero shots, transitions, overlays)
        - Opening hook and closing call-to-action
        - Total video duration recommendations

        Output a structured JSON directing plan that coordinates all elements into a
        cohesive promotional video narrative.
        """;

    public DirectorAgentImpl(IChatClient chatClient, IConfiguration configuration)
        : base(
            chatClient,
            configuration,
            "Director",
            "Composes the overall video narrative structure.",
            AgentType.DirectorAgent,
            DefaultPrompt)
    {
    }
}
