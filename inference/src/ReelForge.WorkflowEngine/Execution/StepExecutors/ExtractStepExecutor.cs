using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Workflows;
using ReelForge.WorkflowEngine.Services.Storage;

namespace ReelForge.WorkflowEngine.Execution.StepExecutors;

/// <summary>
/// Executes <see cref="StepType.Extract"/> steps: deterministic, non-LLM, code-only projection
/// or resolution of prior JSON step outputs (or the project's file inventory) into a bounded
/// "{view, meta}" envelope. See plan §A.5.
///
/// Purity is enforced by the dependency list: this executor never depends on an IChatClient or
/// IAgentRegistry — it performs no model calls whatsoever. The only I/O it performs is an
/// optional read through <see cref="IProjectFileWorkspace"/> for the <c>files</c> operation.
///
/// This executor never throws. Every code path — including unexpected exceptions — returns a
/// <see cref="StepExecutionResult"/> whose Output is valid JSON, because
/// <c>WorkflowStepResult.OutputJson</c> is a jsonb column.
/// </summary>
public class ExtractStepExecutor : IStepExecutor
{
    // Enum values in ExtractConfigJson use the exact C# member name (e.g. "Project", "Step"),
    // matching the rest of the app's convention where StepType/AgentType are exposed to the
    // frontend as PascalCase strings — the frontend's ExtractOperation/ExtractInputSource TS
    // unions mirror that. Property names still follow JsonSerializerDefaults.Web (camelCase).
    private static readonly JsonSerializerOptions ConfigJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions EnvelopeJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IProjectFileWorkspace _workspace;
    private readonly ILogger<ExtractStepExecutor> _logger;

