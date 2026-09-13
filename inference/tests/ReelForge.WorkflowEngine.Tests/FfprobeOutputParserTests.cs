using FluentAssertions;
using ReelForge.WorkflowEngine.Services.Video;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Covers <see cref="FfprobeOutputParser"/> against captured/constructed fixture JSON — no real
/// ffprobe process is required (plan §WS2 DoD).
/// </summary>
public class FfprobeOutputParserTests
{
    private const string FixtureJson = """
        {
          "streams": [
            {
              "codec_type": "video",
              "codec_name": "h264",
              "width": 1920,
              "height": 1080,
              "r_frame_rate": "30000/1001",
              "duration": "184.320000"
            },
            {
              "codec_type": "audio",
              "codec_name": "aac",
              "sample_rate": "48000"
            }
          ],
          "format": {
            "duration": "184.320000"
          }
        }
        """;

    [Fact]
    public void Parse_extracts_duration_dimensions_and_codecs()
    {
        MediaProbeResult result = FfprobeOutputParser.Parse(FixtureJson);

        result.DurationSec.Should().Be(184.32);
        result.Width.Should().Be(1920);
        result.Height.Should().Be(1080);
        result.VideoCodec.Should().Be("h264");
        result.AudioCodec.Should().Be("aac");
        result.AudioSampleRate.Should().Be(48000);
    }

    [Fact]
    public void Parse_reads_fps_as_exact_rational_without_collapsing_to_double()
    {
        MediaProbeResult result = FfprobeOutputParser.Parse(FixtureJson);

        // 30000/1001 (NTSC ~29.97fps) must survive as an exact numerator/denominator pair (R9) —
        // never rounded to a lossy double like 29.97.
        result.FpsNum.Should().Be(30000);
        result.FpsDen.Should().Be(1001);
    }

    [Fact]
    public void Parse_falls_back_to_stream_duration_when_format_duration_is_absent()
    {
        const string json = """
            {
              "streams": [
                { "codec_type": "video", "codec_name": "h264", "width": 640, "height": 480,
                  "r_frame_rate": "30/1", "duration": "10.5" }
              ],
              "format": {}
            }
            """;

        MediaProbeResult result = FfprobeOutputParser.Parse(json);

        result.DurationSec.Should().Be(10.5);
        result.FpsNum.Should().Be(30);
        result.FpsDen.Should().Be(1);
    }

    [Fact]
    public void Parse_handles_a_video_only_stream_with_no_audio()
    {
        const string json = """
            {
              "streams": [
                { "codec_type": "video", "codec_name": "vp9", "width": 1280, "height": 720,
                  "r_frame_rate": "24/1" }
              ],
              "format": { "duration": "5.0" }
            }
            """;

        MediaProbeResult result = FfprobeOutputParser.Parse(json);

        result.AudioCodec.Should().BeNull();
        result.AudioSampleRate.Should().BeNull();
        result.VideoCodec.Should().Be("vp9");
    }

    [Theory]
    [InlineData("30000/1001", 30000, 1001)]
    [InlineData("30/1", 30, 1)]
    [InlineData("25/1", 25, 1)]
    public void TryParseRational_parses_valid_rationals_exactly(string raw, int expectedNum, int expectedDen)
    {
        bool ok = FfprobeOutputParser.TryParseRational(raw, out int num, out int den);

        ok.Should().BeTrue();
        num.Should().Be(expectedNum);
        den.Should().Be(expectedDen);
    }

    [Theory]
    [InlineData("30/0")]
    [InlineData("not-a-rational")]
    [InlineData("30")]
    [InlineData("")]
    public void TryParseRational_rejects_malformed_or_zero_denominator_values(string raw)
    {
        bool ok = FfprobeOutputParser.TryParseRational(raw, out _, out _);

        ok.Should().BeFalse();
    }
}
