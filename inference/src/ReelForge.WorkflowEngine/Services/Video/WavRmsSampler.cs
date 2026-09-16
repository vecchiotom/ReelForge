namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// Pure, unit-testable static class that windows a canonical 16-bit-PCM RIFF/WAVE file (the exact
/// format <see cref="FfmpegAudioExtractor"/> produces: 16 kHz mono signed-16-bit PCM, though this
/// parser reads the real sample rate/channel count from the file's own <c>fmt </c> chunk rather
/// than assuming it) into fixed-size RMS/peak windows. No ffmpeg dependency — operates on bytes
/// already on disk/in memory.
/// </summary>
public static class WavRmsSampler
{
    public sealed record RmsWindow(
        double StartSec, double EndSec, double RmsDbfs, double PeakDbfs,
        /// <summary>
        /// Phase 4 (D5) addition — fraction of consecutive channel-interleaved samples in this
        /// window whose sign flipped, in 0..1. Appended with a default so every pre-existing
        /// <c>new RmsWindow(...)</c> call site keeps compiling.
        /// </summary>
        double ZeroCrossingRate = 0);

    /// <summary>dBFS floor for a silent (all-zero) window, used instead of -Infinity.</summary>
    private const double SilenceFloorDbfs = -96.0;

    /// <summary>
    /// Parses the RIFF/WAVE header, then returns one <see cref="RmsWindow"/> per
    /// <paramref name="windowMs"/> of audio (the last window may be shorter if the sample count
    /// doesn't divide evenly). Only mono/stereo 16-bit PCM is supported (multi-channel samples are
    /// averaged across channels before computing RMS/peak); an unrecognized or malformed header
    /// yields an empty list rather than throwing, matching this feature's "never throws" discipline
    /// for anything that reaches a step executor.
    /// </summary>
    public static IReadOnlyList<RmsWindow> Sample(byte[] wavBytes, int windowMs = 250)
    {
        if (!TryParseHeader(wavBytes, out int sampleRate, out int channels, out int bitsPerSample, out int dataOffset, out int dataLength))
        {
            return [];
        }

        if (bitsPerSample != 16 || channels < 1 || sampleRate <= 0 || windowMs <= 0)
        {
            return [];
        }

        int bytesPerFrame = 2 * channels; // one int16 sample per channel
        int totalFrames = dataLength / bytesPerFrame;
        if (totalFrames <= 0)
        {
            return [];
        }

        int windowFrames = Math.Max(1, (int)Math.Round(sampleRate * (windowMs / 1000.0)));
        var windows = new List<RmsWindow>();

        for (int frameStart = 0; frameStart < totalFrames; frameStart += windowFrames)
        {
            int frameEnd = Math.Min(totalFrames, frameStart + windowFrames);
            double sumSquares = 0;
            int peakAbs = 0;
            long sampleCount = 0;
            long crossings = 0;
            int prevSign = 0;

            for (int f = frameStart; f < frameEnd; f++)
            {
                int frameOffset = dataOffset + f * bytesPerFrame;
                for (int c = 0; c < channels; c++)
                {
                    int byteOffset = frameOffset + c * 2;
                    short sample = (short)(wavBytes[byteOffset] | (wavBytes[byteOffset + 1] << 8));
                    sumSquares += (double)sample * sample;
                    int abs = Math.Abs((int)sample);
                    if (abs > peakAbs)
                    {
                        peakAbs = abs;
                    }

                    int sign = Math.Sign((int)sample);
                    if (sign != 0)
                    {
                        if (prevSign != 0 && sign != prevSign)
                        {
                            crossings++;
                        }

                        prevSign = sign;
                    }

                    sampleCount++;
                }
            }

            double rms = sampleCount > 0 ? Math.Sqrt(sumSquares / sampleCount) : 0.0;
            double startSec = (double)frameStart / sampleRate;
            double endSec = (double)frameEnd / sampleRate;
            double zcr = sampleCount > 1 ? crossings / (double)(sampleCount - 1) : 0.0;

            windows.Add(new RmsWindow(startSec, endSec, ToDbfs(rms), ToDbfs(peakAbs), zcr));
        }

        return windows;
    }

    private static double ToDbfs(double amplitude) =>
        amplitude <= 0 ? SilenceFloorDbfs : Math.Max(SilenceFloorDbfs, 20 * Math.Log10(amplitude / 32768.0));

    /// <summary>
    /// Minimal canonical RIFF/WAVE chunk walk: validates the "RIFF"/"WAVE" magic, then scans
    /// chunks for "fmt " (sample rate/channels/bits) and "data" (the PCM payload bounds). Chunks
    /// are padded to even length per the RIFF spec.
    /// </summary>
    private static bool TryParseHeader(
        byte[] bytes, out int sampleRate, out int channels, out int bitsPerSample, out int dataOffset, out int dataLength)
    {
        sampleRate = 0;
        channels = 0;
        bitsPerSample = 0;
        dataOffset = 0;
        dataLength = 0;

        const int riffHeaderSize = 12;
        if (bytes.Length < riffHeaderSize)
        {
            return false;
        }

        if (bytes[0] != 'R' || bytes[1] != 'I' || bytes[2] != 'F' || bytes[3] != 'F')
        {
            return false;
        }

        if (bytes[8] != 'W' || bytes[9] != 'A' || bytes[10] != 'V' || bytes[11] != 'E')
        {
            return false;
        }

        int pos = riffHeaderSize;
        bool haveFmt = false, haveData = false;

        while (pos + 8 <= bytes.Length)
        {
            char c0 = (char)bytes[pos], c1 = (char)bytes[pos + 1], c2 = (char)bytes[pos + 2], c3 = (char)bytes[pos + 3];
            string chunkId = new([c0, c1, c2, c3]);
            uint chunkSize = ReadUInt32LE(bytes, pos + 4);
            int chunkDataStart = pos + 8;

            if (chunkId == "fmt " && chunkDataStart + 16 <= bytes.Length)
            {
                channels = ReadUInt16LE(bytes, chunkDataStart + 2);
                sampleRate = (int)ReadUInt32LE(bytes, chunkDataStart + 4);
                bitsPerSample = ReadUInt16LE(bytes, chunkDataStart + 14);
                haveFmt = true;
            }
            else if (chunkId == "data")
            {
                long available = bytes.Length - chunkDataStart;
                long declared = chunkSize;
                dataLength = (int)Math.Max(0, Math.Min(declared, available));
                dataOffset = chunkDataStart;
                haveData = true;
            }

            long advance = chunkSize + (chunkSize % 2 == 1 ? 1 : 0);
            long next = chunkDataStart + advance;
            if (next <= pos || next > bytes.Length)
            {
                break;
            }

            pos = (int)next;
        }

        return haveFmt && haveData;
    }

    private static ushort ReadUInt16LE(byte[] bytes, int offset) =>
        (ushort)(bytes[offset] | (bytes[offset + 1] << 8));

    private static uint ReadUInt32LE(byte[] bytes, int offset) =>
        (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24));
}
