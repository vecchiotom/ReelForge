using System.Reflection;
using System.Text.Json;
using System.Collections;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Inference;

namespace ReelForge.WorkflowEngine.Agents;

public abstract class ReelForgeAgentBase : IReelForgeAgent
{
    private readonly IAgentChatClientProvider _chatClients;
    private readonly List<AIFunction> _tools;
    private readonly Type? _outputSchemaType;
    private readonly int _agentRunTimeoutSeconds;

    protected ReelForgeAgentBase(
        IAgentChatClientProvider chatClients,
        IConfiguration configuration,
        string name,
        string description,
        AgentType agentType,
        string defaultSystemPrompt,
        IEnumerable<AIFunction>? tools = null,
        Guid? agentId = null,
        Type? outputSchemaType = null)
    {
        _chatClients = chatClients;
        _outputSchemaType = outputSchemaType;
        Name = name;
        Description = description;
        AgentType = agentType;
        AgentId = agentId;
        _tools = tools?.ToList() ?? new List<AIFunction>();
        _agentRunTimeoutSeconds = Math.Clamp(
            configuration.GetValue("WorkflowEngine:AgentRunTimeoutSeconds", 300),
            30,
            1800);

        string configKey = $"Agents:{name}:SystemPrompt";
        SystemPrompt = BuildSystemPrompt(
            configuration[configKey] ?? defaultSystemPrompt,
            _outputSchemaType);

        // Generate JSON schema documentation if output type is specified
        if (_outputSchemaType != null)
        {
            OutputSchemaJson = GenerateJsonSchemaDocumentation(_outputSchemaType);
        }
    }

    public Guid? AgentId { get; }
    public string Name { get; }
    public string Description { get; }
    public string SystemPrompt { get; }
    public AgentType AgentType { get; }
    public IReadOnlyList<AIFunction> Tools => _tools.AsReadOnly();
    public string? OutputSchemaJson { get; }
    public Type? OutputSchemaType => _outputSchemaType;

