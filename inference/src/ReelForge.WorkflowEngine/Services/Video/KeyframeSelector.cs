using ReelForge.Shared.Workflows;

namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// Pure, unit-testable helpers deciding WHICH shots get a Phase 2 vision caption and WHERE in
/// each shot the representative keyframe is taken from. No ffmpeg, no I/O — see
/// <see cref="IKeyframeExtractor"/> for the actual frame extraction and <c>IShotCaptioner</c> for
/// the vision call. See docs/video-editing.md "Vision captioning" for the selection strategies.
/// </summary>
public static class KeyframeSelector
{
    /// <summary>
    /// Picks the sample time for a shot's representative keyframe: the midpoint of its longest
    /// <see cref="VideoAnalysisShotVisual.StillWindows"/> entry when at least one exists (a calm
    /// moment makes a cleaner, less motion-blurred frame than an arbitrary point), else the
    /// shot's own midpoint. Falls back to the shot midpoint when <see cref="VideoAnalysisShot.Visual"/>
    /// is <c>null</c> (visual analysis off, degraded, or simply produced no still windows).
    /// </summary>
    public static double ChooseKeyframeSec(VideoAnalysisShot shot)
    {
        IReadOnlyList<VideoAnalysisStillWindow>? stillWindows = shot.Visual?.StillWindows;
        if (stillWindows is { Count: > 0 })
        {
            VideoAnalysisStillWindow longest = stillWindows
                .OrderByDescending(w => w.EndSec - w.StartSec)
                .First();
            return (longest.StartSec + longest.EndSec) / 2.0;
        }

        return (shot.StartSec + shot.EndSec) / 2.0;
    }

    /// <summary>
    /// Selects up to <paramref name="maxCaptionedShots"/> shot ids to caption, per
    /// <paramref name="strategy"/>, excluding any shot shorter than
    /// <paramref name="minCaptionShotSeconds"/>. The returned list is always in
    /// shot-chronological (original list) order — never selection order — so caller code that
    /// iterates it for keyframe extraction/captioning processes shots front-to-back regardless of
    /// which strategy picked them.
    /// </summary>
    public static IReadOnlyList<string> SelectShotsToCaption(
        IReadOnlyList<VideoAnalysisShot> shots,
        IReadOnlyList<VideoAnalysisDuplicateGroup>? duplicateGroups,
        VideoCaptionSelection strategy,
        int maxCaptionedShots,
        double minCaptionShotSeconds)
    {
        if (shots.Count == 0 || maxCaptionedShots <= 0)
            return [];

        Dictionary<string, VideoAnalysisShot> byId = shots.ToDictionary(s => s.Id);
        bool Eligible(VideoAnalysisShot s) => (s.EndSec - s.StartSec) >= minCaptionShotSeconds;

        HashSet<string> selected = new();

        switch (strategy)
        {
            case VideoCaptionSelection.PerDuplicateGroup:
                SelectPerDuplicateGroup(shots, duplicateGroups, byId, Eligible, maxCaptionedShots, selected);
                break;

            case VideoCaptionSelection.LongestShots:
                SelectLongest(shots, Eligible, maxCaptionedShots, selected);
                break;

            case VideoCaptionSelection.EvenlySpaced:
            default:
                SelectEvenlySpaced(shots, Eligible, maxCaptionedShots, selected);
                break;
        }

        // Re-sort to shot-chronological order regardless of the strategy's own selection order.
        return shots.Where(s => selected.Contains(s.Id)).Select(s => s.Id).ToList();
    }

    private static void SelectPerDuplicateGroup(
        IReadOnlyList<VideoAnalysisShot> shots,
        IReadOnlyList<VideoAnalysisDuplicateGroup>? duplicateGroups,
        IReadOnlyDictionary<string, VideoAnalysisShot> byId,
        Func<VideoAnalysisShot, bool> eligible,
        int maxCaptionedShots,
        HashSet<string> selected)
    {
        // Pass 1: one caption per duplicate group, spent on the group's best take — N takes of
        // one setup then cost a single vision call instead of N.
        if (duplicateGroups is not null)
        {
            foreach (VideoAnalysisDuplicateGroup group in duplicateGroups)
            {
                if (selected.Count >= maxCaptionedShots)
                    return;

                if (byId.TryGetValue(group.BestShotId, out VideoAnalysisShot? best) && eligible(best))
                    selected.Add(best.Id);
            }
        }

        // Pass 2: fill any remaining budget with the longest not-yet-selected eligible shots.
        if (selected.Count >= maxCaptionedShots)
            return;

        foreach (VideoAnalysisShot s in shots.Where(eligible).Where(s => !selected.Contains(s.Id))
                     .OrderByDescending(s => s.EndSec - s.StartSec))
        {
            if (selected.Count >= maxCaptionedShots)
                return;

            selected.Add(s.Id);
        }
    }

    private static void SelectLongest(
        IReadOnlyList<VideoAnalysisShot> shots,
        Func<VideoAnalysisShot, bool> eligible,
        int maxCaptionedShots,
        HashSet<string> selected)
    {
        foreach (VideoAnalysisShot s in shots.Where(eligible).OrderByDescending(s => s.EndSec - s.StartSec))
        {
            if (selected.Count >= maxCaptionedShots)
                return;

            selected.Add(s.Id);
        }
    }

    private static void SelectEvenlySpaced(
        IReadOnlyList<VideoAnalysisShot> shots,
        Func<VideoAnalysisShot, bool> eligible,
        int maxCaptionedShots,
        HashSet<string> selected)
    {
        List<VideoAnalysisShot> eligibleShots = shots.Where(eligible).ToList();
        if (eligibleShots.Count == 0)
            return;

        int take = Math.Min(maxCaptionedShots, eligibleShots.Count);
        if (take >= eligibleShots.Count)
        {
            foreach (VideoAnalysisShot s in eligibleShots)
                selected.Add(s.Id);
            return;
        }

        double step = (double)eligibleShots.Count / take;
        for (int k = 0; k < take; k++)
        {
            int index = Math.Min(eligibleShots.Count - 1, (int)Math.Floor(k * step));
            selected.Add(eligibleShots[index].Id);
        }
    }
}
