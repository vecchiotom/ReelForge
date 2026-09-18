using ReelForge.Shared.Data.Models;

namespace ReelForge.Shared.Agents;

/// <summary>
/// Single source of truth for agent tool scoping: which <see cref="ToolGroup"/>s a function name
/// belongs to, and which groups a built-in <see cref="AgentType"/> is granted.
///
/// This class only knows NAMES and GROUPING — it has no dependency on the tool implementations
/// (which live in the WorkflowEngine, where the actual <c>AIFunctionFactory.Create(...)</c> calls
/// happen against injected tool-provider instances). Two very different consumers read from this
/// one place:
/// - <c>AgentToolProvider.GetTools</c> (WorkflowEngine) — the real, load-bearing decision that
///   constructs the runnable <c>AIFunction[]</c> an agent actually executes with.
/// - <c>DatabaseSeeder.GetAvailableToolsJson</c> (Inference API) — display-only metadata shown in
///   the UI's "Available Tools" card on an agent's detail page.
///
/// These two consumers hand-maintained separate mappings before this class existed, and that
/// duplication drifted twice in practice (see the git history / prior comments this class's
/// content was moved from): once <c>FailWorkflow</c> was silently missing from the display
/// metadata entirely, and once <c>MotionGraphicsPlanner</c> was widened in the real tool provider
/// without the corresponding display-metadata update. Both call sites must now derive their
/// answer from <see cref="GroupsFor"/> + <see cref="FunctionNamesFor"/> instead of maintaining
/// their own list, so the two can never again disagree about what a given <see cref="AgentType"/>
/// is granted.
/// </summary>
public static class ToolGroupCatalog
{
    /// <summary>
    /// The tool/function names belonging to a <see cref="ToolGroup"/>. Names must match the
    /// actual C# method names on the WorkflowEngine tool-provider classes
    /// (<c>ProjectFileAgentTools</c>, <c>ReactRemotionSandboxTools</c>,
    /// <c>WorkflowControlAgentTools</c>) exactly, since
    /// <c>AIFunctionFactory.Create</c> names each constructed <c>AIFunction</c> after the method
    /// it wraps.
    /// </summary>
    public static IReadOnlyList<string> FunctionNamesFor(ToolGroup group) => group switch
    {
        ToolGroup.ProjectRead =>
        [
            "ListProjectFiles", "ReadProjectFile", "SearchProjectFiles", "GetDeterministicContextFiles"
        ],
        ToolGroup.ProjectWrite =>
        [
            "WriteProjectFile"
        ],
        ToolGroup.SandboxBrowse =>
        [
            "GetSandboxStatus", "ListSandboxFiles", "ReadSandboxFile"
        ],
        ToolGroup.SandboxMetadata =>
        [
            "GetSandbox"
        ],
        ToolGroup.SandboxLint =>
        [
            "CheckLintAndTypeErrors"
        ],
        ToolGroup.SandboxAuthoring =>
        [
            "EnsureSandbox", "WriteSandboxFile", "DeleteSandboxPath", "InstallNpmPackages", "RunSandboxNpmScript"
        ],
        ToolGroup.SandboxRender =>
        [
            "RunSandboxRemotionCommand", "RenderVideoAndUploadToStorage", "CompleteSandbox"
        ],
        ToolGroup.WorkflowControl =>
        [
            "FailWorkflow"
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(group), group, "Unknown ToolGroup.")
    };