    public ExtractStepExecutor(IProjectFileWorkspace workspace, ILogger<ExtractStepExecutor> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    public StepType StepType => StepType.Extract;

    public async Task<StepExecutionResult> ExecuteAsync(StepExecutionContext context)
    {
        Stopwatch sw = Stopwatch.StartNew();
        WorkflowStep step = context.Step;

        ExtractStepConfig? config;
        try
        {
            if (string.IsNullOrWhiteSpace(step.ExtractConfigJson))
                return Failure(context, sw, "CONFIG_INVALID", "Extract step has no ExtractConfigJson configured.", null, null);

            config = JsonSerializer.Deserialize<ExtractStepConfig>(step.ExtractConfigJson, ConfigJsonOptions);
        }
        catch (JsonException ex)
        {
            return Failure(context, sw, "CONFIG_INVALID", $"ExtractConfigJson is not valid JSON: {ex.Message}", null, null);
        }

        if (config is null)
            return Failure(context, sw, "CONFIG_INVALID", "ExtractConfigJson deserialized to null.", null, null);

        try
        {
            Dictionary<string, ResolvedInput> resolvedInputs = await ResolveInputsAsync(context, config);

            return config.Operation switch
            {
                ExtractOperation.Project => ExecuteProject(context, config, resolvedInputs, sw),
                ExtractOperation.Resolve => ExecuteResolve(context, config, resolvedInputs, sw),
                ExtractOperation.Files => await ExecuteFilesAsync(context, config, sw),
                _ => Failure(context, sw, "CONFIG_INVALID", $"Unknown Extract operation '{config.Operation}'.", config.Operation.ToString(), null)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Extract step {StepOrder} failed unexpectedly", step.StepOrder);
            return Failure(context, sw, "CONFIG_INVALID", $"Unexpected error: {ex.Message}", config.Operation.ToString(), null);
        }
    }

    // ---------------------------------------------------------------------
    // Operation: project
    // ---------------------------------------------------------------------

    private StepExecutionResult ExecuteProject(
        StepExecutionContext context,
        ExtractStepConfig config,
        Dictionary<string, ResolvedInput> resolvedInputs,
        Stopwatch sw)
    {
        if (!resolvedInputs.TryGetValue("source", out ResolvedInput? source) || string.IsNullOrWhiteSpace(source.Content))
            return Failure(context, sw, "INPUT_MISSING", "Extract op=project requires a non-empty 'source' input.", "project", resolvedInputs);

        string path = config.Path ?? "";
        string? rawAtPath = ExpressionEvaluator.ExtractJsonValue(source.Content, path);
        if (rawAtPath is null)
            return Failure(context, sw, "PATH_NOT_FOUND", $"Path '{path}' did not resolve against the 'source' input.", "project", resolvedInputs);

        JsonElement element;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(rawAtPath);
            element = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            // rawAtPath was a bare scalar string (e.g. a plain string field) — treat as a
            // shape mismatch, since project only supports arrays/objects.
            return Failure(context, sw, "SHAPE_MISMATCH", $"Path '{path}' resolved to a non-JSON scalar; project requires an array or object.", "project", resolvedInputs);
        }

        int sourceChars = source.Content.Length;

        if (element.ValueKind == JsonValueKind.Array)
        {
            List<string> rawItems = ExpressionEvaluator.ExtractJsonArray(source.Content, path);
            int totalItemCount = rawItems.Count;

            List<(int OriginalIndex, JsonElement Element)> indexed = rawItems
                .Select((raw, idx) => (idx, ParseElement(raw)))
                .ToList();

            if (!string.IsNullOrWhiteSpace(config.SortBy))
                indexed = SortByField(indexed, config.SortBy!);

            IEnumerable<(int OriginalIndex, JsonElement Element)> windowed = indexed.Skip(Math.Max(0, config.Skip));
            if (config.Take.HasValue)
                windowed = windowed.Take(Math.Max(0, config.Take.Value));

            List<JsonObject> items = windowed
                .Select(x => BuildProjectedItem(x.Element, config.Fields, config.IdField, config.IdPrefix, x.OriginalIndex))
                .ToList();

            (JsonObject view, int itemCount, int droppedItems, bool truncated, int outputChars) = EnforceItemsCap(
                items, totalItemCount, ClampMaxOutputChars(config.MaxOutputChars));

            JsonObject meta = BuildMeta(
                operation: "project",
                shape: "items",
                sourceChars: sourceChars,
                outputChars: outputChars,
                itemCount: itemCount,
                totalItemCount: totalItemCount,
                truncated: truncated,
                droppedItems: droppedItems,
                inputs: resolvedInputs);

            return FinalizeSuccess(context, sw, config, view, meta, "project", resolvedInputs, itemCount);
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            JsonObject projectedObject = ApplyFieldWhitelist(element, config.Fields);
            string objectJson = projectedObject.ToJsonString(EnvelopeJsonOptions);
            int cap = ClampMaxOutputChars(config.MaxOutputChars);
            bool truncated = false;
            int droppedItems = 0;

            if (objectJson.Length > cap && projectedObject.Count > 0)
            {
                List<string> keys = projectedObject.Select(kv => kv.Key).ToList();
                while (keys.Count > 0 && projectedObject.ToJsonString(EnvelopeJsonOptions).Length > cap)
                {
                    string lastKey = keys[^1];
                    keys.RemoveAt(keys.Count - 1);
                    projectedObject.Remove(lastKey);
                    droppedItems++;
                    truncated = true;
                }
            }

            var view = new JsonObject { ["object"] = projectedObject };
            int outputChars = view.ToJsonString(EnvelopeJsonOptions).Length;

            JsonObject meta = BuildMeta(
                operation: "project",
                shape: "object",
                sourceChars: sourceChars,
                outputChars: outputChars,
                itemCount: null,
                totalItemCount: null,
                truncated: truncated,
                droppedItems: droppedItems,
                inputs: resolvedInputs);

            return FinalizeSuccess(context, sw, config, view, meta, "project", resolvedInputs, null);
        }

        return Failure(context, sw, "SHAPE_MISMATCH", $"Path '{path}' resolved to a JSON {element.ValueKind}; project requires an array or object.", "project", resolvedInputs);
    }

    // ---------------------------------------------------------------------
    // Operation: resolve
    // ---------------------------------------------------------------------

    private StepExecutionResult ExecuteResolve(
        StepExecutionContext context,
        ExtractStepConfig config,
        Dictionary<string, ResolvedInput> resolvedInputs,
        Stopwatch sw)
    {
        if (!resolvedInputs.TryGetValue("ids", out ResolvedInput? idsInput) || string.IsNullOrWhiteSpace(idsInput.Content))
            return Failure(context, sw, "INPUT_MISSING", "Extract op=resolve requires a non-empty 'ids' input.", "resolve", resolvedInputs);

        if (!resolvedInputs.TryGetValue("records", out ResolvedInput? recordsInput) || string.IsNullOrWhiteSpace(recordsInput.Content))
            return Failure(context, sw, "INPUT_MISSING", "Extract op=resolve requires a non-empty 'records' input.", "resolve", resolvedInputs);

        string idsPath = config.IdsPath ?? "";
        string recordsPath = config.RecordsPath ?? "";

        string? idsRaw = ExpressionEvaluator.ExtractJsonValue(idsInput.Content, idsPath);
        if (idsRaw is null)
            return Failure(context, sw, "PATH_NOT_FOUND", $"IdsPath '{idsPath}' did not resolve against the 'ids' input.", "resolve", resolvedInputs);

        string? recordsRaw = ExpressionEvaluator.ExtractJsonValue(recordsInput.Content, recordsPath);
        if (recordsRaw is null)
            return Failure(context, sw, "PATH_NOT_FOUND", $"RecordsPath '{recordsPath}' did not resolve against the 'records' input.", "resolve", resolvedInputs);

        List<string> ids;
        try
        {
            using JsonDocument idsDoc = JsonDocument.Parse(idsRaw);
            if (idsDoc.RootElement.ValueKind != JsonValueKind.Array)
                return Failure(context, sw, "SHAPE_MISMATCH", $"IdsPath '{idsPath}' did not resolve to a JSON array.", "resolve", resolvedInputs);

            ids = idsDoc.RootElement.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.GetRawText())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();
        }
        catch (JsonException)
        {
            return Failure(context, sw, "SHAPE_MISMATCH", $"IdsPath '{idsPath}' did not resolve to valid JSON.", "resolve", resolvedInputs);
        }

        List<string> rawRecords = ExpressionEvaluator.ExtractJsonArray(recordsInput.Content, recordsPath);
        if (rawRecords.Count == 0)
        {
            // Distinguish "genuinely empty array" from "not an array at all".
            try
            {
                using JsonDocument recordsDoc = JsonDocument.Parse(recordsRaw);
                if (recordsDoc.RootElement.ValueKind != JsonValueKind.Array)
                    return Failure(context, sw, "SHAPE_MISMATCH", $"RecordsPath '{recordsPath}' did not resolve to a JSON array.", "resolve", resolvedInputs);
            }
            catch (JsonException)
            {
                return Failure(context, sw, "SHAPE_MISMATCH", $"RecordsPath '{recordsPath}' did not resolve to valid JSON.", "resolve", resolvedInputs);
            }
        }

        string idField = string.IsNullOrWhiteSpace(config.IdField) ? "id" : config.IdField!;
        var recordsById = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (string raw in rawRecords)
        {
            JsonElement recordElement = ParseElement(raw);
            string? recordId = ExpressionEvaluator.ExtractJsonValue(raw, idField);
            if (!string.IsNullOrWhiteSpace(recordId) && !recordsById.ContainsKey(recordId))
                recordsById[recordId] = recordElement;
        }

        int totalItemCount = ids.Count;
        var resolvedItems = new List<JsonObject>();
        int originalIndex = 0;

        foreach (string id in ids)
        {
            if (recordsById.TryGetValue(id, out JsonElement record))
            {
                resolvedItems.Add(BuildProjectedItemWithKnownId(record, config.Fields, id, originalIndex));
            }
            else if (config.OnUnknownId == ExtractUnknownIdBehaviour.Fail)
            {
                return Failure(context, sw, "UNKNOWN_ID", $"Id '{id}' was not found among resolved records.", "resolve", resolvedInputs);
            }
            // Skip: silently omit the unresolved id — it is reflected in droppedItems below.

            originalIndex++;
        }

        (JsonObject view, int itemCount, int droppedItems, bool truncated, int outputChars) = EnforceItemsCap(
            resolvedItems, totalItemCount, ClampMaxOutputChars(config.MaxOutputChars));

        int sourceChars = idsInput.Content.Length + recordsInput.Content.Length;

        JsonObject meta = BuildMeta(
            operation: "resolve",
            shape: "items",
            sourceChars: sourceChars,
            outputChars: outputChars,
            itemCount: itemCount,
            totalItemCount: totalItemCount,
            truncated: truncated,
            droppedItems: droppedItems,
            inputs: resolvedInputs);

        return FinalizeSuccess(context, sw, config, view, meta, "resolve", resolvedInputs, itemCount);
    }

