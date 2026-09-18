using ReelForge.Shared.Workflows;

namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// Pure, unit-testable, deterministic chroma-plate quad tracker — the tracking engine behind
/// tracked screen inserts (see docs/video-editing.md "Tracked screen inserts (Phase 5)"). Given a
/// raw RGB24 frame grid (the exact <see cref="FrameGridResult"/> shape
/// <see cref="IFrameGridSampler"/> already produces, sampled at a higher resolution/fps than the
/// Phase 1 pass), it finds — per frame — the largest connected region of a target chroma color
/// (green/blue/magenta), fits a quadrilateral to it via the extreme-point method, and assembles
/// per-frame quads into contiguous, smoothed <see cref="VideoInsertRegionTrack"/>s with a
/// confidence score.
///
/// <para>
/// <b>Why marker/chroma-based, not general feature tracking:</b> this codebase's whole video
/// pipeline is built on "deterministic first-party C# computes every number; the model only picks
/// offered ids". A chroma plate is the one tracking target that is reliably detectable with pure
/// pixel statistics — no OpenCV, no native binding, no new dependency, fully testable against
/// synthetic in-memory frames. General markerless planar tracking (KLT/homography estimation over
/// feature correspondences) was considered and rejected for v1: it requires either a native CV
/// library (OpenCvSharp ships no musl/Alpine binaries the WorkflowEngine image could use without
/// building OpenCV from source, growing the image by hundreds of MB and adding a large native
/// attack surface parsing untrusted media — the exact class of cost the "why ffmpeg is not in the
/// sandbox" analysis weighs) or a from-scratch feature tracker whose robustness could not honestly
/// be validated in this environment. The constraint this buys is stated plainly: the source
/// footage must actually contain a uniform-color plate (the standard practice for exactly this
/// kind of commercial shot). See docs/video-editing.md for the full decision record.
/// </para>
/// </summary>
public static class ChromaQuadTracker
{
    /// <summary>Tracker knobs — every numeric here is workflow-author config or a compiled default, never model output.</summary>
    public sealed record Options(
        /// <summary>"green" / "blue" / "magenta" — matched in C# channel-ratio space only; never reaches ffmpeg.</summary>
        string ColorName = "green",
        /// <summary>Minimum fraction of frame area the matched component must cover to count as a plate.</summary>
        double MinAreaRatio = 0.004,
        /// <summary>Minimum (component area) / (fitted quad area) — rejects scattered noise and strongly non-quad blobs.</summary>
        double MinFillRatio = 0.65,
        /// <summary>Tracks shorter than this are dropped.</summary>
        double MinTrackSeconds = 1.0,
        /// <summary>Detection dropouts up to this many consecutive frames are bridged by linear interpolation instead of splitting the track.</summary>
        int MaxGapFrames = 3,
        /// <summary>Centered moving-average window (frames) applied to each corner coordinate series — odd, &gt;= 1.</summary>
        int SmoothingWindow = 3,
        /// <summary>Cap on emitted tracks (longest kept, then re-sorted chronologically).</summary>
        int MaxRegions = 8,
        /// <summary>Maximum normalized centroid jump between consecutive detected frames for them to belong to the same track.</summary>
        double MaxCentroidJump = 0.2);

    /// <summary>One frame's detection result — internal exchange type, normalized coordinates.</summary>
    internal sealed record FrameQuad(
        int FrameIndex,
        double X0, double Y0, double X1, double Y1, double X2, double Y2, double X3, double Y3,
        double AreaRatio, double FillRatio)
    {
        public double CentroidX => (X0 + X1 + X2 + X3) / 4.0;
        public double CentroidY => (Y0 + Y1 + Y2 + Y3) / 4.0;
    }

