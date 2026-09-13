using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// Sanitizes model-authored overlay text (<see cref="ReelForge.Shared.Data.OutputSchemas.MotionGraphicsOverlay.Text"/>/
/// <c>Subtext</c>) before it can reach a scratch text file that <c>DrawtextFilterBuilder</c>
/// references via drawtext's <c>textfile=</c> option (see docs/video-editing.md "Motion graphics
/// (Phase 3)"). This is the FIRST of two independent layers of defense — the second is that the
/// text never appears in the ffmpeg filter STRING at all (only in a separately-written file, with
/// <c>expansion=none</c> also set on every drawtext filter) — but this sanitizer is written and
/// tested as if it were the only layer, since defense-in-depth only works when each layer holds
/// on its own.
/// </summary>
public static class OverlayTextSanitizer
{
    /// <summary>
    /// An ALLOWLIST — never a denylist/blocklist of "bad" characters, which is only ever safe
    /// against characters someone thought of. Letters, digits, spaces, and a small safe
    /// punctuation set.
    /// </summary>
    /// <remarks>
    /// Colon (<c>:</c>) and percent (<c>%</c>) are deliberately EXCLUDED from the punctuation set,
    /// even though this feature's architecture already neutralizes drawtext's own use of those
    /// characters (option separator, expansion syntax) — the text never enters the filter STRING
    /// at all (see <c>DrawtextFilterBuilder</c>'s <c>textfile=</c>/<c>expansion=none</c>
    /// discipline). Excluding them here anyway is defense-in-depth, not the primary safety
    /// mechanism: it means a future refactor that accidentally interpolated this text directly
    /// into a filter string could still not use it to terminate a drawtext option list or invoke
    /// an <c>%{eif:...}</c>/<c>%{pts}</c> expansion. Backslash is excluded outright (not part of
    /// any punctuation a video overlay plausibly needs). Emoji are also excluded — they fall
    /// outside <c>\p{L}</c>/<c>\p{N}</c> (Unicode Symbol/Other categories, not Letter/Number), and
    /// this is a deliberate choice, not an oversight: an overlay-safe font (see
    /// <c>VideoEditingOptions.FontFilePath</c>) is not guaranteed to carry emoji glyphs, so
    /// admitting them risks silently rendering tofu boxes instead of the intended character.
    /// </remarks>
    private static readonly Regex AllowedCharsPattern = new(
        @"[^\p{L}\p{N} .,!?'""()\-—;/&#@+]", RegexOptions.Compiled);

    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// NFC-normalizes, strips to the allowlist above, collapses all whitespace (including
    /// newlines/tabs — drawtext treats a raw newline as a forced line break) to single spaces,
    /// trims, and truncates to <paramref name="maxChars"/> without splitting a multi-byte
    /// grapheme (surrogate pair or combining-mark sequence). Returns <see cref="string.Empty"/>
    /// when the result is empty or whitespace-only after sanitization — the caller then drops
    /// that overlay/line entirely rather than emitting empty drawtext content.
    /// </summary>
    public static string Sanitize(string? raw, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        string normalized = raw.Normalize(NormalizationForm.FormC);

        // Collapse ALL whitespace (including newlines/tabs — drawtext treats a raw newline as a
        // forced line break) to single spaces BEFORE the allowlist strip below, so a newline
        // becomes a space rather than being silently deleted (which would wrongly glue two words
        // together, e.g. "Line one\nLine two" -> "Line oneLine two").
        string spaceCollapsed = WhitespacePattern.Replace(normalized, " ").Trim();
        string allowlisted = AllowedCharsPattern.Replace(spaceCollapsed, string.Empty);
        string collapsed = WhitespacePattern.Replace(allowlisted, " ").Trim();

        if (collapsed.Length == 0)
            return string.Empty;

        string truncated = TruncateByTextElements(collapsed, Math.Max(0, maxChars));
        string result = truncated.Trim();
        return result;
    }

    /// <summary>
    /// Truncates by Unicode text element (grapheme cluster, via <see cref="StringInfo"/>) rather
    /// than raw UTF-16 code unit, so a surrogate pair or a base-character-plus-combining-mark
    /// sequence is never split mid-character.
    /// </summary>
    private static string TruncateByTextElements(string value, int maxChars)
    {
        if (maxChars <= 0)
            return string.Empty;

        if (value.Length <= maxChars)
            return value;

        StringBuilder sb = new(maxChars);
        TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(value);
        while (enumerator.MoveNext())
        {
            string element = (string)enumerator.Current;
            if (sb.Length + element.Length > maxChars)
                break;

            sb.Append(element);
        }

        return sb.ToString();
    }
}
