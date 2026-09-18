using ReelForge.Shared.Workflows;

namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// A resolved whole-piece fade-in/fade-out envelope — already guarded against overlapping (a
/// fade-in and fade-out together exceeding the piece's own duration), exactly like
/// <c>VideoCompileStepExecutor.ResolveMusicAsync</c>'s own <c>fadeInSec + fadeOutSec > playEndSec</c>
/// guard for the background-music track. Video and audio are guarded independently since
/// <see cref="VideoCompileStepConfig.ProgramFadeInMs"/>/<see cref="VideoCompileStepConfig.ProgramFadeOutMs"/>
/// and <see cref="VideoCompileStepConfig.ProgramAudioFadeInMs"/>/<see cref="VideoCompileStepConfig.ProgramAudioFadeOutMs"/>
/// are independent config values.
/// </summary>
public sealed record ProgramFadeResolved(double VideoInSec, double VideoOutSec, double AudioInSec, double AudioOutSec, string Color);

/// <summary>
/// Builds the whole-finished-piece fade-in/fade-out filter fragments (see docs/video-editing.md
/// "Cut transitions") — the video fade is always the LAST video filter stage (after overlays), the
/// audio fade always the LAST audio filter stage (after the music mix). Pure — no I/O, no ffmpeg.
/// </summary>
public static class ProgramEnvelopeFilterBuilder
{
    private static string Num(double value) => FfmpegArgvFormat.Number(Math.Round(value, 5));

    /// <summary>True when any of the four <c>Program*FadeMs</c> config values is greater than 0 — the executor only emits the <c>programFade</c> output node and applies this stage at all when this is true.</summary>
    public static bool IsEnabled(VideoCompileStepConfig config) =>
        config.ProgramFadeInMs > 0 || config.ProgramFadeOutMs > 0 ||
        config.ProgramAudioFadeInMs > 0 || config.ProgramAudioFadeOutMs > 0;

    public static ProgramFadeResolved Resolve(VideoCompileStepConfig config, double totalSec)
    {
        double videoIn = Math.Max(0, config.ProgramFadeInMs) / 1000.0;
        double videoOut = Math.Max(0, config.ProgramFadeOutMs) / 1000.0;
        double audioIn = Math.Max(0, config.ProgramAudioFadeInMs) / 1000.0;
        double audioOut = Math.Max(0, config.ProgramAudioFadeOutMs) / 1000.0;

        if (totalSec > 0 && videoIn + videoOut > totalSec)
        {
            double collapsed = totalSec / 3.0;
            videoIn = collapsed;
            videoOut = collapsed;
        }

        if (totalSec > 0 && audioIn + audioOut > totalSec)
        {
            double collapsed = totalSec / 3.0;
            audioIn = collapsed;
            audioOut = collapsed;
        }

        return new ProgramFadeResolved(videoIn, videoOut, audioIn, audioOut, config.ProgramFadeColor);
    }

    /// <summary>
    /// The video fade suffix (leading comma, ready to append directly after the last existing
    /// video filter stage — overlays or the cut/transition stage when there are none). Empty when
    /// neither <see cref="ProgramFadeResolved.VideoInSec"/> nor <see cref="ProgramFadeResolved.VideoOutSec"/> is positive.
    /// </summary>
    public static string BuildVideoFadeSuffix(ProgramFadeResolved fade, double totalSec)
    {
        List<string> parts = new();

        if (fade.VideoInSec > 0)
            parts.Add($"fade=t=in:st=0:d={Num(fade.VideoInSec)}:color={fade.Color}");

        if (fade.VideoOutSec > 0)
        {
            parts.Add(
                $"fade=t=out:st={Num(Math.Max(0, totalSec - fade.VideoOutSec))}:d={Num(fade.VideoOutSec)}:color={fade.Color}");
        }

        return parts.Count == 0 ? "" : "," + string.Join(",", parts);
    }

    /// <summary>The audio analogue of <see cref="BuildVideoFadeSuffix"/> — appended after the last existing audio filter stage (the music mix, or the dialogue cut/transition stage when music is off).</summary>
    public static string BuildAudioFadeSuffix(ProgramFadeResolved fade, double totalSec)
    {
        List<string> parts = new();

        if (fade.AudioInSec > 0)
            parts.Add($"afade=t=in:st=0:d={Num(fade.AudioInSec)}:curve=tri");

        if (fade.AudioOutSec > 0)
            parts.Add($"afade=t=out:st={Num(Math.Max(0, totalSec - fade.AudioOutSec))}:d={Num(fade.AudioOutSec)}:curve=tri");

        return parts.Count == 0 ? "" : "," + string.Join(",", parts);
    }
}
