using Microsoft.Extensions.AI;
using ReelForge.Shared.Data.Models;

namespace ReelForge.WorkflowEngine.Agents.Tools;

public class AgentToolProvider : IAgentToolProvider
{
    private readonly ProjectFileAgentTools _projectFileTools;
    private readonly ReactRemotionSandboxTools _sandboxTools;
    private readonly Dictionary<AgentType, Func<IEnumerable<AIFunction>>> _agentSpecificTools;

    public AgentToolProvider(
        ProjectFileAgentTools projectFileTools,
        ReactRemotionSandboxTools sandboxTools)
    {
        _projectFileTools = projectFileTools;
        _sandboxTools = sandboxTools;
        _agentSpecificTools = new Dictionary<AgentType, Func<IEnumerable<AIFunction>>>
        {
            [AgentType.CodeStructureAnalyzer] = () =>
            [
                AIFunctionFactory.Create(ProjectDataFormatterTools.ReadFileTree),
                AIFunctionFactory.Create(ProjectDataFormatterTools.ReadFileContent)
            ],
            [AgentType.DependencyAnalyzer] = () =>
            [
                AIFunctionFactory.Create(ProjectDataFormatterTools.ReadPackageManifest),
                AIFunctionFactory.Create(ProjectDataFormatterTools.ReadFileContent)
            ],
            [AgentType.ComponentInventoryAnalyzer] = () =>
            [
                AIFunctionFactory.Create(ProjectDataFormatterTools.ReadFileContent),
                AIFunctionFactory.Create(ProjectDataFormatterTools.ListFilesByExtension)
            ],
            [AgentType.RouteAndApiAnalyzer] = () =>
            [
                AIFunctionFactory.Create(ProjectDataFormatterTools.ReadFileContent),
                AIFunctionFactory.Create(ProjectDataFormatterTools.SearchPatterns)
            ],
            [AgentType.StyleAndThemeExtractor] = () =>
            [
                AIFunctionFactory.Create(ProjectDataFormatterTools.ReadStyleConfig),
                AIFunctionFactory.Create(ProjectDataFormatterTools.ReadFileContent)
            ]
        };
    }

    public IReadOnlyList<AIFunction> GetTools(AgentType agentType)
    {
        List<AIFunction> tools =
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
            AIFunctionFactory.Create(_sandboxTools.CompleteSandbox)
        ];

        if (_agentSpecificTools.TryGetValue(agentType, out Func<IEnumerable<AIFunction>>? specificTools))
            tools.AddRange(specificTools());

        return tools;
    }
}
