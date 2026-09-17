using ReelForge.Shared.Workflows;

namespace ReelForge.WorkflowEngine.Execution.StepExecutors;

/// <summary>
/// The single place the video-editing pipeline decides "does this transcript segment's own text
/// read as a finished sentence?", plus how much that punctuation signal can be TRUSTED for the
/// source clip it came from.
///
/// <para>
/// Why the reliability half exists: trailing punctuation is the only sentence-boundary signal an
/// ASR transcript carries, and some ASR deployments (observed in production with a local Whisper
/// build) punctuate only a small minority of their segments. On such a source, "this segment does
/// not end in a full stop" says nothing at all about the edit — it is a property of the
/// transcriber, not of the cut. Both the story-editor's bounded view
/// (<c>meta.transcription.punctuated</c>, per source) and the compile step's deterministic
/// <c>sentenceCheck</c> evidence for the review agent therefore carry the measured per-source
/// ratio alongside the per-segment flag, so neither an agent nor a rubric treats an unpunctuated
/// transcript as a defective edit.
/// </para>
///
/// <para>
/// Deliberately computed from the FULL artifact segment list, never from the bounded view's
/// possibly text-truncated copies (<c>VideoAnalyzeStepConfig.MaxSegmentTextChars</c> can chop a
/// trailing full stop off the view's text) — the flag and the ratio must describe what the
/// transcriber actually produced.
/// </para>
/// </summary>
internal static class TranscriptPunctuation
{
    /// <summary>Trailing characters (after stripping closing quotes/parens) that count as a sentence ending.</summary>
    private static readonly char[] SentenceTerminalChars = ['.', '!', '?', '…'];

    /// <summary>Trailing closing-quote/paren characters stripped before checking for terminal punctuation, so `He said "stop."` still counts.</summary>
    private static readonly char[] TrailingWrapperChars = ['"', '\'', '”', '’', ')', ']'];

    /// <summary>
    /// Fraction of a source's transcript segments that must end in terminal punctuation before the
    /// punctuation signal is treated as trustworthy for that source. A well-punctuated ASR pass
    /// sits far above this (typically &gt; 0.8); the production case that motivated the threshold
    /// sat at 0.12.
    /// </summary>
    internal const double ReliableRatioThreshold = 0.5;

    /// <summary>True when <paramref name="text"/> reads as a finished sentence on its own.</summary>
    internal static bool EndsSentence(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string trimmed = text.TrimEnd().TrimEnd(TrailingWrapperChars);
        return trimmed.Length > 0 && SentenceTerminalChars.Contains(trimmed[^1]);
    }

    /// <summary>
    /// Punctuation statistics for one source clip's transcript segments. <see cref="Ratio"/> is
    /// null (and <see cref="Reliable"/> false) when the source produced no transcript segments at
    /// all — "unknown", never "0% punctuated".
    /// </summary>
    internal readonly record struct Stats(int SegmentCount, int PunctuatedCount)
    {
        internal double? Ratio => SegmentCount > 0 ? (double)PunctuatedCount / SegmentCount : null;

        internal bool Reliable => Ratio is { } ratio && ratio >= ReliableRatioThreshold;
    }

    /// <summary>Summarizes one source clip's segments (pass the FULL, untruncated texts).</summary>
    internal static Stats Summarize(IEnumerable<VideoAnalysisSegment> segments)
    {
        int total = 0, punctuated = 0;
        foreach (VideoAnalysisSegment s in segments)
        {
            total++;
            if (EndsSentence(s.Text))
                punctuated++;
        }

        return new Stats(total, punctuated);
    }

    /// <summary>
    /// Summarizes every source clip present in <paramref name="segments"/>, keyed by
    /// <see cref="VideoAnalysisSegment.SourceIndex"/>. A source with no transcript segments simply
    /// has no entry (the caller decides whether that means "no transcript" or "unknown source").
    /// </summary>
    internal static IReadOnlyDictionary<int, Stats> SummarizeBySource(IEnumerable<VideoAnalysisSegment> segments)
    {
        var counts = new Dictionary<int, (int Total, int Punctuated)>();
        foreach (VideoAnalysisSegment s in segments)
        {
            (int total, int punctuated) = counts.TryGetValue(s.SourceIndex, out (int Total, int Punctuated) existing)
                ? existing
                : (0, 0);
            counts[s.SourceIndex] = (total + 1, punctuated + (EndsSentence(s.Text) ? 1 : 0));
        }

        return counts.ToDictionary(kv => kv.Key, kv => new Stats(kv.Value.Total, kv.Value.Punctuated));
    }

    /// <summary>Rounds a ratio to 2dp for the JSON envelope, so `0.11764705882352941` never reaches a prompt.</summary>
    internal static double Round(double ratio) => Math.Round(ratio, 2);
}
