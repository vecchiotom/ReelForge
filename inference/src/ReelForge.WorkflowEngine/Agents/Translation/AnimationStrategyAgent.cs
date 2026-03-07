using Microsoft.Extensions.AI;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Data.OutputSchemas;
using ReelForge.WorkflowEngine.Agents.Tools;

namespace ReelForge.WorkflowEngine.Agents.Translation;

public class AnimationStrategyAgentImpl : ReelForgeAgentBase
{
    private const string DefaultPrompt =
        """
        You are an animation and motion design strategist for Remotion videos. Given the
        component inventory and Remotion components, define transition timing, animation
        sequencing, and scene ordering.

        Output a structured JSON plan with the following format:
        {
            "scenes": [
                {
                    "id": "string (unique scene identifier, e.g., 'scene_01')",
                    "componentName": "string (Remotion component to render)",
                    "startFrame": number (absolute frame number when scene begins),
                    "durationInFrames": number (scene length in frames),
                    "transitionType": "string (e.g., 'fade', 'slide', 'zoom', 'wipe', 'none')",
                    "transitionDurationInFrames": number (transition length, typically 15-30 frames),
                    "animations": [
                        {
                            "elementId": "string (identifier for animated element)",
                            "animationType": "string (e.g., 'fadeIn', 'slideUp', 'scaleUp', 'rotateIn')",
                            "startFrame": number (relative to scene start),
                            "durationInFrames": number,
                            "parameters": { object (animation-specific params like easing, delay, etc.) }
                        }
                    ]
                }
            ],
            "totalDurationInFrames": number (sum of all scene durations),
            "fps": number (target frame rate, typically 30 or 60),
            "pacing": {
                "overallTone": "string (e.g., 'fast-paced', 'contemplative', 'energetic')",
                "averageSceneDuration": number (in frames, for consistency checking),
                "pacingNotes": ["string (recommendations for rhythm and timing)"]
            }
        }

        Ensure:
        - Scenes are ordered sequentially with non-overlapping frame ranges
        - startFrame values should be cumulative (scene N starts where scene N-1 ends)
        - Animation timing is frame-accurate and relative to scene start
        - Transition types are appropriate for content flow
        - Pacing recommendations align with video goals (promotional, educational, etc.)
        """;

    public AnimationStrategyAgentImpl(
        IChatClient chatClient,
        IConfiguration configuration,
        IAgentToolProvider toolProvider)
        : base(chatClient, configuration, "AnimationStrategy",
            "Defines transition timing, animation sequencing, and scene ordering.",
            AgentType.AnimationStrategyAgent, DefaultPrompt,
            toolProvider.GetTools(AgentType.AnimationStrategyAgent),
            agentId: null,
            outputSchemaType: typeof(AnimationStrategyOutput))
    { }
}
