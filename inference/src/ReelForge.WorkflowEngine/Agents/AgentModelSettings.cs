namespace ReelForge.WorkflowEngine.Agents;

/// <summary>
/// Per-agent sampling/reasoning defaults, supplied by each <see cref="ReelForgeAgentBase"/>
/// subclass's constructor and overridable per-deployment via <c>Agents:&lt;Name&gt;:Temperature</c>
/// / <c>:TopP</c> / <c>:TopK</c> / <c>:ReasoningEffort</c> config keys (same override pattern as
/// <c>Agents:&lt;Name&gt;:SystemPrompt</c> — appsettings.json, or a Docker Compose
/// <c>Agents__&lt;Name&gt;__ReasoningEffort</c> env var, which only needs a container restart, not
/// a rebuild).
/// </summary>
/// <param name="Temperature">Left null to inherit whatever the resolved provider/model defaults to.</param>
/// <param name="TopP">Left null to inherit the provider/model default.</param>
/// <param name="TopK">Left null to inherit the provider/model default.</param>
/// <param name="ReasoningEffort">
/// See <see cref="ReelForgeAgentBase"/>'s <c>ValidReasoningEfforts</c> for the exact accepted
/// vocabulary and why it is deployment-specific, not the generic OpenAI one. Left null to inherit
/// whatever the resolved provider's own server-side default is (may itself enable or disable
/// reasoning, depending on how that provider is configured).
/// </param>
public sealed record AgentModelSettings(
    float? Temperature = null,
    float? TopP = null,
    int? TopK = null,
    string? ReasoningEffort = null);
