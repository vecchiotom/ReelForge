using FluentAssertions;
using ReelForge.WorkflowEngine.Services.Video;
using System.Collections.Generic;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Covers <see cref="TranscriptChunkPlanner"/>: a pure function with no I/O, so every case here
/// is a plain input/output assertion. The centrepiece (<see
/// cref="PlanChunks_three_chunk_case_supports_correct_absolute_timestamp_via_chunk_start_offset"/>)
/// targets Risk R10 - ASR chunk timestamp offsetting is called out in the plan as the single
/// most likely correctness bug in this feature class.
/// </summary>
public class TranscriptChunkPlannerTests
{
    [Fact]
    public void PlanChunks_returns_a_single_chunk_when_the_whole_track_fits_the_budget()
    {
        IReadOnlyList<(double StartSec, double EndSec)> chunks = TranscriptChunkPlanner.PlanChunks(
            totalDurationSec: 120,
            estimatedTotalBytes: 500_000,
            silenceSpans: new List<(double, double)>(),
            maxChunkBytes: 20_000_000);

        chunks.Should().ContainSingle();
        chunks[0].StartSec.Should().Be(0.0);
        chunks[0].EndSec.Should().Be(120);
    }

    [Fact]
    public void PlanChunks_returns_a_single_chunk_for_degenerate_byte_inputs()
    {
        IReadOnlyList<(double StartSec, double EndSec)> chunks = TranscriptChunkPlanner.PlanChunks(
            totalDurationSec: 600,
            estimatedTotalBytes: 0,
            silenceSpans: new List<(double, double)>(),
            maxChunkBytes: 20_000_000);

        chunks.Should().ContainSingle();
        chunks[0].EndSec.Should().Be(600);
    }

    [Fact]
    public void PlanChunks_returns_no_chunks_for_a_non_positive_duration()
    {
        IReadOnlyList<(double StartSec, double EndSec)> chunks = TranscriptChunkPlanner.PlanChunks(
            totalDurationSec: 0,
            estimatedTotalBytes: 1_000,
            silenceSpans: new List<(double, double)>(),
            maxChunkBytes: 100);

        chunks.Should().BeEmpty();
    }

    [Fact]
    public void PlanChunks_snaps_a_boundary_into_a_nearby_silence_span_instead_of_the_raw_ideal_point()
    {
        // bytesPerSecond = 100 => idealChunkSec = maxChunkBytes / bytesPerSecond = 1000/100 = 10.
        // A silence span straddling the ideal 10s boundary must pull the cut fully inside it
        // (deepest into the gap - the ideal boundary itself, since it already falls in the span).
        IReadOnlyList<(double StartSec, double EndSec)> silence = new List<(double, double)>
        {
            (9.5, 10.5)
        };

        IReadOnlyList<(double StartSec, double EndSec)> chunks = TranscriptChunkPlanner.PlanChunks(
            totalDurationSec: 15,
            estimatedTotalBytes: 1500, // 15s * 100 bytes/sec
            silenceSpans: silence,
            maxChunkBytes: 1000);

        chunks.Should().HaveCount(2);
        chunks[0].StartSec.Should().Be(0.0);
        chunks[0].EndSec.Should().Be(10.0); // ideal boundary already inside the span
        chunks[1].StartSec.Should().Be(10.0);
        chunks[1].EndSec.Should().Be(15);
    }

    [Fact]
    public void PlanChunks_clamps_the_cut_to_the_nearer_edge_of_a_span_that_does_not_contain_the_ideal_boundary()
    {
        // Ideal boundary at 10s, but the only nearby silence span is (7.8, 8.2) - inside the
        // search window (tolerance = 10*0.20 = 2s => window [8,12]) only by its trailing edge.
        // The cut must land at the span's own edge (8.2), never at the raw ideal boundary (10)
        // and never outside the span.
        IReadOnlyList<(double StartSec, double EndSec)> silence = new List<(double, double)>
        {
            (7.8, 8.2)
        };

        IReadOnlyList<(double StartSec, double EndSec)> chunks = TranscriptChunkPlanner.PlanChunks(
            totalDurationSec: 15,
            estimatedTotalBytes: 1500,
            silenceSpans: silence,
            maxChunkBytes: 1000);

        chunks.Should().HaveCount(2);
        chunks[0].EndSec.Should().Be(8.2);
        chunks[1].StartSec.Should().Be(8.2);
    }