    // ---------------------------------------------------------------------
    // Operation: files
    // ---------------------------------------------------------------------

    private async Task<StepExecutionResult> ExecuteFilesAsync(
        StepExecutionContext context,
        ExtractStepConfig config,
        Stopwatch sw)
    {
        IReadOnlyList<ProjectWorkspaceFile> allFiles;
        try
        {
            allFiles = await _workspace.ListFilesAsync(context.Execution.ProjectId, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Extract op=files failed to list project files for project {ProjectId}", context.Execution.ProjectId);
            return Failure(context, sw, "INPUT_MISSING", $"Failed to list project files: {ex.Message}", "files", new Dictionary<string, ResolvedInput>());
        }

        IEnumerable<ProjectWorkspaceFile> filtered = allFiles;

        if (config.Categories is { Count: > 0 })
        {
            HashSet<string> categories = config.Categories.Select(c => c.ToLowerInvariant()).ToHashSet();
            filtered = filtered.Where(f => categories.Contains(f.Category.ToLowerInvariant()));
        }

        if (config.IncludeExtensions is { Count: > 0 })
        {
            HashSet<string> extensions = config.IncludeExtensions
                .Select(e => e.TrimStart('.').ToLowerInvariant())
                .ToHashSet();
            filtered = filtered.Where(f =>
            {
                string ext = Path.GetExtension(f.OriginalFileName).TrimStart('.').ToLowerInvariant();
                return extensions.Contains(ext);
            });
        }

        if (config.ExcludePathContains is { Count: > 0 })
        {
            List<string> excludes = config.ExcludePathContains.Select(e => e.ToLowerInvariant()).ToList();
            filtered = filtered.Where(f =>
            {
                string haystack = $"{f.OriginalPath}/{f.OriginalFileName}".ToLowerInvariant();
                return !excludes.Any(haystack.Contains);
            });
        }

        List<ProjectWorkspaceFile> finalList = filtered.ToList();
        int totalItemCount = finalList.Count;

        var items = new List<JsonObject>(finalList.Count);
        foreach (ProjectWorkspaceFile file in finalList)
        {
            var item = new JsonObject
            {
                ["id"] = file.Id.ToString(),
                ["fileName"] = file.OriginalFileName,
                ["path"] = file.OriginalPath,
                ["category"] = file.Category,
                ["mimeType"] = file.MimeType,
                ["sizeBytes"] = file.SizeBytes
            };

            if (config.IncludeSummaries && !string.IsNullOrWhiteSpace(file.AgentSummary))
                item["summary"] = file.AgentSummary;

            if (config.IncludeContent)
            {
                try
                {
                    string content = await _workspace.ReadFileAsync(context.Execution.ProjectId, file.Id.ToString(), context.CancellationToken);
                    int cap = Math.Max(0, config.MaxCharsPerFile);
                    item["content"] = content.Length > cap ? content[..cap] : content;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Extract op=files could not read content for file {FileId}", file.Id);
                }
            }

            items.Add(item);
        }

        (JsonObject view, int itemCount, int droppedItems, bool truncated, int outputChars) = EnforceItemsCap(
            items, totalItemCount, ClampMaxOutputChars(config.MaxOutputChars));

        var emptyInputs = new Dictionary<string, ResolvedInput>();
        JsonObject meta = BuildMeta(
            operation: "files",
            shape: "items",
            sourceChars: allFiles.Count,
            outputChars: outputChars,
            itemCount: itemCount,
            totalItemCount: totalItemCount,
            truncated: truncated,
            droppedItems: droppedItems,
            inputs: emptyInputs);

        return FinalizeSuccess(context, sw, config, view, meta, "files", emptyInputs, itemCount);
    }

    // ---------------------------------------------------------------------
    // Shared helpers
    // ---------------------------------------------------------------------

    private async Task<Dictionary<string, ResolvedInput>> ResolveInputsAsync(StepExecutionContext context, ExtractStepConfig config)
    {
        var result = new Dictionary<string, ResolvedInput>(StringComparer.Ordinal);

        foreach ((string name, ExtractInputRef inputRef) in config.Inputs)
        {
            string? content = inputRef.From switch
            {
                ExtractInputSource.Previous => context.StepOutputHistory
                    .LastOrDefault(h => !string.IsNullOrWhiteSpace(h.Output))?.Output,
                ExtractInputSource.Step => inputRef.StepOrder.HasValue
                    ? context.StepOutputHistory.FirstOrDefault(h => h.StepOrder == inputRef.StepOrder.Value)?.Output
                    : null,
                ExtractInputSource.Accumulated => context.AccumulatedOutput,
                ExtractInputSource.ProjectFiles => await BuildProjectFilesJsonAsync(context),
                _ => null
            };

            // Previous/Step pull a prior AGENT step's raw completion text verbatim — the same
            // failure mode RobustJsonExtractor exists for everywhere else in this codebase (video
            // story editor decisions, room syntheses, review agent output): a reasoning-capable
            // model emits valid JSON preceded by prose ("Now let me compile the inventory...")
            // before the actual object, which a strict parse of the whole string rejects outright.
            // Extracting the first balanced {...} before handing content to
            // ExpressionEvaluator.ExtractJsonValue is a no-op for content that's already clean JSON
            // (object OR array — ExtractJsonObject only ever narrows, never invents), so this is
            // pure hardening, not a behavior change for the already-working case.
            if ((inputRef.From is ExtractInputSource.Previous or ExtractInputSource.Step) && content is not null)
                content = RobustJsonExtractor.ExtractJsonObject(content) ?? content;

            result[name] = new ResolvedInput(inputRef, content);
        }

        return result;
    }

    private async Task<string?> BuildProjectFilesJsonAsync(StepExecutionContext context)
    {
        try
        {
            IReadOnlyList<ProjectWorkspaceFile> files = await _workspace.ListFilesAsync(context.Execution.ProjectId, context.CancellationToken);
            var array = new JsonArray();
            foreach (ProjectWorkspaceFile file in files)
            {
                array.Add(new JsonObject
                {
                    ["id"] = file.Id.ToString(),
                    ["fileName"] = file.OriginalFileName,
                    ["path"] = file.OriginalPath,
                    ["category"] = file.Category,
                    ["mimeType"] = file.MimeType,
                    ["sizeBytes"] = file.SizeBytes
                });
            }
            return array.ToJsonString(EnvelopeJsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to build ProjectFiles input for Extract step");
            return null;
        }
    }

    private static JsonElement ParseElement(string raw)
    {
        using JsonDocument doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    private static List<(int OriginalIndex, JsonElement Element)> SortByField(
        List<(int OriginalIndex, JsonElement Element)> items,
        string field)
    {
        return items
            .Select(x => (x.OriginalIndex, x.Element, Key: ExpressionEvaluator.ExtractJsonValue(x.Element.GetRawText(), field)))
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => (x.OriginalIndex, x.Element))
            .ToList();
    }

    /// <summary>
    /// Builds a projected item object for the <c>project</c> operation: applies the field
    /// whitelist (if any) and computes a stable id from IdField or IdPrefix+originalIndex.
    /// </summary>
    private static JsonObject BuildProjectedItem(
        JsonElement element,
        IReadOnlyList<string>? fields,
        string? idField,
        string idPrefix,
        int originalIndex)
    {
        string? id = null;
        if (!string.IsNullOrWhiteSpace(idField))
            id = ExpressionEvaluator.ExtractJsonValue(element.GetRawText(), idField);

        id ??= $"{idPrefix}{originalIndex}";

        if (fields is { Count: > 0 })
        {
            JsonObject whitelisted = ApplyFieldWhitelist(element, fields);
            whitelisted.Remove("id");
            var result = new JsonObject { ["id"] = id };
            foreach ((string key, JsonNode? value) in whitelisted)
                result[key] = value?.DeepClone();
            return result;
        }

        // No whitelist: keep the whole element, but ensure an "id" is present.
        JsonNode? whole = JsonNode.Parse(element.GetRawText());
        if (whole is JsonObject wholeObject)
        {
            if (!wholeObject.ContainsKey("id"))
                wholeObject.Insert(0, "id", id);
            return wholeObject;
        }

        return new JsonObject { ["id"] = id, ["value"] = whole };
    }

    private static JsonObject BuildProjectedItemWithKnownId(
        JsonElement element,
        IReadOnlyList<string>? fields,
        string knownId,
        int originalIndex)
    {
        _ = originalIndex;

        if (fields is { Count: > 0 })
        {
            JsonObject whitelisted = ApplyFieldWhitelist(element, fields);
            whitelisted.Remove("id");
            var result = new JsonObject { ["id"] = knownId };
            foreach ((string key, JsonNode? value) in whitelisted)
                result[key] = value?.DeepClone();
            return result;
        }

        JsonNode? whole = JsonNode.Parse(element.GetRawText());
        if (whole is JsonObject wholeObject)
        {
            wholeObject.Remove("id");
            wholeObject.Insert(0, "id", knownId);
            return wholeObject;
        }

        return new JsonObject { ["id"] = knownId, ["value"] = whole };
    }

    private static JsonObject ApplyFieldWhitelist(JsonElement element, IReadOnlyList<string>? fields)
    {
        if (fields is not { Count: > 0 })
        {
            JsonNode? whole = JsonNode.Parse(element.GetRawText());
            return whole as JsonObject ?? new JsonObject { ["value"] = whole };
        }

        var result = new JsonObject();
        string rawJson = element.GetRawText();
        foreach (string field in fields)
        {
            string? valueRaw = ExpressionEvaluator.ExtractJsonValue(rawJson, field);
            if (valueRaw is null)
            {
                result[field] = null;
                continue;
            }

            try
            {
                result[field] = JsonNode.Parse(valueRaw);
            }
            catch (JsonException)
            {
                result[field] = valueRaw;
            }
        }

        return result;
    }

    /// <summary>
    /// Serializes <paramref name="items"/> as a "{items:[...]}" view, dropping trailing items
    /// (never truncating mid-JSON) until the view fits within <paramref name="maxOutputChars"/>.
    /// <paramref name="totalItemCount"/> is the count before any Skip/Take/cap reduction was
    /// applied, so droppedItems/truncated reflect the full picture regardless of which stage
    /// caused the reduction.
    /// </summary>
    private static (JsonObject View, int ItemCount, int DroppedItems, bool Truncated, int OutputChars) EnforceItemsCap(
        List<JsonObject> items,
        int totalItemCount,
        int maxOutputChars)
    {
        var array = new JsonArray();
        foreach (JsonObject item in items)
            array.Add(item.DeepClone());

        var view = new JsonObject { ["items"] = array };
        string serialized = view.ToJsonString(EnvelopeJsonOptions);

        while (serialized.Length > maxOutputChars && array.Count > 0)
        {
            array.RemoveAt(array.Count - 1);
            serialized = view.ToJsonString(EnvelopeJsonOptions);
        }

        int itemCount = array.Count;
        int droppedItems = Math.Max(0, totalItemCount - itemCount);
        bool truncated = droppedItems > 0;

        return (view, itemCount, droppedItems, truncated, serialized.Length);
    }

    private static int ClampMaxOutputChars(int configured) => Math.Clamp(configured, 256, 200_000);

    private static string CamelCase(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];

    private static JsonObject BuildMeta(
        string operation,
        string shape,
        int sourceChars,
        int outputChars,
        int? itemCount,
        int? totalItemCount,
        bool truncated,
        int droppedItems,
        Dictionary<string, ResolvedInput> inputs)
    {
        var meta = new JsonObject
        {
            ["operation"] = operation,
            ["shape"] = shape,
            ["sourceChars"] = sourceChars,
            ["outputChars"] = outputChars,
            ["truncated"] = truncated,
            ["droppedItems"] = droppedItems,
            ["inputs"] = BuildInputsArray(inputs)
        };

        if (itemCount.HasValue)
            meta["itemCount"] = itemCount.Value;
        if (totalItemCount.HasValue)
            meta["totalItemCount"] = totalItemCount.Value;

        return meta;
    }

    private static JsonArray BuildInputsArray(Dictionary<string, ResolvedInput> inputs)
    {
        var array = new JsonArray();
        foreach ((string name, ResolvedInput resolved) in inputs)
        {
            var entry = new JsonObject
            {
                ["name"] = name,
                ["from"] = CamelCase(resolved.Ref.From.ToString()),
                ["chars"] = resolved.Content?.Length ?? 0
            };
            if (resolved.Ref.StepOrder.HasValue)
                entry["stepOrder"] = resolved.Ref.StepOrder.Value;

            array.Add(entry);
        }
        return array;
    }

    /// <summary>
    /// Builds the compact "what was consumed" descriptor recorded via
    /// <see cref="StepExecutionContext.RecordResolvedInput"/> — never the raw source content.
    /// </summary>
    private static string BuildResolvedInputDescriptor(string operation, Dictionary<string, ResolvedInput> inputs, int? itemCount)
    {
        var descriptor = new JsonObject
        {
            ["operation"] = operation,
            ["inputs"] = BuildInputsArray(inputs)
        };
        if (itemCount.HasValue)
            descriptor["itemCount"] = itemCount.Value;

        return descriptor.ToJsonString(EnvelopeJsonOptions);
    }

    private StepExecutionResult FinalizeSuccess(
        StepExecutionContext context,
        Stopwatch sw,
        ExtractStepConfig config,
        JsonObject view,
        JsonObject meta,
        string operation,
        Dictionary<string, ResolvedInput> resolvedInputs,
        int? itemCount)
    {
        string? expectError = EvaluateExpect(config.Expect, view, meta);
        if (expectError is not null)
        {
            var failureEnvelope = new JsonObject
            {
                ["view"] = null,
                ["meta"] = meta.DeepClone(),
                ["error"] = new JsonObject
                {
                    ["code"] = "EXPECT_FAILED",
                    ["message"] = expectError
                }
            };

            _logger.LogWarning(
                "Extract step {StepOrder} failed expect check: {Message}",
                context.Step.StepOrder, expectError);

            context.RecordResolvedInput(BuildResolvedInputDescriptor(operation, resolvedInputs, itemCount));

            return new StepExecutionResult
            {
                Output = failureEnvelope.ToJsonString(EnvelopeJsonOptions),
                NextStepIndex = context.CurrentStepIndex + 1,
                NewIterationCount = context.IterationCount,
                DurationMs = sw.ElapsedMilliseconds,
                TokensUsed = 0,
                Status = StepStatus.Failed,
                ErrorDetails = expectError
            };
        }

        context.RecordResolvedInput(BuildResolvedInputDescriptor(operation, resolvedInputs, itemCount));

        var envelope = new JsonObject
        {
            ["view"] = view,
            ["meta"] = meta
        };

        return new StepExecutionResult
        {
            Output = envelope.ToJsonString(EnvelopeJsonOptions),
            NextStepIndex = context.CurrentStepIndex + 1,
            NewIterationCount = context.IterationCount,
            DurationMs = sw.ElapsedMilliseconds,
            TokensUsed = 0,
            Status = StepStatus.Completed
        };
    }

    private static string? EvaluateExpect(ExtractExpectation? expect, JsonObject view, JsonObject meta)
    {
        if (expect is null)
            return null;

        if (expect.MinItems.HasValue)
        {
            int itemCount = (int?)meta["itemCount"] ?? 0;
            if (itemCount < expect.MinItems.Value)
                return $"expect.minItems={expect.MinItems.Value} but itemCount was {itemCount}.";
        }

        if (expect.MaxItems.HasValue)
        {
            int itemCount = (int?)meta["itemCount"] ?? 0;
            if (itemCount > expect.MaxItems.Value)
                return $"expect.maxItems={expect.MaxItems.Value} but itemCount was {itemCount}.";
        }

        string viewJson = view.ToJsonString(EnvelopeJsonOptions);

        if (expect.RequiredPaths is { Count: > 0 })
        {
            foreach (string path in expect.RequiredPaths)
            {
                string? value = ExpressionEvaluator.ExtractJsonValue(viewJson, path);
                if (value is null)
                    return $"expect.requiredPaths: path '{path}' was not found in the resolved view.";
            }
        }

        if (expect.NonEmptyStringPaths is { Count: > 0 })
        {
            foreach (string path in expect.NonEmptyStringPaths)
            {
                string? value = ExpressionEvaluator.ExtractJsonValue(viewJson, path);
                if (string.IsNullOrWhiteSpace(value))
                    return $"expect.nonEmptyStringPaths: path '{path}' was empty or missing in the resolved view.";
            }
        }

        return null;
    }

    private StepExecutionResult Failure(
        StepExecutionContext context,
        Stopwatch sw,
        string code,
        string message,
        string? operation,
        Dictionary<string, ResolvedInput>? resolvedInputs)
    {
        _logger.LogWarning(
            "Extract step {StepOrder} failed: [{Code}] {Message}",
            context.Step.StepOrder, code, message);

        var meta = new JsonObject
        {
            ["operation"] = operation ?? "unknown",
            ["inputs"] = BuildInputsArray(resolvedInputs ?? new Dictionary<string, ResolvedInput>())
        };

        var envelope = new JsonObject
        {
            ["view"] = null,
            ["meta"] = meta,
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };

        if (resolvedInputs is not null)
            context.RecordResolvedInput(BuildResolvedInputDescriptor(operation ?? "unknown", resolvedInputs, null));

        return new StepExecutionResult
        {
            Output = envelope.ToJsonString(EnvelopeJsonOptions),
            NextStepIndex = context.CurrentStepIndex + 1,
            NewIterationCount = context.IterationCount,
            DurationMs = sw.ElapsedMilliseconds,
            TokensUsed = 0,
            Status = StepStatus.Failed,
            ErrorDetails = message
        };
    }

    private sealed record ResolvedInput(ExtractInputRef Ref, string? Content);
}
