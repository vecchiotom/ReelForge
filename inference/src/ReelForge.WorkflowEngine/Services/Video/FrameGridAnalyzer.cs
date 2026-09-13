using ReelForge.Shared.Workflows;

namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// Pure, unit-testable C# analyzer that derives every Phase 1 visual descriptor
/// (motion/camera-move/exposure/color/regions/Ken-Burns candidacy/near-duplicate signatures) from
/// a single low-res raw-frame grid buffer sampled by <see cref="IFrameGridSampler"/>. No ffmpeg
/// stderr scraping, no process I/O, no dependency on anything but arrays of RGB24 bytes — see
/// docs/video-editing.md "Scene/visual analysis (Phase 1)" for the technique and the formulas
/// documented here.
/// </summary>
public static class FrameGridAnalyzer
{
    /// <summary>Tunables that mirror the corresponding <see cref="VideoAnalyzeStepConfig"/> fields.</summary>
    public sealed record Options(
        double StillMotionThreshold = 0.02,
        int MinStillWindowMs = 400,
        int MaxStillWindowsPerShot = 3);

    /// <summary>
    /// A shot's near-duplicate/best-take signature: <c>LumaSig</c> is the shot's per-pixel
    /// time-averaged, z-normalized luma grid (length = gridWidth*gridHeight, e.g. 576 for 32x18);
    /// <c>ColorHist</c> is the shot's L1-normalized 64-bin (4 levels/channel) RGB histogram.
    /// </summary>
    public sealed record ShotSignature(double[] LumaSig, double[] ColorHist);

    private const int HistogramBins = 64; // 4 levels/channel (top 2 bits of each 8-bit channel), 4^3.