    public async Task<AgentRunResult> RunAsync(string prompt, Guid? agentDefinitionId = null, CancellationToken ct = default)
    {
        AgentResponse agentResponse;
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_agentRunTimeoutSeconds));
        CancellationToken effectiveToken = timeoutCts.Token;

        try
        {
            // Structured output is enforced via ChatResponseFormat.ForJsonSchema<T>() when OutputSchemaType is specified
            AIAgent agent = await CreateAgentAsync(agentDefinitionId, effectiveToken);

            // If structured output is required, configure ResponseFormat at runtime via AgentRunOptions
            if (_outputSchemaType != null)
            {
                // Use reflection to call ChatResponseFormat.ForJsonSchema<T>() with the runtime type
                var method = typeof(ChatResponseFormat).GetMethod(nameof(ChatResponseFormat.ForJsonSchema),
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    Type.EmptyTypes,
                    null);

                if (method != null)
                {
                    var genericMethod = method.MakeGenericMethod(_outputSchemaType);
                    var responseFormat = genericMethod.Invoke(null, null) as ChatResponseFormat;

                    if (responseFormat != null)
                    {
                        var runOptions = new AgentRunOptions
                        {
                            ResponseFormat = responseFormat
                        };

                        agentResponse = await agent.RunAsync(prompt, options: runOptions, cancellationToken: effectiveToken);
                    }
                    else
                    {
                        // Fallback if reflection fails
                        agentResponse = await agent.RunAsync(prompt, cancellationToken: effectiveToken);
                    }
                }
                else
                {
                    // Fallback if reflection fails
                    agentResponse = await agent.RunAsync(prompt, cancellationToken: effectiveToken);
                }
            }
            else
            {
                // Fallback if reflection fails
                agentResponse = await agent.RunAsync(prompt, cancellationToken: effectiveToken);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Agent '{Name}' timed out after {_agentRunTimeoutSeconds} seconds while processing the step.");
        }

        var chatResponse = agentResponse.AsChatResponse();
        string output = chatResponse.Text ?? string.Empty;

        // Extract token usage from the response
        int totalTokens = 0;
        int? inputTokens = null;
        int? outputTokens = null;

        if (chatResponse.Usage != null)
        {
            inputTokens = (int?)(chatResponse.Usage.InputTokenCount ?? 0);
            outputTokens = (int?)(chatResponse.Usage.OutputTokenCount ?? 0);
            totalTokens = (int)(chatResponse.Usage.TotalTokenCount ??
                          ((inputTokens ?? 0) + (outputTokens ?? 0)));
        }

        (IReadOnlyList<AgentToolCallTrace> toolCalls, IReadOnlyList<string> reasoning) = ExtractDiagnostics(agentResponse, chatResponse);

        return new AgentRunResult
        {
            Output = output,
            TokensUsed = totalTokens,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            ToolCalls = toolCalls,
            Reasoning = reasoning
        };
    }

    private static (IReadOnlyList<AgentToolCallTrace> ToolCalls, IReadOnlyList<string> Reasoning) ExtractDiagnostics(object agentResponse, object chatResponse)
    {
        List<AgentToolCallTrace> toolCalls = [];
        List<string> reasoning = [];

        ExtractFromContainer(agentResponse, toolCalls, reasoning);
        ExtractFromContainer(chatResponse, toolCalls, reasoning);

        return (toolCalls, reasoning);
    }

    private static void ExtractFromContainer(object container, List<AgentToolCallTrace> toolCalls, List<string> reasoning)
    {
        foreach (object content in EnumerateProperty(container, "Contents"))
            ExtractFromContent(content, toolCalls, reasoning);

        foreach (object message in EnumerateProperty(container, "Messages"))
        {
            foreach (object content in EnumerateProperty(message, "Contents"))
            {
                ExtractFromContent(content, toolCalls, reasoning);
            }
        }
    }

    private static void ExtractFromContent(object content, List<AgentToolCallTrace> toolCalls, List<string> reasoning)
    {
        string typeName = content.GetType().Name;

        if (typeName.Contains("FunctionCall", StringComparison.OrdinalIgnoreCase) || typeName.Contains("ToolCall", StringComparison.OrdinalIgnoreCase))
        {
            string toolName = ReadStringProperty(content, "Name", "FunctionName", "ToolName") ?? "unknown_tool";
            string? arguments = ReadStringProperty(content, "Arguments", "ArgumentsJson", "Input", "Value");
            toolCalls.Add(new AgentToolCallTrace
            {
                ToolName = toolName,
                Arguments = arguments,
                Result = null
            });
            return;
        }

        if (typeName.Contains("FunctionResult", StringComparison.OrdinalIgnoreCase) || typeName.Contains("ToolResult", StringComparison.OrdinalIgnoreCase))
        {
            string? resultText = ReadStringProperty(content, "Result", "Output", "Value", "Text", "Content");
            if (!string.IsNullOrWhiteSpace(resultText) && toolCalls.Count > 0)
            {
                AgentToolCallTrace last = toolCalls[^1];
                toolCalls[^1] = new AgentToolCallTrace
                {
                    ToolName = last.ToolName,
                    Arguments = last.Arguments,
                    Result = resultText
                };
            }
            return;
        }

        if (typeName.Contains("Reasoning", StringComparison.OrdinalIgnoreCase) || typeName.Contains("Thought", StringComparison.OrdinalIgnoreCase))
        {
            string? text = ReadStringProperty(content, "Text", "Content", "Value", "Reasoning");
            if (!string.IsNullOrWhiteSpace(text))
            {
                reasoning.Add(text);
            }
        }
    }

    private static IEnumerable<object> EnumerateProperty(object instance, string propertyName)
    {
        PropertyInfo? property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property == null)
            yield break;

        object? value = property.GetValue(instance);
        if (value is null || value is string)
            yield break;

        if (value is IEnumerable enumerable)
        {
            foreach (object? item in enumerable)
            {
                if (item != null)
                    yield return item;
            }
        }
    }

    private static string? ReadStringProperty(object instance, params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            PropertyInfo? property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property == null)
                continue;

            object? value = property.GetValue(instance);
            if (value is null)
                continue;

            if (value is string text && !string.IsNullOrWhiteSpace(text))
                return text;

            if (value is JsonElement jsonElement)
                return jsonElement.ToString();

            if (value is not string)
                return value.ToString();
        }

        return null;
    }

    private async ValueTask<AIAgent> CreateAgentAsync(Guid? agentDefinitionId, CancellationToken ct)
    {
        // Structured output is applied via AgentRunOptions at runtime, not here.
        // Prefer the caller-supplied definition id (the actual WorkflowStep.AgentDefinitionId
        // driving this run) so a per-agent provider override resolves correctly; AgentId (set
        // once at DI-registration time) is only a fallback for callers that don't have one.
        IChatClient chatClient = await _chatClients.GetAsync(AgentType, agentDefinitionId ?? AgentId, ct);
        return chatClient.AsAIAgent(
            instructions: SystemPrompt,
            name: Name,
            tools: _tools.Cast<AITool>().ToList());
    }

    private static string BuildSystemPrompt(string basePrompt, Type? outputSchemaType)
    {
        if (outputSchemaType == null)
            return basePrompt;

        const string instruction = """

        ## Output Contract (Mandatory)
        - Return ONLY a single valid JSON object matching the configured schema.
        - Do not include markdown, code fences, commentary, explanations, or extra text.
        - Do not wrap JSON in backticks.
        - Every field must conform to the schema's expected shape and types.
        """;

        if (basePrompt.Contains("## Output Contract (Mandatory)", StringComparison.Ordinal))
            return basePrompt;

        return $"{basePrompt}\n{instruction}";
    }

    /// <summary>
    /// Generates a human-readable JSON schema documentation string for UI display.
    /// The actual schema enforcement is handled by ChatResponseFormat.ForJsonSchema at runtime.
    /// </summary>
    private static string GenerateJsonSchemaDocumentation(Type type)
    {
        try
        {
            var properties = new Dictionary<string, object>();

            // Use reflection to extract properties from the output schema type
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var propSchema = new Dictionary<string, object>
                {
                    { "type", GetJsonType(prop.PropertyType) }
                };

                // Add description if available from XML docs or attributes
                var descAttr = prop.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
                    .FirstOrDefault() as System.ComponentModel.DescriptionAttribute;
                if (descAttr != null)
                {
                    propSchema["description"] = descAttr.Description;
                }

                // Handle collections
                if (IsCollection(prop.PropertyType, out var elementType))
                {
                    propSchema["type"] = "array";
                    if (elementType != null && !IsSimpleType(elementType))
                    {
                        // Recursively generate schema for complex array elements
                        var itemsJson = GenerateJsonSchemaDocumentation(elementType);
                        var itemsSchema = JsonSerializer.Deserialize<Dictionary<string, object>>(itemsJson);
                        if (itemsSchema != null)
                        {
                            propSchema["items"] = itemsSchema;
                        }
                    }
                    else if (elementType != null)
                    {
                        propSchema["items"] = new Dictionary<string, object>
                        {
                            { "type", GetJsonType(elementType) }
                        };
                    }
                }
                // Handle nested objects
                else if (!IsSimpleType(prop.PropertyType))
                {
                    var nestedJson = GenerateJsonSchemaDocumentation(prop.PropertyType);
                    var nestedSchema = JsonSerializer.Deserialize<Dictionary<string, object>>(nestedJson);
                    if (nestedSchema != null && nestedSchema.ContainsKey("properties"))
                    {
                        propSchema["properties"] = nestedSchema["properties"];
                    }
                }

                properties[prop.Name] = propSchema;
            }

            var schema = new Dictionary<string, object>
            {
                { "type", "object" },
                { "schemaType", type.FullName ?? type.Name },
                { "properties", properties }
            };

            return JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"{{\"error\": \"Failed to generate schema documentation: {ex.Message}\"}}";
        }
    }

    private static string GetJsonType(Type type)
    {
        // Handle nullable types
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        if (underlyingType == typeof(string))
            return "string";
        if (underlyingType == typeof(int) || underlyingType == typeof(long) ||
            underlyingType == typeof(short) || underlyingType == typeof(byte))
            return "integer";
        if (underlyingType == typeof(float) || underlyingType == typeof(double) ||
            underlyingType == typeof(decimal))
            return "number";
        if (underlyingType == typeof(bool))
            return "boolean";
        if (IsCollection(type, out _))
            return "array";

        return "object";
    }

    private static bool IsSimpleType(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        return underlyingType.IsPrimitive ||
               underlyingType == typeof(string) ||
               underlyingType == typeof(decimal) ||
               underlyingType == typeof(DateTime) ||
               underlyingType == typeof(DateTimeOffset) ||
               underlyingType == typeof(Guid);
    }

    private static bool IsCollection(Type type, out Type? elementType)
    {
        elementType = null;

        if (type.IsArray)
        {
            elementType = type.GetElementType();
            return true;
        }

        if (type.IsGenericType)
        {
            var genericDef = type.GetGenericTypeDefinition();
            if (genericDef == typeof(List<>) ||
                genericDef == typeof(IList<>) ||
                genericDef == typeof(ICollection<>) ||
                genericDef == typeof(IEnumerable<>))
            {
                elementType = type.GetGenericArguments()[0];
                return true;
            }
        }

        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type) && type != typeof(string))
        {
            // Try to get element type from IEnumerable<T>
            var enumerableInterface = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            if (enumerableInterface != null)
            {
                elementType = enumerableInterface.GetGenericArguments()[0];
                return true;
            }
        }

        return false;
    }
}
