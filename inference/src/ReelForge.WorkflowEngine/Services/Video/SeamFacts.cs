using ReelForge.Shared.Workflows;
using ReelForge.WorkflowEngine.Execution.StepExecutors;

namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// Deterministic, measured facts about ONE cut-seam (the boundary between resolved span <c>i</c>
/// and resolved span <c>i+1</c>) — the sole input <see cref="SeamTransitionPlanner"/> reads to pick
/// a <see cref="SeamTreatment"/>. Every field here comes from data the system itself already
/// computed (Phase 1/4 visual/audio descriptors, transcript punctuation) — never from a model, and
/// never from anything the story editor chose beyond which ids to keep. A fact that cannot be
/// determined (missing visual analysis, a shot lookup miss) is left null/false rather than
/// thrown — <see cref="SeamTransitionPlanner"/>'s default rule (R7) naturally degrades an
/// all-unknown seam to <see cref="SeamTreatment.AudioOnly"/>.
/// </summary>
public sealed record SeamFacts(
    int SeamIndex,
    bool SameSource,
    double? RemovedGapSec,
    string? OutShotId,
    string? InShotId,
    bool SameShot,
    string? OutLook,
    string? InLook,
    bool LookJump,
    bool CutOutStill,
    bool CutInStill,
    bool DupPair,
    string? OutAudioChar,
    string? InAudioChar,
    string? OutMove,
    string? InMove,
    double? OutMoveConfidence,
    double? InMoveConfidence,
    bool EndsSentenceAtSeam);

/// <summary>
/// Builds one <see cref="SeamFacts"/> per seam between consecutive resolved spans. Pure — no I/O,
/// no logging, no static mutable state. See docs/video-editing.md "Cut transitions".
/// </summary>
public static class SeamFactsBuilder
{
    /// <summary>Tolerance (seconds) used to nudge a cut boundary just inside a shot's own [Start, End) window before locating it — a cut moment landing exactly ON a shot boundary must still resolve to the correct adjacent shot.</summary>
    private const double BoundaryEpsilonSec = 1e-3;

    internal static IReadOnlyList<SeamFacts> Build(
        VideoAnalysisArtifact artifact, IReadOnlyList<VideoCompileStepExecutor.ResolvedSpan> resolvedSpans)
    {
        if (resolvedSpans.Count < 2)
            return [];

        // Grouped by source index so the per-shot lookup below is a simple linear/bounds scan
        // rather than a full-artifact scan per seam.
        var shotsBySource = new Dictionary<int, List<VideoAnalysisShot>>();
        foreach (VideoAnalysisShot shot in artifact.Shots)
        {
            if (!shotsBySource.TryGetValue(shot.SourceIndex, out List<VideoAnalysisShot>? list))
                shotsBySource[shot.SourceIndex] = list = new List<VideoAnalysisShot>();
            list.Add(shot);
        }

        var segmentsBySource = new Dictionary<int, List<VideoAnalysisSegment>>();
        foreach (VideoAnalysisSegment seg in artifact.Segments)
        {
            if (!segmentsBySource.TryGetValue(seg.SourceIndex, out List<VideoAnalysisSegment>? list))
                segmentsBySource[seg.SourceIndex] = list = new List<VideoAnalysisSegment>();
            list.Add(seg);
        }

        var facts = new List<SeamFacts>(resolvedSpans.Count - 1);
        for (int i = 0; i < resolvedSpans.Count - 1; i++)
        {
            VideoCompileStepExecutor.ResolvedSpan outSpan = resolvedSpans[i];
            VideoCompileStepExecutor.ResolvedSpan inSpan = resolvedSpans[i + 1];

            bool sameSource = outSpan.SourceIndex == inSpan.SourceIndex;
            double? removedGapSec = sameSource ? inSpan.SnappedStart - outSpan.SnappedEnd : null;

            VideoAnalysisShot? outShot = FindShot(shotsBySource, outSpan.SourceIndex, outSpan.SnappedEnd - BoundaryEpsilonSec);
            VideoAnalysisShot? inShot = FindShot(shotsBySource, inSpan.SourceIndex, inSpan.SnappedStart + BoundaryEpsilonSec);

            bool sameShot = outShot is not null && inShot is not null && outShot.Id == inShot.Id;

            string? outLook = outShot?.Visual?.LookGroupId;
            string? inLook = inShot?.Visual?.LookGroupId;
            bool lookJump = outLook is not null && inLook is not null && outLook != inLook;

            bool cutOutStill = IsStillAt(outShot, outSpan.SnappedEnd);
            bool cutInStill = IsStillAt(inShot, inSpan.SnappedStart);

            bool dupPair = outShot?.Visual?.DuplicateGroupId is not null &&
                           outShot.Visual.DuplicateGroupId == inShot?.Visual?.DuplicateGroupId;

            string? outAudioChar = outShot?.Audio?.AudioCharacterClass;
            string? inAudioChar = inShot?.Audio?.AudioCharacterClass;

            string? outMove = outShot?.Visual?.CameraMove;
            string? inMove = inShot?.Visual?.CameraMove;
            double? outMoveConfidence = outShot?.Visual?.CameraMoveConfidence;
            double? inMoveConfidence = inShot?.Visual?.CameraMoveConfidence;

            bool endsSentenceAtSeam = EndsSentenceAtSeamMoment(segmentsBySource, outSpan);

            facts.Add(new SeamFacts(
                SeamIndex: i,
                SameSource: sameSource,
                RemovedGapSec: removedGapSec,
                OutShotId: outShot?.Id,
                InShotId: inShot?.Id,
                SameShot: sameShot,
                OutLook: outLook,
                InLook: inLook,
                LookJump: lookJump,
                CutOutStill: cutOutStill,
                CutInStill: cutInStill,
                DupPair: dupPair,
                OutAudioChar: outAudioChar,
                InAudioChar: inAudioChar,
                OutMove: outMove,
                InMove: inMove,
                OutMoveConfidence: outMoveConfidence,
                InMoveConfidence: inMoveConfidence,
                EndsSentenceAtSeam: endsSentenceAtSeam));
        }

        return facts;
    }