    /// <summary>
    /// Runs the full per-frame detection + track assembly over one raw RGB24 grid buffer. Frame
    /// <c>i</c>'s sample time is exactly <c>i / fps</c> (the <c>fps</c> filter emits CFR from t=0
    /// — same contract as <see cref="FrameGridResult"/>). Local ids are assigned <c>r0</c>,
    /// <c>r1</c>, … in chronological order; the caller remaps them into the artifact's global id
    /// space exactly like every other id kind.
    /// </summary>
    public static IReadOnlyList<VideoInsertRegionTrack> Track(
        byte[] pixelData, int frameCount, int width, int height, double fps, Options options)
    {
        if (frameCount <= 0 || width <= 0 || height <= 0 || fps <= 0)
            return Array.Empty<VideoInsertRegionTrack>();

        int frameSize = width * height * 3;
        var quads = new FrameQuad?[frameCount];
        for (int f = 0; f < frameCount; f++)
        {
            int offset = f * frameSize;
            if (offset + frameSize > pixelData.Length)
                break;

            quads[f] = DetectQuad(pixelData, offset, width, height, f, options);
        }

        List<List<FrameQuad>> runs = AssembleRuns(quads, options);

        var tracks = new List<VideoInsertRegionTrack>();
        foreach (List<FrameQuad> run in runs)
        {
            double startSec = run[0].FrameIndex / fps;
            double endSec = (run[^1].FrameIndex + 1) / fps;
            if (endSec - startSec < options.MinTrackSeconds)
                continue;

            List<FrameQuad> smoothed = Smooth(run, Math.Max(1, options.SmoothingWindow | 1));

            int spanFrames = run[^1].FrameIndex - run[0].FrameIndex + 1;
            double coverage = Math.Clamp(run.Count / (double)Math.Max(1, spanFrames), 0, 1);
            double meanFill = run.Average(q => q.FillRatio);
            double confidence = Math.Clamp(coverage * meanFill, 0, 1);

            double meanArea = run.Average(q => q.AreaRatio);
            double meanAspect = run.Average(q => QuadAspect(q, width, height));

            tracks.Add(new VideoInsertRegionTrack(
                Id: string.Empty, // assigned below, after the MaxRegions cap, so ids stay chronological+contiguous
                ShotId: null,     // resolved by the caller against its own shot list
                StartSec: startSec,
                EndSec: endSec,
                Keyframes: smoothed
                    .Select(q => new VideoInsertQuadKeyframe(
                        q.FrameIndex / fps, q.X0, q.Y0, q.X1, q.Y1, q.X2, q.Y2, q.X3, q.Y3))
                    .ToList(),
                Confidence: Math.Round(confidence, 4),
                MeanAreaRatio: Math.Round(meanArea, 5),
                MeanAspectRatio: Math.Round(meanAspect, 3),
                MotionClass: ClassifyMotion(run, fps),
                ColorName: NormalizeColorName(options.ColorName)));
        }

        int maxRegions = Math.Max(0, options.MaxRegions);
        if (tracks.Count > maxRegions)
        {
            tracks = tracks
                .OrderByDescending(t => t.EndSec - t.StartSec)
                .Take(maxRegions)
                .OrderBy(t => t.StartSec)
                .ToList();
        }

        for (int i = 0; i < tracks.Count; i++)
            tracks[i] = tracks[i] with { Id = $"r{i}" };

        return tracks;
    }

    /// <summary>"green"/"blue"/"magenta", defaulting unknown values to "green" — C#-side only, never an ffmpeg value.</summary>
    internal static string NormalizeColorName(string? raw) =>
        raw?.Trim().ToLowerInvariant() switch
        {
            "blue" => "blue",
            "magenta" => "magenta",
            _ => "green"
        };

