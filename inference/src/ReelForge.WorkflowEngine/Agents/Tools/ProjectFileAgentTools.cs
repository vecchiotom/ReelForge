using System.ComponentModel;
using System.IO;
using System.Linq;
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
        _logger.LogInformation(
            "Tool call list_project_files (ExecutionId={ExecutionId}, ProjectId={ProjectId}, CorrelationId={CorrelationId})",
            context.ExecutionId,
            context.ProjectId,
            context.CorrelationId);
        IReadOnlyList<ProjectWorkspaceFile> files = await _workspace.ListFilesAsync(context.ProjectId, CancellationToken.None);
        _logger.LogInformation(
            "Tool result list_project_files returned {FileCount} file(s) for project {ProjectId}",
            files.Count,
            context.ProjectId);
        return JsonSerializer.Serialize(files);
    }

    [Description("Read a project file by file ID, storage key, or original filename. Only read files that are strictly necessary for the current task/context; avoid broad or exhaustive reading.")]
    public async Task<string> ReadProjectFile(
        [Description("File ID, storage key, or original filename")] string fileReference)
    {
        WorkflowExecutionContext context = RequireContext();
        _logger.LogInformation(
            "Tool call read_project_file (ExecutionId={ExecutionId}, ProjectId={ProjectId}, Reference={FileReference})",
            context.ExecutionId,
            context.ProjectId,
            fileReference);
        try
        {
            string content = await _workspace.ReadFileAsync(context.ProjectId, fileReference, CancellationToken.None);
            _logger.LogInformation(
                "Tool result read_project_file returned {ContentChars} chars for reference {FileReference}",
                content.Length,
                fileReference);
            return content;
        }
        catch (InvalidOperationException ex)
        {
            // ProjectFileWorkspace.ReadFileAsync deliberately throws rather than decoding a
            // binary file (music/SFX/video candidate, etc.) as UTF-8 text — a real guardrail
            // (see its own doc comment), not a bug. Left uncaught here, that exception propagated
            // all the way out of the agent run and failed the WHOLE step outright — observed live
            // producing a real promo video: VideoStoryEditor's own curiosity about an unrelated
            // file (a music-track candidate it never needed) took down a step that otherwise had
            // everything it needed. Returning the message AS THE TOOL RESULT (the same
            // catch-and-degrade discipline SearchProjectFiles above already applies) lets the
            // agent see the refusal and continue past it instead.
            _logger.LogInformation(
                "Tool result read_project_file for reference {FileReference} refused: {Message}", fileReference, ex.Message);
            return $"Error: {ex.Message}";
        }
        catch (KeyNotFoundException ex)
        {
            // Same catch-and-degrade discipline as the binary-file guardrail above, for the other
            // way this tool routinely fails: the reference simply does not resolve to a
            // project_files row. Left uncaught this killed the entire step, and the trigger is not
            // hypothetical — a VideoAnalyze step's bounded view hands the agent
            // meta.artifactStorageKey ("projects/{id}/agentFiles/video-analysis/{execId}/step-N-
            // analysis.json"), which is an S3 object but deliberately NOT a project file, so an
            // agent that follows that key straight into this tool takes the step down with it.
            // Observed live twice: once failing a VideoStoryEditor step outright, and once as the
            // stated reason a SoundDesigner step gave up ("step-1-analysis.json not found in
            // project scope").
            //
            // The message tells the agent what to do instead, because the analysis view it was
            // already given in its prompt IS the authoritative, bounded form of that artifact —
            // without that hint an agent tends to retry the same unreadable key or, worse,
            // conclude it cannot do the task at all.
            _logger.LogInformation(
                "Tool result read_project_file for reference {FileReference} not found: {Message}", fileReference, ex.Message);
            return
                $"Error: {ex.Message} " +
                "Note: analysis artifacts (agentFiles/video-analysis/...) and step outputs are not project files " +
                "and cannot be read with this tool. The analysis view already included in your prompt is the " +
                "authoritative, bounded form of that data — use it directly rather than trying to open the artifact. " +
                "Call ListProjectFiles to see what this project actually contains.";
        }
    }

    [Description("Search project files semantically using vector index and return the most relevant file snippets for the current project.")]
    public async Task<string> SearchProjectFiles(
        [Description("Natural language query used for semantic file search")] string query,
        [Description("Maximum number of results to return, defaults to 5")] int limit = 5)
    {
        WorkflowExecutionContext context = RequireContext();
        _logger.LogInformation(
            "Tool call search_project_files (ExecutionId={ExecutionId}, ProjectId={ProjectId}, QueryLength={QueryLength}, Limit={Limit})",
            context.ExecutionId,
            context.ProjectId,
            query?.Length ?? 0,
            limit);
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
                payload["fallbackFiles"] = JsonSerializer.SerializeToNode(
                    await BuildDeterministicFallbackCandidatesAsync(context.ProjectId, query, 12));
            }

            _logger.LogInformation(
                "Tool result search_project_files (ProjectId={ProjectId}, IndexNotReady={IndexNotReady})",
                context.ProjectId,
                indexNotReady);

            return payload.ToJsonString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Semantic project file search failed for project {ProjectId}", context.ProjectId);
            IReadOnlyList<object> fallbackFiles = await BuildDeterministicFallbackCandidatesAsync(context.ProjectId, query, 12);
            return JsonSerializer.Serialize(new
            {
                results = Array.Empty<object>(),
                indexNotReady = true,
                fallbackHint = SemanticSearchFallbackHint,
                fallbackFiles,
                error = ex.Message
            });
        }
    }

    [Description("Get a deterministic ranked list of project files to read when semantic index is unavailable. Prioritizes remotion/composition entry points, TSX/TS source files, styles, and configuration.")]
    public async Task<string> GetDeterministicContextFiles(
        [Description("Optional focus query to bias ranking, e.g. 'composition timeline transitions' or 'brand theme colors'")] string? focusQuery = null,
        [Description("Maximum number of files to return, defaults to 12")] int maxFiles = 12)
    {
        WorkflowExecutionContext context = RequireContext();
        _logger.LogInformation(
            "Tool call get_deterministic_context_files (ExecutionId={ExecutionId}, ProjectId={ProjectId}, FocusQueryLength={FocusQueryLength}, MaxFiles={MaxFiles})",
            context.ExecutionId,
            context.ProjectId,
            focusQuery?.Length ?? 0,
            maxFiles);
        IReadOnlyList<object> candidates = await BuildDeterministicFallbackCandidatesAsync(context.ProjectId, focusQuery, maxFiles);
        _logger.LogInformation(
            "Tool result get_deterministic_context_files returned {CandidateCount} candidate file(s) for project {ProjectId}",
            candidates.Count,
            context.ProjectId);
        return JsonSerializer.Serialize(new
        {
            mode = "deterministic-fallback",
            maxFiles = NormalizeFallbackLimit(maxFiles),
            files = candidates
        });
    }

    [Description("Create or add a new text file to the current workflow project.")]
    public async Task<string> WriteProjectFile(
        [Description("The new file name or relative path (e.g. scene-01.tsx or folder/scene-01.tsx)")] string fileName,
        [Description("File contents to store")] string content,
        [Description("MIME type, defaults to text/plain")] string? contentType = null)
    {
        if (string.IsNullOrEmpty(content))
            throw new InvalidOperationException("content is required.");
        string safeContent = content;

        WorkflowExecutionContext context = RequireContext();
        _logger.LogInformation(
            "Tool call write_project_file (ExecutionId={ExecutionId}, ProjectId={ProjectId}, FileName={FileName}, ContentChars={ContentChars}, ContentType={ContentType})",
            context.ExecutionId,
            context.ProjectId,
            fileName,
            safeContent.Length,
            contentType ?? "text/plain");
        // treat the provided name as the original path and basename (agentFiles category)
        ProjectWorkspaceFile file = await _workspace.WriteTextFileAsync(
            context.ProjectId,
            Path.GetFileName(fileName),
            safeContent,
            string.IsNullOrWhiteSpace(contentType) ? "text/plain" : contentType,
            CancellationToken.None,
            category: "agentFiles",
            originalPath: fileName);

        _logger.LogInformation(
            "Tool result write_project_file wrote file {FileId} ({OriginalFileName}) with storage key {StorageKey}",
            file.Id,
            file.OriginalFileName,
            file.StorageKey);

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

    private async Task<IReadOnlyList<object>> BuildDeterministicFallbackCandidatesAsync(Guid projectId, string? focusQuery, int maxFiles)
    {
        IReadOnlyList<ProjectWorkspaceFile> files = await _workspace.ListFilesAsync(projectId, CancellationToken.None);
        string[] focusTokens = Tokenize(focusQuery);
        int limit = NormalizeFallbackLimit(maxFiles);

        List<object> ranked = files
            .Select(file =>
            {
                string effectivePath = file.OriginalPath ?? file.OriginalFileName;
                int score = CalculateDeterministicScore(file, effectivePath, focusTokens);
                string reason = BuildReason(file, effectivePath, focusTokens);
                return new
                {
                    fileId = file.Id,
                    fileName = file.OriginalFileName,
                    filePath = effectivePath,
                    category = file.Category,
                    mimeType = file.MimeType,
                    score,
                    reason
                };
            })
            .OrderByDescending(item => item.score)
            .ThenBy(item => item.filePath, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Cast<object>()
            .ToList();

        return ranked;
    }

    private static int NormalizeFallbackLimit(int maxFiles)
        => Math.Clamp(maxFiles <= 0 ? 12 : maxFiles, 1, 30);

    private static int CalculateDeterministicScore(ProjectWorkspaceFile file, string effectivePath, string[] focusTokens)
    {
        int score = 0;
        string path = effectivePath.ToLowerInvariant();
        string fileName = file.OriginalFileName.ToLowerInvariant();
        string ext = Path.GetExtension(fileName).ToLowerInvariant();

        score += file.Category switch
        {
            "userFiles" => 220,
            "agentFiles" => 140,
            "outputFiles" => 10,
            _ => 80
        };

        score += ext switch
        {
            ".tsx" => 320,
            ".ts" => 260,
            ".jsx" => 220,
            ".js" => 180,
            ".css" or ".scss" => 170,
            ".json" => 140,
            ".md" => 80,
            _ => 60
        };

        if (fileName is "root.tsx" or "index.ts" or "index.tsx")
            score += 260;

        if (path.Contains("remotion", StringComparison.Ordinal) ||
            path.Contains("composition", StringComparison.Ordinal) ||
            path.Contains("timeline", StringComparison.Ordinal) ||
            path.Contains("sequence", StringComparison.Ordinal) ||
            path.Contains("transition", StringComparison.Ordinal))
            score += 220;

        if (path.Contains("src/", StringComparison.Ordinal) ||
            path.Contains("components", StringComparison.Ordinal) ||
            path.Contains("app/", StringComparison.Ordinal) ||
            path.Contains("pages", StringComparison.Ordinal))
            score += 150;

        if (path.Contains("style", StringComparison.Ordinal) ||
            path.Contains("theme", StringComparison.Ordinal) ||
            path.Contains("tailwind", StringComparison.Ordinal) ||
            path.Contains("color", StringComparison.Ordinal) ||
            path.Contains("font", StringComparison.Ordinal))
            score += 120;

        if (fileName is "package.json" or "tsconfig.json")
            score += 150;

        if (file.SizeBytes > 600_000)
            score -= 70;

        if (focusTokens.Length > 0)
        {
            string haystack = (effectivePath + " " + (file.AgentSummary ?? string.Empty)).ToLowerInvariant();
            foreach (string token in focusTokens)
            {
                if (haystack.Contains(token, StringComparison.Ordinal))
                    score += 55;
            }
        }

        return score;
    }

    private static string BuildReason(ProjectWorkspaceFile file, string effectivePath, string[] focusTokens)
    {
        List<string> reasons = [];
        string ext = Path.GetExtension(file.OriginalFileName).ToLowerInvariant();
        string path = effectivePath.ToLowerInvariant();

        if (ext is ".tsx" or ".ts") reasons.Add("source-code");
        if (ext is ".css" or ".scss") reasons.Add("styling");
        if (file.OriginalFileName.Equals("root.tsx", StringComparison.OrdinalIgnoreCase)) reasons.Add("timeline-entrypoint");
        if (path.Contains("remotion", StringComparison.Ordinal) || path.Contains("composition", StringComparison.Ordinal)) reasons.Add("remotion-related");
        if (path.Contains("theme", StringComparison.Ordinal) || path.Contains("style", StringComparison.Ordinal) || path.Contains("tailwind", StringComparison.Ordinal)) reasons.Add("brand-tokens");

        if (focusTokens.Length > 0)
        {
            string haystack = (effectivePath + " " + (file.AgentSummary ?? string.Empty)).ToLowerInvariant();
            if (focusTokens.Any(token => haystack.Contains(token, StringComparison.Ordinal)))
                reasons.Add("focus-query-match");
        }

        if (reasons.Count == 0)
            reasons.Add("deterministic-priority");

        return string.Join(", ", reasons.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string[] Tokenize(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        char[] separators = [' ', '\t', '\r', '\n', ',', '.', ';', ':', '(', ')', '[', ']', '{', '}', '/', '\\', '-', '_', '"', '\''];
        return query
            .ToLowerInvariant()
            .Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 3)
            .Distinct(StringComparer.Ordinal)
            .Take(12)
            .ToArray();
    }
}
