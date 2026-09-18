using System.Text;
using System.Text.Json;

namespace ReelForge.WorkflowEngine.Execution.Context;

/// <summary>
/// Caps applied by <see cref="JsonOutputDigest.Digest"/> — kept as its own value type so
/// <see cref="Execution.Context.AgentInputBudget"/> can build one from
/// <see cref="AgentInputBudgetOptions"/> without the digest itself depending on the options type
/// (the digest is reusable/pure on its own; only <see cref="AgentInputBudget"/> knows about
/// step budgets).
/// </summary>
public readonly record struct JsonDigestSettings(int MaxOutputChars, int MaxStringChars, int MaxArrayItems, int MaxDepth);

/// <summary>
/// Deterministic, non-LLM shrinker for one step's output text, used by
/// <see cref="Execution.Context.AgentInputBudget"/> once the overall prompt budget is exceeded.
/// Pure and static — no I/O, no randomness, same input always yields the same output.
///
/// <para>
/// Two shrink strategies, chosen automatically:
/// </para>
/// <list type="bullet">
/// <item>the input parses as JSON: rewrite it structurally (truncate long strings, cap array
/// length, cut off past a depth limit) so the RESULT IS STILL VALID JSON — this is the common
/// case, since almost everything flowing through this pipeline is a step's structured JSON
/// output;</item>
/// <item>the input is not JSON (or rewriting it still doesn't fit): fall back to a middle-out
/// text truncation that keeps the start and end of the string, which is where the most
/// orienting content usually lives, and drops the middle.</item>
/// </list>
///
/// <para>
/// Callers that must never see a digested output at all (a machine-consumed contract like a
/// <c>{view, meta}</c> envelope on <c>AgentInputContextMode.PreviousStepOnly</c>, the most recent
/// N steps under <see cref="AgentInputBudgetOptions.RecentStepsVerbatim"/>, or a
/// <c>CustomMappedSubset</c> mapping) must simply never call this type — see
/// <see cref="Execution.Context.AgentInputBudget"/>'s doc comment for the full list.
/// </para>
/// </summary>
public static class JsonOutputDigest
{
    private const string DepthOmittedMarker = "…(nested content omitted by context budget)";

    /// <summary>
    /// Returns <paramref name="output"/> unchanged when it already fits
    /// <paramref name="settings"/>.<c>MaxOutputChars</c> (this is the byte-identical fast path
    /// that keeps every already-small step untouched). Otherwise shrinks it — see the type's
    /// doc comment for the two strategies. Never throws: any unexpected failure while parsing or
    /// rewriting the JSON falls back to the middle-out text truncation, which cannot itself throw
    /// for a finite input.
    /// </summary>
    public static string Digest(string output, JsonDigestSettings settings)
    {
        if (string.IsNullOrEmpty(output) || output.Length <= settings.MaxOutputChars)
            return output;

        try
        {
            using JsonDocument document = JsonDocument.Parse(output);
            string rewritten = RewriteDigested(document.RootElement, settings);
            if (rewritten.Length <= settings.MaxOutputChars)
                return rewritten;

            // The structural rewrite alone did not get under budget (e.g. many small-enough
            // strings/arrays that individually pass their caps but still add up) — fall through
            // to the same middle-out text truncation non-JSON input gets, applied to the
            // rewritten (already-shrunk) text so we are truncating as little as possible.
            return MiddleOutTruncate(rewritten, settings.MaxOutputChars);
        }
        catch
        {
            // Not JSON, or something about this document trips up the writer (e.g. a JSON value
            // kind future .NET adds) — never let a digest failure fail the step it is shrinking
            // context for. Middle-out on the original raw text is always safe.
            return MiddleOutTruncate(output, settings.MaxOutputChars);
        }
    }

