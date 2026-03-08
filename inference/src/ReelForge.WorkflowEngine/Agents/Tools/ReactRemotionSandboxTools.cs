using System.ComponentModel;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
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
        "compositions",
        "lint"
    };

    private static readonly HashSet<string> AllowedRemotionCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "render",
        "still",
        "compositions"
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWorkflowExecutionContextAccessor _executionContextAccessor;
    private readonly IAmazonS3 _s3Client;
    private readonly string _sandboxBaseUrl;
    private readonly string _bucketName;

    public ReactRemotionSandboxTools(
        IHttpClientFactory httpClientFactory,
        IWorkflowExecutionContextAccessor executionContextAccessor,
        IAmazonS3 s3Client,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _executionContextAccessor = executionContextAccessor;
        _s3Client = s3Client;
        _sandboxBaseUrl = configuration["Sandbox:BaseUrl"] ?? "http://sandbox-executor:8080";
        _bucketName = configuration["MinIO:BucketName"] ?? "reelforge";
    }

    // ──────────────────────────────────────────────
    // 1. Status / readiness
    // ──────────────────────────────────────────────

    [Description(
        "Check the current state of the sandbox React/Remotion environment. " +
        "Returns whether the sandbox exists, whether it is fully ready (package.json + node_modules present), " +
        "and metadata about the container. Call this before writing source files or running builds.")]
    public async Task<string> GetSandboxStatus()
    {
        string executionId = RequireContext().ExecutionId.ToString();
        using HttpClient client = CreateClient();
        HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/sandboxes/{executionId}/status",
            CancellationToken.None);

        SandboxStatusResponse status = await ReadJsonAsync<SandboxStatusResponse>(response);
        return JsonSerializer.Serialize(status);
    }

    // ──────────────────────────────────────────────
    // 2. Package installation
    // ──────────────────────────────────────────────

    [Description(
        "Install one or more npm packages into the sandbox workspace. This runs only inside " +
        "the isolated sandbox container and cannot affect the host or other services. " +
        "Use this when the project you are recreating depends on specific libraries " +
        "(e.g. '@mantine/core', 'framer-motion', 'three'). " +
        "Packages are validated server-side; only standard npm package names are accepted. " +
        "Returns the npm install output.")]
    public async Task<string> InstallNpmPackages(
        [Description("Array of npm package names to install (e.g. [\"@mantine/core\", \"framer-motion@10\"])")]
        string[] packages)
    {
        if (packages is not { Length: > 0 })
            throw new InvalidOperationException("At least one package name is required.");

        string executionId = RequireContext().ExecutionId.ToString();
        using HttpClient client = CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/sandboxes/{executionId}/packages",
            new { packages },
            CancellationToken.None);

        SandboxExecResponse payload = await ReadJsonAsync<SandboxExecResponse>(response);
        return JsonSerializer.Serialize(payload);
    }

    // ──────────────────────────────────────────────
    // 3. Lint / type-check
    // ──────────────────────────────────────────────

    [Description(
        "Run TypeScript type-checking and (if a lint script is configured) ESLint on the sandbox workspace " +
        "to surface compile errors, type errors, and lint warnings. " +
        "Use this after writing or modifying source files to verify correctness before rendering.")]
    public async Task<string> CheckLintAndTypeErrors()
    {
        string executionId = RequireContext().ExecutionId.ToString();

        // Run typecheck first, then lint – collect both outputs.
        string typecheckOutput = await RunNpmScriptInternal(executionId, "typecheck");
        string? lintOutput = null;

        // Check if a lint script exists in package.json before running it.
        string packageJsonRaw = await TryReadSandboxFileRaw(executionId, "package.json");
        if (!string.IsNullOrEmpty(packageJsonRaw))
        {
            using JsonDocument doc = JsonDocument.Parse(packageJsonRaw);
            if (doc.RootElement.TryGetProperty("scripts", out JsonElement scripts) &&
                scripts.TryGetProperty("lint", out _))
            {
                lintOutput = await RunNpmScriptInternal(executionId, "lint");
            }
        }

        var result = new
        {
            typecheck = typecheckOutput,
            lint = lintOutput,
            hasErrors = typecheckOutput.Contains("error TS", StringComparison.OrdinalIgnoreCase)
                        || (lintOutput?.Contains(" error ", StringComparison.OrdinalIgnoreCase) ?? false)
        };
        return JsonSerializer.Serialize(result);
    }

    // ──────────────────────────────────────────────
    // 4. Render video / image and upload to S3
    // ──────────────────────────────────────────────

    [Description(
        "Render a Remotion composition to a video or image file inside the sandbox, then upload the result " +
        "to S3 storage under the 'outputs/{executionId}/' prefix. " +
        "On success, the storage key is automatically attached to the current workflow step result " +
        "so it can be retrieved and played back from the UI. " +
        "Returns the S3 storage key and file size on success. " +
        "Tip: run CheckLintAndTypeErrors first to confirm the project compiles cleanly.")]
    public async Task<string> RenderVideoAndUploadToStorage(
        [Description("Remotion composition ID to render (must match a composition registered in the project)")]
        string compositionId,
        [Description("Output file name including extension, e.g. 'promo.mp4' or 'thumbnail.png'")]
        string outputFileName,
        [Description("Optional extra Remotion CLI args, e.g. [\"--props={}\", \"--scale=1\"]")]
        string[]? remotionArgs = null)
    {
        if (string.IsNullOrWhiteSpace(compositionId))
            throw new InvalidOperationException("compositionId is required.");
        if (string.IsNullOrWhiteSpace(outputFileName))
            throw new InvalidOperationException("outputFileName is required.");

        WorkflowExecutionContext ctx = RequireContext();
        string executionId = ctx.ExecutionId.ToString();

        // Build Remotion render args: remotion render <compositionId> out/<outputFileName> [extra args]
        string outputPath = $"out/{outputFileName}";
        List<string> args = [compositionId, outputPath];
        if (remotionArgs is { Length: > 0 })
            args.AddRange(remotionArgs);

        // Run the render
        using HttpClient client = CreateClient();
        HttpResponseMessage renderResponse = await client.PostAsJsonAsync(
            $"/api/v1/sandboxes/{executionId}/exec",
            new SandboxExecRequest("npx", ["remotion", "render", .. args], TimeoutSeconds: 600),
            CancellationToken.None);

        SandboxExecResponse renderResult = await ReadJsonAsync<SandboxExecResponse>(renderResponse);
        if (!string.IsNullOrEmpty(renderResult.Error))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = renderResult.Error,
                output = renderResult.Output
            });
        }

        // Read the rendered file from the sandbox
        string encodedPath = Uri.EscapeDataString(outputPath);
        HttpResponseMessage fileResponse = await client.GetAsync(
            $"/api/v1/sandboxes/{executionId}/files/content?path={encodedPath}",
            CancellationToken.None);

        SandboxFileContent payload = await ReadJsonAsync<SandboxFileContent>(fileResponse);
        byte[] fileBytes = Convert.FromBase64String(payload.ContentBase64);

        // Upload to S3
        string storageKey = $"outputs/{executionId}/{outputFileName}";
        string contentType = InferContentType(outputFileName);

        await using MemoryStream ms = new(fileBytes);
        PutObjectRequest putRequest = new()
        {
            BucketName = _bucketName,
            Key = storageKey,
            InputStream = ms,
            ContentType = contentType
        };
        putRequest.Metadata["workflow-execution-id"] = executionId;
        putRequest.Metadata["composition-id"] = compositionId;
        await _s3Client.PutObjectAsync(putRequest, CancellationToken.None);

        // Signal to the executor that this step produced a media artifact
        ctx.PendingOutputStorageKey = storageKey;

        return JsonSerializer.Serialize(new
        {
            success = true,
            storageKey,
            fileName = outputFileName,
            sizeBytes = fileBytes.Length,
            contentType
        });
    }

    // ──────────────────────────────────────────────
    // Existing tools (unchanged)
    // ──────────────────────────────────────────────

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

    [Description("Execute an allowed npm script in the current workflow sandbox (`build`, `render`, `typecheck`, `compositions`, `lint`).")]
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

    // ──────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────

    private async Task<string> RunNpmScriptInternal(string executionId, string script)
    {
        using HttpClient client = CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/sandboxes/{executionId}/exec",
            new SandboxExecRequest("npm", ["run", script], TimeoutSeconds: 120),
            CancellationToken.None);

        string body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using JsonDocument doc = JsonDocument.Parse(body);
        string output = doc.RootElement.TryGetProperty("output", out JsonElement o) ? o.GetString() ?? string.Empty : string.Empty;
        string error = doc.RootElement.TryGetProperty("error", out JsonElement e) ? e.GetString() ?? string.Empty : string.Empty;
        return string.IsNullOrEmpty(error) ? output : $"{output}\n{error}".Trim();
    }

    private async Task<string> TryReadSandboxFileRaw(string executionId, string path)
    {
        try
        {
            string encodedPath = Uri.EscapeDataString(path);
            using HttpClient client = CreateClient();
            HttpResponseMessage response = await client.GetAsync(
                $"/api/v1/sandboxes/{executionId}/files/content?path={encodedPath}",
                CancellationToken.None);
            if (!response.IsSuccessStatusCode) return string.Empty;
            SandboxFileContent payload = await ReadJsonAsync<SandboxFileContent>(response);
            return Encoding.UTF8.GetString(Convert.FromBase64String(payload.ContentBase64));
        }
        catch
        {
            return string.Empty;
        }
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

    private static string InferContentType(string fileName)
    {
        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".gif" => "image/gif",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }

    private sealed record SandboxExecRequest(string Command, string[] Args, int TimeoutSeconds);
    private sealed record SandboxExecResponse(string? Output, string? Error);
    private sealed record SandboxError(string? Error);
    private sealed record SandboxStatusResponse(
        bool Exists, bool Ready, bool HasPackageJson, bool HasNodeModules,
        string? ContainerName, string? WorkspacePath,
        DateTime? CreatedAt, DateTime? LastActivity);
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