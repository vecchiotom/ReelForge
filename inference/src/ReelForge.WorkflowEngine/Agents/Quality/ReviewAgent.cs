using Microsoft.Extensions.AI;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Data.OutputSchemas;
using ReelForge.WorkflowEngine.Agents.Tools;

namespace ReelForge.WorkflowEngine.Agents.Quality;

public class ReviewAgentImpl : ReelForgeAgentBase
{
    private const string DefaultPrompt =
        """
        You are a quality assurance reviewer for promotional video production. Review the
        RenderManifest, script, and component outputs for quality. Score the output from
        1 to 10 and provide structured feedback.

        Output ONLY valid JSON in this exact format:
        {
            "overallScore": <number 1-10>,
            "criteria": [
                {
                    "name": "<criterion name>",
                    "score": <number 1-10>,
                    "feedback": "<detailed feedback for this criterion>"
                }
            ],
            "strengths": ["<positive aspect 1>", "<positive aspect 2>", ...],
            "improvementAreas": ["<issue or improvement 1>", "<issue or improvement 2>", ...],
            "passesReview": <true/false>,
            "summary": "<overall assessment summary>"
        }

        Evaluate these criteria:
        - narrativeClarity: Story flow and messaging clarity
        - visualAccuracy: Alignment with brand and content requirements
        - timing: Pacing and duration appropriateness
        - completeness: All required elements present and functional

        For each criterion, provide a score (1-10) and specific feedback.
        Set passesReview to true only if overallScore >= 9 and no critical issues exist.
        List strengths separately from improvementAreas.

        Be rigorous: only score 9 or above if the output is production-ready with no
        significant issues.
        """;

    public ReviewAgentImpl(
        IChatClient chatClient,
        IConfiguration configuration,
        IAgentToolProvider toolProvider)
        : base(chatClient, configuration, "Review",
            "Scores output quality and provides structured feedback.",
            AgentType.ReviewAgent, DefaultPrompt,
            toolProvider.GetTools(AgentType.ReviewAgent),
            agentId: null,
            outputSchemaType: typeof(ReviewOutput))
    { }
}