    // ---------------------------------------------------------------------------------------
    // Per-shot visual analysis
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Analyzes one shot's window of sampled grid frames (already sliced by the caller — see
    /// <see cref="SliceShotFrames"/>) into a full <see cref="VideoAnalysisShotVisual"/>.
    /// <see cref="VideoAnalysisShotVisual.DuplicateGroupId"/>/<c>GroupRank</c>/<c>IsBestTake</c>
    /// are left at their defaults here — those are cross-shot and patched in afterward by
    /// <see cref="GroupDuplicates"/>'s caller.
    /// </summary>
    public static VideoAnalysisShotVisual AnalyzeShot(
        IReadOnlyList<byte[]> frames, int gridWidth, int gridHeight, double fps, Options options)
    {
        if (frames.Count == 0)
        {
            return EmptyVisual();
        }

        int pixelCount = gridWidth * gridHeight;
        var lumaFrames = new double[frames.Count][];
        for (int i = 0; i < frames.Count; i++)
        {
            lumaFrames[i] = ComputeLuma(frames[i], pixelCount);
        }

        // ---- Motion: mean absolute per-pixel luma delta between consecutive frames, 0..1 ----
        int pairCount = Math.Max(0, frames.Count - 1);
        double[] frameDeltaMean = new double[pairCount]; // per-pair scalar mean delta
        var deltaFrames = new double[pairCount][]; // per-pair, per-pixel |delta|, reused for regions/zoom

        for (int i = 0; i < pairCount; i++)
        {
            double[] prev = lumaFrames[i];
            double[] curr = lumaFrames[i + 1];
            double[] delta = new double[pixelCount];
            double sum = 0;
            for (int p = 0; p < pixelCount; p++)
            {
                delta[p] = Math.Abs(curr[p] - prev[p]);
                sum += delta[p];
            }

            deltaFrames[i] = delta;
            frameDeltaMean[i] = sum / pixelCount;
        }

        double motionMean = pairCount > 0 ? frameDeltaMean.Average() : 0.0;
        double motionPeak = pairCount > 0 ? frameDeltaMean.Max() : 0.0;
        double motionStdDev = StdDev(frameDeltaMean, motionMean);
        string motionClass = ClassifyMotion(motionMean);

        // ---- Camera move: 1-D SAD pixel-shift search on column-sum (pan) / row-sum (tilt)
        //      luma profiles between consecutive frames. Heuristic, not ground truth — always
        //      paired with CameraMoveConfidence. ----
        (string cameraMove, double cameraMoveConfidence) = ClassifyCameraMove(
            lumaFrames, deltaFrames, gridWidth, gridHeight, motionMean);

        // ---- Head/tail motion: mean per-pair delta over the shot's first/last 250ms ----
        double headMotion = HeadTailMotion(frameDeltaMean, fps, fromStart: true);
        double tailMotion = HeadTailMotion(frameDeltaMean, fps, fromStart: false);

        // ---- Still windows: runs where per-pair delta < threshold, lasting >= MinStillWindowMs ----
        List<VideoAnalysisStillWindow> stillWindows = FindStillWindows(frameDeltaMean, fps, options);

        // ---- Exposure/color, averaged over every sampled pixel of every frame in the shot ----
        double brightnessSum = 0, brightnessSqSum = 0, contrastSum = 0, saturationSum = 0;
        long clippedCount = 0, crushedCount = 0;
        long totalPixels = (long)frames.Count * pixelCount;
        int[] histogram = new int[HistogramBins];
        double[] perPixelLumaSum = new double[pixelCount];

        for (int i = 0; i < frames.Count; i++)
        {
            byte[] frame = frames[i];
            double[] luma = lumaFrames[i];
            double frameSum = 0, frameSqSum = 0;
            for (int p = 0; p < pixelCount; p++)
            {
                double l = luma[p];
                frameSum += l;
                frameSqSum += l * l;
                perPixelLumaSum[p] += l;

                byte r = frame[p * 3], g = frame[p * 3 + 1], b = frame[p * 3 + 2];
                if (l > 250.0 / 255.0)
                {
                    clippedCount++;
                }

                if (l < 5.0 / 255.0)
                {
                    crushedCount++;
                }

                saturationSum += Saturation(r, g, b);
                histogram[HistogramBin(r, g, b)]++;
            }

            double frameMean = frameSum / pixelCount;
            double frameVariance = Math.Max(0, frameSqSum / pixelCount - frameMean * frameMean);
            brightnessSum += frameMean;
            brightnessSqSum += frameMean * frameMean;
            contrastSum += Math.Sqrt(frameVariance);
        }

        double brightnessMean = brightnessSum / frames.Count;
        double brightnessVarianceAcrossFrames = Math.Max(0, brightnessSqSum / frames.Count - brightnessMean * brightnessMean);
        double brightnessStdDev = Math.Sqrt(brightnessVarianceAcrossFrames); // temporal flicker, not intra-frame contrast
        double contrastRms = contrastSum / frames.Count;
        double clippedRatio = totalPixels > 0 ? (double)clippedCount / totalPixels : 0.0;
        double crushedRatio = totalPixels > 0 ? (double)crushedCount / totalPixels : 0.0;
        double saturationMean = totalPixels > 0 ? saturationSum / totalPixels : 0.0;

        List<VideoAnalysisColor> dominantColors = TopDominantColors(histogram, totalPixels);

        double[] avgLumaPerPixel = new double[pixelCount];
        for (int p = 0; p < pixelCount; p++)
        {
            avgLumaPerPixel[p] = perPixelLumaSum[p] / frames.Count;
        }

        double[] avgMotionPerPixel = AverageMotionPerPixel(deltaFrames, pixelCount);

        List<VideoAnalysisRegion> regions = BuildRegions(gridWidth, gridHeight, avgLumaPerPixel, avgMotionPerPixel);
        string? bestOverlayRegion = regions
            .Where(r => r.Name is "LowerThird" or "UpperThird" or "CenterBand")
            .OrderByDescending(r => r.Suitability)
            .Select(r => (string?)r.Name)
            .FirstOrDefault();

        double shotDurationSec = frames.Count / Math.Max(fps, 1e-6);
        double bestBandSuitability = regions
            .Where(r => r.Name is "LowerThird" or "UpperThird" or "CenterBand")
            .Select(r => r.Suitability)
            .DefaultIfEmpty(0.0)
            .Max();

        bool kenBurnsCandidate = cameraMove == "Static" && motionMean < 0.02
            && shotDurationSec >= 2.5 && bestBandSuitability >= 0.5;
        string? kenBurnsReason = kenBurnsCandidate
            ? $"Static, low-motion shot ({shotDurationSec:F1}s) with a clean {bestOverlayRegion ?? "region"} suitable for a slow pan/zoom reveal."
            : null;

        return new VideoAnalysisShotVisual(
            MotionMean: motionMean,
            MotionPeak: motionPeak,
            MotionStdDev: motionStdDev,
            MotionClass: motionClass,
            CameraMove: cameraMove,
            CameraMoveConfidence: cameraMoveConfidence,
            HeadMotion: headMotion,
            TailMotion: tailMotion,
            StillWindows: stillWindows,
            BrightnessMean: brightnessMean,
            BrightnessStdDev: brightnessStdDev,
            ContrastRms: contrastRms,
            ClippedHighlightRatio: clippedRatio,
            CrushedBlackRatio: crushedRatio,
            SaturationMean: saturationMean,
            DominantColors: dominantColors,
            Regions: regions,
            BestOverlayRegion: bestOverlayRegion,
            ActiveCrop: null,
            Sharpness: null,
            KenBurnsCandidate: kenBurnsCandidate,
            KenBurnsReason: kenBurnsReason,
            DuplicateGroupId: null,
            GroupRank: null,
            IsBestTake: false);
    }

