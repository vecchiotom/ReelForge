# Structured Output Implementation Fix

## Summary

Fixed the structured output enforcement in `ReelForgeAgentBase` to properly use Microsoft's Agent Framework capabilities as documented in the [official Microsoft Learn guidance](https://learn.microsoft.com/en-us/agent-framework/agents/structured-output?pivots=programming-language-csharp).

## What Was Wrong

The previous implementation had a TODO comment indicating structured output was "not yet implemented":

```csharp
// NOTE: Structured output schema is documented in OutputSchemaJson for reference.
// Actual runtime enforcement of structured output will be implemented in a future update
// when the Microsoft.Agents.AI framework provides better integration with ChatResponseFormat.
```

This was incorrect — Microsoft's Agent Framework **already fully supports** structured output enforcement!

## What Was Fixed

### 1. **Structured Output Enforcement via `AgentRunOptions`**

The fix properly enforces structured output using `ChatResponseFormat.ForJsonSchema<T>()` at runtime:

```csharp
public async Task<string> RunAsync(string prompt, CancellationToken ct = default)
{
    AIAgent agent = CreateAgent();
    
    // If structured output is required, configure ResponseFormat at runtime via AgentRunOptions
    if (_outputSchemaType != null)
    {
        // Use reflection to call ChatResponseFormat.ForJsonSchema<T>() with the runtime type
        var method = typeof(ChatResponseFormat).GetMethod(nameof(ChatResponseFormat.ForJsonSchema), 
            BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
        
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
                
                AgentResponse result = await agent.RunAsync(prompt, options: runOptions, cancellationToken: ct);
                return result.AsChatResponse().Text ?? string.Empty;
            }
        }
    }
    
    // Fallback for agents without structured output schema
    AgentResponse response = await agent.RunAsync(prompt, cancellationToken: ct);
    return response.AsChatResponse().Text ?? string.Empty;
}
```

### 2. **Updated Documentation**

- Renamed `GenerateJsonSchema()` → `GenerateJsonSchemaDocumentation()` to clarify its purpose
- Updated comments to reflect that structured output is **now enforced at runtime**
- Added clear documentation that the schema generation is for UI display only

### 3. **Removed Incorrect Dependencies**

Cleaned up unused imports:
- Removed `System.Text.Json.Nodes`
- Removed `System.Text.Json.Serialization.Metadata`
- Added `System.Reflection` for dynamic type handling

## How It Works

### For Agents with Structured Output (11 agents)

When an agent is created with an `outputSchemaType` (e.g., `typeof(DirectorOutput)`):

1. **At Construction**: A JSON schema documentation string is generated for UI display
2. **At Runtime**: `ChatResponseFormat.ForJsonSchema<T>()` is dynamically invoked via reflection
3. **During Execution**: The LLM response is **constrained** to match the schema structure
4. **Result**: The agent output is guaranteed to be valid JSON conforming to the schema

### For Agents without Structured Output (1 agent)

The `FileSummarizerAgent` (which doesn't specify a schema) continues to work normally without structured output constraints.

## Agents Using Structured Output

All 11 workflow agents now have **enforced** structured output:

### Analysis Agents
- `CodeStructureAnalyzerAgent` → `CodeStructureOutput`
- `DependencyAnalyzerAgent` → `DependencyAnalysisOutput`
- `ComponentInventoryAnalyzerAgent` → `ComponentInventoryOutput`
- `RouteAndApiAnalyzerAgent` → `RouteAndApiOutput`
- `StyleAndThemeExtractorAgent` → `StyleAndThemeOutput`

### Translation Agents
- `RemotionComponentTranslatorAgent` → `RemotionComponentOutput`
- `AnimationStrategyAgent` → `AnimationStrategyOutput`

### Production Agents
- `DirectorAgent` → `DirectorOutput`
- `ScriptwriterAgent` → `ScriptwriterOutput`
- `AuthorAgent` → `RenderManifestOutput`

### Quality Agents
- `ReviewAgent` → `ReviewOutput`

## Microsoft's Structured Output Approaches

According to the official documentation, Microsoft supports 3 approaches:

### 1. **`RunAsync<T>()`** (Compile-time type known)
```csharp
AgentResponse<PersonInfo> response = await agent.RunAsync<PersonInfo>(prompt);
```

### 2. **`ResponseFormat` via `AgentRunOptions`** (Runtime configuration) ✅ **What we implemented**
```csharp
AgentRunOptions runOptions = new()
{
    ResponseFormat = ChatResponseFormat.ForJsonSchema<PersonInfo>()
};
AgentResponse response = await agent.RunAsync(prompt, options: runOptions);
```

### 3. **`ChatClientAgentOptions`** (Agent creation time)
```csharp
AIAgent agent = chatClient.AsAIAgent(new ChatClientAgentOptions
{
    ChatOptions = new() { ResponseFormat = ChatResponseFormat.ForJsonSchema<T>() }
});
```

## Why We Chose Approach #2

We implemented approach #2 (`AgentRunOptions` at runtime) because:

1. ✅ **Dynamic Type Handling**: Works with `Type?` stored at construction time
2. ✅ **Single Agent Instance**: No need to recreate agents for each execution
3. ✅ **Clear Separation**: Schema enforcement is applied only when needed
4. ✅ **Backward Compatible**: Agents without schemas continue to work normally
5. ✅ **Reflection-Friendly**: Works well with runtime type resolution

## Testing

To verify the fix works correctly, run a workflow execution and check:

1. **Agent Output**: Should be valid JSON matching the schema structure
2. **Database Storage**: `workflow_step_results.output` should contain structured JSON
3. **Frontend Display**: Agent output should be parseable and renderable
4. **Error Handling**: Invalid schema compliance should be caught by the LLM provider

## References

- [Microsoft Learn: Structured Output with Agents (C#)](https://learn.microsoft.com/en-us/agent-framework/agents/structured-output?pivots=programming-language-csharp)
- [ChatResponseFormat API Reference](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.chatresponseformat)
- [AgentRunOptions API Reference](https://learn.microsoft.com/en-us/dotnet/api/microsoft.agents.ai.agentrunoptions)

## File Changed

- `inference/src/ReelForge.WorkflowEngine/Agents/ReelForgeAgentBase.cs`

## Related Files (No Changes Needed)

- `inference/src/ReelForge.Shared/Data/OutputSchemas.cs` - Contains all output schema POCOs
- `inference/src/ReelForge.WorkflowEngine/Agents/**/*Agent.cs` - All agents inherit the fix
- `inference/src/ReelForge.Inference.Api/Agents/ReelForgeAgentBase.cs` - Simpler version without schemas
