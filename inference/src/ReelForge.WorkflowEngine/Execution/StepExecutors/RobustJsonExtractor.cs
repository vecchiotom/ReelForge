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

    /// <summary>
    /// Like <see cref="ExtractJsonObject"/> but returns the LAST top-level balanced <c>{...}</c>
    /// object in <paramref name="raw"/> instead of the first. Needed for a caller whose model
    /// reliably reasons extensively BEFORE emitting its real structured answer — observed live
    /// from <c>AgentType.VideoStoryEditor</c>: its chain-of-thought used small, complete, informal
    /// brace-pair shorthand (<c>"{s0,s0} and {s1,s1}"</c>, mid-reasoning notation for candidate
    /// spans) thousands of characters before the real final decision JSON at the very end of the
    /// response. <see cref="ExtractJsonObject"/>'s first-match strategy latches onto that
    /// incidental early object instead of the real answer; scanning for the last one that actually
    /// closes (tracking depth exactly like <see cref="ExtractJsonObject"/>, forward in one pass —
    /// scanning backward would need unreliable backward escape-sequence lookahead) finds the real
    /// answer regardless of how much brace-heavy reasoning precedes it. Returns null if no balanced
    /// object is found anywhere in <paramref name="raw"/>.
    /// </summary>
    public static string? ExtractLastJsonObject(string raw)
    {
        int depth = 0;
        bool inString = false;
        bool escape = false;
        int start = -1;
        string? lastComplete = null;

        for (int i = 0; i < raw.Length; i++)
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
            if (c == '{')
            {
                if (depth == 0) start = i;
                depth++;
            }
            else if (c == '}')
            {
                if (depth == 0) continue; // unmatched close outside any object — ignore
                depth--;
                if (depth == 0 && start >= 0)
                {
                    lastComplete = raw.Substring(start, i - start + 1);
                    start = -1;
                }
            }
        }

        return lastComplete;
    }

    /// <summary>
    /// Best-effort repair for near-miss JSON that uses single quotes as string delimiters —
    /// observed live from a local/open vision-language model that didn't honor a JSON-schema
    /// response format and instead emitted Python-dict-style output, e.g.
    /// <c>{'summary': 'a frame...'}</c>. Only ever meant to be tried AFTER a strict
    /// <see cref="System.Text.Json.JsonSerializer"/> parse of the raw text has already failed —
    /// never applied to output that already parses, since it is a lossy best-effort transform, not
    /// a general JSON5/relaxed parser.
    ///
    /// <para>
    /// Walks the text once, treating BOTH <c>'</c> and <c>"</c> as valid string delimiters
    /// (whichever opens a given string) and re-emitting every string canonicalized to
    /// double-quoted JSON: a bare <c>"</c> found inside a single-quoted string is escaped (it must
    /// be, now that <c>"</c> is the outer delimiter), an escaped delimiter (<c>\'</c> inside a
    /// single-quoted string, or <c>\"</c> inside a double-quoted one) becomes a bare character
    /// inside the new double-quoted string except where that character is itself <c>"</c> (which
    /// must stay escaped), and every other backslash escape (<c>\\</c>, <c>\n</c>, <c>\uXXXX</c>,
    /// ...) is copied through verbatim since it's already valid JSON escape syntax. Structural
    /// characters outside any string (braces, brackets, colons, commas, numbers, literals,
    /// whitespace) are copied through unchanged.
    /// </para>
    /// </summary>
    public static string? NormalizeQuotedStrings(string raw)
    {
        System.Text.StringBuilder sb = new(raw.Length + 16);
        int i = 0;
        while (i < raw.Length)
        {
            char c = raw[i];
            if (c == '\'' || c == '"')
            {
                char quote = c;
                i++;
                sb.Append('"');
                while (true)
                {
                    if (i >= raw.Length) return null; // unterminated string — give up

                    char sc = raw[i];
                    if (sc == '\\' && i + 1 < raw.Length)
                    {
                        char next = raw[i + 1];
                        if (next == quote)
                        {
                            // The escaped delimiter itself — becomes a bare character in the
                            // new double-quoted string, except a literal double-quote must stay
                            // escaped (it's the new delimiter).
                            sb.Append(next == '"' ? "\\\"" : next.ToString());
                        }
                        else
                        {
                            // Any other valid JSON escape (\\, \n, \t, \uXXXX, ...) — copy the
                            // backslash AND the character through verbatim.
                            sb.Append(sc).Append(next);
                        }
                        i += 2;
                        continue;
                    }

                    if (sc == quote)
                    {
                        sb.Append('"');
                        i++;
                        break;
                    }

                    if (sc == '"')
                    {
                        // A bare double-quote inside a single-quoted string must be escaped now
                        // that the canonicalized string is itself double-quoted.
                        sb.Append("\\\"");
                        i++;
                        continue;
                    }

                    sb.Append(sc);
                    i++;
                }
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Best-effort repair for a model response that opens with a DOUBLED brace —
    /// <c>{{"summary": ...}</c> instead of <c>{"summary": ...}</c> — but only ever emits one
    /// matching close, so <see cref="ExtractJsonObject"/>'s balanced-brace scan (which correctly
    /// treats the second <c>{</c> as opening a real nested object) never sees depth return to 0 and
    /// gives up. Observed live and repeatedly from a vLLM-served vision model: a stray extra
    /// opening brace, always at the very start, always exactly one extra, never anywhere else in
    /// the response and never doubled at the close. Safe to collapse unconditionally for THIS
    /// schema specifically — <c>VideoShotCaption</c> (the only caller) has no nested-object-valued
    /// property, so its root JSON object structurally never needs more than one leading <c>{</c>;
    /// a real, intentional nested object would need this repair to leave it alone, which is why
    /// this is a narrow fix for one caller's schema, not a general "collapse repeated braces"
    /// utility.
    /// </summary>
    public static string CollapseDuplicateLeadingBrace(string raw)
    {
        int i = 0;
        while (i < raw.Length && char.IsWhiteSpace(raw[i])) i++;

        int braceRunEnd = i;
        while (braceRunEnd < raw.Length && raw[braceRunEnd] == '{') braceRunEnd++;

        int extraBraces = braceRunEnd - i - 1; // 1 real opening brace is always kept
        if (extraBraces <= 0) return raw;

        return raw[..i] + "{" + raw[braceRunEnd..];
    }
}