    private static VideoAnalysisShotVisual EmptyVisual() => new(
        MotionMean: 0, MotionPeak: 0, MotionStdDev: 0, MotionClass: "Static",
        CameraMove: "Unknown", CameraMoveConfidence: 0,
        HeadMotion: 0, TailMotion: 0,
        StillWindows: [],
        BrightnessMean: 0, BrightnessStdDev: 0, ContrastRms: 0,
        ClippedHighlightRatio: 0, CrushedBlackRatio: 0, SaturationMean: 0,
        DominantColors: [], Regions: [], BestOverlayRegion: null,
        ActiveCrop: null, Sharpness: null,
        KenBurnsCandidate: false, KenBurnsReason: null,
        DuplicateGroupId: null, GroupRank: null, IsBestTake: false);

    /// <summary>
    /// Slices the caller's raw grid buffer (all sampled frames of the whole video, CFR from
    /// t=0 at <paramref name="fps"/>) down to just the frames whose sample time
    /// <c>i / fps</c> falls in <c>[shotStartSec, shotEndSec)</c>. Guarantees at least one frame
    /// when the buffer is non-empty, even for a shot shorter than one sample interval.
    /// </summary>
    public static List<byte[]> SliceShotFrames(
        byte[] pixelData, int frameCount, int gridWidth, int gridHeight, double fps,
        double shotStartSec, double shotEndSec)
    {
        int frameSize = gridWidth * gridHeight * 3;
        if (frameCount <= 0 || fps <= 0)
        {
            return [];
        }

        int startIdx = Math.Clamp((int)Math.Ceiling(shotStartSec * fps - 1e-9), 0, frameCount);
        int endIdxExclusive = Math.Clamp((int)Math.Ceiling(shotEndSec * fps - 1e-9), 0, frameCount);
        if (endIdxExclusive <= startIdx)
        {
            endIdxExclusive = Math.Min(startIdx + 1, frameCount);
        }

        var result = new List<byte[]>(Math.Max(0, endIdxExclusive - startIdx));
        for (int i = startIdx; i < endIdxExclusive; i++)
        {
            var frame = new byte[frameSize];
            Buffer.BlockCopy(pixelData, i * frameSize, frame, 0, frameSize);
            result.Add(frame);
        }

        return result;
    }

    // ---------------------------------------------------------------------------------------
    // Near-duplicate / best-take signatures and grouping
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Computes a shot's near-duplicate signature from its (already-sliced) frames: a
    /// z-normalized, time-averaged luma grid plus an L1-normalized 64-bin color histogram.
    /// </summary>
    public static ShotSignature ComputeSignature(IReadOnlyList<byte[]> frames, int gridWidth, int gridHeight)
    {
        int pixelCount = gridWidth * gridHeight;
        if (frames.Count == 0)
        {
            return new ShotSignature(new double[pixelCount], new double[HistogramBins]);
        }

        double[] lumaSum = new double[pixelCount];
        int[] histogram = new int[HistogramBins];
        long totalPixels = 0;

        foreach (byte[] frame in frames)
        {
            for (int p = 0; p < pixelCount; p++)
            {
                byte r = frame[p * 3], g = frame[p * 3 + 1], b = frame[p * 3 + 2];
                lumaSum[p] += Luma(r, g, b);
                histogram[HistogramBin(r, g, b)]++;
                totalPixels++;
            }
        }

        double[] avgLuma = new double[pixelCount];
        for (int p = 0; p < pixelCount; p++)
        {
            avgLuma[p] = lumaSum[p] / frames.Count;
        }

        double[] lumaSig = ZNormalize(avgLuma);
        double[] colorHist = new double[HistogramBins];
        if (totalPixels > 0)
        {
            for (int i = 0; i < HistogramBins; i++)
            {
                colorHist[i] = histogram[i] / (double)totalPixels;
            }
        }

        return new ShotSignature(lumaSig, colorHist);
    }