    /// <summary>
    /// Per-frame detection: chroma mask → largest 4-connected component → extreme-point quad fit →
    /// area/fill gates. Internal so tests can drive a single synthetic frame directly.
    /// </summary>
    internal static FrameQuad? DetectQuad(
        byte[] pixelData, int frameOffset, int width, int height, int frameIndex, Options options)
    {
        string color = NormalizeColorName(options.ColorName);
        int pixelCount = width * height;

        // 1. Chroma mask: relative channel dominance, robust to brightness changes.
        var mask = new bool[pixelCount];
        for (int p = 0; p < pixelCount; p++)
        {
            int b0 = frameOffset + p * 3;
            byte r = pixelData[b0], g = pixelData[b0 + 1], b = pixelData[b0 + 2];
            mask[p] = color switch
            {
                "blue" => b >= 60 && b * 10 > r * 13 + 100 && b * 10 > g * 13 + 100,
                "magenta" => r >= 60 && b >= 60 && r * 10 > g * 13 + 100 && b * 10 > g * 13 + 100,
                _ => g >= 60 && g * 10 > r * 13 + 100 && g * 10 > b * 13 + 100
            };
        }

        // 2. Largest 4-connected component (iterative BFS — the grid is small, <= 640x360).
        var componentOf = new int[pixelCount];
        Array.Fill(componentOf, -1);
        int bestComponentSize = 0;
        List<int>? bestComponent = null;
        var queue = new Queue<int>();

        for (int seed = 0; seed < pixelCount; seed++)
        {
            if (!mask[seed] || componentOf[seed] >= 0)
                continue;

            var component = new List<int>();
            componentOf[seed] = seed;
            queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                int p = queue.Dequeue();
                component.Add(p);
                int x = p % width, y = p / width;
                Consider(x - 1, y);
                Consider(x + 1, y);
                Consider(x, y - 1);
                Consider(x, y + 1);

                void Consider(int nx, int ny)
                {
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                        return;
                    int np = ny * width + nx;
                    if (mask[np] && componentOf[np] < 0)
                    {
                        componentOf[np] = seed;
                        queue.Enqueue(np);
                    }
                }
            }

            if (component.Count > bestComponentSize)
            {
                bestComponentSize = component.Count;
                bestComponent = component;
            }
        }

        if (bestComponent is null)
            return null;

        double areaRatio = bestComponentSize / (double)pixelCount;
        if (areaRatio < options.MinAreaRatio)
            return null;

        // 3. Extreme-point quad fit: TL=min(x+y), BR=max(x+y), TR=max(x-y), BL=min(x-y).
        (double x, double y) tl = default, tr = default, bl = default, br = default;
        int minSum = int.MaxValue, maxSum = int.MinValue, minDiff = int.MaxValue, maxDiff = int.MinValue;
        foreach (int p in bestComponent)
        {
            int x = p % width, y = p / width;
            int sum = x + y, diff = x - y;
            if (sum < minSum) { minSum = sum; tl = (x, y); }
            if (sum > maxSum) { maxSum = sum; br = (x, y); }
            if (diff > maxDiff) { maxDiff = diff; tr = (x, y); }
            if (diff < minDiff) { minDiff = diff; bl = (x, y); }
        }

        // 4. Fill-ratio gate: component area vs. the fitted quad's own (shoelace) area — rejects
        //    strongly non-quadrilateral blobs (L-shapes, scattered noise the mask happened to join).
        double quadArea = Math.Abs(
            Shoelace(tl, tr, br) + Shoelace(tl, br, bl));
        if (quadArea < 1)
            return null;

        double fillRatio = Math.Clamp(bestComponentSize / quadArea, 0, 1.5);
        if (fillRatio < options.MinFillRatio)
            return null;

        // Normalize to 0..1 of frame dimensions, at pixel centers, so the coordinates are
        // resolution-independent (the compile step multiplies by the canonical encode dimensions).
        double NX(double x) => (x + 0.5) / width;
        double NY(double y) => (y + 0.5) / height;

        return new FrameQuad(
            frameIndex,
            NX(tl.x), NY(tl.y), NX(tr.x), NY(tr.y), NX(bl.x), NY(bl.y), NX(br.x), NY(br.y),
            areaRatio, Math.Min(fillRatio, 1.0));

