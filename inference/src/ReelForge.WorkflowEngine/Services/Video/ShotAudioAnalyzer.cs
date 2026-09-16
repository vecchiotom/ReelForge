namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// Pure, static, <see cref="FrameGridAnalyzer"/>-style D5 audio-character classifier — no ffmpeg,
/// no I/O. See docs/video-editing.md "Semantic visual dimensions (Phase 4)" for the design and
/// Phase 4 plan §1's judgment call 7 for why this ships a ZCR/crest/noise-floor heuristic instead
/// of a spectral (FFT) classifier: this repo's only audio fixtures are synthetic sine tones, and a
/// threshold set tuned against a 440 Hz sine would pass CI while misclassifying real footage.
/// </summary>
public static class ShotAudioAnalyzer
{
    public sealed record Result(
        double CrestFactorDb, double NoiseFloorDbfs, double LevelStability,
        double ZeroCrossingRate, string CharacterClass, double CharacterConfidence);

    /// <param name="windowRmsDbfs">Per-window RMS of the windows overlapping this shot, in time order.</param>
    public static Result Analyze(
        double rmsDbfs, double peakDbfs, double speechRatio,
        IReadOnlyList<double> windowRmsDbfs, double zeroCrossingRate)
    {
        double crest = peakDbfs - rmsDbfs;

        // 10th-percentile window RMS == "how loud is this shot when nothing is happening".
        double floor = windowRmsDbfs.Count >= 3
            ? windowRmsDbfs.OrderBy(v => v).ElementAt(
                  Math.Clamp((int)Math.Floor(0.10 * (windowRmsDbfs.Count - 1)), 0, windowRmsDbfs.Count - 1))
            : rmsDbfs;

        double sd = StdDev(windowRmsDbfs);
        double stability = 1 - Math.Clamp(sd / 12.0, 0, 1); // 12 dB of spread == zero stability

        // ORDER IS LOAD-BEARING. Dialogue is the fallthrough, so it is never a positive claim.
        string cls;
        double conf;
        if (rmsDbfs <= -50)
        {
            cls = "Silent";
            conf = 1.0;
        }
        else if (stability >= 0.70 && crest < 14 && speechRatio < 0.85 && zeroCrossingRate < 0.22)
        {
            // level-steady + compressed + fewer fricative crossings than speech
            cls = "Music";
            conf = Math.Clamp(
                Math.Min(Math.Min((stability - 0.70) / 0.20, (14 - crest) / 8.0), (0.22 - zeroCrossingRate) / 0.10),
                0, 1);
        }
        else if (floor > -45 && speechRatio < 0.5)
        {
            cls = "Noisy";
            conf = Math.Clamp((floor + 45) / 10.0, 0, 1);
        }
        else if (rmsDbfs < -32 && speechRatio < 0.4)
        {
            cls = "Ambient";
            conf = Math.Clamp((-32 - rmsDbfs) / 10.0, 0, 1);
        }
        else
        {
            cls = "Dialogue";
            conf = Math.Clamp(speechRatio, 0, 1);
        }

        return new Result(crest, floor, stability, zeroCrossingRate, cls, conf);
    }

    private static double StdDev(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0.0;
        }

        double mean = values.Average();
        double sumSq = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sumSq / values.Count);
    }
}