    /// <summary>
    /// <c>Distance = 0.7*(1-cosine(LumaSig)) + 0.3*(0.5*L1(ColorHist))</c>, <c>Similarity = 1-Distance</c>.
    /// </summary>
    public static double Similarity(ShotSignature a, ShotSignature b)
    {
        double cosine = CosineSimilarity(a.LumaSig, b.LumaSig);
        double l1 = L1Distance(a.ColorHist, b.ColorHist);
        double distance = 0.7 * (1 - cosine) + 0.3 * (0.5 * l1);
        return 1 - Math.Clamp(distance, 0, 2);
    }

    /// <summary>
    /// Heuristic "how good is this take" score in 0..1, used only to rank members of a duplicate
    /// group (never compared across groups). Documented formula (plan): 35% inverse motion
    /// jitter, 25% audio level (louder = more confident delivery, within reason), 20% exposure
    /// (closer to mid-brightness = better exposed), 10% duration (longer takes preferred, capped),
    /// 10% neutral placeholder for sharpness (not computed in Phase 1).
    /// </summary>
    public static double ComputeTakeQuality(
        double motionStdDev, double? audioRmsDbfs, double brightnessMean, double durationSec)
    {
        double motionTerm = 1 - Normalize(motionStdDev, 0, 0.15);
        double audioTerm = audioRmsDbfs.HasValue ? Normalize(audioRmsDbfs.Value, -40, -6) : 0.5;
        double exposureTerm = 1 - Math.Abs(brightnessMean - 0.5) * 2;
        double durationTerm = Normalize(durationSec, 0, 8.0);
        const double sharpnessTerm = 0.5; // neutral — no sharpness signal in Phase 1

        return Math.Clamp(
            0.35 * motionTerm + 0.25 * audioTerm + 0.20 * Math.Clamp(exposureTerm, 0, 1)
            + 0.10 * durationTerm + 0.10 * sharpnessTerm,
            0, 1);
    }

    /// <summary>
    /// Single-linkage clustering over a sliding time window: shot <c>i</c> is only ever compared
    /// against the previous <paramref name="windowShots"/> shots (not all pairs) — both faster and
    /// more correct, since multi-take shots are temporally adjacent.
    /// </summary>
    public static IReadOnlyList<VideoAnalysisDuplicateGroup> GroupDuplicates(
        IReadOnlyList<string> shotIds,
        IReadOnlyList<ShotSignature> signatures,
        IReadOnlyList<double> takeQualities,
        double similarityThreshold,
        int windowShots,
        out IReadOnlyDictionary<string, (string GroupId, int GroupRank, bool IsBestTake)> assignments)
    {
        int n = shotIds.Count;
        var parent = new int[n];
        for (int i = 0; i < n; i++)
        {
            parent[i] = i;
        }

        int Find(int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }

            return x;
        }

