namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// Builds the ffmpeg <c>overlay</c> filter-chain fragment for Phase 3's rendered-asset
/// motion-graphics overlays — a Remotion-authored, transparent-background clip
/// (<c>MotionGraphicsOverlay.RenderedAssetStorageKey</c>) composited onto the edited video, as a
/// parallel path alongside <see cref="DrawtextFilterBuilder"/>'s plain-text drawbox/drawtext path
/// (see docs/video-editing.md "Motion graphics (Phase 3)"). Pure — no I/O, no ffmpeg. The caller
/// has already downloaded and ffprobe-validated each asset to a local scratch file and added it as
/// its own extra <c>-i</c> input BEFORE building this filter string (see
/// <c>VideoCompileStepExecutor.ResolveGraphicsAsync</c>/<c>EncodeReencodeAsync</c>), and supplies
/// the resulting ffmpeg input index for each overlay via <paramref name="inputIndexForIndex"/> —
/// this builder never touches a file path or a storage key itself.
/// </summary>
/// <remarks>
/// Each asset is stretch-scaled to the resolved placement's compact ACCENT box (the same pixel
/// geometry <see cref="DrawtextFilterBuilder"/> computes for a text overlay at the same placement,
/// via the shared <see cref="DrawtextFilterBuilder.ComputeAccentBoxPixels"/> helper — deliberately
/// smaller than the named safe-zone band itself, see that method's doc comment) and time-shifted with
/// <c>setpts</c> so its own frame 0 lands at the overlay's actual on-screen start time on the
/// OUTPUT timeline — the filter graph's framesync otherwise runs every input's PTS from that
/// input's own t=0, which would show the wrong part (or nothing at all) of the asset during the
/// intended window. <c>eof_action=pass</c> means once the asset's own content runs out the
/// overlay simply stops contributing (reverts to the plain cut) rather than freezing on its last
/// frame for the remainder of a longer "Hold" window — a short animated graphic is expected to be
/// shorter than its on-screen window and should not appear to hang.
/// </remarks>
public static class OverlayAssetFilterBuilder
{
    /// <summary>
    /// Builds the filter-chain fragment for <paramref name="overlays"/> (must be non-empty and
    /// every entry must be <see cref="ResolvedOverlay.IsAssetOverlay"/> — the caller partitions
    /// text vs. asset overlays before calling either builder). Starts from
    /// <paramref name="baseFilterChainEndLabel"/> and ends at <paramref name="finalLabel"/>
    /// (default <c>"[vout]"</c>).
    /// </summary>
    public static string BuildFilterChain(
        string baseFilterChainEndLabel,
        IReadOnlyList<ResolvedOverlay> overlays,
        int probedWidth,
        int probedHeight,
        Func<int, int> inputIndexForIndex,
        string finalLabel = "[vout]",
        int boxHeightPct = 16,
        int boxWidthPct = 82)
    {
        if (overlays.Count == 0)
            throw new ArgumentException("BuildFilterChain requires at least one overlay.", nameof(overlays));

        List<string> segments = new();
        string currentLabel = baseFilterChainEndLabel;

        for (int i = 0; i < overlays.Count; i++)
        {
            ResolvedOverlay overlay = overlays[i];
            if (!overlay.IsAssetOverlay)
            {
                throw new ArgumentException(
                    $"OverlayAssetFilterBuilder received a non-asset overlay at index {i} (placement '{overlay.PlacementId}') — " +
                    "the caller must partition text vs. asset overlays before calling this builder.",
                    nameof(overlays));
            }

            bool isLast = i == overlays.Count - 1;
            int inputIndex = inputIndexForIndex(i);

            (int boxX, int boxY, int boxW, int boxH) = DrawtextFilterBuilder.ComputeAccentBoxPixels(
                overlay.Rect, overlay.PlacementRegion, probedWidth, probedHeight, boxHeightPct, boxWidthPct);
            string start = FfmpegArgvFormat.Number(overlay.OutputStartSec);
            string end = FfmpegArgvFormat.Number(overlay.OutputEndSec);
            string enableExpr = $"between(t,{start},{end})";

            // Scale to the placement's own box and delay this input's timeline so its content
            // starts playing exactly at the overlay's output-timeline start — see remarks above.
            // format=rgba comes FIRST, before scale: libavfilter's format negotiation between
            // scale and the downstream overlay can otherwise silently pick a non-alpha common
            // pixel format even when the decoded input genuinely carries alpha (a well-known
            // ffmpeg gotcha), which is exactly what turned a real transparent-background render
            // into a solid, opaque block over the edited video. Forcing rgba here pins the
            // format before any negotiation happens, regardless of the source's own alpha
            // subsampling (yuva420p, yuva444p10le, argb, ...) — VideoCompileStepExecutor has
            // already rejected any overlay asset lacking a real alpha plane at all (see
            // AlphaPixelFormats.HasAlpha in ResolveGraphicsAsync), so this is strictly a
            // format-pinning step, never a source of new transparency that wasn't already there.
            string scaledLabel = $"[ovsrc{i}]";
            segments.Add(
                $"[{inputIndex}:v]format=rgba,scale={boxW}:{boxH}:flags=bilinear,setpts=PTS+{start}/TB{scaledLabel}");

            string outLabel = isLast ? finalLabel : $"[ov{i}]";
            segments.Add(
                $"{currentLabel}{scaledLabel}overlay=x={boxX}:y={boxY}:eof_action=pass:enable='{enableExpr}'{outLabel}");

            currentLabel = outLabel;
        }

        return string.Join(";", segments);
    }
}
