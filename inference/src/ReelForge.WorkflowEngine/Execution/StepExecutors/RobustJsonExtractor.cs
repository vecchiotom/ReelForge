namespace ReelForge.WorkflowEngine.Execution.StepExecutors;

/// <summary>
/// Shared helper for extracting a balanced JSON object out of a model's raw completion text.
/// Hoisted out of <see cref="VideoCompileStepExecutor"/> (where it was originally added, and
/// remains used, for the story-editor decision JSON) so <see cref="ReviewLoopStepExecutor"/> can
/// apply the exact same hardening to <c>AgentType.VideoReviewAgent</c>'s output — that agent has
/// tools bound (see <c>AgentToolProvider</c>) just like the story editor, so it is exposed to the
/// identical failure mode: a reasoning-capable model (observed live on this stack) emitting valid
/// JSON followed by trailing prose, a markdown fence, or leaked tool-call-closing tokens once it
/// has any tools bound at all.
/// </summary>
internal static class RobustJsonExtractor
{
    /// <summary>
    /// Finds the first <c>{</c> in <paramref name="raw"/> and returns the substring through its
    /// matching balanced <c>}</c> (brace depth tracked with string-literal/escape awareness, so a
    /// <c>{</c>/<c>}</c> inside a quoted JSON string value never miscounts), discarding anything
    /// before or after. Returns null if no balanced object is found. Pure string scanning — no
    /// dependency on the JSON actually being well-formed beyond bracket balance, since the real
    /// validation happens wherever the caller deserializes/parses the extracted substring next.
    /// </summary>
    public static string? ExtractJsonObject(string raw)
    {
        int start = raw.IndexOf('{');
        if (start < 0) return null;

        int depth = 0;
        bool inString = false;
        bool escape = false;
        for (int i = start; i < raw.Length; i++)
        {
            char c = raw[i];
            if (inString)
            {
                if (escape) escape = false;
                else if (c == '\\') escape = true;
                else if (c == '"') inString = false;
                continue;
            }

            if (c == '"') { inString = true; continue; }
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return raw.Substring(start, i - start + 1);
            }
        }

        return null; // unbalanced — never seen depth return to 0
    }
}
