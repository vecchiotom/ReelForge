namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// Pure, no-I/O, no-DI weighted progress model for a <c>StepType.VideoAnalyze</c> step (mirrors
/// <c>FrameGridAnalyzer</c>/<c>MusicMixPlanner</c>'s "pure static-ish, unit-testable" house style).
/// Per-source stages form one contiguous block per source (source 0 runs all its stages, then
/// source 1 runs all of its, and so on) rather than a per-stage band across all sources — a
/// per-stage-band model would emit percentages that go backwards at every source boundary.
/// See docs/video-editing.md "Semantic visual dimensions (Phase 4)" for the full algorithm write-up.
/// </summary>
public sealed class VideoAnalyzeProgressPlan
{
    /// <summary>Declaration order IS execution order — <see cref="Percent"/> accumulates preceding weight from it.</summary>
    public enum Stage
    {
        // ---- per-source (repeated once per analyzed clip, in clip order) ----
        DownloadSource, ProbeSource, DetectSilence, DetectShots,
        ResolveTranscription, Transcribe, SampleAudioLevels,
        SampleFrameGrid, AnalyzeShots, SampleSharpness, GroupDuplicates,
        // ---- step-scope (run once, after every source finished) ----
        MatchLooks, ListMusicCandidates, ExtractKeyframes, CaptionShots,
        BuildView, UploadArtifact
    }

    private static readonly Stage[] PerSourceStagesInOrder =
    {
        Stage.DownloadSource, Stage.ProbeSource, Stage.DetectSilence, Stage.DetectShots,
        Stage.ResolveTranscription, Stage.Transcribe, Stage.SampleAudioLevels,
        Stage.SampleFrameGrid, Stage.AnalyzeShots, Stage.SampleSharpness, Stage.GroupDuplicates
    };

    private static readonly Stage[] StepStagesInOrder =
    {
        Stage.MatchLooks, Stage.ListMusicCandidates, Stage.ExtractKeyframes, Stage.CaptionShots,
        Stage.BuildView, Stage.UploadArtifact
    };

    /// <summary>
    /// Relative weights — constants; <see cref="Percent"/> normalizes by the enabled total, so they
    /// need not sum to 100. Deliberately no "color analysis"/"letterbox" stage: D1-D3/D6 run inside
    /// the existing per-shot <see cref="AnalyzeShots"/> pass with no independent duration.
    /// </summary>
    private static readonly IReadOnlyDictionary<Stage, int> Weights = new Dictionary<Stage, int>
    {
        [Stage.DownloadSource] = 8,
        [Stage.ProbeSource] = 3,
        [Stage.DetectSilence] = 4,
        [Stage.DetectShots] = 8,
        [Stage.ResolveTranscription] = 1,
        [Stage.Transcribe] = 28,
        [Stage.SampleAudioLevels] = 5,
        [Stage.SampleFrameGrid] = 12,
        [Stage.AnalyzeShots] = 10,
        [Stage.SampleSharpness] = 4,
        [Stage.GroupDuplicates] = 2,
        [Stage.MatchLooks] = 2,
        [Stage.ListMusicCandidates] = 1,
        [Stage.ExtractKeyframes] = 5,
        [Stage.CaptionShots] = 14,
        [Stage.BuildView] = 1,
        [Stage.UploadArtifact] = 2,
    };

    private readonly HashSet<Stage> _enabled;
    private readonly int _sourceCount;
    private readonly double _perSourceTotal;   // P
    private readonly double _stepTotal;        // S
    private readonly double _grandTotal;       // T = P + S
    private readonly IReadOnlyDictionary<Stage, double> _cumPerSourceBefore;
    private readonly IReadOnlyDictionary<Stage, double> _cumStepBefore;
    private int _last;

    public VideoAnalyzeProgressPlan(IReadOnlyCollection<Stage> enabled, int sourceCount)
    {
        _enabled = new HashSet<Stage>(enabled);
        _sourceCount = Math.Max(1, sourceCount);

        double perSourceRunning = 0;
        var cumPerSource = new Dictionary<Stage, double>();
        foreach (Stage stage in PerSourceStagesInOrder)
        {
            cumPerSource[stage] = perSourceRunning;
            if (_enabled.Contains(stage))
                perSourceRunning += Weights[stage];
        }
        _cumPerSourceBefore = cumPerSource;
        _perSourceTotal = perSourceRunning;

        double stepRunning = 0;
        var cumStep = new Dictionary<Stage, double>();
        foreach (Stage stage in StepStagesInOrder)
        {
            cumStep[stage] = stepRunning;
            if (_enabled.Contains(stage))
                stepRunning += Weights[stage];
        }
        _cumStepBefore = cumStep;
        _stepTotal = stepRunning;

        _grandTotal = _perSourceTotal + _stepTotal;
        _last = 0;
    }

    /// <summary>0-100, monotonically non-decreasing across calls on this instance.</summary>
    public int Percent(Stage stage, int sourceIndex = 0, double fractionWithinStage = 0)
    {
        double f = Math.Clamp(fractionWithinStage, 0, 1);
        bool isPerSource = Array.IndexOf(PerSourceStagesInOrder, stage) >= 0;
        double weight = _enabled.Contains(stage) ? Weights[stage] : 0;
        double raw;

        if (isPerSource)
        {
            double cumBefore = _cumPerSourceBefore.GetValueOrDefault(stage, 0);
            double within = _perSourceTotal > 0 ? (cumBefore + weight * f) / _perSourceTotal : 0;
            int clampedIndex = Math.Clamp(sourceIndex, 0, _sourceCount - 1);
            raw = (_perSourceTotal / _sourceCount) * (clampedIndex + within);
        }
        else
        {
            double cumBefore = _cumStepBefore.GetValueOrDefault(stage, 0);
            raw = _perSourceTotal + cumBefore + weight * f;
        }

        int percent = _grandTotal > 0
            ? (int)Math.Clamp(Math.Round(100 * raw / _grandTotal), 0, 100)
            : 100;

        _last = Math.Max(_last, percent);
        return _last;
    }

    /// <summary>Throttle helper: true for the first item, every ceil(count/20)th item, and the last.</summary>
    public static bool ShouldReportItem(int index, int count)
    {
        if (count <= 0)
            return false;
        if (index == 0 || index == count - 1)
            return true;

        int reportEvery = Math.Max(1, (int)Math.Ceiling(count / 20.0));
        return index % reportEvery == 0;
    }
}
