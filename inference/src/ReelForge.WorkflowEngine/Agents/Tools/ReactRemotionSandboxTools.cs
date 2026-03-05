using System.ComponentModel;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ReelForge.WorkflowEngine.Execution;

namespace ReelForge.WorkflowEngine.Agents.Tools;

public class ReactRemotionSandboxTools
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly HashSet<string> AllowedNpmScripts = new(StringComparer.OrdinalIgnoreCase)
    {
        "build",
        "render",
        "typecheck",
        "compositions"
    };

    private static readonly HashSet<string> AllowedRemotionCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "render",
        "still",
        "compositions"
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWorkflowExecutionContextAccessor _executionContextAccessor;
    private readonly string _sandboxBaseUrl;

    public ReactRemotionSandboxTools(
        IHttpClientFactory httpClientFactory,
        IWorkflowExecutionContextAccessor executionContextAccessor,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _executionContextAccessor = executionContextAccessor;
        _sandboxBaseUrl = configuration["Sandbox:BaseUrl"] ?? "http://sandbox-executor:8080";
    }

    [Description("Create or reuse the sandbox bound to the current workflow execution. Call this before sandbox file/exec actions.")]
    public async Task<string> EnsureSandbox()
    {
        string executionId = RequireContext().ExecutionId.ToString();
        using HttpClient client = CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/sandboxes",
            new { workflowExecutionId = executionId },
            CancellationToken.None);

        SandboxInfo sandbox = await ReadJsonAsync<SandboxInfo>(response);
        return JsonSerializer.Serialize(sandbox);
    }

    [Description("Get sandbox metadata (container/workspace/activity timestamps) for the current workflow execution.")]
    public async Task<string> GetSandbox()
    {
        string executionId = RequireContext().ExecutionId.ToString();
        using HttpClient client = CreateClient();
        HttpResponseMessage response = await client.GetAsync($"/api/v1/sandboxes/{executionId}", CancellationToken.None);
        SandboxInfo sandbox = await ReadJsonAsync<SandboxInfo>(response);
        return JsonSerializer.Serialize(sandbox);
    }

    [Description("List files/directories inside the current workflow sandbox path. Use \".\" to list the workspace root.")]
    public async Task<string> ListSandboxFiles(
        [Description("Relative path inside sandbox workspace, defaults to root")] string path = ".")
    {
        string executionId = RequireContext().ExecutionId.ToString();
        string encodedPath = Uri.EscapeDataString(path);
        using HttpClient client = CreateClient();
        HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/sandboxes/{executionId}/files?path={encodedPath}",
            CancellationToken.None);

        List<SandboxFileEntry> entries = await ReadJsonAsync<List<SandboxFileEntry>>(response);
        return JsonSerializer.Serialize(entries);
    }

    [Description("Read a text file from the current workflow sandbox by relative path.")]
    public async Task<string> ReadSandboxFile(
        [Description("Relative file path inside sandbox workspace")] string path)
    {
        string executionId = RequireContext().ExecutionId.ToString();
        string encodedPath = Uri.EscapeDataString(path);
        using HttpClient client = CreateClient();
        HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/sandboxes/{executionId}/files/content?path={encodedPath}",
            CancellationToken.None);

        SandboxFileContent payload = await ReadJsonAsync<SandboxFileContent>(response);
        byte[] data = Convert.FromBase64String(payload.ContentBase64);
        string text = Encoding.UTF8.GetString(data);
        return JsonSerializer.Serialize(new { payload.Path, content = text });
    }

    [Description("Write UTF-8 text content to a file in the current workflow sandbox. Missing directories are created automatically.")]
    public async Task<string> WriteSandboxFile(
        [Description("Relative file path inside sandbox workspace")] string path,
        [Description("UTF-8 text content to write")] string content)
    {
        string executionId = RequireContext().ExecutionId.ToString();
        string encodedPath = Uri.EscapeDataString(path);
        using HttpClient client = CreateClient();
        string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));
        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/v1/sandboxes/{executionId}/files/content?path={encodedPath}",
            new { contentBase64 = base64 },
            CancellationToken.None);

        SandboxActionResult result = await ReadJsonAsync<SandboxActionResult>(response);
        return JsonSerializer.Serialize(new { result.Ok, path });
    }

    [Description("Delete a file or directory path from the current workflow sandbox.")]
    public async Task<string> DeleteSandboxPath(
        [Description("Relative file or directory path to remove")] string path)
    {
        string executionId = RequireContext().ExecutionId.ToString();
        string encodedPath = Uri.EscapeDataString(path);
        using HttpClient client = CreateClient();
        HttpResponseMessage response = await client.DeleteAsync(
            $"/api/v1/sandboxes/{executionId}/files?path={encodedPath}",
            CancellationToken.None);

        SandboxActionResult result = await ReadJsonAsync<SandboxActionResult>(response);
        return JsonSerializer.Serialize(new { result.Ok, path });
    }

    [Description("Execute an allowed npm script in the current workflow sandbox (`build`, `render`, `typecheck`, `compositions`).")]
    public Task<string> RunSandboxNpmScript(
        [Description("Allowed npm script name")] string script,
        [Description("Additional npm arguments")] string[]? additionalArgs = null,
        [Description("Optional timeout in seconds (0 = sandbox default)")] int timeoutSeconds = 0)
    {
        if (!AllowedNpmScripts.Contains(script))
            throw new InvalidOperationException($"Unsupported npm script '{script}'. Allowed: {string.Join(", ", AllowedNpmScripts)}.");

        List<string> args = ["run", script];
        if (additionalArgs is { Length: > 0 })
            args.AddRange(additionalArgs);

        return ExecuteSandboxCommand("npm", args.ToArray(), timeoutSeconds);
    }

    [Description("Execute an allowed Remotion CLI command in the current workflow sandbox (`render`, `still`, `compositions`).")]
    public Task<string> RunSandboxRemotionCommand(
        [Description("Allowed Remotion command")] string command,
        [Description("Remotion command arguments")] string[]? args = null,
        [Description("Optional timeout in seconds (0 = sandbox default)")] int timeoutSeconds = 0)
    {
        if (!AllowedRemotionCommands.Contains(command))
            throw new InvalidOperationException($"Unsupported remotion command '{command}'. Allowed: {string.Join(", ", AllowedRemotionCommands)}.");

        List<string> commandArgs = ["remotion", command];
        if (args is { Length: > 0 })
            commandArgs.AddRange(args);

        return ExecuteSandboxCommand("npx", commandArgs.ToArray(), timeoutSeconds);
    }

    [Description("Delete and finalize the sandbox for the current workflow execution when work is complete.")]
    public async Task<string> CompleteSandbox()
    {
        string executionId = RequireContext().ExecutionId.ToString();
        using HttpClient client = CreateClient();
        HttpResponseMessage response = await client.PostAsync(
            $"/api/v1/sandboxes/{executionId}/complete",
            content: null,
            CancellationToken.None);

        SandboxActionResult result = await ReadJsonAsync<SandboxActionResult>(response);
        return JsonSerializer.Serialize(result);
    }

    private async Task<string> ExecuteSandboxCommand(string command, string[] args, int timeoutSeconds)
    {
        string executionId = RequireContext().ExecutionId.ToString();
        using HttpClient client = CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/sandboxes/{executionId}/exec",
            new SandboxExecRequest(command, args, timeoutSeconds),
            CancellationToken.None);

        SandboxExecResponse payload = await ReadJsonAsync<SandboxExecResponse>(response);
        return JsonSerializer.Serialize(payload);
    }

    private HttpClient CreateClient()
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(_sandboxBaseUrl, UriKind.Absolute);
        return client;
    }

    private async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        if (!response.IsSuccessStatusCode)
        {
            SandboxError? error = JsonSerializer.Deserialize<SandboxError>(body, JsonOptions);
            throw new InvalidOperationException(
                $"Sandbox API request failed ({(int)response.StatusCode}): {error?.Error ?? body}");
        }

        T? result = JsonSerializer.Deserialize<T>(body, JsonOptions);
        if (result is null)
            throw new InvalidOperationException("Sandbox API response body was empty.");

        return result;
    }

    private WorkflowExecutionContext RequireContext() =>
        _executionContextAccessor.Current
        ?? throw new InvalidOperationException("No workflow execution context is available for sandbox tools.");

    private sealed record SandboxExecRequest(string Command, string[] Args, int TimeoutSeconds);
    private sealed record SandboxExecResponse(string? Output, string? Error);
    private sealed record SandboxError(string? Error);
    private sealed record SandboxInfo(
        string Id,
        string WorkflowExecutionId,
        string ContainerName,
        string WorkspacePath,
        DateTime CreatedAt,
        DateTime LastActivity);
    private sealed record SandboxFileEntry(string Name, bool IsDir, long Size, DateTime ModTime);
    private sealed record SandboxFileContent(string Path, string ContentBase64);
    private sealed record SandboxActionResult(bool Ok);
}
