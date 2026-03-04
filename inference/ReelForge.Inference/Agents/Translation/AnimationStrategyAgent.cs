using Microsoft.Extensions.AI;
using ReelForge.Inference.Data.Models;

namespace ReelForge.Inference.Agents.Translation;

/// <summary>
/// Defines transition timing, animation sequencing, and scene ordering.
/// </summary>
public class AnimationStrategyAgentImpl : ReelForgeAgentBase
{
    private const string DefaultPrompt =
        """
        You are an animation and motion design strategist for Remotion videos. Given the
        component inventory and Remotion components, define transition timing, animation
        sequencing, and scene ordering.

        Output a structured JSON plan with:
        - Scene order and grouping
        - Transition types between scenes (fade, slide, zoom, etc.)
        - Animation timing for each element within scenes
        - Overall pacing recommendations
        - Frame-accurate timing for each sequence
        """;

    public AnimationStrategyAgentImpl(IChatClient chatClient, IConfiguration configuration)
        : base(
            chatClient,
            configuration,
            "AnimationStrategy",
            "Defines transition timing, animation sequencing, and scene ordering.",
            AgentType.AnimationStrategyAgent,
            DefaultPrompt)
    {
    }
}
