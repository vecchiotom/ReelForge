using Microsoft.Extensions.AI;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Data.OutputSchemas;
using ReelForge.WorkflowEngine.Agents.Tools;

namespace ReelForge.WorkflowEngine.Agents.Production;

public class ScriptwriterAgentImpl : ReelForgeAgentBase
{
    private const string DefaultPrompt =
        """
        You are a professional copywriter specializing in app promotional video scripts.
        Write compelling scripts for each scene based on the app's purpose, features, and
        target audience.

        Provide:
        - Title: A catchy title for the video script
        - DurationInSeconds: Total script duration across all scenes
        - Narrative: Overall narrative arc/story summary (1-2 sentences)

        For each scene in Scenes array, provide:
        - SceneId: Identifier matching the scene
        - StartTime: When this scene starts (in seconds)
        - Duration: Scene duration (in seconds)
        - Voiceover: Natural, conversational voiceover text
        - OnScreenText: List of on-screen text/caption elements (can be multiple per scene)
        - VisualDescription: What should be visually happening during this scene

        Ensure voiceover timing aligns with Duration. Use clear, benefit-focused language
        that emphasizes the app's value proposition.

        Output as ScriptwriterOutput JSON.
        """;

    public ScriptwriterAgentImpl(
        IChatClient chatClient,
        IConfiguration configuration,
        IAgentToolProvider toolProvider)
        : base(chatClient, configuration, "Scriptwriter",
            "Writes the voiceover/caption script for each scene.",
            AgentType.ScriptwriterAgent, DefaultPrompt,
            toolProvider.GetTools(AgentType.ScriptwriterAgent),
            agentId: null,
            outputSchemaType: typeof(ScriptwriterOutput))
    { }
}
