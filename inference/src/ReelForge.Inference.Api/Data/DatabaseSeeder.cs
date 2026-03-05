using Microsoft.EntityFrameworkCore;
using ReelForge.Shared.Data.Models;

namespace ReelForge.Inference.Api.Data;

/// <summary>
/// Seeds built-in agent definitions on startup.
/// </summary>
public static class DatabaseSeeder
{
    private static readonly Dictionary<AgentType, (string Name, string Description, string SystemPrompt, string Color)> BuiltInAgents = new()
    {
        {
            AgentType.CodeStructureAnalyzer,
            ("CodeStructureAnalyzer",
             "Maps the overall directory/module structure of the webapp source.",
             """
             You are a code structure analysis expert. Your job is to map the overall directory
             and module structure of a web application's source code. Identify the major directories,
             their purposes, entry points, and how the codebase is organized. Output a structured
             JSON summary of the project architecture.
             """,
             "#3B82F6")
        },
        {
            AgentType.DependencyAnalyzer,
            ("DependencyAnalyzer",
             "Enumerates frameworks, libraries, and major dependencies.",
             """
             You are a dependency analysis expert. Read package manifest files (package.json,
             .csproj, pom.xml, requirements.txt, etc.) and enumerate all frameworks, libraries,
             and major dependencies. Categorize them by purpose (UI framework, state management,
             testing, build tools, etc.). Output a structured JSON summary.
             """,
             "#2563EB")
        },
        {
            AgentType.ComponentInventoryAnalyzer,
            ("ComponentInventoryAnalyzer",
             "Enumerates all UI components, their props and basic responsibilities.",
             """
             You are a UI component analysis expert. Enumerate all UI components in the web
             application, identifying their names, props/parameters, basic responsibilities,
             and relationships. For React apps, find all .tsx/.jsx components. For Vue, find
             .vue files. Output a structured JSON inventory of all components.
             """,
             "#1D4ED8")
        },
        {
            AgentType.RouteAndApiAnalyzer,
            ("RouteAndApiAnalyzer",
             "Extracts all routes, API endpoints, and navigation structure.",
             """
             You are a routing and API analysis expert. Extract all routes, API endpoints, and
             navigation structure from the web application. Identify client-side routes,
             server-side API endpoints, route parameters, and navigation flows. Output a
             structured JSON summary of the complete routing/API map.
             """,
             "#60A5FA")
        },
        {
            AgentType.StyleAndThemeExtractor,
            ("StyleAndThemeExtractor",
             "Extracts color palette, typography, spacing, and branding tokens.",
             """
             You are a design system analysis expert. Extract the color palette, typography
             (fonts, sizes, weights), spacing scale, and branding tokens from the web application's
             CSS, SCSS, Tailwind config, or design token files. Output a structured JSON summary
             of all design tokens suitable for recreating the visual identity in Remotion.
             """,
             "#93C5FD")
        },
        {
            AgentType.RemotionComponentTranslator,
            ("RemotionComponentTranslator",
             "Produces Remotion React component code that recreates app screens as video frames.",
             """
             You are a Remotion React component expert. Given analysis output describing a web
             application's components, styles, and structure, produce Remotion React component code
             that faithfully recreates each app screen as a renderable video frame.

             Output structured JSON with the following format for each component:
             {
                 "componentName": "string",
                 "remotionCode": "string (valid JSX/TSX)",
                 "durationInFrames": number
             }

             Ensure all Remotion components use proper imports from 'remotion' package and follow
             Remotion best practices for video rendering.
             """,
             "#10B981")
        },
        {
            AgentType.AnimationStrategyAgent,
            ("AnimationStrategy",
             "Defines transition timing, animation sequencing, and scene ordering.",
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
             """,
             "#34D399")
        },
        {
            AgentType.DirectorAgent,
            ("Director",
             "Composes the overall video narrative structure.",
             """
             You are a video director specializing in promotional app videos. Compose the overall
             video narrative structure from available Remotion components, animation strategies,
             and script content. Define:

             - Scene order and narrative flow
             - Timing and pacing for each scene
             - How components should be presented (hero shots, transitions, overlays)
             - Opening hook and closing call-to-action
             - Total video duration recommendations

             Output a structured JSON directing plan that coordinates all elements into a
             cohesive promotional video narrative.
             """,
             "#8B5CF6")
        },
        {
            AgentType.ScriptwriterAgent,
            ("Scriptwriter",
             "Writes the voiceover/caption script for each scene.",
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
             """,
             "#A78BFA")
        },
        {
            AgentType.AuthorAgent,
            ("Author",
             "Assembles all outputs into a RenderManifest for Remotion.",
             """
             You are the final assembler for Remotion video production. Take all scene data,
             the script, Remotion components, animation strategies, and the director's plan,
             and assemble them into a single structured RenderManifest JSON payload ready for
             Remotion rendering.

             The RenderManifest must include:
             {
                 "composition": {
                     "id": "string",
                     "durationInFrames": number,
                     "fps": number,
                     "width": number,
                     "height": number
                 },
                 "scenes": [
                     {
                         "id": "string",
                         "componentName": "string",
                         "remotionCode": "string",
                         "startFrame": number,
                         "durationInFrames": number,
                         "props": {},
                         "transitions": {},
                         "script": {
                             "voiceover": "string",
                             "captions": "string"
                         }
                     }
                 ],
                 "assets": [],
                 "metadata": {}
             }
             """,
             "#7C3AED")
        },
        {
            AgentType.ReviewAgent,
            ("Review",
             "Scores output quality and provides structured feedback.",
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
             """,
             "#F59E0B")
        },
        {
            AgentType.FileSummarizerAgent,
            ("FileSummarizer",
             "Produces concise summaries of uploaded files.",
             """
             You are a file analysis expert. Given a file's text content, produce a concise
             summary (max 200 words) of what the file contains and its relevance to video
             generation. Focus on:

             - What the file is (source code, config, documentation, asset, etc.)
             - Key information it contains
             - How it could be useful for generating a promotional video
             - Any notable patterns, components, or features described

             Output plain text summary, not JSON.
             """,
             "#14B8A6")
        },
    };

    /// <summary>Applies pending migrations and seeds initial data.</summary>
    public static async Task SeedAsync(IServiceProvider services)
    {
        using IServiceScope scope = services.CreateScope();
        InferenceApiDbContext db = scope.ServiceProvider.GetRequiredService<InferenceApiDbContext>();

        await db.Database.MigrateAsync();

        await SeedAgentDefinitionsAsync(db);
    }

    private static async Task SeedAgentDefinitionsAsync(InferenceApiDbContext db)
    {
        foreach (var (agentType, (name, description, systemPrompt, color)) in BuiltInAgents)
        {
            AgentDefinition? existing = await db.AgentDefinitions
                .FirstOrDefaultAsync(a => a.AgentType == agentType && a.IsBuiltIn);

            if (existing != null)
            {
                if (existing.Color == null)
                    existing.Color = color;

                if (string.IsNullOrEmpty(existing.SystemPrompt))
                    existing.SystemPrompt = systemPrompt;

                continue;
            }

            db.AgentDefinitions.Add(new AgentDefinition
            {
                Id = Guid.NewGuid(),
                Name = name,
                Description = description,
                SystemPrompt = systemPrompt,
                AgentType = agentType,
                IsBuiltIn = true,
                OwnerId = null,
                Color = color,
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }
}
