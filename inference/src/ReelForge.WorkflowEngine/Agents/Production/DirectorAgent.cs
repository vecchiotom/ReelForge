using Microsoft.Extensions.AI;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Data.OutputSchemas;
using ReelForge.WorkflowEngine.Agents.Tools;

namespace ReelForge.WorkflowEngine.Agents.Production;

public class DirectorAgentImpl : ReelForgeAgentBase
{
    private const string DefaultPrompt =
        """
        You are a video director specializing in promotional app videos. Break down the video
        into individual SHOTS (which belong to scenes) and provide detailed cinematographic
        direction for each.

        For each shot, specify:
        - ShotId and which SceneId it belongs to
        - Shot description and purpose in the narrative
        - StartTime and Duration (in seconds)
        - Camera direction: Angle (e.g., "close-up", "wide", "over-shoulder"),
          Movement (e.g., "static", "pan-right", "zoom-in"), and Focus point
        - VisualElements to emphasize (e.g., ["app interface", "text overlay", "CTA button"])

        Also define:
        - VisualTheme: overall Mood, ColorGrading style, and VisualMotifs (recurring visual elements)
        - Audio: recommended MusicStyle, SoundEffects timing, and Voiceover tone/pacing
        - TotalDurationInSeconds for the complete video

        Output as DirectorOutput JSON with all shots coordinating into a cohesive narrative
        with clear opening hook and closing call-to-action.
        """;

    public DirectorAgentImpl(
        IChatClient chatClient,
        IConfiguration configuration,
        IAgentToolProvider toolProvider)
        : base(chatClient, configuration, "Director",
            "Composes the overall video narrative structure.",
            AgentType.DirectorAgent, DefaultPrompt,
            toolProvider.GetTools(AgentType.DirectorAgent),
            agentId: null,
            outputSchemaType: typeof(DirectorOutput))
    { }
}
