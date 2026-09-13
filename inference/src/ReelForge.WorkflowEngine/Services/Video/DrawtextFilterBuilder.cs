using System.Globalization;
using ReelForge.Shared.Workflows;

namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// A single fully-resolved motion-graphics overlay, ready to be rendered into ffmpeg filter
/// syntax by <see cref="DrawtextFilterBuilder"/>. Every field here is already a safe, concrete
/// value — sanitized text (<see cref="OverlayTextSanitizer"/>), server-resolved output-timeline
/// seconds (<c>VideoCompileStepExecutor.MapSourceWindowToOutput</c>), and a fixed enum word for
/// duration/emphasis already mapped to milliseconds/style by the caller. The model never supplied
/// any of the geometry/timing here directly.
/// </summary>
public sealed record ResolvedOverlay(
    string PlacementId,
    string Kind,
    string SanitizedText,
    string SanitizedSubtext,
    int DurationMs,
    string Emphasis,
    double OutputStartSec,
    double OutputEndSec,
    VideoAnalysisRect Rect,
    string TextColor);

/// <summary>
/// Builds the drawbox/drawtext filter-chain fragment appended after the existing select/setpts
/// cut stage when <c>VideoCompileStepConfig.EnableGraphics</c> is on (see docs/video-editing.md
/// "Motion graphics (Phase 3)"). Pure — no I/O, no ffmpeg — the caller writes each overlay's
/// sanitized text to its own scratch file (see <see cref="MainTextSlot"/>/<see cref="SubtextSlot"/>)
/// BEFORE invoking this, and only ever passes this builder a <c>textFilePathForIndex</c> callback
/// to resolve those paths into the filter string.
/// </summary>
/// <remarks>
/// <b>Text never appears in the returned filter string.</b> Each drawtext filter references its
/// text via <c>textfile=</c> (a scratch file path — a path is not the text itself), never via an
/// inline <c>text=</c> value, and every drawtext filter also carries <c>expansion=none</c> to
/// disable drawtext's own <c>%{...}</c> expansion syntax as defense-in-depth. All geometry/timing
/// numbers are computed here in C# from already-validated inputs (a probed frame size, a resolved
/// output-timeline window, a normalized safe-zone rect) and formatted via
/// <see cref="FfmpegArgvFormat.Number(double)"/> — culture-invariant, exactly like
/// <c>VideoCompileStepExecutor.EncodeReencodeAsync</c>'s own <c>BetweenTerms()</c> helper.
/// </remarks>
public static class DrawtextFilterBuilder
{
    /// <summary>The scratch-file "slot" index for overlay <paramref name="overlayIndex"/>'s main text file — the single source of truth both this builder and its caller must use.</summary>
    public static int MainTextSlot(int overlayIndex) => overlayIndex * 2;

    /// <summary>The scratch-file "slot" index for overlay <paramref name="overlayIndex"/>'s subtext file (only used when <see cref="ResolvedOverlay.SanitizedSubtext"/> is non-empty).</summary>
    public static int SubtextSlot(int overlayIndex) => overlayIndex * 2 + 1;

