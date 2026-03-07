using System.Reflection;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ReelForge.Shared.Data.Models;

namespace ReelForge.WorkflowEngine.Agents;

public abstract class ReelForgeAgentBase : IReelForgeAgent
{
    private readonly IChatClient _chatClient;
    private readonly List<AIFunction> _tools;
    private readonly Type? _outputSchemaType;

    protected ReelForgeAgentBase(
        IChatClient chatClient,
        IConfiguration configuration,
        string name,
        string description,
        AgentType agentType,
        string defaultSystemPrompt,
        IEnumerable<AIFunction>? tools = null,
        Guid? agentId = null,
        Type? outputSchemaType = null)
    {
        _chatClient = chatClient;
        _outputSchemaType = outputSchemaType;
        Name = name;
        Description = description;
        AgentType = agentType;
        AgentId = agentId;
        _tools = tools?.ToList() ?? new List<AIFunction>();

        string configKey = $"Agents:{name}:SystemPrompt";
        SystemPrompt = configuration[configKey] ?? defaultSystemPrompt;

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
    public AIAgent AIAgent => CreateAgent();
    public string? OutputSchemaJson { get; }
    public Type? OutputSchemaType => _outputSchemaType;

    public async Task<AgentRunResult> RunAsync(string prompt, CancellationToken ct = default)
    {
        // Structured output is enforced via ChatResponseFormat.ForJsonSchema<T>() when OutputSchemaType is specified
        AIAgent agent = CreateAgent();

        AgentResponse agentResponse;

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

                    agentResponse = await agent.RunAsync(prompt, options: runOptions, cancellationToken: ct);
                }
                else
                {
                    // Fallback if reflection fails
                    agentResponse = await agent.RunAsync(prompt, cancellationToken: ct);
                }
            }
            else
            {
                // Fallback if reflection fails
                agentResponse = await agent.RunAsync(prompt, cancellationToken: ct);
            }
        }
        else
        {
            // Fallback for agents without structured output schema
            agentResponse = await agent.RunAsync(prompt, cancellationToken: ct);
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

        return new AgentRunResult
        {
            Output = output,
            TokensUsed = totalTokens,
            InputTokens = inputTokens,
            OutputTokens = outputTokens
        };
    }

    private AIAgent CreateAgent()
    {
        // Create agent with standard parameters - structured output is applied via AgentRunOptions at runtime
        return _chatClient.AsAIAgent(
            instructions: SystemPrompt,
            name: Name,
            tools: _tools.Cast<AITool>().ToList());
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
