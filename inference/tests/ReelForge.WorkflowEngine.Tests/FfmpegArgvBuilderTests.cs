using System.Globalization;
using FluentAssertions;
using ReelForge.WorkflowEngine.Services.Video;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Asserts exact argv token arrays for every <see cref="FfmpegArgvBuilder"/> method — no real
/// ffmpeg process is required, since these builders are pure string[] construction (plan §WS2
/// DoD).
/// </summary>
/// <remarks>
/// <see cref="BuildSilenceDetectArgs_formats_numbers_with_invariant_culture_even_under_de_DE"/> is
/// the single most important test in this workstream (R8): under a comma-decimal culture such as
/// de-DE, a bare <c>value.ToString()</c> would silently emit e.g. "12,4" instead of "12.4", which
/// ffmpeg then misparses with no error surfaced back to the caller. Every numeric argv token in
/// this feature must route through <see cref="FfmpegArgvFormat"/>, and this test proves it does.
/// </remarks>
public class FfmpegArgvBuilderTests
{
    [Fact]
    public void BuildProbeArgs_produces_the_exact_expected_token_array()
    {
        string[] args = FfmpegArgvBuilder.BuildProbeArgs("/scratch/input.mp4");

        args.Should().Equal(
            "-v", "error",
            "-print_format", "json",
            "-show_format", "-show_streams",
            "-protocol_whitelist", "file",
            "/scratch/input.mp4");
    }

    [Fact]
    public void BuildSilenceDetectArgs_produces_the_exact_expected_token_array()
    {
        string[] args = FfmpegArgvBuilder.BuildSilenceDetectArgs("/scratch/input.mp4", -34.0, 0.5);

        args.Should().Equal(
            "-nostdin", "-hide_banner", "-y",
            "-loglevel", "info",
            "-protocol_whitelist", "file",
            "-i", "/scratch/input.mp4",
            "-af", "silencedetect=noise=-34dB:d=0.5",
            "-f", "null", "-");
    }

    [Fact]
    public void BuildShotDetectArgs_produces_the_exact_expected_token_array()
    {
        string[] args = FfmpegArgvBuilder.BuildShotDetectArgs("/scratch/input.mp4", 0.3);

        args.Should().Equal(
            "-nostdin", "-hide_banner", "-y",
            "-loglevel", "info",
            "-protocol_whitelist", "file",
            "-i", "/scratch/input.mp4",
            "-vf", "select='gt(scene,0.3)',showinfo",
            "-f", "null", "-");
    }

    [Fact]
    public void BuildExtractAudioArgs_with_no_range_produces_the_exact_expected_token_array()
    {
        string[] args = FfmpegArgvBuilder.BuildExtractAudioArgs("/scratch/input.mp4", "/scratch/out.wav");

        args.Should().Equal(
            "-nostdin", "-hide_banner", "-y",
            "-loglevel", "error",
            "-protocol_whitelist", "file",
            "-i", "/scratch/input.mp4",
            "-vn", "-ac", "1", "-ar", "16000", "-sample_fmt", "s16",
            "-f", "wav", "/scratch/out.wav");
    }

    [Fact]
    public void BuildExtractAudioArgs_with_a_range_places_ss_before_i_and_t_after()
    {
        string[] args = FfmpegArgvBuilder.BuildExtractAudioArgs(
            "/scratch/input.mp4", "/scratch/chunk0.wav", startSeconds: 10.5, durationSeconds: 30.0);

        args.Should().Equal(
            "-nostdin", "-hide_banner", "-y",
            "-loglevel", "error",
            "-protocol_whitelist", "file",
            "-ss", "10.5",
            "-i", "/scratch/input.mp4",
            "-t", "30",
            "-vn", "-ac", "1", "-ar", "16000", "-sample_fmt", "s16",
            "-f", "wav", "/scratch/chunk0.wav");
    }

    [Fact]
    public void BuildSilenceDetectArgs_formats_numbers_with_invariant_culture_even_under_de_DE()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            // de-DE renders 12.4 as "12,4" through a culture-sensitive ToString() — if any numeric
            // token here used that instead of FfmpegArgvFormat, this string would contain a comma
            // and ffmpeg would silently misparse the filter graph.
            string[] args = FfmpegArgvBuilder.BuildSilenceDetectArgs("/scratch/input.mp4", -34.5, 0.35);

            args[9].Should().Be("-af");
            string filterArg = args[10];
            filterArg.Should().Be("silencedetect=noise=-34.5dB:d=0.35");
            filterArg.Should().NotContain(",");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void BuildShotDetectArgs_formats_numbers_with_invariant_culture_even_under_de_DE()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            string[] args = FfmpegArgvBuilder.BuildShotDetectArgs("/scratch/input.mp4", 0.42);

            args[9].Should().Be("-vf");
            string filterArg = args[10];
            // The filter syntax itself uses a comma as an argument separator (gt(scene,0.42)) —
            // the real assertion is that the *number* renders with a decimal point ("0.42"), not
            // a decimal comma ("0,42"), under de-DE.
            filterArg.Should().Be("select='gt(scene,0.42)',showinfo");
            filterArg.Should().NotContain("0,42");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void BuildExtractAudioArgs_formats_range_numbers_with_invariant_culture_even_under_de_DE()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            string[] args = FfmpegArgvBuilder.BuildExtractAudioArgs(
                "/scratch/input.mp4", "/scratch/chunk0.wav", startSeconds: 10.75, durationSeconds: 30.25);

            args.Should().Contain("10.75");
            args.Should().Contain("30.25");
            args.Should().NotContain(a => a.Contains(','));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void BuildKeyframeArgs_places_ss_before_i_and_produces_the_exact_expected_token_array()
    {
        string[] args = FfmpegArgvBuilder.BuildKeyframeArgs("/scratch/input.mp4", "/scratch/keyframe-s2.jpg", 12.5, 512);

        args.Should().Equal(
            "-nostdin", "-hide_banner", "-y",
            "-loglevel", "error",
            "-protocol_whitelist", "file",
            "-ss", "12.5",
            "-i", "/scratch/input.mp4",
            "-frames:v", "1",
            "-vf", "scale='min(512,iw)':-2",
            "-f", "image2",
            "-c:v", "mjpeg",
            "-q:v", "4",
            "/scratch/keyframe-s2.jpg");
    }

    [Fact]
    public void BuildKeyframeArgs_formats_numbers_with_invariant_culture_even_under_de_DE()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            string[] args = FfmpegArgvBuilder.BuildKeyframeArgs("/scratch/input.mp4", "/scratch/keyframe.jpg", 10.75, 640);

            // The scale filter's own syntax legitimately uses a comma as an argument separator
            // (min(640,iw)) — the real assertion is that the *numbers* render with decimal points
            // ("10.75", "640"), never a decimal comma, under de-DE.
            args.Should().Contain("10.75");
            args.Should().Contain("scale='min(640,iw)':-2");
            args.Should().NotContain(a => a.Contains("10,75"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void FfmpegArgvFormat_Number_is_invariant_for_int_and_long_overloads_too()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            FfmpegArgvFormat.Number(1000).Should().Be("1000");
            FfmpegArgvFormat.Number(1000L).Should().Be("1000");
            FfmpegArgvFormat.Number(12.4).Should().Be("12.4");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
