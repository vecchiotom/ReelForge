namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// One tracked-quad corner keyframe in PIXEL space on the canonical encode frame, keyed by the
/// insert branch's own frame index (frame 0 = the insert's on-screen start). Every number here was
/// computed by deterministic C# (<c>ChromaQuadTracker</c> corners, mapped through
/// <c>OutputTimeline</c> and scaled by the probed frame size in
/// <c>VideoCompileStepExecutor.BuildInsertKeyframes</c>) — never model-supplied.
/// </summary>
public sealed record InsertQuadFrame(
    long FrameIndex,
    double X0, double Y0, double X1, double Y1, double X2, double Y2, double X3, double Y3);

/// <summary>
/// A fully-resolved tracked screen insert, ready to be rendered into ffmpeg filter syntax by
/// <see cref="ScreenInsertFilterBuilder"/>. The caller (<c>VideoCompileStepExecutor.ResolveInsertsAsync</c>)
/// has already validated the region id against <c>OfferedInsertRegionIds</c>, validated + downloaded
/// + probed the rendered asset to a local scratch file, mapped the track's window onto the output
/// timeline, and converted the tracked corners to pixel keyframes.
/// </summary>
public sealed record ResolvedScreenInsert(
    string RegionId,
    string LocalAssetPath,
    double OutputStartSec,
    double OutputEndSec,
    IReadOnlyList<InsertQuadFrame> Keyframes,
    int FpsNum,
    int FpsDen);

/// <summary>
/// Builds the ffmpeg filter-chain fragment that corner-pins a rendered scene into a tracked,
/// deforming quadrilateral region of the compiled video (see docs/video-editing.md "Tracked screen
/// inserts (Phase 5)"). Pure — no I/O, no ffmpeg. The recipe, per insert (validated end-to-end
/// against a real ffmpeg run during design):
///
/// <code>
/// [N:v] scale=(W-2)x(H-2), fps=FPS, pad to WxH with a 1px black border,
///       perspective sense=destination eval=frame with per-frame corner expressions,
///       setpts (+outputStart delay)                                     -> warped content
/// color=white (W-2)x(H-2), pad to WxH with 1px black border, format=gray,
///       the SAME perspective expressions, the same setpts delay          -> warped mask
/// alphamerge(content, mask)                                              -> content w/ alpha
/// overlay onto the base at 0:0, enable='between(t,start,end)'
/// </code>
///
/// The 1px black border is load-bearing: ffmpeg's <c>perspective</c> clamps out-of-range source
/// coordinates to edge pixels, so an unbordered warp smears the content's edge pixels across the
/// whole frame outside the destination quad. Padding both the content and an all-white mask with a
/// black border makes everything outside the warped quad black — which the mask then converts to
/// full transparency via <c>alphamerge</c>. <c>perspective</c> has no alpha-capable pixel format of
/// its own, which is why the mask branch exists at all.
///
/// Corner coordinates are piecewise-linear expressions in <c>perspective</c>'s per-frame <c>in</c>
/// variable (input frame number — the filter sits BEFORE <c>setpts</c>, so frame 0 is the insert's
/// own first frame), built from keyframes by <see cref="BuildPiecewiseExpr"/>. Every number is
/// formatted via <see cref="FfmpegArgvFormat"/> — culture-invariant, same as every other filter
/// builder in this feature. Not one model-originated character reaches this string: the model
/// contributed only an offered region id, and every number below came from
/// <c>ChromaQuadTracker</c>/<c>OutputTimeline</c>/ffprobe.
/// </summary>
public static class ScreenInsertFilterBuilder
{
    /// <summary>Extra seconds of synthesized mask beyond the on-screen window, so the mask never hits EOF before the window ends (the content branch may — framesync then repeats its last frame, which for screen content is the right behavior: better a held UI frame than the plate showing through).</summary>
    private const double MaskDurationMarginSec = 1.0;

