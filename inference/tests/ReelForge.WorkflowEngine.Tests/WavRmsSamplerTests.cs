using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using ReelForge.WorkflowEngine.Services.Video;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>Tests <see cref="WavRmsSampler"/> against small synthetic canonical WAV byte arrays.</summary>
public class WavRmsSamplerTests
{
    private const int SampleRate = 16000;

    private static byte[] BuildWav(short[] samples, int sampleRate = SampleRate)
    {
        int dataBytes = samples.Length * 2;

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1); // PCM
        w.Write((short)1); // mono
        w.Write(sampleRate);
        w.Write(sampleRate * 2); // byte rate
        w.Write((short)2); // block align
        w.Write((short)16); // bits per sample
        w.Write("data"u8.ToArray());
        w.Write(dataBytes);

        foreach (short s in samples)
        {
            w.Write(s);
        }

        return ms.ToArray();
    }

    private static short[] Silence(int count) => new short[count];

    private static short[] FullScaleSquareWave(int count, int periodSamples)
    {
        var samples = new short[count];
        for (int i = 0; i < count; i++)
        {
            samples[i] = (i % periodSamples) < periodSamples / 2 ? short.MaxValue : short.MinValue;
        }

        return samples;
    }

    private static short[] FullScaleSineWave(int count, double frequencyHz, int sampleRate = SampleRate)
    {
        var samples = new short[count];
        for (int i = 0; i < count; i++)
        {
            samples[i] = (short)Math.Round(short.MaxValue * Math.Sin(2 * Math.PI * frequencyHz * i / sampleRate));
        }

        return samples;
    }

    [Fact]
    public void Silence_produces_very_negative_dBFS()
    {
        byte[] wav = BuildWav(Silence(SampleRate)); // 1 second
        IReadOnlyList<WavRmsSampler.RmsWindow> windows = WavRmsSampler.Sample(wav, windowMs: 250);

        windows.Should().NotBeEmpty();
        foreach (WavRmsSampler.RmsWindow w in windows)
        {
            w.RmsDbfs.Should().BeLessThan(-80);
            w.PeakDbfs.Should().BeLessThan(-80);
        }
    }

    [Fact]
    public void Full_scale_square_wave_is_close_to_zero_dBFS()
    {
        byte[] wav = BuildWav(FullScaleSquareWave(SampleRate, periodSamples: 32));
        IReadOnlyList<WavRmsSampler.RmsWindow> windows = WavRmsSampler.Sample(wav, windowMs: 250);

        windows.Should().NotBeEmpty();
        foreach (WavRmsSampler.RmsWindow w in windows)
        {
            // A full-scale square wave has RMS == peak == amplitude, so both should sit within a
            // fraction of a dB of 0 dBFS (32767/32768 full scale).
            w.RmsDbfs.Should().BeInRange(-0.5, 0.1);
            w.PeakDbfs.Should().BeInRange(-0.5, 0.1);
        }
    }

    [Fact]
    public void Full_scale_sine_wave_rms_is_close_to_expected_minus_3dB()
    {
        byte[] wav = BuildWav(FullScaleSineWave(SampleRate, frequencyHz: 440));
        IReadOnlyList<WavRmsSampler.RmsWindow> windows = WavRmsSampler.Sample(wav, windowMs: 250);

        windows.Should().NotBeEmpty();
        // RMS of a full-scale sine = amplitude/sqrt(2) => 20*log10(1/sqrt2) ≈ -3.01 dBFS.
        foreach (WavRmsSampler.RmsWindow w in windows)
        {
            w.RmsDbfs.Should().BeInRange(-3.5, -2.5);
        }
    }

    [Fact]
    public void Window_count_and_boundaries_are_correct_for_an_exact_multiple_duration()
    {
        byte[] wav = BuildWav(Silence(SampleRate)); // exactly 1.0s
        IReadOnlyList<WavRmsSampler.RmsWindow> windows = WavRmsSampler.Sample(wav, windowMs: 250);

        windows.Should().HaveCount(4);
        for (int i = 0; i < windows.Count; i++)
        {
            windows[i].StartSec.Should().BeApproximately(i * 0.25, 1e-9);
            windows[i].EndSec.Should().BeApproximately((i + 1) * 0.25, 1e-9);
        }
    }

    [Fact]
    public void Trailing_partial_window_is_included_and_shorter()
    {
        // 1.125s of audio at 250ms windows => 4 full windows + 1 partial (0.125s) window.
        int totalSamples = SampleRate + SampleRate / 8;
        byte[] wav = BuildWav(Silence(totalSamples));
        IReadOnlyList<WavRmsSampler.RmsWindow> windows = WavRmsSampler.Sample(wav, windowMs: 250);

        windows.Should().HaveCount(5);
        windows[4].StartSec.Should().BeApproximately(1.0, 1e-9);
        windows[4].EndSec.Should().BeApproximately(1.125, 1e-9);
    }

    [Fact]
    public void Malformed_header_returns_empty_list_instead_of_throwing()
    {
        byte[] garbage = { 0x00, 0x01, 0x02, 0x03 };
        IReadOnlyList<WavRmsSampler.RmsWindow> windows = WavRmsSampler.Sample(garbage, windowMs: 250);
        windows.Should().BeEmpty();
    }
}
