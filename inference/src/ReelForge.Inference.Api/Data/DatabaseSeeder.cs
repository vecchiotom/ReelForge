using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
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

                // Update output schema if not set
                if (string.IsNullOrEmpty(existing.OutputSchemaJson))
                    existing.OutputSchemaJson = GetOutputSchemaJson(agentType);

                // Always refresh capability metadata
                existing.AvailableToolsJson = GetAvailableToolsJson(agentType);
                existing.GeneratesOutput = GetOutputSchemaName(agentType) != null;
                existing.OutputSchemaName = GetOutputSchemaName(agentType);

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
                OutputSchemaJson = GetOutputSchemaJson(agentType),
                AvailableToolsJson = GetAvailableToolsJson(agentType),
                GeneratesOutput = GetOutputSchemaName(agentType) != null,
                OutputSchemaName = GetOutputSchemaName(agentType),
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }

    private static readonly string[] BaseTools =
    [
        "ListProjectFiles", "ReadProjectFile", "WriteProjectFile",
        "EnsureSandbox", "GetSandbox", "ListSandboxFiles", "ReadSandboxFile",
        "WriteSandboxFile", "DeleteSandboxPath", "RunSandboxNpmScript",
        "RunSandboxRemotionCommand", "CompleteSandbox"
    ];

    private static string GetAvailableToolsJson(AgentType agentType)
    {
        string[] extra = agentType switch
        {
            AgentType.CodeStructureAnalyzer => ["ReadFileTree", "ReadFileContent"],
            AgentType.DependencyAnalyzer => ["ReadPackageManifest", "ReadFileContent"],
            AgentType.ComponentInventoryAnalyzer => ["ReadFileContent", "ListFilesByExtension"],
            AgentType.RouteAndApiAnalyzer => ["ReadFileContent", "SearchPatterns"],
            AgentType.StyleAndThemeExtractor => ["ReadStyleConfig", "ReadFileContent"],
            _ => []
        };
        string[] all = [.. BaseTools, .. extra];
        return JsonSerializer.Serialize(all);
    }

    private static string? GetOutputSchemaName(AgentType agentType) => agentType switch
    {
        AgentType.CodeStructureAnalyzer => "CodeStructureOutput",
        AgentType.DependencyAnalyzer => "DependencyAnalysisOutput",
        AgentType.ComponentInventoryAnalyzer => "ComponentInventoryOutput",
        AgentType.RouteAndApiAnalyzer => "RouteAndApiOutput",
        AgentType.StyleAndThemeExtractor => "StyleAndThemeOutput",
        AgentType.RemotionComponentTranslator => "RemotionComponentOutput",
        AgentType.AnimationStrategyAgent => "AnimationStrategyOutput",
        AgentType.DirectorAgent => "DirectorOutput",
        AgentType.ScriptwriterAgent => "ScriptwriterOutput",
        AgentType.AuthorAgent => "RenderManifestOutput",
        AgentType.ReviewAgent => "ReviewOutput",
        _ => null
    };

    private static string? GetOutputSchemaJson(AgentType agentType)
    {
        object? schema = agentType switch
        {
            AgentType.CodeStructureAnalyzer => GenerateCodeStructureSchema(),
            AgentType.DependencyAnalyzer => GenerateDependencyAnalysisSchema(),
            AgentType.ComponentInventoryAnalyzer => GenerateComponentInventorySchema(),
            AgentType.RouteAndApiAnalyzer => GenerateRouteAndApiSchema(),
            AgentType.StyleAndThemeExtractor => GenerateStyleAndThemeSchema(),
            AgentType.RemotionComponentTranslator => GenerateRemotionComponentSchema(),
            AgentType.AnimationStrategyAgent => GenerateAnimationStrategySchema(),
            AgentType.DirectorAgent => GenerateDirectorSchema(),
            AgentType.ScriptwriterAgent => GenerateScriptwriterSchema(),
            AgentType.AuthorAgent => GenerateRenderManifestSchema(),
            AgentType.ReviewAgent => GenerateReviewSchema(),
            _ => null
        };

        if (schema == null)
            return null;

        try
        {
            return JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return null;
        }
    }

    private static object GenerateCodeStructureSchema() => new
    {
        type = "object",
        properties = new
        {
            projectType = new { type = "string", description = "Type of project (e.g., 'React SPA', 'Next.js', 'Vue.js')" },
            framework = new { type = "string", description = "Framework name and version" },
            directories = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new { type = "string" },
                        purpose = new { type = "string" },
                        fileCount = new { type = "integer" }
                    }
                }
            },
            entryPoints = new { type = "array", items = new { type = "string" } },
            overallArchitecture = new { type = "string", description = "Description of the architectural pattern" }
        },
        required = new[] { "projectType", "framework", "directories" }
    };

    private static object GenerateDependencyAnalysisSchema() => new
    {
        type = "object",
        properties = new
        {
            dependencies = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string" },
                        version = new { type = "string" },
                        purpose = new { type = "string" },
                        isCore = new { type = "boolean" }
                    }
                }
            },
            devDependencies = new { type = "array", items = new { type = "string" } },
            packageManager = new { type = "string", description = "npm, yarn, pnpm, etc." },
            recommendations = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        type = new { type = "string" },
                        description = new { type = "string" }
                    }
                }
            }
        }
    };

    private static object GenerateComponentInventorySchema() => new
    {
        type = "object",
        properties = new
        {
            components = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string" },
                        filePath = new { type = "string" },
                        props = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    name = new { type = "string" },
                                    type = new { type = "string" },
                                    required = new { type = "boolean" },
                                    defaultValue = new { type = "string", nullable = true }
                                }
                            }
                        },
                        responsibility = new { type = "string" },
                        dependencies = new { type = "array", items = new { type = "string" } }
                    }
                }
            },
            totalComponents = new { type = "integer" },
            commonPatterns = new { type = "array", items = new { type = "string" } }
        }
    };

    private static object GenerateRouteAndApiSchema() => new
    {
        type = "object",
        properties = new
        {
            clientRoutes = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new { type = "string" },
                        componentName = new { type = "string" },
                        parameters = new { type = "array", items = new { type = "string" } },
                        requiresAuth = new { type = "boolean" }
                    }
                }
            },
            apiEndpoints = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        method = new { type = "string", description = "GET, POST, PUT, DELETE, etc." },
                        path = new { type = "string" },
                        purpose = new { type = "string" },
                        parameters = new { type = "array", items = new { type = "string" } }
                    }
                }
            },
            routingStrategy = new { type = "string", description = "e.g., 'File-based routing', 'React Router', etc." }
        }
    };

    private static object GenerateStyleAndThemeSchema() => new
    {
        type = "object",
        properties = new
        {
            colors = new
            {
                type = "object",
                properties = new
                {
                    primary = new { type = "string", description = "HEX color code" },
                    secondary = new { type = "string", description = "HEX color code" },
                    background = new { type = "string", description = "HEX color code" },
                    text = new { type = "string", description = "HEX color code" },
                    additional = new { type = "object", additionalProperties = new { type = "string" } }
                }
            },
            typography = new
            {
                type = "object",
                properties = new
                {
                    primaryFont = new { type = "string" },
                    secondaryFont = new { type = "string" },
                    fontSizes = new { type = "object", additionalProperties = new { type = "string" } }
                }
            },
            spacing = new
            {
                type = "object",
                properties = new
                {
                    unit = new { type = "string", description = "e.g., 'px', 'rem'" },
                    scale = new { type = "object", additionalProperties = new { type = "string" } }
                }
            },
            componentStyles = new { type = "array", items = new { type = "string" } },
            stylingApproach = new { type = "string", description = "CSS, SCSS, CSS-in-JS, Tailwind, etc." }
        }
    };

    private static object GenerateRemotionComponentSchema() => new
    {
        type = "object",
        properties = new
        {
            components = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        componentName = new { type = "string" },
                        remotionCode = new { type = "string", description = "Valid JSX/TSX code for Remotion component" },
                        durationInFrames = new { type = "integer" },
                        description = new { type = "string" },
                        defaultProps = new { type = "object", additionalProperties = true }
                    },
                    required = new[] { "componentName", "remotionCode", "durationInFrames" }
                }
            },
            remotionVersion = new { type = "string", description = "Remotion version (e.g., '4.0')" },
            requiredImports = new { type = "array", items = new { type = "string" } }
        }
    };

    private static object GenerateAnimationStrategySchema() => new
    {
        type = "object",
        properties = new
        {
            scenes = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        id = new { type = "string" },
                        componentName = new { type = "string" },
                        startFrame = new { type = "integer" },
                        durationInFrames = new { type = "integer" },
                        transitionType = new { type = "string", description = "fade, slide, zoom, etc." },
                        transitionDurationInFrames = new { type = "integer" },
                        animations = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    elementId = new { type = "string" },
                                    animationType = new { type = "string" },
                                    startFrame = new { type = "integer" },
                                    durationInFrames = new { type = "integer" },
                                    parameters = new { type = "object", additionalProperties = true }
                                }
                            }
                        }
                    }
                }
            },
            totalDurationInFrames = new { type = "integer" },
            fps = new { type = "integer", description = "Frames per second (default: 30)" },
            pacing = new
            {
                type = "object",
                properties = new
                {
                    overallTone = new { type = "string" },
                    averageSceneDuration = new { type = "integer" },
                    pacingNotes = new { type = "array", items = new { type = "string" } }
                }
            }
        }
    };

    private static object GenerateDirectorSchema() => new
    {
        type = "object",
        properties = new
        {
            shots = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        shotId = new { type = "string" },
                        sceneId = new { type = "string" },
                        description = new { type = "string" },
                        startTime = new { type = "integer" },
                        duration = new { type = "integer" },
                        camera = new
                        {
                            type = "object",
                            properties = new
                            {
                                angle = new { type = "string" },
                                movement = new { type = "string" },
                                focus = new { type = "string" }
                            }
                        },
                        visualElements = new { type = "array", items = new { type = "string" } }
                    }
                }
            },
            visualTheme = new
            {
                type = "object",
                properties = new
                {
                    mood = new { type = "string" },
                    colorGrading = new { type = "string" },
                    visualMotifs = new { type = "array", items = new { type = "string" } }
                }
            },
            audio = new
            {
                type = "object",
                properties = new
                {
                    musicStyle = new { type = "string" },
                    soundEffects = new { type = "string" },
                    voiceover = new { type = "string" }
                }
            },
            totalDurationInSeconds = new { type = "integer" }
        }
    };

    private static object GenerateScriptwriterSchema() => new
    {
        type = "object",
        properties = new
        {
            title = new { type = "string" },
            durationInSeconds = new { type = "integer" },
            scenes = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        sceneId = new { type = "string" },
                        startTime = new { type = "integer" },
                        duration = new { type = "integer" },
                        voiceover = new { type = "string" },
                        onScreenText = new { type = "array", items = new { type = "string" } },
                        visualDescription = new { type = "string" }
                    }
                }
            },
            narrative = new { type = "string", description = "Overall narrative arc description" }
        }
    };

    private static object GenerateRenderManifestSchema() => new
    {
        type = "object",
        properties = new
        {
            projectName = new { type = "string" },
            video = new
            {
                type = "object",
                properties = new
                {
                    width = new { type = "integer", description = "Video width in pixels (default: 1920)" },
                    height = new { type = "integer", description = "Video height in pixels (default: 1080)" },
                    fps = new { type = "integer", description = "Frames per second (default: 30)" },
                    durationInFrames = new { type = "integer" }
                }
            },
            compositions = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        id = new { type = "string" },
                        componentName = new { type = "string" },
                        startFrame = new { type = "integer" },
                        durationInFrames = new { type = "integer" },
                        props = new { type = "object", additionalProperties = true },
                        script = new
                        {
                            type = "object",
                            properties = new
                            {
                                voiceover = new { type = "string" },
                                captions = new { type = "array", items = new { type = "string" } }
                            }
                        }
                    }
                }
            },
            assets = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        id = new { type = "string" },
                        type = new { type = "string" },
                        path = new { type = "string" },
                        properties = new { type = "object", additionalProperties = true }
                    }
                }
            },
            metadata = new { type = "object", additionalProperties = true }
        },
        required = new[] { "projectName", "video", "compositions" }
    };

    private static object GenerateReviewSchema() => new
    {
        type = "object",
        properties = new
        {
            overallScore = new { type = "integer", minimum = 1, maximum = 10, description = "Overall quality score from 1 to 10" },
            criteria = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string" },
                        score = new { type = "integer", minimum = 1, maximum = 10 },
                        feedback = new { type = "string" }
                    }
                }
            },
            strengths = new { type = "array", items = new { type = "string" } },
            improvementAreas = new { type = "array", items = new { type = "string" } },
            passesReview = new { type = "boolean", description = "Whether the output meets quality standards" },
            summary = new { type = "string", description = "Overall review summary" }
        },
        required = new[] { "overallScore", "passesReview", "summary" }
    };
}