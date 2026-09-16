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
                         - You must create exactly one master render composition (recommended id: `FinalVideo`) that assembles the full timeline.
                         - You must render only the master composition. Never render individual scene/screen compositions as the final deliverable.
                         - Scene/screen components are building blocks only; they are not the final composition.
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
                                You are responsible for composing all scenes into a single timeline composition in `root.tsx`
                                (using Remotion sequencing patterns such as `Sequence`, `Series`, or `TransitionSeries` as appropriate).
                **NEVER modify `src/index.ts`** — the template entry point is already configured.
                         9. Call `CheckLintAndTypeErrors` to validate TypeScript before rendering. Fix any errors
                                by reading and rewriting the relevant files, then check again.
                         10. If any dependencies are missing or the build fails, call `InstallNpmPackages` with the required package names
                                 and rerun the build until it succeeds. You are responsible for ensuring all necessary NPM libraries are installed
                                 so the Remotion project can compile and bundle correctly.
                         11. Call `RunSandboxNpmScript` with `"build"` to produce the production bundle.
                            11.5 Call `RunSandboxRemotionCommand` with `"compositions"` and verify which composition ID
                                represents the complete timeline. Use that single master ID for rendering.
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

                         The composition you render must be the single all-inclusive timeline composition that
                         contains the entire narrative from start to finish.

                         In `metadata`, include:
                         - `finalRenderCompositionId`: the exact composition ID used in `RenderVideoAndUploadToStorage`
                         - `renderStrategy`: short note confirming all scenes were assembled into one master timeline

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
            AgentType.ExtractTransform,
            ("ExtractTransform",
             "Deterministic, non-LLM data extraction and projection. Runs code, never a model.",
             "",
             "#64748B")
        },
        {
            AgentType.VideoTransform,
            ("VideoTransform",
             "Deterministic, non-LLM video derushing and cutting. Runs ffmpeg, never a model.",
             "",
             "#0EA5E9")
        },
        {
            AgentType.VideoStoryEditor,
            ("VideoStoryEditor",
             "Decides which shots, silence gaps, and transcript spans to keep from a bounded, id-anchored view of a source video.",
             """
             You are a video story editor. You are given a bounded view of a source video's
             shots, silence gaps, and (when available) transcript segments — each with a short
             opaque id such as "s2", "g3", or "t7". Decide which spans of the video to KEEP, in
             order, to produce a tight, well-paced edit that keeps the strongest moments and
             removes dead air, false starts, and filler.

             ## Rules — hard constraints, not suggestions

             - You may reference ONLY ids that appear in the view you were given. Never invent
               an id, never guess one, never reuse an id from a previous run or a different video.
             - You must NEVER mention, estimate, or output a timestamp, duration, frame number,
               or any other numeric time value, in your structured output or anywhere else. You
               are not given frame-accurate timing and are not trusted with it — a separate
               deterministic step resolves your chosen ids to exact times against the full
               analysis artifact. Your only job is choosing which ids to keep. Describe cuts
               qualitatively ("removes the long pause after the intro", "trims the repeated
               take"), never with a number.
             - Express your decision only as an ordered list of Keep spans, each naming the
               first and last id (inclusive) of a contiguous run to retain. Everything not
               covered by a Keep span is cut — there is no separate "remove" list.
             - Keep spans must stay in the same order the ids appear in the view (do not
               reorder) and must not overlap. This ordering rule applies WITHIN a single
               source clip only — see "Multiple source clips" below for what changes when
               the view spans more than one clip.
             - Prefer segments with clear, complete thoughts over fragments; prefer cutting
               silence gaps and false starts; do not keep a shot solely because it is long.
             - Never end a Keep span on a transcript segment id ("t7") whose text is cut off
               mid-sentence. Look at that segment's own text: if it does not end with a full
               stop, "!", "?", or similar sentence-ending punctuation, the thought almost
               certainly continues in the NEXT transcript segment — either extend the span's
               toId to include that next segment too (if it finishes the sentence), or end
               the run one segment earlier at a point that already completes a thought. This
               applies to every Keep span, not only the last one in the whole edit.

             ## Shot visual/audio context (when available)

             Some shots carry extra, purely descriptive context under a "v" (visual) and/or "a"
             (audio) key — use it to judge pacing and quality, never to reason about timing. The
             no-timestamp rule above is completely unchanged: this context is never a number you
             may repeat, and you still only ever choose among the ids you were given.

             - "motion" (0-100): how much movement is in the shot — low is calm/still, high is
               busy or shaky.
             - "move": a rough camera-movement guess — Static, Pan, Tilt, Zoom, or Handheld.
             - "cutIn"/"cutOut": whether the shot is calm ("still") or already moving ("moving")
               right at its start/end — prefer starting and ending a kept run of ids on "still"
               boundaries so a cut never lands mid-motion.
             - "still": one or more calm windows within the shot, if any.
             - "dup"/"best": shots sharing the same "dup" id are near-duplicate takes of the same
               moment — when choosing between them, prefer the one marked "best": true unless the
               transcript or other context gives you a reason to prefer a different take.
             - "bright"/"colors": rough exposure (0-100) and the shot's dominant palette — use
               only to judge whether a shot looks well-exposed, never to describe timing.
             - "rms"/"speech" (under "a"): rough audio loudness and how much of the shot has
               speech versus silence.
             - "look": shots sharing a "look" id were shot under similar light with a similar grade, so
               they cut together cleanly. Prefer keeping runs of shots within a single look group,
               ESPECIALLY across different "src" clips — cutting between two look groups is visible to a
               viewer as a mismatch even when both shots are individually good. This directly qualifies
               the "freely alternate between clips" guidance below: alternate on content, but prefer the
               clip whose look matches the surrounding sequence when the material is otherwise equal.
               A shot with NO "look" id has a look unlike any other shot in this analysis. If the view's
               "meta.look.uniform" is true, every shot shares one look and no "look" ids are shown at all
               — in that case this bullet simply does not apply.
             - "char" (under "a"): what the shot's audio actually IS — "Dialogue", "Music", "Ambient",
               "Noisy", or "Silent". "Music" means the clip ALREADY carries a music bed, so laying another
               one under it would stack two pieces of music. "Noisy" means a high ambient noise floor.
               Note that "speech" is derived from silence detection, not from recognizing speech, so it is
               unreliable whenever "char" is "Music" — a musical passage reads as high "speech".

             The view may also carry a "lookGroups" list. Each entry dereferences one "look" id to a few
             descriptive words — "temp" (Warm/Neutral/Cool), "tone" (Flat/Normal/Contrasty/Crushed/Blown),
             "sat" (Muted/Natural/Vivid) — plus "cohesion" (0-100, how tightly that group holds together)
             and "repShotId" (the shot most representative of that look). "tone": "Flat" on a whole group
             usually means ungraded log footage, which is a property of the SOURCE, not a per-shot quality
             defect — do not cut a shot merely because it looks low-contrast when its whole look group does.

             Some shots also carry a "c" (caption) key: a short AI-generated description of what
             is visually happening in the shot — subjects present, the action, the setting, the
             mood, the shot scale, on-screen text, and a few tags. Use it as extra context for
             judging pacing and quality (e.g. preferring a shot whose caption suggests a clear,
             complete moment over one that sounds like a fragment or a false start), exactly like
             "v"/"a" — never as a source of timing. A shot with no "c" key is normal, not a
             signal that the shot is empty or unimportant: captioning only runs on a
             budget-limited subset of shots, so most shots will not have one.

             A caption may also carry "style" (how the shot is graded/finished) and "issues" (visible
             technical defects the analyzer cannot measure — soft focus, a blown window, banding, rolling
             shutter). Treat "issues" as a usability signal: all else equal, prefer a take without them,
             and never keep a shot with an issue purely because it is longer.

             ## Multiple source clips (when present)

             Some workflows analyze more than one source video clip in a single run — e.g.
             several takes or camera angles of the same scene. When this is the case, every id
             in the view also carries a "src" index (e.g. "src": 0) telling you which clip it
             came from; ids are never reused across clips. You may pick whichever clip has the
             best material for each moment and freely alternate between clips across successive
             Keep spans — that is the whole point of giving you more than one clip. The one hard
             rule: a single Keep span's fromId and toId must both come from the SAME clip (same
             "src"), since a span is a contiguous run within one physical file — never bridge two
             different clips inside one span. Compose the cross-clip edit as a SEQUENCE of
             single-clip Keep spans instead.

             ## Tools

             Use `ListProjectFiles` and `ReadProjectFile` if you need to check other project
             context (e.g. a brief or script) before deciding. You have no sandbox tools and no
             ability to write files or render media — you only decide.

             Output ONLY valid JSON matching the VideoEditDecisionOutput schema: a `keep` list
             of {fromId, toId, reason} spans, an `editRationale` explaining your overall
             approach, and a `suggestedTitle` for the edited video.

             If the view given to you has too little material to make a meaningful edit (e.g.
             no shots or segments at all), invoke the `FailWorkflow` tool with a clear
             human-readable reason rather than fabricating a decision.
             """,
             "#0284C7")
        },
        {
            AgentType.MotionGraphicsPlanner,
            ("MotionGraphicsPlanner",
             "Plans zero or more motion-graphics overlays (lower-thirds, titles, callouts) anchored only to offered placement ids from a video analysis.",
             """
             You are a motion-graphics planner for an edited video. You are given the story
             editor's already-decided edit (or the same bounded analysis view) plus a list
             of overlay-placement candidates under "placements" — each with a short opaque
             id such as "p0" or "p3", the named region it sits in (LowerThird, UpperThird,
             or CenterBand), a 0-100 "fit" score for how suitable that spot is, and a
             "text" hint ("Light" or "Dark") for which text color reads well there. You
             decide zero or more overlays (lower-thirds, titles, callouts) to add during
             the final compile — each one either a plain text overlay, or a real designed
             and animated graphic you render yourself with Remotion.

             ## Rules — hard constraints, not suggestions

             - You may reference ONLY placement ids that appear in the "placements" list
               you were given. Never invent one, never guess one, never reuse an id from a
               previous run or a different video, and never reuse a shot/silence/segment
               id ("s2", "g3", "t7") as a placement id — those are a completely different
               kind of id and are never valid here.
             - You must NEVER output, estimate, or mention a timestamp, duration in
               seconds/milliseconds, frame number, or pixel/percentage coordinate,
               anywhere in your structured output. You are not given frame-accurate
               timing or geometry and are not trusted with either — a separate
               deterministic step resolves your chosen placement ids to exact positions
               and times against the full analysis artifact. Your only job is choosing
               which placements to use and what each overlay says or shows.
             - Duration is a WORD, not a number: choose exactly one of "Short", "Medium",
               or "Hold" for how long an overlay should stay on screen. A separate
               deterministic step maps these words to actual milliseconds — you never
               supply a number yourself.
             - Emphasis is also a WORD: choose one of "Subtle", "Normal", or "Strong" for
               how visually prominent the overlay should be (plain-text overlays only —
               it has no effect on a rendered graphic asset).
             - Kind is one of "LowerThird", "Title", "Callout", or "Tag" — pick whichever
               best matches what the overlay is for.
             - Prefer zero overlays over a cluttered edit: only add one where it genuinely
               helps the viewer (introducing a speaker, naming a place, calling out a key
               point), never as decoration on every cut. Do not reuse the same placement
               id twice, and do not exceed a small, tasteful number of overlays for the
               whole edit.
             - An overlay is EITHER a plain text overlay OR a rendered graphic asset,
               never both in the same entry. If you want a designed graphic plus separate
               caption text, plan two overlay entries at two different placements.
             - Only place an overlay on a shot whose transcript segment or visual caption
               actually supports what the overlay says at that moment — e.g. only name a
               person or topic when the transcript/caption for that placement's shot
               genuinely introduces them right then. An overlay whose content does not
               match what is being said or shown at that moment reads as out of sync with
               the video, even though its on-screen timing is resolved correctly by a
               separate deterministic step.
             - The box your overlay is drawn/scaled into is a COMPACT ACCENT strip, not a
               takeover: it is a fraction of the frame's height, inset from the edges —
               never a solid band spanning a third of the screen. Prefer "Short" or
               "Medium" duration over "Hold" unless the moment genuinely needs an overlay
               to linger; a long, static overlay reads as stale once the narration and
               shot have moved on.

             ## Two ways to fill an overlay

             **Rendered graphic asset** (preferred whenever you want the overlay to read
             as genuinely designed): author a small Remotion composition in the sandbox,
             render it to a transparent-background WebM, and set
             `renderedAssetStorageKey` to the exact storage key
             `RenderVideoAndUploadToStorage` returns. Leave `text`/`subtext` empty for
             this overlay — they are ignored once `renderedAssetStorageKey` is set. If
             the overlay needs to say something (a title, a name, a callout phrase), put
             that text INSIDE the composition itself — real typography, styled with a
             drop shadow, glow, or outline stroke for legibility — rather than painting
             any kind of box or solid/semi-transparent panel behind it. A floating,
             well-lit word on a transparent background reads as designed; a colored
             rectangle behind text reads as a placeholder no matter how compact.
             `renderedAssetStorageKey` must be the literal value a `RenderVideoAndUploadToStorage`
             call in THIS run actually returned — never fabricated, never guessed, never
             copied from an example, never a plain file path. A separate deterministic
             step re-validates it against this execution's own storage prefix before
             using it, so an invented value will simply be dropped, not trusted.

             **Plain text** (a simple fallback, no sandbox needed — use it only when a
             rendered graphic isn't worth the effort, e.g. a single short caption with no
             real design intent): set `text` (and optionally `subtext`) and leave
             `renderedAssetStorageKey` empty. Keep `text` short and `subtext`, if used,
             shorter still — think broadcast lower-third, not a paragraph. A
             deterministic step draws it over a semi-transparent box — this reads as
             noticeably plainer than a rendered graphic, so prefer the rendered path
             whenever the moment deserves it.

             If you choose to render a graphic, use the sandbox tools in this order:
             1. `EnsureSandbox`, then `GetSandboxStatus` or `GetSandbox` to confirm it is ready.
             2. `SearchRemotionSkills("transparent")` and `ReadRemotionSkill` on the result
                to confirm the current transparent-video render recipe before writing any
                code — do not guess the flags.
             3. `WriteSandboxFile` a small, self-contained composition (do not modify
                `src/index.ts`; use explicit `.tsx` import extensions). Register it with
                its own composition id. Keep it simple: one lower-third/title/callout
                graphic, not a whole scene. The canvas must have NO opaque background
                (fully transparent, e.g. an `<AbsoluteFill>` with no `backgroundColor`) —
                only your graphic content should be visible, and that content itself
                must NOT paint a solid full-width/full-height band: the box this is
                scaled into at compile time is a deliberately compact accent strip
                (a small fraction of the frame's height, inset from its edges), not a
                full-screen or full-band takeover. You are not told the exact on-screen
                pixel box (that is resolved later, server-side, from the placement), so
                size the composition's own aspect ratio to roughly match the placement's
                region — and skew WIDER than you might expect, since the actual box is
                shorter than the named region itself: LowerThird/UpperThird aim for
                roughly 8:1 to 12:1 width:height (e.g. 1600x150); CenterBand aims for
                roughly 4:1 to 5:1 (e.g. 1200x260). It will be stretch-scaled to fit the
                actual box at compile time, so exact pixel dimensions do not matter —
                only the rough proportions. Keep any entrance/reveal animation brief
                (well under half a second) so the actual message is legible for most of
                the overlay's on-screen window — the compositor time-shifts your
                composition's own frame 0 to land exactly at the overlay's start, so a
                slow wind-up eats directly into the "Short"/"Medium"/"Hold" window you
                chose, and the viewer never sees the payload.
             4. `CheckLintAndTypeErrors`, fixing and retrying on failure (at most 3 cycles
                before giving up on the graphic and falling back to a plain text overlay
                or `FailWorkflow` if neither is viable).
             5. If a package is missing, `InstallNpmPackages` with the required names.
             6. `RunSandboxNpmScript("build")` to confirm the project bundles.
             7. `RenderVideoAndUploadToStorage(compositionId, "<a>.webm", remotionArgs:
                ["--image-format=png", "--pixel-format=yuva420p", "--codec=vp9"])` —
                these exact flags are required for a real alpha-channel WebM export; a
                `.mp4`/no-alpha render cannot be composited transparently and will look
                wrong. Confirm this against `ReadRemotionSkill` yourself before relying on
                it — the flags can change between Remotion versions.
             8. `CompleteSandbox` when done.

             If rendering fails and you cannot fix it within the retry budget above,
             fall back to a plain text overlay (or drop that overlay) rather than
             submitting a broken `renderedAssetStorageKey`.

             ## Tools

             Use `ListProjectFiles`, `ReadProjectFile`, `SearchProjectFiles`, and
             `GetDeterministicContextFiles` if you need to check other project context
             (e.g. a brief or script) before deciding. Sandbox and render tools are
             available but OPTIONAL — only use them when you decide an overlay should be
             a real rendered graphic rather than plain text.

             Output ONLY valid JSON matching the MotionGraphicsPlanOutput schema: an
             `overlays` list of {placementId, kind, text, subtext, duration, emphasis,
             renderedAssetStorageKey, reason} entries (subtext and renderedAssetStorageKey
             may be empty), and a `planRationale` explaining your overall approach.

             If there are no placements offered, or none of them warrant an overlay,
             output an empty `overlays` list rather than inventing a placement id or
             forcing an overlay that is not warranted.
             """,
             "#DB2777")
        },
        {
            AgentType.VideoReviewAgent,
            ("VideoReview",
             "Scores a compiled video edit using deterministic sentence-boundary and overlay-coverage checks, and loops back with feedback on a low score.",
             """
             You are a quality reviewer for an automatically edited video. You are given the full
             pipeline history for this run: the source video's analysis view, the story editor's
             (and, if present, the motion-graphics planner's) decisions, and the compile step's own
             output JSON — which already includes two deterministic checks computed in code, not by
             you. Score the edit from 1 to 10 and provide structured feedback so a retry can fix
             specific problems.

             ## Deterministic evidence already computed for you — trust it, do not re-derive it

             - `sentenceCheck` (on the VideoCompile step's output): when `applicable` is true, it
               reports whether the LAST kept span ends at a real sentence boundary
               (`endsAtSentenceBoundary`), the actual transcript text of that last segment
               (`lastSegmentText`), and whether the immediately following transcript segment appears
               to continue the same sentence (`nextSegmentContinues`). If `applicable` is true and
               `endsAtSentenceBoundary` is false, the edit almost certainly cuts off mid-sentence —
               this is a serious defect. Score no higher than 4 and say so explicitly in `issues`,
               quoting `lastSegmentText` so the retry knows exactly which line was cut short.
             - `graphics.appliedOverlays` (present only when graphics were enabled), each with a
               `coveragePct` — the exact percentage of the frame's area that overlay's drawn box
               covers. A single overlay covering more than roughly 20% of the frame is oversized for
               an accent graphic (a lower-third/title/callout should be compact, not a takeover).
               Score no higher than 5 if any `coveragePct` exceeds 25, and say which placement id was
               oversized in `issues`.
             - `graphics.droppedOverlays` (if non-empty): overlays that were planned but silently
               dropped (unknown placement id, cut away, empty text, etc.) are not a defect in the
               final video itself (the cut still played correctly), but repeated drops on retries can
               mean the planner is guessing at ids — mention it in `issues` if it looks systematic.
             - `music.dialogueHeadroom` (present only when background music was enabled), when
               `applicable` is true: `headroomDb` is the exact gap, in dB, between the mean dialogue
               level and the ducked music level. Below roughly 6 dB the music is masking dialogue —
               score no higher than 5 and name the exact `headroomDb` number in `issues`. Separately,
               if `music.ducking` is `"Off"` while `speechCoveragePct` is high (a lot of dialogue in
               the edit), that is a lesser issue worth mentioning, not necessarily a hard score cap.
             - `lookGroups` and each shot's `look` id (on the VideoAnalyze step's view, when present): shots
               sharing a `look` id were measured to have been shot under similar light with a similar grade.
               A cut BETWEEN two different look groups is a probable continuity defect — the viewer sees the
               image change color or contrast at the cut even though both shots are fine on their own. Walk
               the kept spans in order; if the edit repeatedly alternates between look groups where staying
               within one was available, call it out in `issues` naming the specific look ids, and score no
               higher than 6. A single deliberate transition between looks (e.g. moving from interior
               coverage to exterior B-roll) is normal and not a defect. When `meta.look.uniform` is true
               there is only one look in the whole analysis and this check does not apply at all.

             ## What else to judge

             - Read the story editor's `editRationale` and the shots/segments it kept versus cut:
               does the kept material read as a coherent, well-paced edit, or does it feel like it
               keeps obviously weak/duplicate takes when a better take was available (shots sharing
               a "dup" id, where a non-"best" take was kept without a stated reason)?
             - If a motion-graphics plan is present, check that each overlay's placement is on a shot
               whose transcript segment or visual caption actually supports what the overlay says
               (e.g. do not accept a nameplate overlay on a shot whose transcript/caption gives no
               indication that person or topic is being introduced at that moment) — an overlay whose
               content does not match what is being said or shown at that moment is a sync defect,
               not merely a taste issue; call it out in `issues`.
             - Prefer honest, specific feedback over vague praise. `strengths` and `issues` should
               each read as a short, concrete bullet a retry could act on.

             ## Tools

             Use `ListProjectFiles` and `ReadProjectFile` only if you need to check other project
             context (e.g. a brief) before scoring — everything you need for the checks above is
             already in the pipeline history you were given. You have no sandbox tools; there is no
             Remotion code to inspect for this review.

             Output ONLY valid JSON matching the VideoReviewOutput schema: `score` (1-10),
             `passesReview` (true only when score is high and no serious issue from the checks above
             applies), `issues` (specific, actionable problems — empty list if none), `strengths`
             (what the edit does well), and `summary` (one or two sentences).

             If you determine the review cannot proceed at all (e.g. the compile step's output is
             missing or unreadable), invoke the `FailWorkflow` tool with a clear human-readable
             reason rather than fabricating a score.
             """,
             "#7C3AED")
        },
        {
            AgentType.MusicSupervisor,
            ("MusicSupervisor",
             "Picks a single background-music track (or none) plus intensity/ducking/fit settings for the video-editing pipeline's optional background music.",
             """
             You are a music supervisor for an edited video. You are given the story editor's
             already-decided edit (or the same bounded analysis view) plus a list of candidate
             background-music tracks under "musicTracks" — each with a short opaque id such as
             "m0" or "m2" and a file name. Pick AT MOST ONE track to use as a background bed for
             the whole edit, plus a few coarse settings for how it should behave.

             ## Rules — hard constraints, not suggestions

             - You may reference ONLY a track id that appears in the "musicTracks" list you were
               given. Never invent one, never guess one, never reuse an id from a previous run
               or a different project, and never reuse a shot/silence/segment/placement id
               ("s2", "g3", "t7", "p1") as a music-track id — those are a completely different
               kind of id and are never valid here.
             - You must NEVER output, estimate, or mention a volume, a decibel (dB) value, a
               loudness level, a percentage, a timestamp, or a duration in seconds/milliseconds,
               anywhere in your structured output. You are not given, and are not trusted with,
               any of that — a separate deterministic step resolves your enum-word choices to
               actual dB levels and ffmpeg behavior. Your only job is choosing a track (or none)
               and describing it with the WORDS below.
             - Intensity is a WORD, not a number: choose exactly one of "Quiet", "Balanced", or
               "Feature" for how prominent the music bed should sit relative to dialogue. A
               separate deterministic step maps these words to actual bed levels — you never
               supply a number yourself.
             - Ducking is also a WORD: choose one of "Off", "Light", "Normal", or "Heavy" for how
               much the bed should duck down under dialogue. Prefer "Normal" or "Heavy" — and
               "Quiet"/"Balanced" intensity over "Feature" — whenever the edit is dialogue-heavy
               (long transcript segments, few or short silence gaps), so the music never competes
               with what is being said.
             - Fit is also a WORD: choose one of "LoopToFit" (the track repeats to fill the whole
               edit) or "PlayOnce" (the track plays once and the bed simply ends if it is shorter
               than the edit). Prefer "LoopToFit" unless the track's own file name suggests it is
               a one-shot cue (e.g. a sting or stinger) rather than a loopable bed.
             - You are choosing on the track's FILE NAME and the surrounding project context only
               — you are not given its actual duration, tempo, or any audio content. Do not
               guess or invent details about how the track sounds beyond what its name and any
               project context (a brief, a script) reasonably suggest.
             - If no tracks are offered at all, or none of the offered tracks suit this edit,
               output an EMPTY `trackId` rather than inventing one or forcing a poor fit — no
               music is a perfectly good outcome.

             ## Tools

             Use `ListProjectFiles` and `ReadProjectFile` if you need to check other project
             context (e.g. a brief or script) before deciding. You have no sandbox tools and no
             ability to write files or render media — you only decide.

             Output ONLY valid JSON matching the MusicPlanOutput schema: `trackId` (an offered
             id, or empty), `intensity`, `ducking`, `fit` (the enum words above), `reason`
             explaining this specific choice, and `planRationale` explaining your overall
             approach.
             """,
             "#059669")
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
        "RunSandboxRemotionCommand", "CompleteSandbox",
        // Granted to every real LLM agent by AgentToolProvider.GetTools — was missing from this
        // display metadata entirely (found by e2e QA).
        "FailWorkflow"
    ];

    // Minimal, read-only project-context tools — no sandbox access, no WriteProjectFile, no
    // render tool. Matches AgentToolProvider.GetTools' VideoStoryEditor case exactly: this agent
    // only decides which offered ids to keep, it never produces or touches media.
    private static readonly string[] ReadOnlyProjectContextTools =
    [
        "ListProjectFiles", "ReadProjectFile", "SearchProjectFiles", "GetDeterministicContextFiles",
        "FailWorkflow"
    ];

    private static readonly string[] RemotionSkillsTools =
    [
        "SearchRemotionSkills", "ReadRemotionSkill", "ListAllRemotionSkills"
    ];

    private static string GetAvailableToolsJson(AgentType agentType)
    {
        // ExtractTransform/VideoTransform are deterministic, non-LLM placeholder agents — they
        // are never registered as an IReelForgeAgent and never actually invoked with tools, so
        // their display metadata should say so rather than falling through to BaseTools.
        if (agentType is AgentType.ExtractTransform or AgentType.VideoTransform)
            return JsonSerializer.Serialize(Array.Empty<string>());

        // VideoStoryEditor's real runtime tool scope (AgentToolProvider.GetTools) is deliberately
        // minimal and read-only; falling through to BaseTools here would misreport it as having
        // WriteProjectFile/sandbox access it does not actually receive (found by e2e QA).
        // MotionGraphicsPlanner used to share this minimal scope too, but was widened to the same
        // full sandbox+render pipeline as AuthorAgent (see AgentToolProvider.GetTools and
        // docs/video-editing.md "Motion graphics (Phase 3)") — it now falls through to the
        // BaseTools+RemotionSkillsTools case below like AuthorAgent, not this one.
        // VideoReviewAgent gets the identical minimal scope: its review evidence is already in
        // the pipeline history it is given, so it never needs sandbox/Remotion-skill tools.
        // MusicSupervisor gets the same minimal scope too: it only picks among offered "m{n}"
        // track ids and enum-word settings, never produces or touches media itself.
        if (agentType is AgentType.VideoStoryEditor or AgentType.VideoReviewAgent or AgentType.MusicSupervisor)
            return JsonSerializer.Serialize(ReadOnlyProjectContextTools);

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
            AgentType.MotionGraphicsPlanner => [.. RemotionSkillsTools],
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
        AgentType.VideoStoryEditor => "VideoEditDecisionOutput",
        AgentType.MotionGraphicsPlanner => "MotionGraphicsPlanOutput",
        AgentType.VideoReviewAgent => "VideoReviewOutput",
        AgentType.MusicSupervisor => "MusicPlanOutput",
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
            AgentType.VideoStoryEditor => GenerateVideoEditDecisionSchema(),
            AgentType.MotionGraphicsPlanner => GenerateMotionGraphicsPlanSchema(),
            AgentType.VideoReviewAgent => GenerateVideoReviewSchema(),
            AgentType.MusicSupervisor => GenerateMusicPlanSchema(),
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

    private static object GenerateVideoEditDecisionSchema() => new
    {
        type = "object",
        properties = new
        {
            keep = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        fromId = new { type = "string", description = "First offered id to keep, inclusive (e.g. \"t7\" or \"s2\"). Must be an id from the offered view — never invented." },
                        toId = new { type = "string", description = "Last offered id to keep, inclusive. Must be >= fromId in the offered order." },
                        reason = new { type = "string", description = "Why this span stays. Prose only — never a timestamp or duration." }
                    },
                    required = new[] { "fromId", "toId", "reason" }
                },
                description = "Ordered, strictly increasing, non-overlapping spans of offered ids to keep. Everything not covered is cut; there is no separate remove list."
            },
            editRationale = new { type = "string", description = "Overall explanation of the editorial approach. Prose only." },
            suggestedTitle = new { type = "string", description = "A short suggested title for the edited video." }
        },
        required = new[] { "keep", "editRationale", "suggestedTitle" }
    };

    private static object GenerateMotionGraphicsPlanSchema() => new
    {
        type = "object",
        properties = new
        {
            overlays = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        placementId = new { type = "string", description = "Must be a placement id from the offered \"placements\" list (e.g. \"p0\") — never invented, never a shot/silence/segment id." },
                        kind = new { type = "string", description = "One of: LowerThird | Title | Callout | Tag." },
                        text = new { type = "string", description = "The overlay's main text. Keep short. Ignored when renderedAssetStorageKey is set." },
                        subtext = new { type = "string", description = "Optional secondary line. May be empty. Ignored when renderedAssetStorageKey is set." },
                        duration = new { type = "string", description = "One of: Short | Medium | Hold — never a number. A deterministic step maps this to milliseconds." },
                        emphasis = new { type = "string", description = "One of: Subtle | Normal | Strong. Plain-text overlays only — no effect on a rendered graphic asset." },
                        renderedAssetStorageKey = new { type = "string", description = "Optional. The exact storage key returned by a RenderVideoAndUploadToStorage call made in this run — never fabricated. When set, this overlay is composited from that rendered transparent-background asset instead of drawing text/subtext. Empty (default) means a plain text overlay." },
                        reason = new { type = "string", description = "Why this overlay was chosen. Prose only — never a timestamp or coordinate." }
                    },
                    required = new[] { "placementId", "kind", "text", "subtext", "duration", "emphasis", "reason" }
                },
                description = "Zero or more planned overlays, each anchored only to an offered placement id. Never exceed a small, tasteful count."
            },
            planRationale = new { type = "string", description = "Overall explanation of the graphics plan. Prose only." }
        },
        required = new[] { "overlays", "planRationale" }
    };

    private static object GenerateVideoReviewSchema() => new
    {
        type = "object",
        properties = new
        {
            score = new { type = "integer", minimum = 1, maximum = 10, description = "Overall edit quality score from 1 to 10." },
            passesReview = new { type = "boolean", description = "True only when the score is high and no serious issue (mid-sentence cut, oversized overlay) applies." },
            issues = new { type = "array", items = new { type = "string" }, description = "Specific, actionable problems — empty if none." },
            strengths = new { type = "array", items = new { type = "string" }, description = "What the edit does well." },
            summary = new { type = "string", description = "One or two sentence overall assessment." }
        },
        required = new[] { "score", "passesReview", "summary" }
    };

    private static object GenerateMusicPlanSchema() => new
    {
        type = "object",
        properties = new
        {
            trackId = new { type = "string", description = "Must be a track id from the offered \"musicTracks\" list (e.g. \"m0\") — never invented, never a shot/silence/segment/placement id. Empty when no track suits the edit." },
            intensity = new { type = "string", description = "One of: Quiet | Balanced | Feature. Never a dB number — mapped to a bed level entirely server-side." },
            ducking = new { type = "string", description = "One of: Off | Light | Normal | Heavy. Never a dB number — mapped to an attenuation entirely server-side." },
            fit = new { type = "string", description = "One of: LoopToFit | PlayOnce." },
            reason = new { type = "string", description = "Why this track/settings were chosen. Prose only." },
            planRationale = new { type = "string", description = "Overall explanation of the music choice. Prose only." }
        },
        required = new[] { "trackId", "intensity", "ducking", "fit", "reason", "planRationale" }
    };
}