using Microsoft.Extensions.AI;
using ReelForge.Shared.Data.Models;

namespace ReelForge.WorkflowEngine.Agents.Tools;

public class AgentToolProvider : IAgentToolProvider
{
    private readonly ProjectFileAgentTools _projectFileTools;
    private readonly ReactRemotionSandboxTools _sandboxTools;
    private readonly WorkflowControlAgentTools _workflowControlTools;

    public AgentToolProvider(
        ProjectFileAgentTools projectFileTools,
        ReactRemotionSandboxTools sandboxTools,
        WorkflowControlAgentTools workflowControlTools)
    {
        _projectFileTools = projectFileTools;
        _sandboxTools = sandboxTools;
        _workflowControlTools = workflowControlTools;
    }

    public IReadOnlyList<AIFunction> GetTools(AgentType agentType) =>
        agentType switch
        {
            // ──────────────────────────────────────────────────────────────────
            // Analysis agents: project read-only + data formatter helpers.
            // No sandbox access — these agents only inspect source files.
            // ──────────────────────────────────────────────────────────────────

            AgentType.CodeStructureAnalyzer =>
            [
                AIFunctionFactory.Create(_projectFileTools.ListProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.ReadProjectFile),
                AIFunctionFactory.Create(ProjectDataFormatterTools.ReadFileTree),
                AIFunctionFactory.Create(ProjectDataFormatterTools.ReadFileContent),
                AIFunctionFactory.Create(_workflowControlTools.FailWorkflow)
            ],

            AgentType.DependencyAnalyzer =>
            [
                AIFunctionFactory.Create(_projectFileTools.ListProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.ReadProjectFile),
                AIFunctionFactory.Create(ProjectDataFormatterTools.ReadPackageManifest),
                AIFunctionFactory.Create(ProjectDataFormatterTools.ReadFileContent),
                AIFunctionFactory.Create(_workflowControlTools.FailWorkflow)
            ],

            AgentType.ComponentInventoryAnalyzer =>
            [
                AIFunctionFactory.Create(_projectFileTools.ListProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.ReadProjectFile),
                AIFunctionFactory.Create(ProjectDataFormatterTools.ReadFileContent),
                AIFunctionFactory.Create(ProjectDataFormatterTools.ListFilesByExtension),
                AIFunctionFactory.Create(_workflowControlTools.FailWorkflow)
            ],

            AgentType.RouteAndApiAnalyzer =>
            [
                AIFunctionFactory.Create(_projectFileTools.ListProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.ReadProjectFile),
                AIFunctionFactory.Create(ProjectDataFormatterTools.ReadFileContent),
                AIFunctionFactory.Create(ProjectDataFormatterTools.SearchPatterns),
                AIFunctionFactory.Create(_workflowControlTools.FailWorkflow)
            ],

            AgentType.StyleAndThemeExtractor =>
            [
                AIFunctionFactory.Create(_projectFileTools.ListProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.ReadProjectFile),
                AIFunctionFactory.Create(ProjectDataFormatterTools.ReadStyleConfig),
                AIFunctionFactory.Create(ProjectDataFormatterTools.ReadFileContent),
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
                AIFunctionFactory.Create(_workflowControlTools.FailWorkflow)
            ],

            AgentType.AnimationStrategyAgent =>
            [
                AIFunctionFactory.Create(_projectFileTools.ListProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.ReadProjectFile),
                AIFunctionFactory.Create(_sandboxTools.GetSandboxStatus),
                AIFunctionFactory.Create(_sandboxTools.GetSandbox),
                AIFunctionFactory.Create(_sandboxTools.ListSandboxFiles),
                AIFunctionFactory.Create(_sandboxTools.ReadSandboxFile),
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
                AIFunctionFactory.Create(_sandboxTools.GetSandboxStatus),
                AIFunctionFactory.Create(_sandboxTools.ListSandboxFiles),
                AIFunctionFactory.Create(_sandboxTools.ReadSandboxFile),
                AIFunctionFactory.Create(_workflowControlTools.FailWorkflow)
            ],

            // ──────────────────────────────────────────────────────────────────
            // Author: full sandbox pipeline including render and upload.
            // This is the only agent that should trigger video rendering.
            // ──────────────────────────────────────────────────────────────────

            AgentType.AuthorAgent =>
            [
                AIFunctionFactory.Create(_projectFileTools.ListProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.ReadProjectFile),
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
                AIFunctionFactory.Create(_sandboxTools.GetSandboxStatus),
                AIFunctionFactory.Create(_sandboxTools.GetSandbox),
                AIFunctionFactory.Create(_sandboxTools.ListSandboxFiles),
                AIFunctionFactory.Create(_sandboxTools.ReadSandboxFile),
                AIFunctionFactory.Create(_sandboxTools.CheckLintAndTypeErrors),
                AIFunctionFactory.Create(_workflowControlTools.FailWorkflow)
            ],

            // ──────────────────────────────────────────────────────────────────
            // Custom / unknown: minimal project read access only.
            // ──────────────────────────────────────────────────────────────────

            _ =>
            [
                AIFunctionFactory.Create(_projectFileTools.ListProjectFiles),
                AIFunctionFactory.Create(_projectFileTools.ReadProjectFile),
                AIFunctionFactory.Create(_workflowControlTools.FailWorkflow)
            ]
        };
}
