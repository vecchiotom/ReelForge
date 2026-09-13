using ReelForge.Shared.Workflows;

namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// Derives deterministic overlay-placement candidates from Phase 1's per-shot region data — the
/// Phase 3 building block for motion-graphics planning (see docs/video-editing.md "Motion
/// graphics (Phase 3)"). Pure, no I/O, no ffmpeg — reads only already-computed
/// <see cref="VideoAnalysisShot"/>/<see cref="VideoAnalysisShotVisual"/> data.
/// </summary>
/// <remarks>
/// Every placement's <c>StartSec</c>/<c>EndSec</c> window is resolved HERE, not left for compile
/// time to guess — <c>VideoCompileStepExecutor</c> only ever maps an already-resolved
/// source-timeline window through the cut, it never invents one. Ids (<c>p{n}</c>) are a
/// SEPARATE namespace from cut-anchor ids (<c>s{n}</c>/<c>g{n}</c>/<c>t{n}</c>) and are assigned
/// sequentially, by a single counter across the whole artifact, only to the placements that
/// survive the <paramref name="maxTotal"/> cap — so the surviving id set is always contiguous
/// (p0..p{N-1}) and globally unique, never sparse.
/// </remarks>
public static class OverlayPlacementBuilder
{
    /// <summary>Only these three named bands are offered as overlay-safe-zone candidates — the 3x3 grid cells (R0..R8) are not.</summary>
    private static readonly string[] NamedOverlayBands = { "LowerThird", "UpperThird", "CenterBand" };

    /// <summary>Target width (seconds) of a placement's time window when no still window is available.</summary>
    private const double DefaultWindowSec = 2.5;

    private sealed record Candidate(
        int ShotIndex, string ShotId, string Region, VideoAnalysisRect Rect,
        string TimeAnchor, double StartSec, double EndSec, double Suitability, string TextColor);

    public static IReadOnlyList<VideoAnalysisPlacement> BuildPlacements(
        IReadOnlyList<VideoAnalysisShot> shots, int maxPerShot, int maxTotal)
    {
        int effectiveMaxPerShot = Math.Max(0, maxPerShot);
        int effectiveMaxTotal = Math.Max(0, maxTotal);

        if (effectiveMaxPerShot == 0 || effectiveMaxTotal == 0)
            return [];

        List<Candidate> candidates = new();

        for (int shotIndex = 0; shotIndex < shots.Count; shotIndex++)
        {
            VideoAnalysisShot shot = shots[shotIndex];
            VideoAnalysisShotVisual? visual = shot.Visual;
            if (visual is null)
                continue;

            (string anchor, double startSec, double endSec) = ChooseTimeWindow(shot, visual);
            if (endSec <= startSec)
                continue;

            IEnumerable<VideoAnalysisRegion> ranked = visual.Regions
                .Where(r => NamedOverlayBands.Contains(r.Name, StringComparer.Ordinal))
                .OrderByDescending(r => r.Suitability)
                .Take(effectiveMaxPerShot);

            foreach (VideoAnalysisRegion region in ranked)
            {
                candidates.Add(new Candidate(
                    shotIndex, shot.Id, region.Name, region.Rect, anchor, startSec, endSec,
                    region.Suitability, region.TextColor));
            }
        }

        // Cap the whole artifact at maxTotal: drop the lowest-suitability candidates first, then
        // re-sort survivors back into deterministic generation order (shot order, then per-shot
        // rank) before assigning sequential, globally unique ids — see remarks above.
        List<Candidate> surviving = candidates.Count > effectiveMaxTotal
            ? candidates
                .OrderByDescending(c => c.Suitability)
                .Take(effectiveMaxTotal)
                .OrderBy(c => c.ShotIndex)
                .ThenByDescending(c => c.Suitability)
                .ToList()
            : candidates;

        List<VideoAnalysisPlacement> placements = new(surviving.Count);
        for (int i = 0; i < surviving.Count; i++)
        {
            Candidate c = surviving[i];
            placements.Add(new VideoAnalysisPlacement(
                $"p{i}", c.ShotId, c.Region, c.Rect, c.TimeAnchor, c.StartSec, c.EndSec, c.Suitability, c.TextColor));
        }

        return placements;
    }

    /// <summary>
    /// Chooses a placement's time window and how it was chosen: the shot's longest still window
    /// when one exists ("LongestStillWindow"), else a default-width window centered on the shot's
    /// midpoint ("ShotMiddle"), else — only if that still produces a degenerate window — a
    /// default-width window anchored at the shot's start ("ShotStart"). Always clamped to the
    /// shot's own [StartSec, EndSec) bounds.
    /// </summary>
    private static (string Anchor, double StartSec, double EndSec) ChooseTimeWindow(
        VideoAnalysisShot shot, VideoAnalysisShotVisual visual)
    {
        double shotStart = shot.StartSec;
        double shotEnd = shot.EndSec;

        if (visual.StillWindows.Count > 0)
        {
            VideoAnalysisStillWindow longest = visual.StillWindows
                .OrderByDescending(w => w.EndSec - w.StartSec)
                .First();

            double start = Math.Clamp(longest.StartSec, shotStart, shotEnd);
            double end = Math.Clamp(longest.EndSec, shotStart, shotEnd);
            if (end > start)
                return ("LongestStillWindow", start, end);
        }

        double center = (shotStart + shotEnd) / 2.0;
        double half = DefaultWindowSec / 2.0;
        double midStart = Math.Clamp(center - half, shotStart, shotEnd);
        double midEnd = Math.Clamp(center + half, shotStart, shotEnd);
        if (midEnd > midStart)
            return ("ShotMiddle", midStart, midEnd);

        double startEnd = Math.Clamp(shotStart + DefaultWindowSec, shotStart, shotEnd);
        return ("ShotStart", shotStart, startEnd);
    }
}