    /// <summary>
    /// Builds the filter-chain fragment for <paramref name="overlays"/> (must be non-empty — the
    /// caller keeps the existing zero-overlay label plumbing unchanged when there is nothing to
    /// draw). Starts from <paramref name="baseFilterChainEndLabel"/> (the cut stage's own output
    /// label, e.g. <c>"[vcut]"</c>) and ends at the fixed label <c>"[vout]"</c>, which the
    /// caller's existing <c>-map "[vout]"</c> continues to reference unchanged.
    /// </summary>
    public static string BuildFilterChain(
        string baseFilterChainEndLabel,
        IReadOnlyList<ResolvedOverlay> overlays,
        int probedWidth,
        int probedHeight,
        int fontSizePct,
        int fadeMs,
        string fontColor,
        string boxColor,
        string fontFilePath,
        Func<int, string> textFilePathForIndex)
    {
        if (overlays.Count == 0)
            throw new ArgumentException("BuildFilterChain requires at least one overlay.", nameof(overlays));

        int fontSize = ComputeFontSize(probedHeight, fontSizePct);
        int subFontSize = Math.Max(10, (int)Math.Round(fontSize * 0.7));
        double fadeSec = Math.Max(0.01, fadeMs) / 1000.0;

        List<string> segments = new();
        string currentLabel = baseFilterChainEndLabel;

        for (int i = 0; i < overlays.Count; i++)
        {
            ResolvedOverlay overlay = overlays[i];
            bool isLast = i == overlays.Count - 1;
            bool hasSubtext = overlay.SanitizedSubtext.Length > 0;

            (int boxX, int boxY, int boxW, int boxH) = ComputeBoxPixels(overlay.Rect, probedWidth, probedHeight);
            string start = FfmpegArgvFormat.Number(overlay.OutputStartSec);
            string end = FfmpegArgvFormat.Number(overlay.OutputEndSec);
            string enableExpr = $"between(t,{start},{end})";
            string alphaExpr = BuildAlphaExpression(start, end, fadeSec);

            string boxLabel = $"[gfx{i}b]";
            segments.Add(
                $"{currentLabel}drawbox=x={boxX}:y={boxY}:w={boxW}:h={boxH}:color={boxColor}:t=fill:" +
                $"enable='{enableExpr}'{boxLabel}");

            string mainTextPath = textFilePathForIndex(MainTextSlot(i));
            string mainLabel = !hasSubtext && isLast ? "[vout]" : hasSubtext ? $"[gfx{i}t]" : $"[gfx{i}]";

            string mainY = hasSubtext
                ? $"{boxY}+({boxH}/2-text_h)/2"
                : $"{boxY}+({boxH}-text_h)/2";

            segments.Add(
                $"{boxLabel}drawtext=fontfile='{EscapeFilterPath(fontFilePath)}':" +
                $"textfile='{EscapeFilterPath(mainTextPath)}':expansion=none:fontsize={fontSize}:" +
                $"fontcolor={fontColor}:x={boxX}+({boxW}-text_w)/2:y={mainY}:" +
                $"alpha='{alphaExpr}':enable='{enableExpr}'{mainLabel}");

            currentLabel = mainLabel;

            if (hasSubtext)
            {
                string subTextPath = textFilePathForIndex(SubtextSlot(i));
                string subLabel = isLast ? "[vout]" : $"[gfx{i}]";
                string subY = $"{boxY}+{boxH}/2+({boxH}/2-text_h)/2";

                segments.Add(
                    $"{currentLabel}drawtext=fontfile='{EscapeFilterPath(fontFilePath)}':" +
                    $"textfile='{EscapeFilterPath(subTextPath)}':expansion=none:fontsize={subFontSize}:" +
                    $"fontcolor={fontColor}:x={boxX}+({boxW}-text_w)/2:y={subY}:" +
                    $"alpha='{alphaExpr}':enable='{enableExpr}'{subLabel}");

                currentLabel = subLabel;
            }
        }

        return string.Join(";", segments);
    }

    /// <summary><c>max(12, round(probedHeight * fontSizePct / 100))</c> — never below 12px regardless of a tiny/misconfigured percentage.</summary>
    internal static int ComputeFontSize(int probedHeight, int fontSizePct) =>
        Math.Max(12, (int)Math.Round(probedHeight * Math.Clamp(fontSizePct, 2, 12) / 100.0));

    internal static (int X, int Y, int W, int H) ComputeBoxPixels(VideoAnalysisRect rect, int probedWidth, int probedHeight)
    {
        int x = (int)Math.Round(rect.X * probedWidth);
        int y = (int)Math.Round(rect.Y * probedHeight);
        int w = (int)Math.Round(rect.W * probedWidth);
        int h = (int)Math.Round(rect.H * probedHeight);
        return (x, y, w, h);
    }

    /// <summary>
    /// A fixed fade-in/hold/fade-out template: 0 before <c>start</c>, linear ramp 0→1 over
    /// <paramref name="fadeSec"/>, held at 1, linear ramp 1→0 over the last <paramref name="fadeSec"/>
    /// before <c>end</c>, 0 after. All literals are pre-formatted, culture-invariant strings.
    /// </summary>
    internal static string BuildAlphaExpression(string start, string end, double fadeSec)
    {
        string fade = fadeSec.ToString(CultureInfo.InvariantCulture);
        return
            $"if(lt(t,{start}),0," +
            $"if(lt(t,{start}+{fade}),(t-{start})/{fade}," +
            $"if(lt(t,{end}-{fade}),1," +
            $"if(lt(t,{end}),({end}-t)/{fade},0))))";
    }

    /// <summary>
    /// Escapes ffmpeg filter-option-value metacharacters (<c>:</c>, <c>'</c>, <c>\</c>) in a
    /// PATH — never in overlay text, which never appears here at all. Paths originate from our
    /// own scratch-space naming, never from model output, but are escaped anyway since a project
    /// name or execution id could in principle contain one of these characters on some platform.
    /// </summary>
    internal static string EscapeFilterPath(string path) =>
        path.Replace("\\", "\\\\").Replace(":", "\\:").Replace("'", "\\'");
}
