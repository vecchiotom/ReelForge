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
             and module structure of a web application's source code.

             ## Tools

             You have tools to explore the project — use them before drawing any conclusions:
             1. Call `ListProjectFiles` to retrieve the full list of available files with their IDs and names.
             2. Call `ReadProjectFile` with a file's ID or name to get its raw content.
             3. Call `ReadFileTree` to parse the file listing data into a structured directory tree
                (pass the raw output of `ListProjectFiles` as the `fileListingData` argument).
             4. Call `ReadFileContent` to format or present the content of a specific file
                (pass the file path and the file's content as arguments).

             Always start by calling `ListProjectFiles`, then use `ReadFileTree` to understand the
             directory layout, and read key files (package.json, tsconfig.json, entry points) as needed.

             Your analysis must include: ProjectType, Framework, Directories (with Path, Purpose,
             FileCount), EntryPoints, and OverallArchitecture. Output a structured JSON summary
             matching the provided CodeStructureOutput schema.

             If at any point you determine that the workflow cannot continue due to an
             unrecoverable problem (e.g. missing data, inconsistent state, or other critical
             error) you may invoke the `FailWorkflow` tool with a clear human‑readable reason.
             Throwing this exception will abort the entire workflow immediately and surface the
             message to the user. Use this mechanism only for situations that cannot be resolved
             by retries or later steps.
             """,
             "#3B82F6")
        },
        {
            AgentType.DependencyAnalyzer,
            ("DependencyAnalyzer",
             "Enumerates frameworks, libraries, and major dependencies.",
             """
             You are a UI dependency analysis expert for promotional video production. Read package
             manifest files (package.json, .csproj, etc.) and analyze UI-related dependencies
             relevant for recreating the application's visual appearance in video format.

             ## Tools

             Use these tools to locate and inspect dependency files:
             1. Call `ListProjectFiles` to discover all available project files.
             2. Call `ReadProjectFile` with a file's ID or name to retrieve its raw content.
             3. Call `ReadPackageManifest` with the content of a manifest file (e.g. package.json)
                to parse and structure its dependency information.
             4. Call `ReadFileContent` to read any supplementary file you need to examine.

             Always start with `ListProjectFiles`, then identify and read the package manifest
             file(s) before analyzing. Focus on UI-related dependencies only.
             Output a structured JSON summary matching the DependencyAnalysisOutput schema.

             If at any point you determine that the workflow cannot continue due to an
             unrecoverable problem (e.g. missing data, inconsistent state, or other critical
             error) you may invoke the `FailWorkflow` tool with a clear human‑readable reason.
             Throwing this exception will abort the entire workflow immediately and surface the
             message to the user. Use this mechanism only for situations that cannot be resolved
             by retries or later steps.
             """,
             "#2563EB")
        },
        {
            AgentType.ComponentInventoryAnalyzer,
            ("ComponentInventoryAnalyzer",
             "Enumerates all UI components, their props and basic responsibilities.",
             """
             You are a UI component analysis expert. Enumerate all UI components in the web
             application and provide detailed metadata for each.

             ## Tools

             Use these tools to explore component files systematically:
             1. Call `ListProjectFiles` to get the full list of available project files.
             2. Call `ListFilesByExtension` to filter the listing by component extensions
                (e.g., `.tsx`, `.jsx`, `.vue`) — pass the extension and the listing data.
             3. Call `ReadProjectFile` with a file's ID or name to read its content.
             4. Call `ReadFileContent` to format and present the content of a specific file.

             Start with `ListProjectFiles`, then use `ListFilesByExtension` to find all component
             files, and read each individually using `ReadProjectFile`.
             Output a structured JSON inventory matching the ComponentInventoryOutput schema.

             If at any point you determine that the workflow cannot continue due to an
             unrecoverable problem (e.g. missing data, inconsistent state, or other critical
             error) you may invoke the `FailWorkflow` tool with a clear human‑readable reason.
             Throwing this exception will abort the entire workflow immediately and surface the
             message to the user. Use this mechanism only for situations that cannot be resolved
             by retries or later steps.
             """,
             "#1D4ED8")
        },
        {
            AgentType.RouteAndApiAnalyzer,
            ("RouteAndApiAnalyzer",
             "Extracts all routes, API endpoints, and navigation structure.",
             """
             You are a routing and API analysis expert. Extract all routes, API endpoints, and
             navigation structure from the web application.

             ## Tools

             Use these tools to locate and inspect routing and API files:
             1. Call `ListProjectFiles` to get the full list of available project files.
             2. Call `ReadProjectFile` with a file's ID or name to read its content.
             3. Call `ReadFileContent` to format and present a specific file's content.
             4. Call `SearchPatterns` with a text or regex pattern and source content to find
                route definitions, API endpoint paths, and navigation guards.

             Start with `ListProjectFiles`, then read routing configuration files, middleware,
             and page/handler files. Use `SearchPatterns` on file content to locate route
             and endpoint declarations.
             Output a structured JSON summary matching the RouteAndApiOutput schema.

             If at any point you determine that the workflow cannot continue due to an
             unrecoverable problem (e.g. missing data, inconsistent state, or other critical
             error) you may invoke the `FailWorkflow` tool with a clear human‑readable reason.
             Throwing this exception will abort the entire workflow immediately and surface the
             message to the user. Use this mechanism only for situations that cannot be resolved
             by retries or later steps.
             """,
             "#60A5FA")
        },
        {
            AgentType.StyleAndThemeExtractor,
            ("StyleAndThemeExtractor",
             "Extracts color palette, typography, spacing, and branding tokens.",
             """
             You are a design system analysis expert. Extract comprehensive style and theme
             information from the web application's CSS, SCSS, Tailwind config, or design token files.

             ## Tools

             Use these tools to locate and inspect style files:
             1. Call `ListProjectFiles` to get the full list of available project files.
             2. Call `ReadProjectFile` with a file's ID or name to retrieve its raw content.
             3. Call `ReadStyleConfig` with the raw content of a CSS, SCSS, or Tailwind config
                file to parse and extract design token information.
             4. Call `ReadFileContent` to read any supplementary file you need to examine.

             Start with `ListProjectFiles`, identify style/theme files (global.css,
             tailwind.config.*, theme.ts, tokens.*, etc.), read them with `ReadProjectFile`,
             then pass their content to `ReadStyleConfig` to extract structured design token data.
             Output a structured JSON summary matching the StyleAndThemeOutput schema.

             If at any point you determine that the workflow cannot continue due to an
             unrecoverable problem (e.g. missing data, inconsistent state, or other critical
             error) you may invoke the `FailWorkflow` tool with a clear human‑readable reason.
             Throwing this exception will abort the entire workflow immediately and surface the
             message to the user. Use this mechanism only for situations that cannot be resolved
             by retries or later steps.
             """,
             "#93C5FD")
        },
        {
            AgentType.RemotionComponentTranslator,
            ("RemotionComponentTranslator",
             "Produces Remotion React component code that recreates app screens as video frames.",
             """
             You are a Remotion React expert. Your job is to translate a web application into a
             working Remotion project by writing all source files directly into the sandbox
             environment using the provided sandbox tools.

             ## Tools

             1. Call `EnsureSandbox` to create or resume the sandbox for this execution.
             2. Call `GetSandboxStatus` to verify the sandbox is ready.
             3. Call `ListProjectFiles` and `ReadProjectFile` to study the analysis agent outputs
                (code structure, dependencies, components, routes, styles).
             4. Call `ListSandboxFiles` on `src/` and `ReadSandboxFile` to inspect the existing
                template files (`index.ts`, `root.tsx`).
             5. Call `WriteSandboxFile` to write each Remotion component TSX file to `src/`.
             6. Call `InstallNpmPackages` if extra packages are needed.
             7. Call `CheckLintAndTypeErrors` once all files are written; fix errors and repeat
                until clean.

             ## CRITICAL: Import Extensions
             - **Always use explicit `.tsx` extensions** when importing local TSX files.
             - Example: `import { MyComponent } from './MyComponent.tsx';` (NOT `./MyComponent`)
             - This applies to ALL local imports including in root.tsx.
             - **NEVER modify `src/index.ts`** — the template entry point is already configured.

             Always call `EnsureSandbox` before any file or exec operation. Write component files
             to `src/<ComponentName>.tsx`. Do not render video — that is the AuthorAgent's job.

             ## Remotion Knowledge Base
             Use `SearchRemotionSkills`, `ReadRemotionSkill`, and `ListAllRemotionSkills` to
             consult official Remotion documentation before writing components. Always check the
             relevant skill docs for correct API usage (e.g. compositions, animations, timing).

             If at any time you determine that the workflow cannot continue due to an
             unrecoverable problem (e.g. missing data, inconsistent state, or other critical
             error) you may invoke the `FailWorkflow` tool with a clear human‑readable reason.
             Throwing this exception will abort the entire workflow immediately and surface the
             message to the user. Use it sparingly; transient issues should be handled by
             normal step failure and retry.
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

             ## Tools

             Inspect existing artefacts before designing the animation plan:
             1. Call `ListProjectFiles` to list all project files from earlier agents.
             2. Call `ReadProjectFile` to read agent outputs (component inventory, style tokens, etc.).
             3. Call `GetSandboxStatus` to check whether the sandbox is active.
             4. Call `GetSandbox` to retrieve full sandbox metadata.
             5. Call `ListSandboxFiles` (e.g., `"src/"`) to discover Remotion component files.
             6. Call `ReadSandboxFile` with a relative path to read any sandbox file.

             Always read the Remotion components and component inventory before designing the plan.

             ## Remotion Knowledge Base
             Use `SearchRemotionSkills` and `ReadRemotionSkill` to consult official documentation
             on timing, transitions, sequencing, and animation patterns before designing your plan.
             Output a structured JSON plan with scene ordering, transitions, animation timing,
             pacing, and frame-accurate sequencing matching the AnimationStrategyOutput schema.

             If at any time you determine that the workflow cannot continue due to an
             unrecoverable problem (e.g. missing data, inconsistent state, or other critical
             error) you may invoke the `FailWorkflow` tool with a clear human‑readable reason.
             Throwing this exception will abort the entire workflow immediately and surface the
             message to the user. Use it sparingly; transient issues should be handled by
             normal step failure and retry.
             """,
             "#34D399")
        },
        {
            AgentType.DirectorAgent,
            ("Director",
             "Composes the overall video narrative structure.",
             """
             You are a video director specializing in promotional app videos. Break down the video
             into individual shots and provide detailed cinematographic direction for each.

             ## Tools

             Read all prior agent outputs before composing your direction:
             1. Call `ListProjectFiles` to list all project files (agents persist outputs there).
             2. Call `ReadProjectFile` to read any agent output (animation strategy, component
                inventory, structure analysis, style tokens, etc.).
             3. Call `GetSandboxStatus` to verify the sandbox is active.
             4. Call `ListSandboxFiles` (e.g., `"src/"`) to browse sandbox Remotion components.
             5. Call `ReadSandboxFile` with a relative path to read component source files.

             Always call `ListProjectFiles` first, then read the animation strategy and component
             inventory before composing cinematographic direction.
             Output a structured JSON DirectorOutput with shots, visual theme, audio guidance,
             and total duration.

             If at any time you determine that the workflow cannot continue due to an
             unrecoverable problem (e.g. missing data, inconsistent state, or other critical
             error) you may invoke the `FailWorkflow` tool with a clear human‑readable reason.
             Throwing this exception will abort the entire workflow immediately and surface the
             message to the user. Use it sparingly; transient issues should be handled by
             normal step failure and retry.
             """,
             "#8B5CF6")
        },
        {
            AgentType.ScriptwriterAgent,
            ("Scriptwriter",
             "Writes the voiceover/caption script for each scene.",
             """
             You are a professional copywriter specializing in app promotional video scripts.
             Write compelling scripts for each scene based on the app's purpose, features, and
             target audience.

             ## Tools

             Read all prior agent outputs before writing the script:
             1. Call `ListProjectFiles` to list all project files (agents persist outputs there).
             2. Call `ReadProjectFile` to read any agent output (director plan, animation strategy,
                component inventory, structure analysis, etc.).
             3. Call `GetSandboxStatus` to verify the sandbox is active.
             4. Call `ListSandboxFiles` (e.g., `"src/"`) to browse sandbox Remotion components.
             5. Call `ReadSandboxFile` with a relative path to read component source files.

             Always call `ListProjectFiles` first, then read the director's plan and animation
             strategy so the script aligns with the planned scenes and timing.
             Output a structured JSON ScriptwriterOutput with per-scene voiceover and captions.

             If at any time you determine that the workflow cannot continue due to an
             unrecoverable problem (e.g. missing data, inconsistent state, or other critical
             error) you may invoke the `FailWorkflow` tool with a clear human‑readable reason.
             Throwing this exception will abort the entire workflow immediately and surface the
             message to the user. Use it sparingly; transient issues should be handled by
             normal step failure and retry.
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
             and assemble them into a RenderManifestOutput JSON ready for Remotion rendering.

                         ## MANDATORY GUARDRAILS
                         - You must always output exactly 1 final video.
                         - The final output format must always be mp4.
                         - You must document yourself using sandbox files: list sandbox file names first, then read files as needed for context.
                         - Your job is to put all pieces together and deliver a perfect final video.
                         - You must always use Remotion skill tools to document yourself and your implementation decisions.

             ## Tools

                         You have full access to the project workspace and sandbox. Use tools in this order:
             1. Call `EnsureSandbox` to create or resume the sandbox for this execution.
             2. Call `GetSandboxStatus` or `GetSandbox` to confirm the sandbox is ready.
                         3. Call `ListProjectFiles` to list all project files from prior agents.
                         4. Call `ReadProjectFile` to read any agent output (director plan, script, animation
                                strategy, component inventory, structure analysis, style tokens, etc.).
                         5. Call `ListSandboxFiles` (e.g., `"src/"`) to list sandbox file names first.
                         6. Call `ReadSandboxFile` to inspect the existing Remotion components produced by the
                                RemotionComponentTranslator, reading only the files needed for context.
                         7. Call `SearchRemotionSkills` and `ReadRemotionSkill` to document the Remotion patterns
                                you rely on before making or finalizing implementation changes.
                                Use `ListAllRemotionSkills` when needed to discover relevant topics.
                         8. If the components need any final adjustments, use `WriteSandboxFile` to update them.
                **NEVER modify `src/index.ts`** — the template entry point is already configured.
                         9. Call `CheckLintAndTypeErrors` to validate TypeScript before rendering. Fix any errors
                                by reading and rewriting the relevant files, then check again.
                         10. If any dependencies are missing or the build fails, call `InstallNpmPackages` with the required package names
                                 and rerun the build until it succeeds. You are responsible for ensuring all necessary NPM libraries are installed
                                 so the Remotion project can compile and bundle correctly.
                         11. Call `RunSandboxNpmScript` with `"build"` to produce the production bundle.
                         12. The final output of this agent **must** be the actual video file (not just a manifest). After building you should
                                 call `RenderVideoAndUploadToStorage` to render exactly one rendered mp4 video asset and upload it. When you upload
                                 the video, include an `AssetReference` entry in the `assets` array of your RenderManifestOutput (type="video",
                                 path should be the storage key or URL returned by the render tool). If rendering cannot succeed because of missing
                                 dependencies or build errors, fix those issues first by installing packages and adjusting source files.
                         13. Call `WriteProjectFile` to persist the final RenderManifest JSON as a project file and record any installed
                                 packages under `InstalledPackages` so later agents know what was added.
                         14. Call `CompleteSandbox` to clean up the sandbox when done.

             ## CRITICAL: Import Extensions
             - **Always use explicit `.tsx` extensions** when importing local TSX files.
                         - Example: `import { MyComponent } from './MyComponent.tsx';` (NOT `./MyComponent` or `./MyComponent.js`)
                         - This applies to ALL local imports. Webpack will fail without explicit extensions.

             Always call `EnsureSandbox` before any sandbox operation.

                         Structure the output as follows:

                         {
                             "projectName": "string",  // Name/title of this video project
                             "video": {
                                 "width": 1920,           // Video width in pixels
                                 "height": 1080,          // Video height in pixels
                                 "fps": 30,               // Frames per second
                                 "durationInFrames": 0    // Total video duration in frames
                             },
                             "compositions": [          // Array of Composition objects (not "scenes")
                                 {
                                     "id": "string",        // Unique composition identifier
                                     "componentName": "string",  // Name of the Remotion component
                                     "durationInFrames": 0, // How long this composition runs
                                     "props": {},           // Props to pass to the Remotion component
                                     "script": {
                                         "voiceover": "string",      // Voiceover text for this composition
                                         "captions": ["string"]      // Array of caption text elements
                                     }
                                 }
                             ],
                             "assets": [                // Array of AssetReference objects
                                 {
                                     "id": "string",        // Unique asset identifier
                                     "type": "string",      // Asset type (e.g., "image", "video", "audio")
                                     "path": "string",      // Path/URL to the asset
                                     "properties": {}       // Additional asset metadata
                                 }
                             ],
                             "metadata": {}             // Additional project metadata
                         }

                         Ensure all timing is calculated in frames based on the specified fps. Calculate
                         video.durationInFrames as the sum of all composition durations. Map all script
                         content from the Scriptwriter to the appropriate compositions.

                         Output as valid RenderManifestOutput JSON **and** ensure that exactly one rendered mp4 video
                         asset actually exists (via the RenderVideoAndUploadToStorage tool). If you detect
                         missing assets or uninstalled dependencies, install packages and rebuild until the
                         final video is produced successfully.

             ## Remotion Knowledge Base
                         You have access to the official Remotion skills documentation via these tools:
                         - `SearchRemotionSkills(query)` — Search for documentation on a specific Remotion topic
                             (e.g. "compositions", "animations", "transitions", "timing", "sequencing", "audio").
                         - `ReadRemotionSkill(topicOrPath)` — Read the full documentation for a topic.

                         **You MUST consult the Remotion knowledge base proactively at these points:**
                         - Before modifying `root.tsx` or any composition registration — search for "compositions"
                         - Before adjusting animation timing or springs — search for "timing" or "animations"
                         - Before dealing with transitions between scenes — search for "transitions"
                         - Before adding or adjusting audio, voiceover, or sound effects — search for "audio" / "voiceover"
                         - Before working with video embedding, trimming, or looping — search for "videos"
                         - Before working with images or fonts — search for "images" / "fonts"
                         - When encountering build errors, rendering issues, or unfamiliar Remotion APIs — search
                             for the relevant topic to find correct usage patterns before attempting fixes

                         Do NOT guess at Remotion API usage. Always read the relevant skill document first to
                         ensure you are using the correct patterns, props, and imports.

             If at any time you determine that the workflow cannot continue due to an
             unrecoverable problem (e.g. missing data, inconsistent state, or other critical
             error) you may invoke the `FailWorkflow` tool with a clear human‑readable reason.
             Throwing this exception will abort the entire workflow immediately and surface the
             message to the user. Use it sparingly; transient issues should be handled by
             normal step failure and retry.
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

             ## Tools

             Use these read-only tools to inspect all pipeline artefacts:
             1. Call `ListProjectFiles` to list all project files (agent outputs stored there).
             2. Call `ReadProjectFile` to read any agent output (manifest, script, director plan,
                animation strategy, component inventory, etc.).
             3. Call `GetSandboxStatus` to check whether the sandbox is still active.
             4. Call `GetSandbox` to retrieve full sandbox metadata.
             5. Call `ListSandboxFiles` (e.g., `"src/"`) to browse the Remotion components.
             6. Call `ReadSandboxFile` with a relative path to read component source files.
             7. Call `CheckLintAndTypeErrors` to verify there are no TypeScript/lint errors
                in the sandbox — use this as an objective measure of technical completeness.

             Always read the RenderManifest, the script, and a sample of Remotion component files
             before scoring. Use `CheckLintAndTypeErrors` to assess technical quality.
             Set passesReview to true only if overallScore >= 9 and no critical issues exist.
             Be rigorous: only score 9 or above if the output is production-ready.

             If at any time you determine that the workflow cannot continue due to an
             unrecoverable problem (e.g. missing data, inconsistent state, or other critical
             error) you may invoke the `FailWorkflow` tool with a clear human‑readable reason.
             Throwing this exception will abort the entire workflow immediately and surface the
             message to the user. Use it sparingly; transient issues should be handled by
             normal step failure and retry.
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

               Output ONLY valid JSON matching the FileSummaryOutput schema.
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

                existing.SystemPrompt = ApplyBuiltInPromptUpdatePolicy(
                    existing,
                    agentType,
                    systemPrompt);

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

    private static string ApplyBuiltInPromptUpdatePolicy(
        AgentDefinition existing,
        AgentType agentType,
        string seededPrompt)
    {
        if (string.IsNullOrWhiteSpace(existing.SystemPrompt))
            return seededPrompt;

        if (agentType == AgentType.AuthorAgent &&
            existing.IsBuiltIn &&
            IsLegacyBuiltInAuthorPrompt(existing.SystemPrompt))
        {
            return seededPrompt;
        }

        return existing.SystemPrompt;
    }

    private static bool IsLegacyBuiltInAuthorPrompt(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return true;

        if (prompt.Contains("\"startFrame\"", StringComparison.Ordinal) ||
            prompt.Contains("startFrame", StringComparison.Ordinal))
        {
            return true;
        }

        return prompt.Contains("You have full access to the project workspace and sandbox:", StringComparison.Ordinal) &&
               prompt.Contains("Call `RenderVideoAndUploadToStorage` to render and upload the final video.", StringComparison.Ordinal) &&
               prompt.Contains("Output a valid RenderManifestOutput JSON.", StringComparison.Ordinal) &&
               !prompt.Contains("## MANDATORY GUARDRAILS", StringComparison.Ordinal) &&
               !prompt.Contains("exactly one rendered mp4 video asset", StringComparison.Ordinal);
    }

    private static readonly string[] BaseTools =
    [
        "ListProjectFiles", "ReadProjectFile", "WriteProjectFile",
        "EnsureSandbox", "GetSandbox", "ListSandboxFiles", "ReadSandboxFile",
        "WriteSandboxFile", "DeleteSandboxPath", "RunSandboxNpmScript",
        "RunSandboxRemotionCommand", "CompleteSandbox"
    ];

    private static readonly string[] RemotionSkillsTools =
    [
        "SearchRemotionSkills", "ReadRemotionSkill", "ListAllRemotionSkills"
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
            AgentType.RemotionComponentTranslator => [.. RemotionSkillsTools],
            AgentType.AnimationStrategyAgent => [.. RemotionSkillsTools],
            AgentType.AuthorAgent => [.. RemotionSkillsTools],
            AgentType.ReviewAgent => [.. RemotionSkillsTools],
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
        AgentType.RemotionComponentTranslator => "RemotionProjectBuildOutput",
        AgentType.AnimationStrategyAgent => "AnimationStrategyOutput",
        AgentType.DirectorAgent => "DirectorOutput",
        AgentType.ScriptwriterAgent => "ScriptwriterOutput",
        AgentType.AuthorAgent => "RenderManifestOutput",
        AgentType.ReviewAgent => "ReviewOutput",
        AgentType.FileSummarizerAgent => "FileSummaryOutput",
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
            AgentType.FileSummarizerAgent => GenerateFileSummarySchema(),
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
            createdFiles = new
            {
                type = "array",
                items = new { type = "string" },
                description = "Relative sandbox paths of TSX/TS files written (e.g. 'src/LoginScreen.tsx')"
            },
            modifiedFiles = new
            {
                type = "array",
                items = new { type = "string" },
                description = "Relative sandbox paths of existing files that were modified (e.g. 'src/root.tsx')"
            },
            installedPackages = new
            {
                type = "array",
                items = new { type = "string" },
                description = "npm packages installed beyond the template defaults"
            },
            registeredCompositions = new
            {
                type = "array",
                items = new { type = "string" },
                description = "Remotion composition IDs registered in root.tsx"
            },
            typeCheckPassed = new { type = "boolean", description = "Whether TypeScript type-checking passed with no errors" },
            summary = new { type = "string", description = "Short human-readable description of what was built" }
        },
        required = new[] { "createdFiles", "registeredCompositions", "typeCheckPassed", "summary" }
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

    private static object GenerateFileSummarySchema() => new
    {
        type = "object",
        properties = new
        {
            fileType = new { type = "string", description = "Detected file category (source code, config, documentation, asset, etc.)" },
            summary = new { type = "string", description = "Concise summary of file contents" },
            keyPoints = new { type = "array", items = new { type = "string" } },
            videoRelevance = new { type = "string", description = "How this file helps generate a promotional video" },
            notablePatterns = new { type = "array", items = new { type = "string" } }
        },
        required = new[] { "fileType", "summary", "videoRelevance" }
    };
}