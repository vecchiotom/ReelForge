using Microsoft.Extensions.AI;
using ReelForge.Shared.Data.Models;

namespace ReelForge.WorkflowEngine.Agents.Tools;

public class AgentToolProvider : IAgentToolProvider
{
    private readonly ProjectFileAgentTools _projectFileTools;
    private readonly Dictionary<AgentType, Func<IEnumerable<AIFunction>>> _agentSpecificTools;

    public AgentToolProvider(ProjectFileAgentTools projectFileTools)
    {
        _projectFileTools = projectFileTools;
        _agentSpecificTools = new Dictionary<AgentType, Func<IEnumerable<AIFunction>>>
        {
            [AgentType.CodeStructureAnalyzer] = () =>
            [
                AIFunctionFactory.Create(FileSystemTools.ReadFileTree),
                AIFunctionFactory.Create(FileSystemTools.ReadFileContent)
            ],
            [AgentType.DependencyAnalyzer] = () =>
            [
                AIFunctionFactory.Create(FileSystemTools.ReadPackageManifest),
                AIFunctionFactory.Create(FileSystemTools.ReadFileContent)
            ],
            [AgentType.ComponentInventoryAnalyzer] = () =>
            [
                AIFunctionFactory.Create(FileSystemTools.ReadFileContent),
                AIFunctionFactory.Create(FileSystemTools.ListFilesByExtension)
            ],
            [AgentType.RouteAndApiAnalyzer] = () =>
            [
                AIFunctionFactory.Create(FileSystemTools.ReadFileContent),
                AIFunctionFactory.Create(FileSystemTools.SearchPatterns)
            ],
            [AgentType.StyleAndThemeExtractor] = () =>
            [
                AIFunctionFactory.Create(FileSystemTools.ReadStyleConfig),
                AIFunctionFactory.Create(FileSystemTools.ReadFileContent)
            ]
        };
    }

    public IReadOnlyList<AIFunction> GetTools(AgentType agentType)
    {
        List<AIFunction> tools =
        [
            AIFunctionFactory.Create(_projectFileTools.ListProjectFiles),
            AIFunctionFactory.Create(_projectFileTools.ReadProjectFile),
            AIFunctionFactory.Create(_projectFileTools.WriteProjectFile)
        ];

        if (_agentSpecificTools.TryGetValue(agentType, out Func<IEnumerable<AIFunction>>? specificTools))
            tools.AddRange(specificTools());

        return tools;
    }
}
