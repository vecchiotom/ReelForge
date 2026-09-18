using Microsoft.Extensions.AI;
using ReelForge.Shared.Agents;
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

    /// <summary>
    /// Resolves the <see cref="ToolGroup"/>s <see cref="ToolGroupCatalog.GroupsFor"/> grants a
    /// given <see cref="AgentType"/> into the actual, runnable <see cref="AIFunction"/>s. The
    /// scoping DECISION (which groups an agent type gets, and why) lives in
    /// <see cref="ToolGroupCatalog"/> — the single source of truth shared with
    /// <c>DatabaseSeeder.GetAvailableToolsJson</c> on the Inference API side. This class only
    /// knows how to turn a group into real <c>AIFunctionFactory.Create(...)</c> calls, since it
    /// is the one holding the injected tool-implementation instances.
    ///
    /// Remotion knowledge-base access is deliberately NOT a static tool group here — it is
    /// resolved per agent-run in <c>ReelForgeAgentBase.CreateAgentAsync</c> via
    /// <c>SkillCatalog.DefaultsFor</c>/<c>ISkillAgentToolsFactory</c> instead, since it is a
    /// named, on-demand-loadable skill (UseSkill/ReadSkillResource), not a tool with a fixed
    /// static surface. See ReelForge.Shared/Skills/SkillCatalog.cs.
    /// </summary>
    public IReadOnlyList<AIFunction> GetTools(AgentType agentType)
    {
        IReadOnlyList<ToolGroup> groups = ToolGroupCatalog.GroupsFor(agentType);

        List<AIFunction> tools = new();
        foreach (ToolGroup group in groups)
            tools.AddRange(BuildGroup(group));

        return tools;
    }

    private IEnumerable<AIFunction> BuildGroup(ToolGroup group) => group switch
    {
        ToolGroup.ProjectRead =>
        [
            AIFunctionFactory.Create(_projectFileTools.ListProjectFiles),
            AIFunctionFactory.Create(_projectFileTools.ReadProjectFile),
            AIFunctionFactory.Create(_projectFileTools.SearchProjectFiles),
            AIFunctionFactory.Create(_projectFileTools.GetDeterministicContextFiles)
        ],

        ToolGroup.ProjectWrite =>
        [
            AIFunctionFactory.Create(_projectFileTools.WriteProjectFile)
        ],

        // ReadSandboxFileLines/GetSandboxFileOutline are read-only — see ToolGroupCatalog's
        // matching comment for why they sit in Browse rather than Authoring.
        ToolGroup.SandboxBrowse =>
        [
            AIFunctionFactory.Create(_sandboxTools.GetSandboxStatus),
            AIFunctionFactory.Create(_sandboxTools.ListSandboxFiles),
            AIFunctionFactory.Create(_sandboxTools.ReadSandboxFile),
            AIFunctionFactory.Create(_sandboxTools.ReadSandboxFileLines),
            AIFunctionFactory.Create(_sandboxTools.GetSandboxFileOutline)
        ],

        ToolGroup.SandboxMetadata =>
        [
            AIFunctionFactory.Create(_sandboxTools.GetSandbox)
        ],

        ToolGroup.SandboxLint =>
        [
            AIFunctionFactory.Create(_sandboxTools.CheckLintAndTypeErrors)
        ],

        // EditSandboxFile/ApplySandboxFileEdits mutate an existing file — see ToolGroupCatalog's
        // matching comment for why they sit in Authoring alongside WriteSandboxFile.
        ToolGroup.SandboxAuthoring =>
        [
            AIFunctionFactory.Create(_sandboxTools.EnsureSandbox),
            AIFunctionFactory.Create(_sandboxTools.WriteSandboxFile),
            AIFunctionFactory.Create(_sandboxTools.EditSandboxFile),
            AIFunctionFactory.Create(_sandboxTools.ApplySandboxFileEdits),
            AIFunctionFactory.Create(_sandboxTools.DeleteSandboxPath),
            AIFunctionFactory.Create(_sandboxTools.InstallNpmPackages),
            AIFunctionFactory.Create(_sandboxTools.RunSandboxNpmScript)
        ],

        ToolGroup.SandboxRender =>
        [
            AIFunctionFactory.Create(_sandboxTools.RunSandboxRemotionCommand),
            AIFunctionFactory.Create(_sandboxTools.RenderVideoAndUploadToStorage),
            AIFunctionFactory.Create(_sandboxTools.CompleteSandbox)
        ],

        ToolGroup.WorkflowControl =>
        [
            AIFunctionFactory.Create(_workflowControlTools.FailWorkflow)
        ],

        _ => throw new ArgumentOutOfRangeException(nameof(group), group, "Unknown ToolGroup.")
    };
}
