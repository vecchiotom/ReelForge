using Microsoft.Extensions.AI;
using ReelForge.Inference.Data.Models;

namespace ReelForge.Inference.Agents.Quality;

/// <summary>
/// Reviews the render manifest and scores the output quality.
/// </summary>
public class ReviewAgentImpl : ReelForgeAgentBase
{
    private const string DefaultPrompt =
        """
        You are a quality assurance reviewer for promotional video production. Review the
        RenderManifest, script, and component outputs for quality. Score the output from
        1 to 10 and provide structured feedback.

        Output ONLY valid JSON in this exact format:
        {
            "score": <number 1-10>,
            "comments": {
                "narrativeClarity": "string",
                "visualAccuracy": "string",
                "timing": "string",
                "completeness": "string"
            },
            "mustFix": ["string array of critical issues that must be addressed"]
        }

        Be rigorous: only score 9 or above if the output is production-ready with no
        significant issues.
        """;

    public ReviewAgentImpl(IChatClient chatClient, IConfiguration configuration)
        : base(
            chatClient,
            configuration,
            "Review",
            "Scores output quality and provides structured feedback.",
            AgentType.ReviewAgent,
            DefaultPrompt)
    {
    }
}