    private static VideoAnalysisShot? FindShot(
        Dictionary<int, List<VideoAnalysisShot>> shotsBySource, int sourceIndex, double momentSec)
    {
        if (!shotsBySource.TryGetValue(sourceIndex, out List<VideoAnalysisShot>? shots))
            return null;

        foreach (VideoAnalysisShot shot in shots)
        {
            if (momentSec >= shot.StartSec && momentSec < shot.EndSec)
                return shot;
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="momentSec"/> falls inside one of <paramref name="shot"/>'s own
    /// measured still windows; falls back to a coarse motion-class check
    /// (<c>MotionClass == "Static"</c>) when no still-window data is present at all (visual
    /// analysis off/degraded, or a shot with no still window long enough to record).
    /// </summary>
    private static bool IsStillAt(VideoAnalysisShot? shot, double momentSec)
    {
        VideoAnalysisShotVisual? visual = shot?.Visual;
        if (visual is null)
            return false;

        if (visual.StillWindows.Count > 0)
        {
            foreach (VideoAnalysisStillWindow window in visual.StillWindows)
            {
                if (momentSec >= window.StartSec && momentSec <= window.EndSec)
                    return true;
            }

            return false;
        }

        return visual.MotionClass == "Static";
    }

    /// <summary>
    /// Whether the last transcript segment fully inside the OUT span's own source clip, ending at
    /// or before the cut moment, reads as a complete sentence — reuses
    /// <see cref="TranscriptPunctuation.EndsSentence"/> verbatim (see that class's doc comment) so
    /// this can never disagree with the bounded view's own per-segment flag or
    /// <c>VideoCompileStepExecutor.BuildSentenceCheck</c>.
    /// </summary>
    private static bool EndsSentenceAtSeamMoment(
        Dictionary<int, List<VideoAnalysisSegment>> segmentsBySource, VideoCompileStepExecutor.ResolvedSpan outSpan)
    {
        if (!segmentsBySource.TryGetValue(outSpan.SourceIndex, out List<VideoAnalysisSegment>? segments))
            return false;

        VideoAnalysisSegment? last = null;
        foreach (VideoAnalysisSegment seg in segments)
        {
            if (seg.EndSec <= outSpan.SnappedEnd + BoundaryEpsilonSec &&
                (last is null || seg.EndSec > last.EndSec))
            {
                last = seg;
            }
        }

        return last is not null && TranscriptPunctuation.EndsSentence(last.Text);
    }
}