    /// <summary>
    /// Builds the filter-chain fragment for <paramref name="inserts"/> (must be non-empty — the
    /// caller keeps the existing label plumbing untouched when there is nothing to composite).
    /// Starts from <paramref name="baseFilterChainEndLabel"/> and ends at
    /// <paramref name="finalLabel"/>. <paramref name="inputIndexForIndex"/> maps insert index to
    /// the ffmpeg input index its asset was added at (the caller adds one extra <c>-i</c> per
    /// insert, after every asset-overlay input and before the music input).
    /// </summary>
    public static string BuildFilterChain(
        string baseFilterChainEndLabel,
        IReadOnlyList<ResolvedScreenInsert> inserts,
        int frameWidth,
        int frameHeight,
        Func<int, int> inputIndexForIndex,
        string finalLabel = "[vout]")
    {
        if (inserts.Count == 0)
            throw new ArgumentException("BuildFilterChain requires at least one insert.", nameof(inserts));

        int innerW = Math.Max(2, frameWidth - 2);
        int innerH = Math.Max(2, frameHeight - 2);

        var segments = new List<string>();
        string currentLabel = baseFilterChainEndLabel;

        for (int i = 0; i < inserts.Count; i++)
        {
            ResolvedScreenInsert insert = inserts[i];
            bool isLast = i == inserts.Count - 1;
            int inputIndex = inputIndexForIndex(i);

            string fps = $"{FfmpegArgvFormat.Number(insert.FpsNum)}/{FfmpegArgvFormat.Number(insert.FpsDen)}";
            string start = FfmpegArgvFormat.Number(insert.OutputStartSec);
            string end = FfmpegArgvFormat.Number(insert.OutputEndSec);
            double maskDuration = Math.Max(0.1, insert.OutputEndSec - insert.OutputStartSec) + MaskDurationMarginSec;

            string perspective = BuildPerspectiveFilter(insert.Keyframes);

            // Content branch: scale to the inner size, conform to the canonical encode fps (so the
            // perspective expressions' `in` frame indices line up with output time), 1px black
            // border, warp, then delay so frame 0 lands at the insert's output start.
            segments.Add(
                $"[{inputIndex}:v]scale={FfmpegArgvFormat.Number(innerW)}:{FfmpegArgvFormat.Number(innerH)}:flags=bilinear," +
                $"fps={fps},format=yuv444p,pad={FfmpegArgvFormat.Number(frameWidth)}:{FfmpegArgvFormat.Number(frameHeight)}:1:1:black," +
                $"{perspective},setpts=PTS-STARTPTS+{start}/TB[sic{i}]");

            // Mask branch: identically-warped white plate (synthesized), so alphamerge turns
            // everything outside the warped quad transparent.
            segments.Add(
                $"color=c=white:size={FfmpegArgvFormat.Number(innerW)}x{FfmpegArgvFormat.Number(innerH)}:rate={fps}:duration={FfmpegArgvFormat.Number(maskDuration)}," +
                $"format=gray,pad={FfmpegArgvFormat.Number(frameWidth)}:{FfmpegArgvFormat.Number(frameHeight)}:1:1:black," +
                $"{perspective},setpts=PTS-STARTPTS+{start}/TB[sim{i}]");

            segments.Add($"[sic{i}][sim{i}]alphamerge[sia{i}]");

            string outLabel = isLast ? finalLabel : $"[si{i}]";
            segments.Add(
                $"{currentLabel}[sia{i}]overlay=x=0:y=0:eof_action=pass:enable='between(t,{start},{end})'{outLabel}");

            currentLabel = outLabel;
        }

        return string.Join(";", segments);
    }

    /// <summary>
    /// The <c>perspective</c> filter for one insert's keyframes — <c>sense=destination</c> (the
    /// bordered content's corners are sent TO the tracked quad) with <c>eval=frame</c> so the
    /// corner expressions re-evaluate per frame against <c>in</c>.
    /// </summary>
    internal static string BuildPerspectiveFilter(IReadOnlyList<InsertQuadFrame> keyframes)
    {
        string X(Func<InsertQuadFrame, double> pick) =>
            BuildPiecewiseExpr(keyframes.Select(k => (k.FrameIndex, pick(k))).ToList());

        return
            $"perspective=x0='{X(k => k.X0)}':y0='{X(k => k.Y0)}'" +
            $":x1='{X(k => k.X1)}':y1='{X(k => k.Y1)}'" +
            $":x2='{X(k => k.X2)}':y2='{X(k => k.Y2)}'" +
            $":x3='{X(k => k.X3)}':y3='{X(k => k.Y3)}'" +
            ":sense=destination:eval=frame";
    }

    /// <summary>
    /// Piecewise-linear interpolation over <c>in</c> (the filter's per-frame input frame number):
    /// a single keyframe becomes a constant; N keyframes become nested
    /// <c>if(lt(in,f_{k+1}), a_k + (in - f_k)*s_k, ...)</c> terms holding the last value beyond
    /// the final keyframe. Keys must be strictly increasing (duplicates are collapsed, keeping the
    /// last). Every literal goes through <see cref="FfmpegArgvFormat.Number(double)"/>.
    /// </summary>
    internal static string BuildPiecewiseExpr(IReadOnlyList<(long Frame, double Value)> keys)
    {
        if (keys.Count == 0)
            return "0";

        // Collapse duplicate frame indices (keep last) and ensure ascending order.
        var cleaned = new List<(long Frame, double Value)>();
        foreach ((long frame, double value) in keys.OrderBy(k => k.Frame))
        {
            if (cleaned.Count > 0 && cleaned[^1].Frame == frame)
                cleaned[^1] = (frame, value);
            else
                cleaned.Add((frame, value));
        }

        // A series whose value never changes (one keyframe, or a perfectly static coordinate)
        // collapses to a plain constant — cheaper to evaluate per frame and much easier to read
        // in a captured filtergraph.
        if (cleaned.Count == 1 || cleaned.All(k => k.Value == cleaned[0].Value))
            return FfmpegArgvFormat.Number(cleaned[0].Value);

        // Innermost term: hold the final keyframe's value.
        string expr = FfmpegArgvFormat.Number(cleaned[^1].Value);

        for (int i = cleaned.Count - 2; i >= 0; i--)
        {
            (long f0, double v0) = cleaned[i];
            (long f1, double v1) = cleaned[i + 1];
            double slope = (v1 - v0) / (f1 - f0);
            string segment =
                $"{FfmpegArgvFormat.Number(v0)}+(in-{FfmpegArgvFormat.Number(f0)})*{FfmpegArgvFormat.Number(slope)}";
            expr = $"if(lt(in,{FfmpegArgvFormat.Number(f1)}),{segment},{expr})";
        }

        return expr;
    }
}
