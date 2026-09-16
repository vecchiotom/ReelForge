using Microsoft.Extensions.AI;
using ReelForge.Shared.Data.Models;

namespace ReelForge.WorkflowEngine.Agents.Tools;

public class AgentToolProvider : IAgentToolProvider
{
    private readonly ProjectFileAgentTools _projectFileTools;
    private readonly ReactRemotionSandboxTools _sandboxTools;
    private readonly WorkflowControlAgentTools _workflowControlTools;
    private readonly RemotionSkillsAgentTools _remotionSkillsTools;

    public AgentToolProvider(
        ProjectFileAgentTools projectFileTools,
        ReactRemotionSandboxTools sandboxTools,
        WorkflowControlAgentTools workflowControlTools,
        RemotionSkillsAgentTools remotionSkillsTools)
    {
        _projectFileTools = projectFileTools;
        _sandboxTools = sandboxTools;
        _workflowControlTools = workflowControlTools;
        _remotionSkillsTools = remotionSkillsTools;
    }

    public IReadOnlyList<AIFunction> GetTools(AgentType agentType) =>
        agentType switch
        {
            // ──────────────────────────────────────────────────────────────────
            // Analysis agents: project read-only access only.
            // No sandbox access — these agents only inspect source files.
            // ──────────────────────────────────────────────────────────────────

            AgentType.CodeStructureAnalyzer =>
            [
                AIFunctionFactory.Create(_projectFileTools.ListProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.ReadProjectFile),
                AIFunctionFactory.Create(_projectFileTools.SearchProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.GetDeterministicContextFiles),
                AIFunctionFactory.Create(_workflowControlTools.FailWorkflow)
            ],

            AgentType.DependencyAnalyzer =>
            [
                AIFunctionFactory.Create(_projectFileTools.ListProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.ReadProjectFile),
                AIFunctionFactory.Create(_projectFileTools.SearchProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.GetDeterministicContextFiles),
                AIFunctionFactory.Create(_workflowControlTools.FailWorkflow)
            ],

            AgentType.ComponentInventoryAnalyzer =>
            [
                AIFunctionFactory.Create(_projectFileTools.ListProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.ReadProjectFile),
                AIFunctionFactory.Create(_projectFileTools.SearchProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.GetDeterministicContextFiles),
                AIFunctionFactory.Create(_workflowControlTools.FailWorkflow)
            ],

            AgentType.RouteAndApiAnalyzer =>
            [
                AIFunctionFactory.Create(_projectFileTools.ListProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.ReadProjectFile),
                AIFunctionFactory.Create(_projectFileTools.SearchProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.GetDeterministicContextFiles),
                AIFunctionFactory.Create(_workflowControlTools.FailWorkflow)
            ],

            AgentType.StyleAndThemeExtractor =>
            [
                AIFunctionFactory.Create(_projectFileTools.ListProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.ReadProjectFile),
                AIFunctionFactory.Create(_projectFileTools.SearchProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.GetDeterministicContextFiles),
                AIFunctionFactory.Create(_workflowControlTools.FailWorkflow)
            ],

            // ──────────────────────────────────────────────────────────────────
            // Translation agents: project read/write + sandbox code authoring.
            // Can install packages and verify correctness, but do NOT render.
            // ──────────────────────────────────────────────────────────────────

            AgentType.RemotionComponentTranslator =>
            [
                AIFunctionFactory.Create(_projectFileTools.ListProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.ReadProjectFile),
                AIFunctionFactory.Create(_projectFileTools.SearchProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.GetDeterministicContextFiles),
                AIFunctionFactory.Create(_projectFileTools.WriteProjectFile),
                AIFunctionFactory.Create(_sandboxTools.GetSandboxStatus),
                AIFunctionFactory.Create(_sandboxTools.EnsureSandbox),
                AIFunctionFactory.Create(_sandboxTools.GetSandbox),
                AIFunctionFactory.Create(_sandboxTools.ListSandboxFiles),
                AIFunctionFactory.Create(_sandboxTools.ReadSandboxFile),
                AIFunctionFactory.Create(_sandboxTools.WriteSandboxFile),
                AIFunctionFactory.Create(_sandboxTools.DeleteSandboxPath),
                AIFunctionFactory.Create(_sandboxTools.InstallNpmPackages),
                AIFunctionFactory.Create(_sandboxTools.CheckLintAndTypeErrors),
                AIFunctionFactory.Create(_sandboxTools.RunSandboxNpmScript),
                AIFunctionFactory.Create(_remotionSkillsTools.SearchRemotionSkills),
                AIFunctionFactory.Create(_remotionSkillsTools.ReadRemotionSkill),
                AIFunctionFactory.Create(_remotionSkillsTools.ListAllRemotionSkills),
                AIFunctionFactory.Create(_workflowControlTools.FailWorkflow)
            ],

            AgentType.AnimationStrategyAgent =>
            [
                AIFunctionFactory.Create(_projectFileTools.ListProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.ReadProjectFile),
                AIFunctionFactory.Create(_projectFileTools.SearchProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.GetDeterministicContextFiles),
                AIFunctionFactory.Create(_sandboxTools.GetSandboxStatus),
                AIFunctionFactory.Create(_sandboxTools.GetSandbox),
                AIFunctionFactory.Create(_sandboxTools.ListSandboxFiles),
                AIFunctionFactory.Create(_sandboxTools.ReadSandboxFile),
                AIFunctionFactory.Create(_remotionSkillsTools.SearchRemotionSkills),
                AIFunctionFactory.Create(_remotionSkillsTools.ReadRemotionSkill),
                AIFunctionFactory.Create(_workflowControlTools.FailWorkflow)
            ],

            // ──────────────────────────────────────────────────────────────────
            // Production planning agents: project + sandbox read-only.
            // Director and Scriptwriter produce creative artefacts from data
            // already in the project/sandbox; they never write or render.
            // ──────────────────────────────────────────────────────────────────

            AgentType.DirectorAgent or AgentType.ScriptwriterAgent =>
            [
                AIFunctionFactory.Create(_projectFileTools.ListProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.ReadProjectFile),
                AIFunctionFactory.Create(_projectFileTools.SearchProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.GetDeterministicContextFiles),
                AIFunctionFactory.Create(_sandboxTools.GetSandboxStatus),
                AIFunctionFactory.Create(_sandboxTools.ListSandboxFiles),
                AIFunctionFactory.Create(_sandboxTools.ReadSandboxFile),
                AIFunctionFactory.Create(_workflowControlTools.FailWorkflow)
            ],

            // ──────────────────────────────────────────────────────────────────
            // Author: full sandbox pipeline including render and upload.
            // This is the only agent producing the pipeline's FINAL deliverable video; the only
            // other agent granted RenderVideoAndUploadToStorage is MotionGraphicsPlanner below,
            // which uses it for a small, optional overlay ASSET (never the final video) as part
            // of the separate video-editing pipeline (see docs/video-editing.md "Motion graphics
            // (Phase 3)").
            // ──────────────────────────────────────────────────────────────────

            AgentType.AuthorAgent =>
            [
                AIFunctionFactory.Create(_projectFileTools.ListProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.ReadProjectFile),
                AIFunctionFactory.Create(_projectFileTools.SearchProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.GetDeterministicContextFiles),
                AIFunctionFactory.Create(_projectFileTools.WriteProjectFile),
                AIFunctionFactory.Create(_sandboxTools.GetSandboxStatus),
                AIFunctionFactory.Create(_sandboxTools.EnsureSandbox),
                AIFunctionFactory.Create(_sandboxTools.GetSandbox),
                AIFunctionFactory.Create(_sandboxTools.ListSandboxFiles),
                AIFunctionFactory.Create(_sandboxTools.ReadSandboxFile),
                AIFunctionFactory.Create(_sandboxTools.WriteSandboxFile),
                AIFunctionFactory.Create(_sandboxTools.DeleteSandboxPath),
                AIFunctionFactory.Create(_sandboxTools.InstallNpmPackages),
                AIFunctionFactory.Create(_sandboxTools.CheckLintAndTypeErrors),
                AIFunctionFactory.Create(_sandboxTools.RunSandboxNpmScript),
                AIFunctionFactory.Create(_sandboxTools.RunSandboxRemotionCommand),
                AIFunctionFactory.Create(_sandboxTools.RenderVideoAndUploadToStorage),
                AIFunctionFactory.Create(_sandboxTools.CompleteSandbox),
                AIFunctionFactory.Create(_remotionSkillsTools.SearchRemotionSkills),
                AIFunctionFactory.Create(_remotionSkillsTools.ReadRemotionSkill),
                AIFunctionFactory.Create(_workflowControlTools.FailWorkflow)
            ],

            // ──────────────────────────────────────────────────────────────────
            // Review agent: read-only access + lint/type checking.
            // Inspects existing artefacts and surfaces quality issues.
            // ──────────────────────────────────────────────────────────────────

            AgentType.ReviewAgent =>
            [
                AIFunctionFactory.Create(_projectFileTools.ListProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.ReadProjectFile),
                AIFunctionFactory.Create(_projectFileTools.SearchProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.GetDeterministicContextFiles),
                AIFunctionFactory.Create(_sandboxTools.GetSandboxStatus),
                AIFunctionFactory.Create(_sandboxTools.GetSandbox),
                AIFunctionFactory.Create(_sandboxTools.ListSandboxFiles),
                AIFunctionFactory.Create(_sandboxTools.ReadSandboxFile),
                AIFunctionFactory.Create(_sandboxTools.CheckLintAndTypeErrors),
                AIFunctionFactory.Create(_remotionSkillsTools.SearchRemotionSkills),
                AIFunctionFactory.Create(_remotionSkillsTools.ReadRemotionSkill),
                AIFunctionFactory.Create(_workflowControlTools.FailWorkflow)
            ],

            // ──────────────────────────────────────────────────────────────────
            // VideoStoryEditor: read-only project context + FailWorkflow only. It decides
            // which offered ids to keep; it never produces or touches media directly.
            // Explicitly NO sandbox tools, no WriteProjectFile, no render tool — unlike the
            // default/unknown case below, this is spelled out on purpose so a future widening
            // of the default case does not silently hand this agent write/render access.
            // ──────────────────────────────────────────────────────────────────

            AgentType.VideoStoryEditor =>
            [
                AIFunctionFactory.Create(_projectFileTools.ListProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.ReadProjectFile),
                AIFunctionFactory.Create(_projectFileTools.SearchProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.GetDeterministicContextFiles),
                AIFunctionFactory.Create(_workflowControlTools.FailWorkflow)
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
                AIFunctionFactory.Create(_projectFileTools.ListProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.ReadProjectFile),
                AIFunctionFactory.Create(_projectFileTools.SearchProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.GetDeterministicContextFiles),
                AIFunctionFactory.Create(_workflowControlTools.FailWorkflow)
            ],

            // ──────────────────────────────────────────────────────────────────
            // VideoReviewAgent: read-only project context + FailWorkflow only, identical scope
            // to VideoStoryEditor above. Its review evidence (sentenceCheck / overlay coverage)
            // is already present in the pipeline history it is given as input — it never needs
            // sandbox/Remotion-skill tools since there is no code to inspect for a video edit.
            // ──────────────────────────────────────────────────────────────────

            AgentType.VideoReviewAgent =>
            [
                AIFunctionFactory.Create(_projectFileTools.ListProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.ReadProjectFile),
                AIFunctionFactory.Create(_projectFileTools.SearchProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.GetDeterministicContextFiles),
                AIFunctionFactory.Create(_workflowControlTools.FailWorkflow)
            ],

            // ──────────────────────────────────────────────────────────────────
            // MotionGraphicsPlanner (Phase 3): the same full sandbox+Remotion+render pipeline as
            // AuthorAgent immediately above it, minus WriteProjectFile — this agent renders a
            // small transparent-background overlay asset (a lower-third, title card, callout),
            // never a whole project artifact, so there is nothing for it to persist as a project
            // file. It still decides overlay placement/content anchored only to offered placement
            // ids (never a timestamp or pixel coordinate — see MotionGraphicsPlanOutput's
            // reflection-tested invariant); the render tools let it OPTIONALLY back that decision
            // with an actual designed/animated graphic instead of only plain drawtext, via
            // MotionGraphicsOverlay.RenderedAssetStorageKey. Previously this case was deliberately
            // minimal (read-only + FailWorkflow only, identical to VideoStoryEditor) — widened
            // here now that the agent can genuinely produce and render Remotion components; a
            // future change should not silently narrow this back down without updating this
            // comment and the mirrored built-in tool-list metadata in DatabaseSeeder.
            // ──────────────────────────────────────────────────────────────────

            AgentType.MotionGraphicsPlanner =>
            [
                AIFunctionFactory.Create(_projectFileTools.ListProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.ReadProjectFile),
                AIFunctionFactory.Create(_projectFileTools.SearchProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.GetDeterministicContextFiles),
                AIFunctionFactory.Create(_sandboxTools.GetSandboxStatus),
                AIFunctionFactory.Create(_sandboxTools.EnsureSandbox),
                AIFunctionFactory.Create(_sandboxTools.GetSandbox),
                AIFunctionFactory.Create(_sandboxTools.ListSandboxFiles),
                AIFunctionFactory.Create(_sandboxTools.ReadSandboxFile),
                AIFunctionFactory.Create(_sandboxTools.WriteSandboxFile),
                AIFunctionFactory.Create(_sandboxTools.DeleteSandboxPath),
                AIFunctionFactory.Create(_sandboxTools.InstallNpmPackages),
                AIFunctionFactory.Create(_sandboxTools.CheckLintAndTypeErrors),
                AIFunctionFactory.Create(_sandboxTools.RunSandboxNpmScript),
                AIFunctionFactory.Create(_sandboxTools.RunSandboxRemotionCommand),
                AIFunctionFactory.Create(_sandboxTools.RenderVideoAndUploadToStorage),
                AIFunctionFactory.Create(_sandboxTools.CompleteSandbox),
                AIFunctionFactory.Create(_remotionSkillsTools.SearchRemotionSkills),
                AIFunctionFactory.Create(_remotionSkillsTools.ReadRemotionSkill),
                AIFunctionFactory.Create(_workflowControlTools.FailWorkflow)
            ],

            // ──────────────────────────────────────────────────────────────────
            // Custom / unknown: minimal project read access only.
            // ──────────────────────────────────────────────────────────────────

            _ =>
            [
                AIFunctionFactory.Create(_projectFileTools.ListProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.ReadProjectFile),
                AIFunctionFactory.Create(_projectFileTools.SearchProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.GetDeterministicContextFiles),
                AIFunctionFactory.Create(_workflowControlTools.FailWorkflow)
            ]
        };
}
