using System.Text.Json;
using NCalc;

namespace ReelForge.WorkflowEngine.Execution;

/// <summary>
/// Evaluates condition expressions against JSON data using NCalc.
/// Supports dot-notation paths like $.score, $.result.items, etc.
/// </summary>
public class ExpressionEvaluator
{
    /// <summary>
    /// Evaluates a condition expression against JSON output.
    /// Returns true if the expression evaluates to true.
    /// </summary>
    public static bool Evaluate(string expression, string jsonOutput)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return true;

        Dictionary<string, object?> parameters = ExtractParameters(jsonOutput);

        // Replace $.xxx references with parameter names
        string ncalcExpression = expression;
        foreach (var (key, _) in parameters)
        {
            ncalcExpression = ncalcExpression.Replace($"$.{key}", $"[{key}]");
        }

        // Also handle AND/OR (NCalc uses &&/||, but we support both)
        ncalcExpression = ncalcExpression
            .Replace(" AND ", " && ")
            .Replace(" OR ", " || ")
            .Replace(" and ", " && ")
            .Replace(" or ", " || ");

        try
        {
            Expression e = new(ncalcExpression);

            foreach (var (key, value) in parameters)
            {
                e.Parameters[key] = value;
            }

            object? result = e.Evaluate();
            return result is true or (int)1 or 1.0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Extracts a value from JSON output using a dot-notation path like "score" or "result.items".
    /// </summary>
    public static string? ExtractJsonValue(string jsonOutput, string path)
    {
        if (string.IsNullOrWhiteSpace(jsonOutput) || string.IsNullOrWhiteSpace(path))
            return null;

        // Remove $. prefix if present
        if (path.StartsWith("$."))
            path = path[2..];

        try
        {
            using JsonDocument doc = JsonDocument.Parse(jsonOutput);
            JsonElement current = doc.RootElement;

            foreach (string segment in path.Split('.'))
            {
                if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(segment, out JsonElement next))
                {
                    current = next;
                }
                else
                {
                    return null;
                }
            }

            return current.ValueKind == JsonValueKind.String
                ? current.GetString()
                : current.GetRawText();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts array elements from JSON output using a dot-notation path.
    /// </summary>
    public static List<string> ExtractJsonArray(string jsonOutput, string path)
    {
        string? value = ExtractJsonValue(jsonOutput, path);
        if (value == null) return new List<string>();

        try
        {
            using JsonDocument doc = JsonDocument.Parse(value);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                return doc.RootElement.EnumerateArray()
                    .Select(e => e.GetRawText())
                    .ToList();
            }
        }
        catch { }

        return new List<string>();
    }

    private static Dictionary<string, object?> ExtractParameters(string jsonOutput)
    {
        var parameters = new Dictionary<string, object?>();
        if (string.IsNullOrWhiteSpace(jsonOutput)) return parameters;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(jsonOutput);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                ExtractFromElement(doc.RootElement, "", parameters);
            }
        }
        catch { }

        return parameters;
    }

    private static void ExtractFromElement(JsonElement element, string prefix, Dictionary<string, object?> parameters)
    {
        foreach (JsonProperty prop in element.EnumerateObject())
        {
            string key = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";

            switch (prop.Value.ValueKind)
            {
                case JsonValueKind.Number:
                    parameters[key] = prop.Value.TryGetInt32(out int intVal) ? intVal : prop.Value.GetDouble();
                    break;
                case JsonValueKind.String:
                    parameters[key] = prop.Value.GetString();
                    break;
                case JsonValueKind.True:
                    parameters[key] = true;
                    break;
                case JsonValueKind.False:
                    parameters[key] = false;
                    break;
                case JsonValueKind.Null:
                    parameters[key] = null;
                    break;
                case JsonValueKind.Object:
                    ExtractFromElement(prop.Value, key, parameters);
                    break;
                case JsonValueKind.Array:
                    parameters[key] = prop.Value.GetRawText();
                    break;
            }
        }
    }
}
