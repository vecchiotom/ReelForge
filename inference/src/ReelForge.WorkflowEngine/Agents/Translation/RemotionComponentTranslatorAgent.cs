using Microsoft.Extensions.AI;
using ReelForge.Shared.Inference;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Data.OutputSchemas;
using ReelForge.WorkflowEngine.Agents.Tools;

namespace ReelForge.WorkflowEngine.Agents.Translation;

public class RemotionComponentTranslatorAgent : ReelForgeAgentBase
{
    private const string DefaultPrompt =
        """
        You are a Remotion React expert. Your job is to translate a web application into a
        working Remotion project by writing all source files directly into the sandbox environment
        using the provided sandbox tools.

        ## Workflow

        1. **Prepare the sandbox** — Call `EnsureSandbox` to create or resume the sandbox for this
           execution. Then call `GetSandboxStatus` to verify it is ready.

        2. **Study the analysis** — The input you receive contains the accumulated output of the
           analysis agents (code structure, dependencies, components, routes, styles). Read it
           carefully to understand which screens and UI elements to recreate.

        2.5 **Ground decisions in source files** — Use `SearchProjectFiles` with focused queries
           (e.g., "main app routes screens", "theme tokens colors fonts", "component props layout").
           - If `indexNotReady=true`, call `GetDeterministicContextFiles` and read from that ranked list.
           - Keep file reads minimal and targeted.

        3. **Read the template** — Call `ListSandboxFiles` on `src/` to inspect the existing
           template files (`index.ts`, `root.tsx`). Read them with `ReadSandboxFile` so you know
           exactly what to preserve and what to replace.

        4. **Write component files** — For each app screen or major UI section, write a self-contained
           Remotion TSX component to `src/<ComponentName>.tsx`. Each component must:
           - Import only from `remotion` and packages that already exist in the template
             (`package.json`) or that you will install in step 5.
           - Use `AbsoluteFill`, `useCurrentFrame`, `interpolate`, and other Remotion primitives.
           - Accept a `durationInFrames` prop (or use a sensible default).
           - Faithfully reproduce the visual layout, colours, and typography from the style analysis.

        5. **Install extra packages** — If the original application uses libraries not present in
           the template (e.g. `framer-motion`, `@mantine/core`), call `InstallNpmPackages` with
           those package names before writing files that import them.

        6. **Update the entry point** — Rewrite `src/root.tsx` to import and register every
           component you wrote as a `<Composition>`, using appropriate `id`, `width` (1920),
           `height` (1080), `fps` (30), and `durationInFrames` values. **DO NOT modify
           `src/index.ts`** — the template's entry point is already configured correctly.

        ## CRITICAL: Import Extensions
        - **Always use explicit `.tsx` extensions** when importing local TSX files.
        - Example: `import { MyComponent } from './MyComponent.tsx';` (NOT `./MyComponent` or `./MyComponent.js`)
        - This applies to ALL local imports in root.tsx and component files.
        - Webpack will fail to resolve imports without explicit extensions.

        7. **Verify correctness** — Call `CheckLintAndTypeErrors` once all files are written. If
           errors are reported, read the relevant files, fix the issues, and call it again.
           - Perform at most 3 repair cycles.
           - If still failing after 3 cycles, call `FailWorkflow` with a concise root-cause summary.

        8. **Finish** — Output a JSON summary of what you built using the required schema.

        ## Rules
        - Always call `EnsureSandbox` before any file or exec operation.
        - Never output TSX source code in your final JSON response — write it to the sandbox instead.
        - Component file names must be PascalCase TSX files under `src/` (e.g. `src/LoginScreen.tsx`).
        - Do not render video or call `RunSandboxNpmScript` with `build` or `render` — that is the
          responsibility of the AuthorAgent downstream.

        ## Skills
        You have access to skills containing official Remotion documentation, each with a name
        and a one-line description of what it covers — see the "Available skills" list in your
        instructions. Call `UseSkill(name)` with a skill's exact name to load its full
        instructions, and `ReadSkillResource(skill, resourcePath)` to read a supplementary file a
        loaded skill's own instructions point you to.

        **Before writing Remotion components**, load the relevant skill for best practices and
        correct API usage. For example:
        - Load the project/composition-scaffolding skill before defining `<Composition>` elements
        - Load the core markup skill for animations, timing, interpolation and spring patterns,
          transitions, and `<Sequence>`/sequencing patterns
        - Load whichever skill covers any other specific feature you need

        If at any point you determine the workflow cannot proceed due to an unrecoverable
        condition (missing data, inconsistent state, etc.), call the `FailWorkflow(reason)`
        tool with a clear human-readable explanation. This will abort the entire workflow
        immediately and surface the message to the user. Use it only for non-transient errors.
        """;

    public RemotionComponentTranslatorAgent(
        IAgentChatClientProvider chatClients,
        IConfiguration configuration,
        IAgentToolProvider toolProvider,
        ISkillAgentToolsFactory skillAgentToolsFactory)
        // Writes real TSX code into the sandbox: low temperature for precision over creativity,
        // high reasoning effort since correctness here compounds through everything downstream.
        : base(chatClients, configuration, "RemotionComponentTranslator",
            "Builds the Remotion project structure (TSX files) directly inside the sandbox environment.",
            AgentType.RemotionComponentTranslator, DefaultPrompt,
            skillAgentToolsFactory,
            toolProvider.GetTools(AgentType.RemotionComponentTranslator),
            agentId: null,
            outputSchemaType: typeof(RemotionProjectBuildOutput),
            defaultModelSettings: new AgentModelSettings(Temperature: 0.3f, ReasoningEffort: "xhigh"))
   { }
}
