using System.ComponentModel;
using System.Text.Json;
using ReelForge.WorkflowEngine.Execution;
using ReelForge.WorkflowEngine.Services.Storage;

namespace ReelForge.WorkflowEngine.Agents.Tools;

public class ProjectFileAgentTools
{
    private readonly IProjectFileWorkspace _workspace;
    private readonly IWorkflowExecutionContextAccessor _executionContextAccessor;

    public ProjectFileAgentTools(
        IProjectFileWorkspace workspace,
        IWorkflowExecutionContextAccessor executionContextAccessor)
    {
        _workspace = workspace;
        _executionContextAccessor = executionContextAccessor;
    }

    [Description("List files available for the current workflow project.")]
    public async Task<string> ListProjectFiles()
    {
        WorkflowExecutionContext context = RequireContext();
        IReadOnlyList<ProjectWorkspaceFile> files = await _workspace.ListFilesAsync(context.ProjectId, CancellationToken.None);
        return JsonSerializer.Serialize(files);
    }

    [Description("Read a project file by file ID, storage key, or original filename.")]
    public Task<string> ReadProjectFile(
        [Description("File ID, storage key, or original filename")] string fileReference)
    {
        WorkflowExecutionContext context = RequireContext();
        return _workspace.ReadFileAsync(context.ProjectId, fileReference, CancellationToken.None);
    }

    [Description("Create or add a new text file to the current workflow project.")]
    public async Task<string> WriteProjectFile(
        [Description("The new file name (e.g. scene-01.tsx)")] string fileName,
        [Description("File contents to store")] string content,
        [Description("MIME type, defaults to text/plain")] string? contentType = null)
    {
        WorkflowExecutionContext context = RequireContext();
        ProjectWorkspaceFile file = await _workspace.WriteTextFileAsync(
            context.ProjectId,
            fileName,
            content,
            string.IsNullOrWhiteSpace(contentType) ? "text/plain" : contentType,
            CancellationToken.None);

        return JsonSerializer.Serialize(file);
    }

    private WorkflowExecutionContext RequireContext() =>
        _executionContextAccessor.Current
        ?? throw new InvalidOperationException("No workflow execution context is available for project file tools.");
}
