using System.ComponentModel;
using System.IO;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ReelForge.WorkflowEngine.Execution;
using ReelForge.WorkflowEngine.Services.Storage;

namespace ReelForge.WorkflowEngine.Agents.Tools;

public class ProjectFileAgentTools
{
    private const string SemanticSearchFallbackHint = "Semantic index is not ready. Use list_project_files to identify relevant files and then read_project_file on the most relevant subset.";

    private readonly IProjectFileWorkspace _workspace;
    private readonly IWorkflowExecutionContextAccessor _executionContextAccessor;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _inferenceApiBaseUrl;
    private readonly ILogger<ProjectFileAgentTools> _logger;

    public ProjectFileAgentTools(
        IProjectFileWorkspace workspace,
        IWorkflowExecutionContextAccessor executionContextAccessor,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ProjectFileAgentTools> logger)
    {
        _workspace = workspace;
        _executionContextAccessor = executionContextAccessor;
        _httpClientFactory = httpClientFactory;
        _inferenceApiBaseUrl = configuration["InferenceApi:BaseUrl"] ?? "http://inference:8080";
        _logger = logger;
    }

    [Description("List files available for the current workflow project. Use this to identify candidate files first, then read only the smallest strictly necessary subset.")]
    public async Task<string> ListProjectFiles()
    {
        WorkflowExecutionContext context = RequireContext();
        IReadOnlyList<ProjectWorkspaceFile> files = await _workspace.ListFilesAsync(context.ProjectId, CancellationToken.None);
        return JsonSerializer.Serialize(files);
    }

    [Description("Read a project file by file ID, storage key, or original filename. Only read files that are strictly necessary for the current task/context; avoid broad or exhaustive reading.")]
    public Task<string> ReadProjectFile(
        [Description("File ID, storage key, or original filename")] string fileReference)
    {
        WorkflowExecutionContext context = RequireContext();
        return _workspace.ReadFileAsync(context.ProjectId, fileReference, CancellationToken.None);
    }

    [Description("Search project files semantically using vector index and return the most relevant file snippets for the current project.")]
    public async Task<string> SearchProjectFiles(
        [Description("Natural language query used for semantic file search")] string query,
        [Description("Maximum number of results to return, defaults to 5")] int limit = 5)
    {
        WorkflowExecutionContext context = RequireContext();
        if (string.IsNullOrWhiteSpace(query))
        {
            return JsonSerializer.Serialize(new
            {
                results = Array.Empty<object>(),
                indexNotReady = false
            });
        }

        try
        {
            using HttpClient client = CreateInferenceApiClient();
            HttpResponseMessage response = await client.PostAsJsonAsync(
                $"/api/v1/projects/{context.ProjectId}/files/search",
                new { query, limit },
                CancellationToken.None);

            string responseBody = await response.Content.ReadAsStringAsync(CancellationToken.None);
            if (!response.IsSuccessStatusCode)
            {
                return JsonSerializer.Serialize(new
                {
                    results = Array.Empty<object>(),
                    indexNotReady = true,
                    fallbackHint = SemanticSearchFallbackHint,
                    error = $"Inference API semantic search failed ({(int)response.StatusCode}): {responseBody}"
                });
            }

            JsonObject payload = ParseObjectResponse(responseBody);
            bool indexNotReady = payload["indexNotReady"]?.GetValue<bool>() ?? false;
            if (indexNotReady)
            {
                payload["fallbackHint"] = SemanticSearchFallbackHint;
            }

            return payload.ToJsonString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Semantic project file search failed for project {ProjectId}", context.ProjectId);
            return JsonSerializer.Serialize(new
            {
                results = Array.Empty<object>(),
                indexNotReady = true,
                fallbackHint = SemanticSearchFallbackHint,
                error = ex.Message
            });
        }
    }

    [Description("Create or add a new text file to the current workflow project.")]
    public async Task<string> WriteProjectFile(
        [Description("The new file name or relative path (e.g. scene-01.tsx or folder/scene-01.tsx)")] string fileName,
        [Description("File contents to store")] string content,
        [Description("MIME type, defaults to text/plain")] string? contentType = null)
    {
        WorkflowExecutionContext context = RequireContext();
        // treat the provided name as the original path and basename (agentFiles category)
        ProjectWorkspaceFile file = await _workspace.WriteTextFileAsync(
            context.ProjectId,
            Path.GetFileName(fileName),
            content,
            string.IsNullOrWhiteSpace(contentType) ? "text/plain" : contentType,
            CancellationToken.None,
            category: "agentFiles",
            originalPath: fileName);

        return JsonSerializer.Serialize(file);
    }

    private WorkflowExecutionContext RequireContext() =>
        _executionContextAccessor.Current
        ?? throw new InvalidOperationException("No workflow execution context is available for project file tools.");

    private HttpClient CreateInferenceApiClient()
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(_inferenceApiBaseUrl, UriKind.Absolute);
        return client;
    }

    private static JsonObject ParseObjectResponse(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return new JsonObject
            {
                ["results"] = JsonSerializer.SerializeToNode(Array.Empty<object>()),
                ["indexNotReady"] = false
            };
        }

        JsonNode? parsed = JsonNode.Parse(responseBody);
        if (parsed is JsonObject obj)
            return obj;

        return new JsonObject
        {
            ["results"] = JsonSerializer.SerializeToNode(Array.Empty<object>()),
            ["indexNotReady"] = false,
            ["rawResponse"] = parsed
        };
    }
}
