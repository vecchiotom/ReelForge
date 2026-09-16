using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using ReelForge.WorkflowEngine.Services.Video;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Pure unit tests for <see cref="ShotAudioAnalyzer.Analyze"/> — no ffmpeg, no executor. See Phase 4
/// plan §5.2/§1 judgment call 7 for why this is a ZCR/crest/noise-floor heuristic (paired with a
/// confidence), not a spectral classifier.
/// </summary>
public class ShotAudioAnalyzerTests
{
    private static IReadOnlyList<double> Constant(double value, int count) =>
        Enumerable.Repeat(value, count).ToList();

    [Fact]
    public void Steady_compressed_low_zcr_audio_is_classified_Music()
    {
        ShotAudioAnalyzer.Result result = ShotAudioAnalyzer.Analyze(
            rmsDbfs: -20, peakDbfs: -14, speechRatio: 0.3,
            windowRmsDbfs: Constant(-20, 5), zeroCrossingRate: 0.10);

        result.CharacterClass.Should().Be("Music");
    }

    [Fact]
    public void Speech_like_high_crest_variable_level_audio_is_classified_Dialogue()
    {
        ShotAudioAnalyzer.Result result = ShotAudioAnalyzer.Analyze(
            rmsDbfs: -18, peakDbfs: 2, speechRatio: 0.9,
            windowRmsDbfs: new List<double> { -10, -25, -15, -30, -12 }, zeroCrossingRate: 0.30);

        result.CharacterClass.Should().Be("Dialogue");
    }

    [Fact]
    public void A_high_noise_floor_with_little_speech_is_classified_Noisy()
    {
        ShotAudioAnalyzer.Result result = ShotAudioAnalyzer.Analyze(
            rmsDbfs: -15, peakDbfs: 2, speechRatio: 0.2,
            windowRmsDbfs: new List<double> { -20, -35, -15, -40, -10 }, zeroCrossingRate: 0.15);

        result.CharacterClass.Should().Be("Noisy");
    }

    [Fact]
    public void Very_quiet_audio_is_classified_Silent_with_full_confidence()
    {
        ShotAudioAnalyzer.Result result = ShotAudioAnalyzer.Analyze(
            rmsDbfs: -60, peakDbfs: -55, speechRatio: 0.5,
            windowRmsDbfs: Constant(-60, 5), zeroCrossingRate: 0.05);

        result.CharacterClass.Should().Be("Silent");
        result.CharacterConfidence.Should().Be(1.0);
    }

    [Fact]
    public void Quiet_low_speech_audio_is_classified_Ambient()
    {
        ShotAudioAnalyzer.Result result = ShotAudioAnalyzer.Analyze(
            rmsDbfs: -38, peakDbfs: -18, speechRatio: 0.2,
            windowRmsDbfs: new List<double> { -50, -49, -48, -50, -47 }, zeroCrossingRate: 0.15);

        result.CharacterClass.Should().Be("Ambient");
    }

    [Fact]
    public void Classification_order_puts_Silent_ahead_of_every_other_rule()
    {
        // Parameters that would otherwise strongly match "Music" (steady, compressed, low ZCR) —
        // Silent must still win because rmsDbfs <= -50.
        ShotAudioAnalyzer.Result result = ShotAudioAnalyzer.Analyze(
            rmsDbfs: -55, peakDbfs: -55, speechRatio: 0.1,
            windowRmsDbfs: Constant(-55, 5), zeroCrossingRate: 0.05);

        result.CharacterClass.Should().Be("Silent");
    }

    [Fact]
    public void Noise_floor_is_the_tenth_percentile_of_the_window_levels_not_the_minimum()
    {
        var windows = new List<double> { -80 }.Concat(Constant(-30, 19)).ToList();

        ShotAudioAnalyzer.Result result = ShotAudioAnalyzer.Analyze(
            rmsDbfs: -30, peakDbfs: -20, speechRatio: 0.5, windowRmsDbfs: windows, zeroCrossingRate: 0.15);

        result.NoiseFloorDbfs.Should().Be(-30);
        result.NoiseFloorDbfs.Should().NotBe(windows.Min());
    }

    [Fact]
    public void Fewer_than_three_windows_falls_back_to_the_shot_rms_as_its_own_floor()
    {
        ShotAudioAnalyzer.Result result = ShotAudioAnalyzer.Analyze(
            rmsDbfs: -25, peakDbfs: -20, speechRatio: 0.5,
            windowRmsDbfs: new List<double> { -40, -10 }, zeroCrossingRate: 0.15);

        result.NoiseFloorDbfs.Should().Be(-25);
    }

    [Theory]
    [InlineData(-60, -55, 0.5, 0.05)]   // Silent
    [InlineData(-20, -14, 0.3, 0.10)]   // Music
    [InlineData(-15, 2, 0.2, 0.15)]     // Noisy
    [InlineData(-38, -18, 0.2, 0.15)]   // Ambient
    [InlineData(-18, 2, 0.9, 0.30)]     // Dialogue
    public void Character_confidence_is_always_within_zero_and_one(
        double rmsDbfs, double peakDbfs, double speechRatio, double zcr)
    {
        ShotAudioAnalyzer.Result result = ShotAudioAnalyzer.Analyze(
            rmsDbfs, peakDbfs, speechRatio, new List<double> { rmsDbfs, rmsDbfs, rmsDbfs }, zcr);

        result.CharacterConfidence.Should().BeInRange(0, 1);
    }
}
