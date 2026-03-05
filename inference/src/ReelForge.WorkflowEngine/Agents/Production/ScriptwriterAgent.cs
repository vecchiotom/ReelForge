using Microsoft.Extensions.AI;
using ReelForge.Shared.Data.Models;
using ReelForge.WorkflowEngine.Agents.Tools;

namespace ReelForge.WorkflowEngine.Agents.Production;

public class ScriptwriterAgentImpl : ReelForgeAgentBase
{
    private const string DefaultPrompt =
        """
        You are a professional copywriter specializing in app promotional video scripts.
        Write compelling voiceover and caption scripts for each scene based on the app's
        purpose, features, and target audience.

        For each scene, provide:
        - Voiceover text (natural, conversational tone)
        - On-screen caption/text overlay
        - Emphasis words or phrases
        - Estimated reading duration

        Output a structured JSON script document keyed by scene.
        """;

    public ScriptwriterAgentImpl(
        IChatClient chatClient,
        IConfiguration configuration,
        IAgentToolProvider toolProvider)
        : base(chatClient, configuration, "Scriptwriter",
            "Writes the voiceover/caption script for each scene.",
            AgentType.ScriptwriterAgent, DefaultPrompt,
            toolProvider.GetTools(AgentType.ScriptwriterAgent))
    { }
}
