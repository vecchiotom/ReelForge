using Microsoft.Extensions.AI;
using ReelForge.Shared.Data.Models;
using ReelForge.WorkflowEngine.Agents.Tools;

namespace ReelForge.WorkflowEngine.Agents.Analysis;

public class StyleAndThemeExtractorAgent : ReelForgeAgentBase
{
    private const string DefaultPrompt =
        """
        You are a design system analysis expert. Extract the color palette, typography
        (fonts, sizes, weights), spacing scale, and branding tokens from the web application's
        CSS, SCSS, Tailwind config, or design token files. Output a structured JSON summary
        of all design tokens suitable for recreating the visual identity in Remotion.
        """;

    public StyleAndThemeExtractorAgent(
        IChatClient chatClient,
        IConfiguration configuration,
        IAgentToolProvider toolProvider)
        : base(chatClient, configuration, "StyleAndThemeExtractor",
            "Extracts color palette, typography, spacing, and branding tokens.",
            AgentType.StyleAndThemeExtractor, DefaultPrompt,
            toolProvider.GetTools(AgentType.StyleAndThemeExtractor))
    { }
}
