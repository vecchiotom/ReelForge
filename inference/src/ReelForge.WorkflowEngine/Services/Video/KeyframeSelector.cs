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
    /// Phase 4 (§7.4) — <c>n==1</c> returns exactly <c>[ChooseKeyframeSec(shot)]</c>, byte-identical
    /// to the pre-Phase-4 single-frame path. <c>n&gt;1</c> returns <paramref name="n"/> times at
    /// even fractions of the shot (n=3 ⇒ 25%/50%/75%), clamped inside the shot's own bounds. n is
    /// clamped to 1..3 here so a bad config can never fan out the argv.
    /// </summary>
    public static IReadOnlyList<double> ChooseKeyframeSecs(VideoAnalysisShot shot, int n)
    {
        int clampedN = Math.Clamp(n, 1, 3);
        if (clampedN == 1)
            return [ChooseKeyframeSec(shot)];

        double duration = Math.Max(0, shot.EndSec - shot.StartSec);
        var secs = new List<double>(clampedN);
        for (int i = 1; i <= clampedN; i++)
        {
            double fraction = i / (double)(clampedN + 1);
            secs.Add(Math.Clamp(shot.StartSec + duration * fraction, shot.StartSec, shot.EndSec));
        }

        return secs;
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

        // Pass 2: fill any remaining budget with the longest not-yet-selected eligible shots —
        // round-robin by source clip (judgment call 9) so a 50-shot budget across many clips isn't
        // spent entirely on one long source. A single source produces one round-robin "group",
        // which is provably identical to plain longest-first, so this changes nothing for the
        // (still overwhelmingly common) single-source case.
        if (selected.Count >= maxCaptionedShots)
            return;

        foreach (VideoAnalysisShot s in RoundRobinBySource(shots.Where(eligible).Where(s => !selected.Contains(s.Id))))
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
        foreach (VideoAnalysisShot s in RoundRobinBySource(shots.Where(eligible)))
        {
            if (selected.Count >= maxCaptionedShots)
                return;

            selected.Add(s.Id);
        }
    }

    /// <summary>
    /// Phase 4 (judgment call 9) — groups candidates by <see cref="VideoAnalysisShot.SourceIndex"/>
    /// (each group ordered longest-first), then round-robins across groups in first-appearance
    /// (i.e. source-index ascending, since callers always pass shots in merged chronological order)
    /// order. A single source produces exactly one group, so the round-robin loop degenerates to
    /// plain longest-first — provably identical output, hence no existing single-source
    /// <c>KeyframeSelectorTests</c> fact changes.
    /// </summary>
    private static IEnumerable<VideoAnalysisShot> RoundRobinBySource(IEnumerable<VideoAnalysisShot> candidates)
    {
        List<Queue<VideoAnalysisShot>> bySource = candidates
            .GroupBy(s => s.SourceIndex)
            .Select(g => new Queue<VideoAnalysisShot>(g.OrderByDescending(s => s.EndSec - s.StartSec)))
            .ToList();

        while (bySource.Any(q => q.Count > 0))
        {
            foreach (Queue<VideoAnalysisShot> q in bySource)
            {
                if (q.Count > 0)
                    yield return q.Dequeue();
            }
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
