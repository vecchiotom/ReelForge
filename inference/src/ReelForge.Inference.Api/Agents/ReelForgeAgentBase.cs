using System.Reflection;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ReelForge.Shared.Data.Models;

namespace ReelForge.Inference.Api.Agents;

/// <summary>
/// Abstract base class for ReelForge agents in the API service.
/// </summary>
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
        Type? outputSchemaType = null)
    {
        _chatClient = chatClient;
        _outputSchemaType = outputSchemaType;
        Name = name;
        Description = description;
        AgentType = agentType;
        _tools = tools?.ToList() ?? new List<AIFunction>();

        string configKey = $"Agents:{name}:SystemPrompt";
        SystemPrompt = BuildSystemPrompt(
            configuration[configKey] ?? defaultSystemPrompt,
            _outputSchemaType);

        if (_outputSchemaType != null)
        {
            OutputSchemaJson = GenerateJsonSchemaDocumentation(_outputSchemaType);
        }
    }

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
        AIAgent agent = CreateAgent();

        AgentResponse agentResponse;
        if (_outputSchemaType != null)
        {
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
                    var runOptions = new AgentRunOptions { ResponseFormat = responseFormat };
                    agentResponse = await agent.RunAsync(prompt, options: runOptions, cancellationToken: ct);
                }
                else
                {
                    agentResponse = await agent.RunAsync(prompt, cancellationToken: ct);
                }
            }
            else
            {
                agentResponse = await agent.RunAsync(prompt, cancellationToken: ct);
            }
        }
        else
        {
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
        return _chatClient.AsAIAgent(
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

    private static string GenerateJsonSchemaDocumentation(Type type)
    {
        try
        {
            var properties = new Dictionary<string, object>();

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var propSchema = new Dictionary<string, object>
                {
                    { "type", GetJsonType(prop.PropertyType) }
                };

                if (IsCollection(prop.PropertyType, out var elementType))
                {
                    propSchema["type"] = "array";
                    if (elementType != null)
                    {
                        propSchema["items"] = new Dictionary<string, object>
                        {
                            { "type", GetJsonType(elementType) }
                        };
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