    /// <summary>
    /// The scoping decision itself — which <see cref="ToolGroup"/>s a built-in
    /// <see cref="AgentType"/> is granted. This is the carefully-reasoned security scoping that
    /// used to live as comments directly in <c>AgentToolProvider.GetTools</c>'s switch
    /// expression (WorkflowEngine) — moved here verbatim, not summarized, since it is the actual
    /// rationale for each agent's access level and both consumers need to keep seeing it.
    /// </summary>
    public static IReadOnlyList<ToolGroup> GroupsFor(AgentType agentType) => agentType switch
    {
        // ──────────────────────────────────────────────────────────────────
        // ExtractTransform / VideoTransform: deterministic, non-LLM placeholder agents — never
        // registered as an IReelForgeAgent and never actually invoked with tools
        // (ExtractStepExecutor / VideoAnalyzeStepExecutor / VideoCompileStepExecutor run
        // deterministic C# code directly, with no IChatClient/IAgentRegistry dependency).
        // Explicitly empty rather than falling through to the default read-only arm below, so
        // both AgentToolProvider (runtime) and DatabaseSeeder (display metadata) agree there is
        // nothing to grant for an AgentType that is never actually run as an agent.
        // ──────────────────────────────────────────────────────────────────

        AgentType.ExtractTransform or AgentType.VideoTransform => [],

        // ──────────────────────────────────────────────────────────────────
        // Analysis agents: project read-only access only.
        // No sandbox access — these agents only inspect source files.
        // ──────────────────────────────────────────────────────────────────

        AgentType.CodeStructureAnalyzer or
        AgentType.DependencyAnalyzer or
        AgentType.ComponentInventoryAnalyzer or
        AgentType.RouteAndApiAnalyzer or
        AgentType.StyleAndThemeExtractor =>
        [
            ToolGroup.ProjectRead,
            ToolGroup.WorkflowControl
        ],

        // ──────────────────────────────────────────────────────────────────
        // Translation agents: project read/write + sandbox code authoring.
        // Can install packages and verify correctness, but do NOT render.
        //
        // Remotion knowledge-base access (UseSkill/ReadSkillResource) is NOT a ToolGroup — it is
        // resolved per agent-run from SkillCatalog.DefaultsFor(AgentType) instead, since it is a
        // named, on-demand-loadable skill rather than a fixed tool surface. See
        // ReelForge.Shared/Skills/SkillCatalog.cs for which of these agents get which skills.
        // ──────────────────────────────────────────────────────────────────

        AgentType.RemotionComponentTranslator =>
        [
            ToolGroup.ProjectRead,
            ToolGroup.ProjectWrite,
            ToolGroup.SandboxBrowse,
            ToolGroup.SandboxMetadata,
            ToolGroup.SandboxLint,
            ToolGroup.SandboxAuthoring,
            ToolGroup.WorkflowControl
        ],

        AgentType.AnimationStrategyAgent =>
        [
            ToolGroup.ProjectRead,
            ToolGroup.SandboxBrowse,
            ToolGroup.SandboxMetadata,
            ToolGroup.WorkflowControl
        ],

        // ──────────────────────────────────────────────────────────────────
        // Production planning agents: project + sandbox read-only.
        // Director and Scriptwriter produce creative artefacts from data
        // already in the project/sandbox; they never write or render.
        //
        // Deliberately no SandboxMetadata (GetSandbox) here — unlike every other sandbox-touching
        // agent type in this catalog, Director/Scriptwriter only ever need to browse sandbox
        // files, not read full sandbox metadata.
        // ──────────────────────────────────────────────────────────────────

        AgentType.DirectorAgent or AgentType.ScriptwriterAgent =>
        [
            ToolGroup.ProjectRead,
            ToolGroup.SandboxBrowse,
            ToolGroup.WorkflowControl
        ],

        // ──────────────────────────────────────────────────────────────────
        // Author: full sandbox pipeline including render and upload.
        // This is the only agent producing the pipeline's FINAL deliverable video; the only
        // other agent granted SandboxRender (RenderVideoAndUploadToStorage) is
        // MotionGraphicsPlanner below, which uses it for a small, optional overlay ASSET (never
        // the final video) as part of the separate video-editing pipeline (see
        // docs/video-editing.md "Motion graphics (Phase 3)").
        // ──────────────────────────────────────────────────────────────────

        AgentType.AuthorAgent =>
        [
            ToolGroup.ProjectRead,
            ToolGroup.ProjectWrite,
            ToolGroup.SandboxBrowse,
            ToolGroup.SandboxMetadata,
            ToolGroup.SandboxLint,
            ToolGroup.SandboxAuthoring,
            ToolGroup.SandboxRender,
            ToolGroup.WorkflowControl
        ],

        // ──────────────────────────────────────────────────────────────────
        // Review agent: read-only access + lint/type checking.
        // Inspects existing artefacts and surfaces quality issues.
        // ──────────────────────────────────────────────────────────────────

        AgentType.ReviewAgent =>
        [
            ToolGroup.ProjectRead,
            ToolGroup.SandboxBrowse,
            ToolGroup.SandboxMetadata,
            ToolGroup.SandboxLint,
            ToolGroup.WorkflowControl
        ],

        // ──────────────────────────────────────────────────────────────────
        // VideoStoryEditor: read-only project context + FailWorkflow only. It decides
        // which offered ids to keep; it never produces or touches media directly.
        // Explicitly NO sandbox tools, no ProjectWrite, no render tool — unlike the
        // default/unknown case below, this is spelled out on purpose so a future widening
        // of the default case does not silently hand this agent write/render access.
        // ──────────────────────────────────────────────────────────────────

        AgentType.VideoStoryEditor =>
        [
            ToolGroup.ProjectRead,
            ToolGroup.WorkflowControl
        ],

        // ──────────────────────────────────────────────────────────────────
        // VideoEditDirector: read-only project context + FailWorkflow only, identical scope to
        // VideoStoryEditor above. Used both as a StepType.EditRoom group-chat participant (via a
        // raw AIAgent the executor builds directly from these same tools) and for the standalone
        // structured-output synthesis call — neither role produces or touches media.
        // ──────────────────────────────────────────────────────────────────

        AgentType.VideoEditDirector =>
        [
            ToolGroup.ProjectRead,
            ToolGroup.WorkflowControl
        ],

        // ──────────────────────────────────────────────────────────────────
        // MusicSupervisor: read-only project context + FailWorkflow only, identical scope to
        // VideoStoryEditor/VideoReviewAgent above. It only picks among offered "m{n}" track
        // ids and enum-word settings — it never produces or touches media directly (no render,
        // no sandbox — unlike MotionGraphicsPlanner, there is no rendered-asset escape hatch
        // here). Explicitly spelled out rather than left to the default arm below, same
        // reasoning as VideoStoryEditor's own comment.
        // ──────────────────────────────────────────────────────────────────

        AgentType.MusicSupervisor =>
        [
            ToolGroup.ProjectRead,
            ToolGroup.WorkflowControl
        ],

        // ──────────────────────────────────────────────────────────────────
        // VideoReviewAgent: read-only project context + FailWorkflow only, identical scope
        // to VideoStoryEditor above. Its review evidence (sentenceCheck / overlay coverage)
        // is already present in the pipeline history it is given as input — it never needs
        // sandbox/Remotion-skill tools since there is no code to inspect for a video edit.
        // ──────────────────────────────────────────────────────────────────

        AgentType.VideoReviewAgent =>
        [
            ToolGroup.ProjectRead,
            ToolGroup.WorkflowControl
        ],

        // ──────────────────────────────────────────────────────────────────
        // MotionGraphicsPlanner (Phase 3): the same full sandbox+Remotion+render pipeline as
        // AuthorAgent immediately above it, minus ProjectWrite (WriteProjectFile) — this agent
        // renders a small transparent-background overlay asset (a lower-third, title card,
        // callout), never a whole project artifact, so there is nothing for it to persist as a
        // project file. It still decides overlay placement/content anchored only to offered
        // placement ids (never a timestamp or pixel coordinate — see MotionGraphicsPlanOutput's
        // reflection-tested invariant); the render tools let it OPTIONALLY back that decision
        // with an actual designed/animated graphic instead of only plain drawtext, via
        // MotionGraphicsOverlay.RenderedAssetStorageKey. Previously this case was deliberately
        // minimal (read-only + FailWorkflow only, identical to VideoStoryEditor) — widened
        // here now that the agent can genuinely produce and render Remotion components; a
        // future change should not silently narrow this back down without updating this
        // comment (both AgentToolProvider's runtime grant and DatabaseSeeder's display metadata
        // now derive from this single arm, so they cannot drift independently of each other).
        // ──────────────────────────────────────────────────────────────────

        AgentType.MotionGraphicsPlanner =>
        [
            ToolGroup.ProjectRead,
            ToolGroup.SandboxBrowse,
            ToolGroup.SandboxMetadata,
            ToolGroup.SandboxLint,
            ToolGroup.SandboxAuthoring,
            ToolGroup.SandboxRender,
            ToolGroup.WorkflowControl
        ],

        // ──────────────────────────────────────────────────────────────────
        // MotionGraphicsDirector: the StepType.GraphicsRoom moderator/synthesizer — granted the
        // SAME full sandbox+Remotion+render set as MotionGraphicsPlanner directly above (minus
        // ProjectWrite, for the same reason), NOT VideoEditDirector's minimal read-only scope.
        // Deliberate, not a copy-paste of either neighbor: the graphics room's synthesis call is
        // where the room's plan can optionally be backed by a real rendered transparent overlay
        // asset (MotionGraphicsOverlay.RenderedAssetStorageKey) — capability parity with the solo
        // planner it replaces, which would otherwise silently regress to plain-drawtext-only
        // whenever a workflow swaps the solo step for the room. The room-PARTICIPANT turns (short
        // free-form prose, ~220 tokens) never get this scope: GraphicsRoomStepExecutor restricts
        // every in-room agent (seats AND the director's room instance) to the ProjectRead +
        // WorkflowControl subset of its grant, so sandbox tools are reachable only from the one
        // standalone structured synthesis call — the same place the solo planner uses them. The
        // prompt-injection tradeoff documented on MotionGraphicsPlanner (media-derived view text
        // reaching a code-executing agent, bounded by the sandbox's containment) applies here
        // identically and is accepted for the same reasons.
        // ──────────────────────────────────────────────────────────────────

        AgentType.MotionGraphicsDirector =>
        [
            ToolGroup.ProjectRead,
            ToolGroup.SandboxBrowse,
            ToolGroup.SandboxMetadata,
            ToolGroup.SandboxLint,
            ToolGroup.SandboxAuthoring,
            ToolGroup.SandboxRender,
            ToolGroup.WorkflowControl
        ],

        // ──────────────────────────────────────────────────────────────────
        // Custom / unknown / FileSummarizerAgent (Inference-API-only — FileSummarizerAgent is
        // never actually routed through this resolver at runtime, but the switch's default arm
        // covers it defensively rather than throwing): minimal project read access only.
        // ──────────────────────────────────────────────────────────────────

        _ =>
        [
            ToolGroup.ProjectRead,
            ToolGroup.WorkflowControl
        ]
    };
}