    private static string RewriteDigested(JsonElement root, JsonDigestSettings settings)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            WriteElement(writer, root, settings, depth: 0);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteElement(Utf8JsonWriter writer, JsonElement element, JsonDigestSettings settings, int depth)
    {
        if (depth > settings.MaxDepth && (element.ValueKind == JsonValueKind.Object || element.ValueKind == JsonValueKind.Array))
        {
            writer.WriteStringValue(DepthOmittedMarker);
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    WriteElement(writer, property.Value, settings, depth + 1);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                int total = element.GetArrayLength();
                int written = 0;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (written >= settings.MaxArrayItems)
                    {
                        writer.WriteStringValue($"…(+{total - settings.MaxArrayItems} more items omitted by context budget)");
                        break;
                    }

                    WriteElement(writer, item, settings, depth + 1);
                    written++;
                }
                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                string value = element.GetString() ?? string.Empty;
                if (value.Length > settings.MaxStringChars)
                {
                    int omitted = value.Length - settings.MaxStringChars;
                    writer.WriteStringValue($"{value[..settings.MaxStringChars]}…(+{omitted} chars)");
                }
                else
                {
                    writer.WriteStringValue(value);
                }
                break;

            case JsonValueKind.Number:
                // WriteRawValue preserves the source's exact textual form (e.g. "1.50" stays
                // "1.50" rather than round-tripping through a decimal/double), matching the
                // "preserve number values exactly" requirement.
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                writer.WriteBooleanValue(element.GetBoolean());
                break;

            case JsonValueKind.Null:
            default:
                writer.WriteNullValue();
                break;
        }
    }

    /// <summary>
    /// Keeps the first ~60% and last ~40% of <paramref name="maxChars"/>, joined by a marker that
    /// states how many characters were dropped. Used for non-JSON text and as JSON's own
    /// last-resort fallback. The marker's own length is folded back into the budget in a second
    /// pass so the final string's length never exceeds <paramref name="maxChars"/> (once
    /// <paramref name="maxChars"/> is at least the marker's length).
    /// </summary>
    private static string MiddleOutTruncate(string text, int maxChars)
    {
        if (maxChars <= 0)
            return string.Empty;
        if (text.Length <= maxChars)
            return text;

        (int keepFirst, int keepLast, string marker) = ComputeMiddleOutSplit(text.Length, maxChars);

        string head = text[..Math.Min(keepFirst, text.Length)];
        string tail = keepLast > 0 ? text[Math.Max(0, text.Length - keepLast)..] : string.Empty;
        return head + marker + tail;
    }

    private static (int KeepFirst, int KeepLast, string Marker) ComputeMiddleOutSplit(int totalLength, int maxChars)
    {
        // The marker text's own length depends on the omitted-character count, which depends on
        // how much we keep — a small chicken-and-egg problem. One refinement pass (using a
        // provisional marker to get a close omitted-count, then rebuilding the marker from the
        // resulting split) converges in practice because the omitted count barely moves between
        // the two passes, and this stays fully deterministic either way.
        int provisionalOmitted = Math.Max(0, totalLength - maxChars);
        string marker = BuildMiddleOutMarker(provisionalOmitted);
        (int keepFirst, int keepLast) = SplitAvailable(totalLength, maxChars, marker.Length);

        int actualOmitted = Math.Max(0, totalLength - keepFirst - keepLast);
        marker = BuildMiddleOutMarker(actualOmitted);
        (keepFirst, keepLast) = SplitAvailable(totalLength, maxChars, marker.Length);

        return (keepFirst, keepLast, marker);
    }

    private static (int KeepFirst, int KeepLast) SplitAvailable(int totalLength, int maxChars, int markerLength)
    {
        int available = Math.Max(0, Math.Min(totalLength, maxChars) - markerLength);
        int keepFirst = (int)(available * 0.6);
        int keepLast = available - keepFirst;
        return (keepFirst, keepLast);
    }

    private static string BuildMiddleOutMarker(int omittedChars) =>
        $"\n…[{omittedChars} characters omitted by context budget]…\n";
}
