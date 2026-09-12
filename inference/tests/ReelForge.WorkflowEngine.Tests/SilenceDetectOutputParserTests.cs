using System.Collections.Generic;
using FluentAssertions;
using ReelForge.WorkflowEngine.Services.Video;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Covers <see cref="SilenceDetectOutputParser"/> against captured/constructed ffmpeg stderr
/// fixtures — no real ffmpeg process is required (plan §WS2 DoD).
/// </summary>
public class SilenceDetectOutputParserTests
{
    [Fact]
    public void Parse_extracts_a_single_start_end_pair()
    {
        const string stderr = """
            [silencedetect @ 0x55d1a2b3c4e0] silence_start: 12.1023
            [silencedetect @ 0x55d1a2b3c4e0] silence_end: 13.9012 | silence_duration: 1.79885
            """;

        IReadOnlyList<(double StartSec, double EndSec)> spans = SilenceDetectOutputParser.Parse(stderr);

        spans.Should().ContainSingle();
        spans[0].StartSec.Should().BeApproximately(12.1023, 1e-6);
        spans[0].EndSec.Should().BeApproximately(13.9012, 1e-6);
    }

    [Fact]
    public void Parse_extracts_multiple_pairs_interleaved_with_other_ffmpeg_output()
    {
        const string stderr = """
            Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'input.mp4':
            [silencedetect @ 0x1] silence_start: 1.5
            [silencedetect @ 0x1] silence_end: 2.75 | silence_duration: 1.25
            frame=  120 fps=0.0 q=-1.0 size=N/A time=00:00:04.00 bitrate=N/A speed=  10x
            [silencedetect @ 0x1] silence_start: 30.0
            [silencedetect @ 0x1] silence_end: 30.5 | silence_duration: 0.5
            """;

        IReadOnlyList<(double StartSec, double EndSec)> spans = SilenceDetectOutputParser.Parse(stderr);

        spans.Should().HaveCount(2);
        spans[0].Should().Be((1.5, 2.75));
        spans[1].Should().Be((30.0, 30.5));
    }

    [Fact]
    public void Parse_returns_empty_when_no_silence_detected()
    {
        const string stderr = "frame=  300 fps=30 q=-1.0 size=N/A time=00:00:10.00 bitrate=N/A speed=20x";

        IReadOnlyList<(double StartSec, double EndSec)> spans = SilenceDetectOutputParser.Parse(stderr);

        spans.Should().BeEmpty();
    }

    [Fact]
    public void Parse_closes_a_trailing_open_silence_at_the_provided_total_duration()
    {
        const string stderr = "[silencedetect @ 0x1] silence_start: 25.0";

        IReadOnlyList<(double StartSec, double EndSec)> spans =
            SilenceDetectOutputParser.Parse(stderr, totalDurationSec: 30.0);

        spans.Should().ContainSingle();
        spans[0].Should().Be((25.0, 30.0));
    }

    [Fact]
    public void Parse_drops_a_trailing_open_silence_when_no_total_duration_is_provided()
    {
        const string stderr = "[silencedetect @ 0x1] silence_start: 25.0";

        IReadOnlyList<(double StartSec, double EndSec)> spans = SilenceDetectOutputParser.Parse(stderr);

        spans.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ignores_an_unmatched_silence_end_with_no_preceding_start()
    {
        const string stderr = "[silencedetect @ 0x1] silence_end: 5.0 | silence_duration: 1.0";

        IReadOnlyList<(double StartSec, double EndSec)> spans = SilenceDetectOutputParser.Parse(stderr);

        spans.Should().BeEmpty();
    }
}
