using System.Collections.Generic;
using FluentAssertions;
using ReelForge.WorkflowEngine.Services.Video;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Covers <see cref="ShowinfoOutputParser"/> against captured/constructed ffmpeg
/// <c>select,showinfo</c> stderr fixtures — no real ffmpeg process is required (plan §WS2 DoD).
/// </summary>
public class ShowinfoOutputParserTests
{
    [Fact]
    public void ParsePtsTimes_extracts_pts_time_values_from_showinfo_lines()
    {
        const string stderr = """
            [Parsed_showinfo_1 @ 0x1] n:   0 pts:  11250 pts_time:3.75    pos:123456 fmt:yuv420p sar:1/1 s:1920x1080 i:P iskey:1 type:I
            [Parsed_showinfo_1 @ 0x1] n:   1 pts:  27000 pts_time:9       pos:234567 fmt:yuv420p sar:1/1 s:1920x1080 i:P iskey:0 type:P
            """;

        IReadOnlyList<double> pts = ShowinfoOutputParser.ParsePtsTimes(stderr);

        pts.Should().Equal(3.75, 9.0);
    }

    [Fact]
    public void ParsePtsTimes_sorts_ascending_and_dedupes_even_if_input_is_out_of_order()
    {
        const string stderr = """
            [Parsed_showinfo_1 @ 0x1] n: 2 pts_time:9
            [Parsed_showinfo_1 @ 0x1] n: 0 pts_time:3.75
            [Parsed_showinfo_1 @ 0x1] n: 1 pts_time:3.75
            """;

        IReadOnlyList<double> pts = ShowinfoOutputParser.ParsePtsTimes(stderr);

        pts.Should().Equal(3.75, 9.0);
    }

    [Fact]
    public void ParsePtsTimes_skips_lines_with_no_numeric_pts_time()
    {
        const string stderr = """
            [Parsed_showinfo_1 @ 0x1] n: 0 pts_time:N/A
            frame=  10 fps=30
            [Parsed_showinfo_1 @ 0x1] n: 1 pts_time:5.0
            """;

        IReadOnlyList<double> pts = ShowinfoOutputParser.ParsePtsTimes(stderr);

        pts.Should().Equal(5.0);
    }

    [Fact]
    public void ParsePtsTimes_returns_empty_when_no_scene_changes_detected()
    {
        const string stderr = "frame=  300 fps=30 q=-1.0 size=N/A time=00:00:10.00 bitrate=N/A speed=20x";

        IReadOnlyList<double> pts = ShowinfoOutputParser.ParsePtsTimes(stderr);

        pts.Should().BeEmpty();
    }

    [Fact]
    public void BuildShotSpans_produces_contiguous_spans_bracketed_by_zero_and_duration()
    {
        IReadOnlyList<(double StartSec, double EndSec)> spans =
            ShowinfoOutputParser.BuildShotSpans(new List<double> { 3.75, 9.0 }, totalDurationSec: 12.4);

        spans.Should().Equal(
            (0.0, 3.75),
            (3.75, 9.0),
            (9.0, 12.4));
    }

    [Fact]
    public void BuildShotSpans_returns_a_single_shot_spanning_the_whole_file_with_no_boundaries()
    {
        IReadOnlyList<(double StartSec, double EndSec)> spans =
            ShowinfoOutputParser.BuildShotSpans(new List<double>(), totalDurationSec: 20.0);

        spans.Should().ContainSingle();
        spans[0].Should().Be((0.0, 20.0));
    }

    [Fact]
    public void BuildShotSpans_drops_boundaries_at_or_beyond_the_total_duration()
    {
        // A boundary exactly at (or past) totalDurationSec would otherwise produce a
        // zero-length/inverted trailing span from floating-point noise in ffmpeg's own timestamps.
        IReadOnlyList<(double StartSec, double EndSec)> spans =
            ShowinfoOutputParser.BuildShotSpans(new List<double> { 5.0, 10.0, 10.0000001 }, totalDurationSec: 10.0);

        spans.Should().Equal(
            (0.0, 5.0),
            (5.0, 10.0));
    }

    [Fact]
    public void BuildShotSpans_returns_empty_for_a_non_positive_duration()
    {
        IReadOnlyList<(double StartSec, double EndSec)> spans =
            ShowinfoOutputParser.BuildShotSpans(new List<double> { 1.0 }, totalDurationSec: 0.0);

        spans.Should().BeEmpty();
    }
}