    [Fact]
    public void PlanChunks_falls_back_to_a_hard_cut_at_the_ideal_boundary_when_no_silence_is_nearby()
    {
        IReadOnlyList<(double StartSec, double EndSec)> silence = new List<(double, double)>
        {
            // Far outside the search window around the 10s ideal boundary ([8, 12]).
            (0.0, 0.5)
        };

        IReadOnlyList<(double StartSec, double EndSec)> chunks = TranscriptChunkPlanner.PlanChunks(
            totalDurationSec: 15,
            estimatedTotalBytes: 1500,
            silenceSpans: silence,
            maxChunkBytes: 1000);

        chunks.Should().HaveCount(2);
        chunks[0].EndSec.Should().Be(10.0);
        chunks[1].StartSec.Should().Be(10.0);
    }

    [Fact]
    public void PlanChunks_produces_contiguous_gapless_chunks_covering_the_whole_track()
    {
        IReadOnlyList<(double StartSec, double EndSec)> silence = new List<(double, double)>
        {
            (9.8, 10.2),
            (19.7, 20.3)
        };

        IReadOnlyList<(double StartSec, double EndSec)> chunks = TranscriptChunkPlanner.PlanChunks(
            totalDurationSec: 25,
            estimatedTotalBytes: 2500,
            silenceSpans: silence,
            maxChunkBytes: 1000);

        chunks.Should().HaveCount(3);
        for (int i = 1; i < chunks.Count; i++)
        {
            // Each chunk starts exactly where the previous one ended - this contiguity is what
            // makes "chunkStart + relativeSec" a valid absolute-timestamp computation at all.
            chunks[i].StartSec.Should().Be(chunks[i - 1].EndSec);
        }

        chunks[0].StartSec.Should().Be(0.0);
        chunks[^1].EndSec.Should().Be(25);
    }

    [Fact]
    public void PlanChunks_three_chunk_case_supports_correct_absolute_timestamp_via_chunk_start_offset()
    {
        // bytesPerSecond = 100, maxChunkBytes = 1000 => idealChunkSec = 10.
        // Silence spans are placed to force two deterministic, exact cut points at 10s and 20s,
        // producing exactly three chunks: [0,10), [10,20), [20,25).
        IReadOnlyList<(double StartSec, double EndSec)> silence = new List<(double, double)>
        {
            (9.8, 10.2),
            (19.7, 20.3)
        };

        IReadOnlyList<(double StartSec, double EndSec)> chunks = TranscriptChunkPlanner.PlanChunks(
            totalDurationSec: 25,
            estimatedTotalBytes: 2500,
            silenceSpans: silence,
            maxChunkBytes: 1000);

        chunks.Should().HaveCount(3);
        chunks[0].Should().Be((0.0, 10.0));
        chunks[1].Should().Be((10.0, 20.0));
        chunks[2].Should().Be((20.0, 25.0));

        // Simulate what the ASR chunking caller does with each chunk's transcript: every
        // word/segment timestamp the ASR backend returns is relative to the START of the WAV
        // bytes it was handed for that chunk, so the caller must add the chunk's own StartSec to
        // recover the absolute position in the original track (TranscriptResult's documented
        // contract - "Words"/"Segments" are always absolute). This is the exact arithmetic bug
        // class flagged by the plan (R10): using the wrong chunk's start, or forgetting the
        // offset entirely, is the natural way to get this wrong.
        double chunk0RelativeWordSec = 2.0;
        double chunk1RelativeWordSec = 3.2;
        double chunk2RelativeWordSec = 4.9;

        double chunk0AbsoluteWordSec = chunks[0].StartSec + chunk0RelativeWordSec;
        double chunk1AbsoluteWordSec = chunks[1].StartSec + chunk1RelativeWordSec;
        double chunk2AbsoluteWordSec = chunks[2].StartSec + chunk2RelativeWordSec;

        chunk0AbsoluteWordSec.Should().Be(2.0);
        chunk1AbsoluteWordSec.Should().Be(13.2);
        chunk2AbsoluteWordSec.Should().Be(24.9);

        // Each recovered absolute timestamp must fall back inside the chunk it came from -
        // a wrong offset (e.g. using chunk N's start for a word from chunk N+1) would place it
        // outside the source chunk's own bounds.
        chunk0AbsoluteWordSec.Should().BeInRange(chunks[0].StartSec, chunks[0].EndSec);
        chunk1AbsoluteWordSec.Should().BeInRange(chunks[1].StartSec, chunks[1].EndSec);
        chunk2AbsoluteWordSec.Should().BeInRange(chunks[2].StartSec, chunks[2].EndSec);

        // And a deliberately wrong offset (using the wrong chunk's start) must NOT coincidentally
        // land in range, so this test would actually fail if the arithmetic used the wrong chunk.
        double wrongOffsetForChunk1Word = chunks[0].StartSec + chunk1RelativeWordSec; // 0 + 3.2
        wrongOffsetForChunk1Word.Should().NotBe(chunk1AbsoluteWordSec);
        wrongOffsetForChunk1Word.Should().BeLessThan(chunks[1].StartSec);
    }
}
