using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using ReelForge.WorkflowEngine.Execution;
using ReelForge.WorkflowEngine.Services.Storage;

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
    private readonly IProjectFileWorkspace _projectFileWorkspace;
    private readonly IAmazonS3 _s3Client;
    private readonly string _sandboxBaseUrl;
    private readonly string _bucketName;
    private readonly ILogger<ReactRemotionSandboxTools> _logger;

    public ReactRemotionSandboxTools(
        IHttpClientFactory httpClientFactory,
        IWorkflowExecutionContextAccessor executionContextAccessor,
        IProjectFileWorkspace projectFileWorkspace,
        IAmazonS3 s3Client,
        IConfiguration configuration,
        ILogger<ReactRemotionSandboxTools> logger)
    {
        _httpClientFactory = httpClientFactory;
        _executionContextAccessor = executionContextAccessor;
        _projectFileWorkspace = projectFileWorkspace;
        _s3Client = s3Client;
        _sandboxBaseUrl = configuration["Sandbox:BaseUrl"] ?? "http://sandbox-executor:8080";
        _bucketName = configuration["MinIO:BucketName"] ?? "reelforge";
        _logger = logger;
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
        _logger.LogInformation("Checking sandbox status for execution {ExecutionId}", executionId);

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
        _logger.LogInformation(
            "Installing {PackageCount} npm package(s) in sandbox for execution {ExecutionId}",
            packages.Length,
            executionId);

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
        WorkflowExecutionContext ctx = RequireContext();
        string executionId = ctx.ExecutionId.ToString();
        _logger.LogInformation("Running typecheck/lint in sandbox for execution {ExecutionId}", executionId);

        await EnsureRequiredSandboxSourcesAsync(ctx, executionId);

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

    private async Task EnsureRequiredSandboxSourcesAsync(WorkflowExecutionContext ctx, string executionId)
    {
        string rootTsx = await TryReadSandboxFileRaw(executionId, "src/root.tsx");
        if (string.IsNullOrWhiteSpace(rootTsx))
            return;

        HashSet<string> requiredFiles = ParseLocalImportFileNames(rootTsx);
        if (requiredFiles.Count == 0)
            return;

        HashSet<string> existingFiles = await ListSandboxEntriesAsync(executionId, "src");
        List<string> missingFiles = requiredFiles
            .Where(file => !existingFiles.Contains(file))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (missingFiles.Count == 0)
            return;

        IReadOnlyList<ProjectWorkspaceFile> projectFiles = await _projectFileWorkspace
            .ListFilesAsync(ctx.ProjectId, CancellationToken.None);

        var candidatesByName = projectFiles
            .Where(IsFrontendSourceCandidate)
            .GroupBy(file => file.OriginalFileName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        int provisionedCount = 0;
        foreach (string missingFile in missingFiles)
        {
            if (!candidatesByName.TryGetValue(missingFile, out List<ProjectWorkspaceFile>? candidates) || candidates.Count == 0)
            {
                _logger.LogDebug(
                    "Sandbox source provisioning: no project file match found for {FileName} (execution {ExecutionId})",
                    missingFile,
                    executionId);
                continue;
            }

            ProjectWorkspaceFile selected = candidates
                .OrderBy(file => file.Category.Equals("agentFiles", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenByDescending(file => file.UploadedAt)
                .First();

            string content = await _projectFileWorkspace.ReadFileAsync(
                ctx.ProjectId,
                selected.Id.ToString(),
                CancellationToken.None);

            await WriteSandboxFileInternal(executionId, $"src/{missingFile}", content);
            existingFiles.Add(missingFile);
            provisionedCount++;
        }

        if (provisionedCount > 0)
        {
            _logger.LogInformation(
                "Provisioned {ProvisionedCount} required source file(s) into sandbox src/ for execution {ExecutionId}",
                provisionedCount,
                executionId);
        }
    }

    // ──────────────────────────────────────────────
    // 4. Render video / image and upload to S3
    // ──────────────────────────────────────────────

    [Description(
        "Render a Remotion composition to a video or image file inside the sandbox, then upload the result " +
        "to S3 storage under the 'projects/{projectId}/outputFiles/' prefix. " +
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
        _logger.LogInformation(
            "Rendering composition {CompositionId} to {OutputFileName} for execution {ExecutionId}",
            compositionId,
            outputFileName,
            executionId);

        // Build Remotion render args: remotion render <compositionId> out/<outputFileName> [extra args]
        string outputPath = $"out/{outputFileName}";
        List<string> args = [compositionId, outputPath];
        if (remotionArgs is { Length: > 0 })
            args.AddRange(remotionArgs);

        // Ensure we specify a Chromium-like executable so Remotion doesn't
        // try to launch whatever it bundled. Historically we pointed at the
        // full /usr/bin/chromium but that binary no longer supports the
        // original --headless flag (errors about headless mode being replaced).
        // Instead we target the lightweight headless-shell runtime that we
        // symlink into every sandbox workspace by default.
        bool hasChromiumArg = args.Any(a => a.StartsWith("--chromium-executable"));
        if (!hasChromiumArg)
        {
            args.Add("--chromium-executable=/workspace/node_modules/.remotion/chrome-headless-shell/linux64/chrome-headless-shell-linux64/chrome-headless-shell");
        }

        // Run the render
        using HttpClient client = CreateClient();
        SandboxCommandInvocation renderResult = await ExecuteSandboxCommandInternal(
            client,
            executionId,
            new SandboxExecRequest("npx", ["remotion", "render", .. args], TimeoutSeconds: 600),
            operation: "render");
        if (!renderResult.Success)
            return JsonSerializer.Serialize(renderResult.ErrorResponse);

        // Read the rendered file from the sandbox
        string encodedPath = Uri.EscapeDataString(outputPath);
        HttpResponseMessage fileResponse = await client.GetAsync(
            $"/api/v1/sandboxes/{executionId}/files/content?path={encodedPath}",
            CancellationToken.None);

        SandboxFileContent payload = await ReadJsonAsync<SandboxFileContent>(fileResponse);
        byte[] fileBytes = Convert.FromBase64String(payload.ContentBase64);

        // Upload to S3 under the new project-centric layout
        string storageKey = $"projects/{ctx.ProjectId}/outputFiles/{executionId}/{outputFileName}";
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
        putRequest.Metadata["category"] = "outputFiles";
        await _s3Client.PutObjectAsync(putRequest, CancellationToken.None);

        // Signal to the executor that this step produced a media artifact
        ctx.PendingOutputStorageKey = storageKey;

        _logger.LogInformation(
            "Uploaded rendered artifact for execution {ExecutionId} to storage key {StorageKey} ({SizeBytes} bytes)",
            executionId,
            storageKey,
            fileBytes.Length);

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
        _logger.LogInformation("Ensuring sandbox exists for execution {ExecutionId}", executionId);

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

        // Provide default chromium-like path when running a render command so
        // the CLI can start in headless mode. We point at the headless-shell
        // binary that is always linked in the workspace; the full chromium
        // build may no longer accept the legacy `--headless` option.
        if (command.Equals("render", StringComparison.OrdinalIgnoreCase))
        {
            bool hasChromiumArg = commandArgs.Any(a => a.StartsWith("--chromium-executable"));
            if (!hasChromiumArg)
            {
                commandArgs.Add("--chromium-executable=/workspace/node_modules/.remotion/chrome-headless-shell/linux64/chrome-headless-shell-linux64/chrome-headless-shell");
            }
        }

        return ExecuteSandboxCommand("npx", commandArgs.ToArray(), timeoutSeconds);
    }

    [Description("Delete and finalize the sandbox for the current workflow execution when work is complete.")]
    public async Task<string> CompleteSandbox()
    {
        string executionId = RequireContext().ExecutionId.ToString();
        _logger.LogInformation("Completing sandbox for execution {ExecutionId}", executionId);

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

    private async Task<HashSet<string>> ListSandboxEntriesAsync(string executionId, string path)
    {
        string encodedPath = Uri.EscapeDataString(path);
        using HttpClient client = CreateClient();
        HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/sandboxes/{executionId}/files?path={encodedPath}",
            CancellationToken.None);

        List<SandboxFileEntry> entries = await ReadJsonAsync<List<SandboxFileEntry>>(response);
        return entries
            .Where(entry => !entry.IsDir)
            .Select(entry => entry.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task WriteSandboxFileInternal(string executionId, string path, string content)
    {
        string encodedPath = Uri.EscapeDataString(path);
        using HttpClient client = CreateClient();
        string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));
        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/v1/sandboxes/{executionId}/files/content?path={encodedPath}",
            new { contentBase64 = base64 },
            CancellationToken.None);

        await ReadJsonAsync<SandboxActionResult>(response);
    }

    private static HashSet<string> ParseLocalImportFileNames(string source)
    {
        const string importPattern = "(?:import\\s+(?:[^;]*?\\s+from\\s+)?|import\\s*)[\"'](?<path>\\./[^\"']+)[\"']";
        MatchCollection matches = Regex.Matches(source, importPattern, RegexOptions.Multiline);

        HashSet<string> fileNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in matches)
        {
            string localPath = match.Groups["path"].Value;
            if (string.IsNullOrWhiteSpace(localPath))
                continue;

            string fileName = Path.GetFileName(localPath);
            if (!string.IsNullOrWhiteSpace(fileName))
                fileNames.Add(fileName);
        }

        return fileNames;
    }

    private static bool IsFrontendSourceCandidate(ProjectWorkspaceFile file)
    {
        if (!file.Category.Equals("userFiles", StringComparison.OrdinalIgnoreCase)
            && !file.Category.Equals("agentFiles", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string extension = Path.GetExtension(file.OriginalFileName);
        return extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ts", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jsx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".js", StringComparison.OrdinalIgnoreCase);
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
        _logger.LogInformation(
            "Executing sandbox command for execution {ExecutionId}: {Command} (args={ArgsCount}, timeout={TimeoutSeconds}s)",
            executionId,
            command,
            args.Length,
            timeoutSeconds);

        using HttpClient client = CreateClient();
        SandboxCommandInvocation response = await ExecuteSandboxCommandInternal(
            client,
            executionId,
            new SandboxExecRequest(command, args, timeoutSeconds),
            operation: "exec");

        if (!response.Success)
            return JsonSerializer.Serialize(response.ErrorResponse);

        return JsonSerializer.Serialize(response.Payload);
    }

    private async Task<SandboxCommandInvocation> ExecuteSandboxCommandInternal(
        HttpClient client,
        string executionId,
        SandboxExecRequest request,
        string operation)
    {
        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync(
                $"/api/v1/sandboxes/{executionId}/exec",
                request,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Sandbox command network error for execution {ExecutionId}: {Command}",
                executionId,
                request.Command);

            string[] networkGuidance =
            [
                "Verify the sandbox service is reachable and healthy.",
                "Call EnsureSandbox, then GetSandboxStatus before retrying execution.",
                "Retry the command once after sandbox readiness is confirmed."
            ];

            return new SandboxCommandInvocation(
                false,
                null,
                BuildLlmFriendlyError(
                    operation,
                    request.Command,
                    request.Args,
                    statusCode: null,
                    rawError: ex.Message,
                    output: string.Empty,
                    networkGuidance));
        }

        string body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        SandboxExecResponse payload = TryDeserialize<SandboxExecResponse>(body) ?? new SandboxExecResponse(null, null);

        bool failed = !response.IsSuccessStatusCode || !string.IsNullOrWhiteSpace(payload.Error);
        if (!failed)
            return new SandboxCommandInvocation(true, payload, null);

        _logger.LogWarning(
            "Sandbox command failed for execution {ExecutionId}: operation={Operation}, command={Command}, statusCode={StatusCode}",
            executionId,
            operation,
            request.Command,
            (int)response.StatusCode);

        string rawError = ExtractError(body, payload.Error);
        string[] guidance = BuildGuidance(rawError, payload.Output, request.Command, request.Args, operation);

        return new SandboxCommandInvocation(
            false,
            payload,
            BuildLlmFriendlyError(
                operation,
                request.Command,
                request.Args,
                (int)response.StatusCode,
                rawError,
                payload.Output,
                guidance));
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

    private static T? TryDeserialize<T>(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions);
        }
        catch
        {
            return default;
        }
    }

    private static string ExtractError(string body, string? payloadError)
    {
        if (!string.IsNullOrWhiteSpace(payloadError))
            return payloadError;

        SandboxError? error = TryDeserialize<SandboxError>(body);
        if (!string.IsNullOrWhiteSpace(error?.Error))
            return error.Error;

        return string.IsNullOrWhiteSpace(body)
            ? "Sandbox execution failed without an error message."
            : body;
    }

    private static string[] BuildGuidance(
        string rawError,
        string? output,
        string command,
        string[] args,
        string operation)
    {
        string combined = $"{rawError}\n{output}";

        if (combined.Contains("sandbox not found", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "Call EnsureSandbox before running exec or render commands.",
                "Call GetSandboxStatus and confirm exists=true and ready=true.",
                "Retry the same command after sandbox readiness is confirmed."
            ];
        }

        if (combined.Contains("command not allowed", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("invalid json body", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "Use only allowed commands through RunSandboxNpmScript or RunSandboxRemotionCommand.",
                "For npm use scripts: build, render, typecheck, compositions, lint.",
                "For remotion use commands: render, still, compositions."
            ];
        }

        if (combined.Contains("context deadline exceeded", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "Increase timeoutSeconds for long builds/renders, then retry.",
                "Run CheckLintAndTypeErrors before render to reduce repeated failures.",
                "If output is very large, simplify composition props or reduce workload."
            ];
        }

        if (combined.Contains("Cannot find module", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("TS2307", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("npm ERR!", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "Install missing packages with InstallNpmPackages and rerun typecheck.",
                "Use CheckLintAndTypeErrors to validate dependency and TypeScript issues first.",
                "Retry the original command only after checks pass."
            ];
        }

        if (operation.Equals("render", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("remotion", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("composition", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("chromium", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("headless", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "Run npx remotion compositions to verify the composition ID exists.",
                "Ensure all required assets and props are present before rendering.",
                "Run CheckLintAndTypeErrors, fix errors, then retry render."
            ];
        }

        return
        [
            "Review outputTail for the concrete tool error and apply the minimal fix.",
            "Run CheckLintAndTypeErrors to collect actionable diagnostics.",
            $"Retry the same {command} command after applying fixes (args count: {args.Length})."
        ];
    }

    private static object BuildLlmFriendlyError(
        string operation,
        string command,
        string[] args,
        int? statusCode,
        string rawError,
        string? output,
        string[] guidance)
    {
        return new
        {
            success = false,
            operation,
            command,
            args,
            statusCode,
            error = rawError,
            outputTail = Tail(output, 30),
            guidance
        };
    }

    private static string Tail(string? text, int maxLines)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length <= maxLines)
            return text;

        string[] tail = lines.Skip(lines.Length - maxLines).ToArray();
        return string.Join(Environment.NewLine, tail);
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
    private sealed record SandboxCommandInvocation(bool Success, SandboxExecResponse? Payload, object? ErrorResponse);
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