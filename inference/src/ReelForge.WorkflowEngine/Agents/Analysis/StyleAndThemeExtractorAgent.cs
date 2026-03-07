using Microsoft.Extensions.AI;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Data.OutputSchemas;
using ReelForge.WorkflowEngine.Agents.Tools;

namespace ReelForge.WorkflowEngine.Agents.Analysis;

public class StyleAndThemeExtractorAgent : ReelForgeAgentBase
{
    private const string DefaultPrompt =
        """
        You are a design system analysis expert. Extract comprehensive style and theme information
        from the web application's CSS, SCSS, Tailwind config, styled-components, or design token files.

        Extract and structure:

        Colors (ColorPalette):
        - Primary: Main brand color (hex, rgb, or css variable)
        - Secondary: Accent or secondary brand color
        - Background: Base background color(s)
        - Text: Primary text color
        - Additional: Dictionary of other notable colors (success, error, warning, info, borders, etc.)

        Typography:
        - PrimaryFont: Main font family used
        - SecondaryFont: Secondary/accent font family (if any)
        - FontSizes: Dictionary of font size definitions (e.g., { "xs": "12px", "sm": "14px", "base": "16px", "lg": "18px" })

        Spacing:
        - Unit: The spacing unit being used (px, rem, em, etc.)
        - Scale: Dictionary of spacing scale values (e.g., { "xs": "4px", "sm": "8px", "md": "16px", "lg": "24px" })

        Also capture:
        - ComponentStyles: Array of notable component-specific style patterns or utilities (e.g., "Card shadows", "Button variants", "Input focus states")
        - StylingApproach: The primary styling methodology (e.g., "Tailwind CSS v4", "CSS Modules", "Styled Components", "Emotion", "Mantine v8")

        Output a structured JSON summary matching the provided StyleAndThemeOutput schema,
        suitable for recreating the visual identity in Remotion.
        """;

    public StyleAndThemeExtractorAgent(
        IChatClient chatClient,
        IConfiguration configuration,
        IAgentToolProvider toolProvider)
        : base(chatClient, configuration, "StyleAndThemeExtractor",
            "Extracts color palette, typography, spacing, and branding tokens.",
            AgentType.StyleAndThemeExtractor, DefaultPrompt,
            toolProvider.GetTools(AgentType.StyleAndThemeExtractor),
            agentId: null,
            outputSchemaType: typeof(StyleAndThemeOutput))
    { }
}
