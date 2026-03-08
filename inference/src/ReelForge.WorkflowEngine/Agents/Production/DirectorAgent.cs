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

        ## Tools

        You have tools to read all prior agent outputs before composing your direction:
        1. Call `ListProjectFiles` to list all project files (agents persist their outputs there).
        2. Call `ReadProjectFile` with a file's ID or name to read any agent output (animation
           strategy, component inventory, structure analysis, style tokens, script, etc.).
        3. Call `GetSandboxStatus` to verify the sandbox is active.
        4. Call `ListSandboxFiles` with a directory path (e.g., `"src/"`) to browse the
           Remotion components in the sandbox.
        5. Call `ReadSandboxFile` with a relative path to read any sandbox file content.

        Always start by calling `ListProjectFiles`, then read the animation strategy, component
        inventory, and style outputs before composing the cinematographic direction.

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

        If at any point you determine the workflow cannot proceed due to an unrecoverable
        condition (missing data, inconsistent state, etc.), call the `FailWorkflow(reason)`
        tool with a clear human-readable explanation. This will abort the entire workflow
        immediately and surface the message to the user. Use it only for non-transient errors.
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