        void Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra != rb)
            {
                parent[rb] = ra;
            }
        }

        int window = Math.Max(0, windowShots);
        for (int i = 1; i < n; i++)
        {
            for (int j = Math.Max(0, i - window); j < i; j++)
            {
                if (Similarity(signatures[i], signatures[j]) >= similarityThreshold)
                {
                    Union(i, j);
                }
            }
        }

        var byRoot = new Dictionary<int, List<int>>();
        for (int i = 0; i < n; i++)
        {
            int root = Find(i);
            if (!byRoot.TryGetValue(root, out List<int>? members))
            {
                byRoot[root] = members = [];
            }

            members.Add(i);
        }

        var groups = new List<VideoAnalysisDuplicateGroup>();
        var assignmentsBuilder = new Dictionary<string, (string, int, bool)>();
        int groupIndex = 0;

        foreach (List<int> members in byRoot.Values.Where(m => m.Count > 1).OrderBy(m => m.Min()))
        {
            string groupId = $"d{groupIndex++}";
            List<int> rankedDescending = members.OrderByDescending(idx => takeQualities[idx]).ThenBy(idx => idx).ToList();

            for (int rank = 0; rank < rankedDescending.Count; rank++)
            {
                int idx = rankedDescending[rank];
                assignmentsBuilder[shotIds[idx]] = (groupId, rank, rank == 0);
            }

            double meanSimilarity = MeanPairwiseSimilarity(members, signatures);
            List<string> chronologicalIds = members.OrderBy(idx => idx).Select(idx => shotIds[idx]).ToList();
            groups.Add(new VideoAnalysisDuplicateGroup(groupId, chronologicalIds, shotIds[rankedDescending[0]], meanSimilarity));
        }

        assignments = assignmentsBuilder;
        return groups;
    }

    private static double MeanPairwiseSimilarity(List<int> members, IReadOnlyList<ShotSignature> signatures)
    {
        if (members.Count < 2)
        {
            return 1.0;
        }

        double sum = 0;
        int count = 0;
        for (int a = 0; a < members.Count; a++)
        {
            for (int b = a + 1; b < members.Count; b++)
            {
                sum += Similarity(signatures[members[a]], signatures[members[b]]);
                count++;
            }
        }

        return count > 0 ? sum / count : 1.0;
    }

    // ---------------------------------------------------------------------------------------
    // Pacing
    // ---------------------------------------------------------------------------------------

    public static VideoAnalysisPacing ComputePacing(
        IReadOnlyList<VideoAnalysisShot> shots, double durationSec, double timelineBinSeconds = 5.0)
    {
        if (shots.Count == 0 || durationSec <= 0)
        {
            return new VideoAnalysisPacing(0, 0, 0, [], timelineBinSeconds);
        }

        double[] durations = shots.Select(s => Math.Max(0, s.EndSec - s.StartSec)).ToArray();
        double mean = durations.Average();
        double median = Median(durations);
        double cutsPerMinute = shots.Count / (durationSec / 60.0);

        int binCount = Math.Max(1, (int)Math.Ceiling(durationSec / timelineBinSeconds));
        double[] timeline = new double[binCount];
        int[] counts = new int[binCount];

        foreach (VideoAnalysisShot shot in shots)
        {
            if (shot.Visual is null)
            {
                continue;
            }

            double midpoint = (shot.StartSec + shot.EndSec) / 2.0;
            int bin = Math.Clamp((int)(midpoint / timelineBinSeconds), 0, binCount - 1);
            timeline[bin] += shot.Visual.MotionMean;
            counts[bin]++;
        }

        for (int i = 0; i < binCount; i++)
        {
            timeline[i] = counts[i] > 0 ? timeline[i] / counts[i] : 0.0;
        }

        bool anyVisual = shots.Any(s => s.Visual is not null);
        return new VideoAnalysisPacing(mean, median, cutsPerMinute, anyVisual ? timeline : [], timelineBinSeconds);
    }

    // ---------------------------------------------------------------------------------------
    // Internal math helpers
    // ---------------------------------------------------------------------------------------

    private static double Luma(byte r, byte g, byte b) => (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;

    private static double Saturation(byte r, byte g, byte b)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        double max = Math.Max(rf, Math.Max(gf, bf));
        double min = Math.Min(rf, Math.Min(gf, bf));
        return max <= 1e-9 ? 0.0 : (max - min) / max;
    }

    private static double[] ComputeLuma(byte[] frame, int pixelCount)
    {
        var luma = new double[pixelCount];
        for (int p = 0; p < pixelCount; p++)
        {
            luma[p] = Luma(frame[p * 3], frame[p * 3 + 1], frame[p * 3 + 2]);
        }

        return luma;
    }

    /// <summary>4 levels/channel (top 2 bits of each 8-bit channel) => 4x4x4 = 64 bins.</summary>
    private static int HistogramBin(byte r, byte g, byte b) => ((r >> 6) << 4) | ((g >> 6) << 2) | (b >> 6);

    private static string BinCenterHex(int bin)
    {
        int r4 = (bin >> 4) & 0x3, g4 = (bin >> 2) & 0x3, b4 = bin & 0x3;
        int r = r4 * 64 + 32, g = g4 * 64 + 32, b = b4 * 64 + 32;
        return $"#{r:x2}{g:x2}{b:x2}";
    }

    private static List<VideoAnalysisColor> TopDominantColors(int[] histogram, long totalPixels)
    {
        if (totalPixels <= 0)
        {
            return [];
        }

        return histogram
            .Select((count, bin) => (bin, count))
            .Where(x => x.count > 0)
            .OrderByDescending(x => x.count)
            .Take(3)
            .Select(x => new VideoAnalysisColor(BinCenterHex(x.bin), (double)x.count / totalPixels))
            .ToList();
    }

    private static string ClassifyMotion(double motionMean) =>
        motionMean switch
        {
            < 0.01 => "Static",
            < 0.04 => "Subtle",
            < 0.10 => "Moderate",
            _ => "Dynamic"
        };

    private static double HeadTailMotion(double[] frameDeltaMean, double fps, bool fromStart)
    {
        if (frameDeltaMean.Length == 0 || fps <= 0)
        {
            return 0.0;
        }

        int windowFrames = Math.Max(1, (int)Math.Round(0.25 * fps));
        IEnumerable<double> window = fromStart
            ? frameDeltaMean.Take(windowFrames)
            : frameDeltaMean.Skip(Math.Max(0, frameDeltaMean.Length - windowFrames));
        double[] arr = window.ToArray();
        return arr.Length > 0 ? arr.Average() : 0.0;
    }

    private static List<VideoAnalysisStillWindow> FindStillWindows(double[] frameDeltaMean, double fps, Options options)
    {
        var windows = new List<VideoAnalysisStillWindow>();
        if (frameDeltaMean.Length == 0 || fps <= 0)
        {
            return windows;
        }

        double minWindowSec = options.MinStillWindowMs / 1000.0;
        int runStart = -1;

        void CloseRun(int runEndExclusive)
        {
            if (runStart < 0)
            {
                return;
            }

            // frameDeltaMean[k] is the delta ending at sample time (k+1)/fps.
            double startSec = runStart / fps;
            double endSec = runEndExclusive / fps;
            if (endSec - startSec >= minWindowSec)
            {
                double meanMotion = frameDeltaMean.Skip(runStart).Take(runEndExclusive - runStart).Average();
                windows.Add(new VideoAnalysisStillWindow(startSec, endSec, meanMotion));
            }

            runStart = -1;
        }

        for (int i = 0; i < frameDeltaMean.Length; i++)
        {
            bool still = frameDeltaMean[i] < options.StillMotionThreshold;
            if (still)
            {
                if (runStart < 0)
                {
                    runStart = i;
                }
            }
            else
            {
                CloseRun(i);
            }
        }

        CloseRun(frameDeltaMean.Length);

        return windows
            .OrderByDescending(w => w.EndSec - w.StartSec)
            .Take(Math.Max(0, options.MaxStillWindowsPerShot))
            .OrderBy(w => w.StartSec)
            .ToList();
    }

    private static double[] AverageMotionPerPixel(double[][] deltaFrames, int pixelCount)
    {
        var avg = new double[pixelCount];
        if (deltaFrames.Length == 0)
        {
            return avg;
        }

        foreach (double[] delta in deltaFrames)
        {
            for (int p = 0; p < pixelCount; p++)
            {
                avg[p] += delta[p];
            }
        }

        for (int p = 0; p < pixelCount; p++)
        {
            avg[p] /= deltaFrames.Length;
        }

        return avg;
    }

    /// <summary>
    /// 1-D SAD pixel-shift search: for each consecutive frame pair, finds the integer shift
    /// dx in [-4,4] minimizing the sum-of-absolute-differences between the current frame's
    /// column-sum luma profile and the previous frame's, shifted by dx (and dy similarly on
    /// row-sum profiles). Classifies from the resulting shift sequence: alternating-sign/high-
    /// variance shifts => Handheld; a consistent same-sign shift => Pan (x) or Tilt (y); higher
    /// motion near the frame center than the border => Zoom; near-zero shift and near-zero
    /// motion => Static; otherwise Unknown. This is a documented heuristic, not ground truth —
    /// always paired with a confidence score.
    /// </summary>
    private static (string CameraMove, double Confidence) ClassifyCameraMove(
        double[][] lumaFrames, double[][] deltaFrames, int width, int height, double motionMean)
    {
        int pairCount = lumaFrames.Length - 1;
        if (pairCount <= 0)
        {
            return ("Static", 1.0);
        }

        var dxs = new int[pairCount];
        var dys = new int[pairCount];

        for (int i = 0; i < pairCount; i++)
        {
            double[] prevCol = ColumnProfile(lumaFrames[i], width, height);
            double[] currCol = ColumnProfile(lumaFrames[i + 1], width, height);
            dxs[i] = BestShift(prevCol, currCol);

            double[] prevRow = RowProfile(lumaFrames[i], width, height);
            double[] currRow = RowProfile(lumaFrames[i + 1], width, height);
            dys[i] = BestShift(prevRow, currRow);
        }

        if (motionMean < 0.02 && dxs.All(d => d == 0) && dys.All(d => d == 0))
        {
            return ("Static", 1.0);
        }

        double handheldScore = SignFlipRatio(dxs, dys);
        bool anyShift = dxs.Any(d => d != 0) || dys.Any(d => d != 0);
        if (handheldScore > 0.5 && anyShift)
        {
            return ("Handheld", Math.Clamp(handheldScore, 0, 1));
        }

        double xConsistency = ConsistencyRatio(dxs);
        double yConsistency = ConsistencyRatio(dys);
        double meanAbsDx = dxs.Select(Math.Abs).Average();
        double meanAbsDy = dys.Select(Math.Abs).Average();

        if (xConsistency >= 0.7 && meanAbsDx >= 1.0 && xConsistency >= yConsistency)
        {
            return ("Pan", xConsistency);
        }

        if (yConsistency >= 0.7 && meanAbsDy >= 1.0)
        {
            return ("Tilt", yConsistency);
        }

        (double centerMotion, double borderMotion) = CenterBorderMotion(deltaFrames, width, height);
        double zoomRatio = centerMotion / Math.Max(borderMotion, 1e-6);
        if (zoomRatio >= 1.5 && motionMean >= 0.02)
        {
            return ("Zoom", Math.Clamp((zoomRatio - 1) / 2.0, 0, 1));
        }

        if (motionMean < 0.02 && meanAbsDx < 1 && meanAbsDy < 1)
        {
            return ("Static", Math.Clamp(1 - motionMean / 0.02, 0, 1));
        }

        return ("Unknown", 0.3);
    }

    private static double[] ColumnProfile(double[] luma, int width, int height)
    {
        var profile = new double[width];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                profile[x] += luma[y * width + x];
            }
        }

        for (int x = 0; x < width; x++)
        {
            profile[x] /= height;
        }

        return profile;
    }

    private static double[] RowProfile(double[] luma, int width, int height)
    {
        var profile = new double[height];
        for (int y = 0; y < height; y++)
        {
            double sum = 0;
            for (int x = 0; x < width; x++)
            {
                sum += luma[y * width + x];
            }

            profile[y] = sum / width;
        }

        return profile;
    }

    private static int BestShift(double[] prev, double[] curr)
    {
        int len = prev.Length;
        int bestShift = 0;
        double bestCost = double.MaxValue;

        for (int shift = -4; shift <= 4; shift++)
        {
            double cost = 0;
            int overlap = 0;
            for (int x = 0; x < len; x++)
            {
                int px = x - shift;
                if (px < 0 || px >= len)
                {
                    continue;
                }

                cost += Math.Abs(curr[x] - prev[px]);
                overlap++;
            }

            if (overlap == 0)
            {
                continue;
            }

            cost /= overlap;
            // Prefer the smaller |shift| on ties, so a genuinely static frame reports shift 0.
            if (cost < bestCost - 1e-9 || (Math.Abs(cost - bestCost) <= 1e-9 && Math.Abs(shift) < Math.Abs(bestShift)))
            {
                bestCost = cost;
                bestShift = shift;
            }
        }

        return bestShift;
    }

    private static double SignFlipRatio(int[] dxs, int[] dys)
    {
        int flips = 0, transitions = 0;
        CountFlips(dxs, ref flips, ref transitions);
        CountFlips(dys, ref flips, ref transitions);
        return transitions > 0 ? (double)flips / transitions : 0.0;
    }

    private static void CountFlips(int[] shifts, ref int flips, ref int transitions)
    {
        int lastSign = 0;
        for (int i = 0; i < shifts.Length; i++)
        {
            int sign = Math.Sign(shifts[i]);
            if (sign == 0)
            {
                continue;
            }

            if (lastSign != 0)
            {
                transitions++;
                if (sign != lastSign)
                {
                    flips++;
                }
            }

            lastSign = sign;
        }
    }

    private static double ConsistencyRatio(int[] shifts)
    {
        int[] nonZero = shifts.Where(s => s != 0).ToArray();
        if (nonZero.Length == 0)
        {
            return 0.0;
        }

        int positive = nonZero.Count(s => s > 0);
        int negative = nonZero.Length - positive;
        return Math.Max(positive, negative) / (double)nonZero.Length;
    }

    private static (double Center, double Border) CenterBorderMotion(double[][] deltaFrames, int width, int height)
    {
        if (deltaFrames.Length == 0)
        {
            return (0, 0);
        }

        int x0 = width / 4, x1 = width - width / 4;
        int y0 = height / 4, y1 = height - height / 4;

        double centerSum = 0, borderSum = 0;
        int centerCount = 0, borderCount = 0;

        foreach (double[] delta in deltaFrames)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double v = delta[y * width + x];
                    bool inCenter = x >= x0 && x < x1 && y >= y0 && y < y1;
                    if (inCenter)
                    {
                        centerSum += v;
                        centerCount++;
                    }
                    else
                    {
                        borderSum += v;
                        borderCount++;
                    }
                }
            }
        }

        double center = centerCount > 0 ? centerSum / centerCount : 0;
        double border = borderCount > 0 ? borderSum / borderCount : 0;
        return (center, border);
    }

    /// <summary>
    /// The 3x3 spatial grid (<c>R0</c>..<c>R8</c>, row-major) plus the three named overlay-
    /// candidate bands. <see cref="VideoAnalysisRegion.Suitability"/> favors low clutter (low
    /// intra-region luma std-dev), low motion, and brightness away from the 0/1 extremes:
    /// <c>0.5*clutterScore + 0.3*motionScore + 0.2*extremeScore</c>.
    /// </summary>
    private static List<VideoAnalysisRegion> BuildRegions(
        int width, int height, double[] avgLumaPerPixel, double[] avgMotionPerPixel)
    {
        var regions = new List<VideoAnalysisRegion>();

        var cells = new List<(string Name, int X0, int Y0, int X1, int Y1)>();
        int colW = Math.Max(1, width / 3), rowH = Math.Max(1, height / 3);
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                int x0 = col * colW;
                int x1 = col == 2 ? width : Math.Min(width, x0 + colW);
                int y0 = row * rowH;
                int y1 = row == 2 ? height : Math.Min(height, y0 + rowH);
                cells.Add(($"R{row * 3 + col}", x0, y0, x1, y1));
            }
        }

        int thirdH = Math.Max(1, height / 3);
        cells.Add(("UpperThird", 0, 0, width, thirdH));
        cells.Add(("LowerThird", 0, Math.Max(0, height - thirdH), width, height));
        cells.Add(("CenterBand", 0, Math.Max(0, (height - thirdH) / 2), width, Math.Min(height, (height - thirdH) / 2 + thirdH)));

        foreach ((string name, int x0, int y0, int x1, int y1) in cells)
        {
            double lumaSum = 0, lumaSqSum = 0, motionSum = 0;
            int count = 0;
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    int idx = y * width + x;
                    double l = avgLumaPerPixel[idx];
                    lumaSum += l;
                    lumaSqSum += l * l;
                    motionSum += avgMotionPerPixel[idx];
                    count++;
                }
            }

            if (count == 0)
            {
                continue;
            }

            double lumaMean = lumaSum / count;
            double lumaVariance = Math.Max(0, lumaSqSum / count - lumaMean * lumaMean);
            double lumaStdDev = Math.Sqrt(lumaVariance);
            double temporalMotion = motionSum / count;

            double clutterScore = 1 - Normalize(lumaStdDev, 0, 0.30);
            double motionScore = 1 - Normalize(temporalMotion, 0, 0.30);
            double extremeScore = Math.Clamp(1 - Math.Abs(lumaMean - 0.5) * 2, 0, 1);
            double suitability = Math.Clamp(0.5 * clutterScore + 0.3 * motionScore + 0.2 * extremeScore, 0, 1);
            string textColor = lumaMean < 0.45 ? "Light" : "Dark";

            var rect = new VideoAnalysisRect(
                (double)x0 / width, (double)y0 / height, (double)(x1 - x0) / width, (double)(y1 - y0) / height);

            regions.Add(new VideoAnalysisRegion(name, rect, lumaMean, lumaStdDev, temporalMotion, suitability, textColor));
        }

        return regions;
    }

    private static double Normalize(double value, double min, double max) =>
        max > min ? Math.Clamp((value - min) / (max - min), 0, 1) : 0.0;

    private static double StdDev(double[] values, double mean)
    {
        if (values.Length == 0)
        {
            return 0.0;
        }

        double sumSq = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sumSq / values.Length);
    }

    private static double Median(double[] values)
    {
        double[] sorted = values.OrderBy(v => v).ToArray();
        int n = sorted.Length;
        if (n == 0)
        {
            return 0.0;
        }

        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }

    private static double[] ZNormalize(double[] values)
    {
        double mean = values.Length > 0 ? values.Average() : 0.0;
        double stdDev = StdDev(values, mean);
        if (stdDev < 1e-9)
        {
            return new double[values.Length]; // flat/constant input — no signal to normalize
        }

        return values.Select(v => (v - mean) / stdDev).ToArray();
    }

    private static double CosineSimilarity(double[] a, double[] b)
    {
        double dot = 0, normA = 0, normB = 0;
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA < 1e-12 && normB < 1e-12)
        {
            return 1.0; // both flat/constant — treat as identical
        }

        if (normA < 1e-12 || normB < 1e-12)
        {
            return 0.0;
        }

        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    private static double L1Distance(double[] a, double[] b)
    {
        double sum = 0;
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
        {
            sum += Math.Abs(a[i] - b[i]);
        }

        return sum;
    }
}
