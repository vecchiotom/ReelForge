using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ReelForge.WorkflowEngine.Execution.Caching;

/// <inheritdoc cref="IStepCacheKeyBuilder"/>
public sealed class StepCacheKeyBuilder : IStepCacheKeyBuilder
{
    /// <summary>
    /// Hashes every hash-eligible field of <paramref name="inputs"/> (everything except
    /// <see cref="StepCacheKeyInputs.WorkflowDefinitionId"/> — see that record's doc comment) as
    /// a length-prefixed <c>name=value</c> block, one per line, then SHA-256s the UTF-8 bytes of
    /// the whole thing.
    ///
    /// <para>
    /// <b>Why length-prefixed, not just newline-separated.</b> A plain <c>"{name}={value}\n"</c>
    /// join is NOT collision-safe: two different (name, value) field sets can concatenate to the
    /// exact same byte string if a value happens to contain the separator characters (a step's
    /// system prompt or JSON config can contain <c>=</c> or <c>\n</c> freely). Prefixing both the
    /// name and the value with their own byte length makes the framing unambiguous regardless of
    /// what either contains — two different field sets can never concatenate to the same string,
    /// which is exactly what the two-field concatenation-collision test in
    /// <c>StepCacheKeyBuilderTests</c> exercises (e.g. <c>{A="ab", B="c"}</c> vs
    /// <c>{A="a", B="bc"}</c>).
    /// </para>
    ///
    /// <para><b>Null handling.</b> A null field hashes as the literal <c>"\0null"</c> — a value no
    /// real string can ever equal (it embeds a NUL byte), so null is always distinguishable from
    /// any actual empty or non-empty string value.</para>
    /// </summary>
    public string Build(StepCacheKeyInputs inputs)
    {
        StringBuilder sb = new();

        AppendField(sb, "SchemaVersion", StepCacheKeyInputs.SchemaVersion);
        AppendField(sb, "ProjectId", inputs.ProjectId.ToString());
        AppendField(sb, "StepType", inputs.StepType.ToString());
        AppendField(sb, "AgentType", inputs.AgentType.ToString());
        AppendField(sb, "AgentDefinitionId", inputs.AgentDefinitionId.ToString());
        AppendField(sb, "AgentSystemPrompt", inputs.AgentSystemPrompt);
        AppendField(sb, "AgentOutputSchemaName", inputs.AgentOutputSchemaName);
        AppendField(sb, "AgentInferenceProviderId", inputs.AgentInferenceProviderId?.ToString());
        AppendField(sb, "AgentAssignedSkillsJson", inputs.AgentAssignedSkillsJson);
        AppendField(sb, "AgentInputContextMode", inputs.AgentInputContextMode.ToString());
        AppendField(sb, "SelectedPriorStepOrdersJson", inputs.SelectedPriorStepOrdersJson);
        AppendField(sb, "InputMappingJson", inputs.InputMappingJson);
        AppendField(sb, "ExtractConfigJson", inputs.ExtractConfigJson);
        AppendField(sb, "VideoAnalyzeConfigJson", inputs.VideoAnalyzeConfigJson);
        AppendField(sb, "VideoCompileConfigJson", inputs.VideoCompileConfigJson);
        AppendField(sb, "EditRoomConfigJson", inputs.EditRoomConfigJson);
        AppendField(sb, "GraphicsRoomConfigJson", inputs.GraphicsRoomConfigJson);
        AppendField(sb, "ColorGradeRoomConfigJson", inputs.ColorGradeRoomConfigJson);
        AppendField(sb, "ConditionExpression", inputs.ConditionExpression);
        AppendField(sb, "MaxIterations", inputs.MaxIterations.ToString(CultureInfo.InvariantCulture));
        AppendField(sb, "MinScore", inputs.MinScore?.ToString(CultureInfo.InvariantCulture));
        AppendField(sb, "ResolvedInput", inputs.ResolvedInput);
        AppendField(sb, "UserRequest", inputs.UserRequest);
        AppendField(sb, "ProjectFileFingerprint", inputs.ProjectFileFingerprint);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void AppendField(StringBuilder sb, string name, string? value)
    {
        string v = value ?? "\0null";
        sb.Append(name.Length).Append(':').Append(name)
          .Append('=')
          .Append(v.Length).Append(':').Append(v)
          .Append('\n');
    }
}