        static double Shoelace((double x, double y) a, (double x, double y) b, (double x, double y) c) =>
            0.5 * ((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y));
    }

    /// <summary>
    /// Groups per-frame detections into contiguous runs: a detection joins the current run when it
    /// is at most <see cref="Options.MaxGapFrames"/>+1 frames after the previous one AND its
    /// centroid did not jump implausibly far; bridged gap frames are filled by linear
    /// interpolation so downstream keyframes stay temporally dense.
    /// </summary>
    internal static List<List<FrameQuad>> AssembleRuns(FrameQuad?[] quads, Options options)
    {
        var runs = new List<List<FrameQuad>>();
        List<FrameQuad>? current = null;

        foreach (FrameQuad? q in quads)
        {
            if (q is null)
                continue;

            if (current is not null)
            {
                FrameQuad last = current[^1];
                int gap = q.FrameIndex - last.FrameIndex - 1;
                double jump = Math.Sqrt(
                    Math.Pow(q.CentroidX - last.CentroidX, 2) + Math.Pow(q.CentroidY - last.CentroidY, 2));

                if (gap <= options.MaxGapFrames && jump <= options.MaxCentroidJump)
                {
                    for (int g = 1; g <= gap; g++)
                    {
                        double t = g / (double)(gap + 1);
                        current.Add(Lerp(last, q, last.FrameIndex + g, t));
                    }

                    current.Add(q);
                    continue;
                }

                runs.Add(current);
            }

            current = [q];
        }

        if (current is not null)
            runs.Add(current);

        return runs;
    }

    private static FrameQuad Lerp(FrameQuad a, FrameQuad b, int frameIndex, double t) => new(
        frameIndex,
        a.X0 + (b.X0 - a.X0) * t, a.Y0 + (b.Y0 - a.Y0) * t,
        a.X1 + (b.X1 - a.X1) * t, a.Y1 + (b.Y1 - a.Y1) * t,
        a.X2 + (b.X2 - a.X2) * t, a.Y2 + (b.Y2 - a.Y2) * t,
        a.X3 + (b.X3 - a.X3) * t, a.Y3 + (b.Y3 - a.Y3) * t,
        a.AreaRatio + (b.AreaRatio - a.AreaRatio) * t,
        a.FillRatio + (b.FillRatio - a.FillRatio) * t);

    /// <summary>Centered moving average over each of the 8 coordinate series — kills single-frame corner jitter from the coarse grid without lagging genuine motion.</summary>
    internal static List<FrameQuad> Smooth(List<FrameQuad> run, int window)
    {
        if (window <= 1 || run.Count <= 2)
            return run;

        int half = window / 2;
        var smoothed = new List<FrameQuad>(run.Count);
        for (int i = 0; i < run.Count; i++)
        {
            int from = Math.Max(0, i - half);
            int to = Math.Min(run.Count - 1, i + half);
            int n = to - from + 1;

            double x0 = 0, y0 = 0, x1 = 0, y1 = 0, x2 = 0, y2 = 0, x3 = 0, y3 = 0;
            for (int j = from; j <= to; j++)
            {
                FrameQuad q = run[j];
                x0 += q.X0; y0 += q.Y0; x1 += q.X1; y1 += q.Y1;
                x2 += q.X2; y2 += q.Y2; x3 += q.X3; y3 += q.Y3;
            }

            smoothed.Add(run[i] with
            {
                X0 = x0 / n, Y0 = y0 / n, X1 = x1 / n, Y1 = y1 / n,
                X2 = x2 / n, Y2 = y2 / n, X3 = x3 / n, Y3 = y3 / n
            });
        }

        return smoothed;
    }

    private static double QuadAspect(FrameQuad q, int width, int height)
    {
        double topW = Dist(q.X0, q.Y0, q.X1, q.Y1, width, height);
        double bottomW = Dist(q.X2, q.Y2, q.X3, q.Y3, width, height);
        double leftH = Dist(q.X0, q.Y0, q.X2, q.Y2, width, height);
        double rightH = Dist(q.X1, q.Y1, q.X3, q.Y3, width, height);
        double h = (leftH + rightH) / 2;
        return h > 1e-6 ? (topW + bottomW) / 2 / h : 1.0;

        static double Dist(double ax, double ay, double bx, double by, int w, int hh) =>
            Math.Sqrt(Math.Pow((bx - ax) * w, 2) + Math.Pow((by - ay) * hh, 2));
    }

    /// <summary>Mean centroid displacement per second, bucketed. Thresholds in normalized frame units/second.</summary>
    internal static string ClassifyMotion(List<FrameQuad> run, double fps)
    {
        if (run.Count < 2)
            return "Static";

        double total = 0;
        for (int i = 1; i < run.Count; i++)
        {
            total += Math.Sqrt(
                Math.Pow(run[i].CentroidX - run[i - 1].CentroidX, 2) +
                Math.Pow(run[i].CentroidY - run[i - 1].CentroidY, 2));
        }

        double perSecond = total / (run.Count - 1) * fps;
        return perSecond switch
        {
            < 0.005 => "Static",
            < 0.03 => "Slow",
            _ => "Moving"
        };
    }
}
