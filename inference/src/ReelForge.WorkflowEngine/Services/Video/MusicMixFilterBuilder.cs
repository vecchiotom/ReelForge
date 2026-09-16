namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// A single fully-resolved background-music mix, ready to be rendered into ffmpeg filter syntax by
/// <see cref="MusicMixFilterBuilder"/>. Every field here is already a safe, concrete value —
/// resolved gains (<see cref="BedGainLinear"/>/<see cref="DuckGainLinear"/>), a locally-downloaded
/// and ffprobe-validated track file, and output-timeline lift windows
/// (<see cref="MusicMixPlanner.PlanLiftWindows"/>). Nothing here is trusted from the model beyond
/// the opaque track id it chose (<see cref="TrackId"/>) — every number is computed in C# by
/// <c>VideoCompileStepExecutor.ResolveMusicAsync</c> from the model's enum-word choices
/// (Intensity/Ducking/Fit) or from workflow-author config.
/// </summary>
public sealed record ResolvedMusic(
    string TrackId,
    Guid ProjectFileId,
    string TrackName,
    string LocalPath,
    double TrackDurationSec,
    double OutputDurationSec,
    /// <summary>Where the music input stops playing — the frame-quantized edit length for LoopToFit, or the track's own duration for PlayOnce when it is shorter than the edit.</summary>
    double PlayEndSec,
    /// <summary>Whether <c>-stream_loop -1</c> must be added to this input's argv (LoopToFit with a track shorter than the edit).</summary>
    bool LoopInput,
    double BedGainLinear,
    double DuckGainLinear,
    double RampSec,
    double FadeInSec,
    double FadeOutSec,
    IReadOnlyList<MusicLiftWindow> LiftWindows);

/// <summary>
/// Builds the ffmpeg audio-filter fragments for a resolved background-music mix (see
/// docs/video-editing.md "Background music"): the music input's own trim/format/volume-envelope/
/// fade chain (<see cref="BuildMusicBranch"/>), and the final <c>amix</c> stage that combines it
/// with the dialogue branch (<see cref="BuildMixStage"/>). Pure — no I/O, no ffmpeg. Mirrors
/// <see cref="DrawtextFilterBuilder"/>'s role for Phase 3 motion graphics.
/// </summary>
public static class MusicMixFilterBuilder
{
    /// <summary>
    /// The music input's own branch: <c>[{inputIndex}:a] … [amus]</c>. <c>asetpts=N/SR/TB</c>
    /// right after <c>atrim</c> normalizes the branch's PTS to 0 regardless of container
    /// start-time weirdness or <c>-stream_loop</c> behavior. <c>aformat=…</c> is mandatory (not
    /// cosmetic): <c>amix</c> requires matching sample rate/channel layout across inputs — the
    /// same 48kHz/stereo target the multi-source compile path's own per-span <c>atrim</c> branches
    /// already normalize to.
    /// </summary>
    public static string BuildMusicBranch(int inputIndex, ResolvedMusic music, string outLabel = "[amus]")
    {
        string playEnd = Num(music.PlayEndSec);
        string envelope = BuildVolumeExpression(music);
        string fadeIn = Num(music.FadeInSec);
        string fadeOut = Num(music.FadeOutSec);
        string fadeOutStart = Num(Math.Max(0, music.PlayEndSec - music.FadeOutSec));

        return
            $"[{inputIndex}:a]atrim=end={playEnd},asetpts=N/SR/TB," +
            "aformat=sample_rates=48000:channel_layouts=stereo," +
            $"volume=eval=frame:volume='{envelope}'," +
            $"afade=t=in:st=0:d={fadeIn}," +
            $"afade=t=out:st={fadeOutStart}:d={fadeOut}{outLabel}";
    }

    /// <summary>
    /// The mix stage: <c>[dialogueLabel][amus]amix=… [aout]</c>. <c>normalize=0</c> is
    /// load-bearing, not cosmetic — without it <c>amix</c> halves every input's level, which would
    /// quietly reduce the dialogue itself (the exact opposite of what this feature is for).
    /// <c>duration=first</c> pins the mixed output's length to the DIALOGUE input (always input 1
    /// here), so a looped/infinite music input can never extend the file. <c>dropout_transition=0</c>
    /// avoids a gain re-ramp when the music branch ends before the dialogue does (PlayOnce with a
    /// track shorter than the edit).
    /// </summary>
    public static string BuildMixStage(string dialogueLabel, string musicLabel = "[amus]", string finalLabel = "[aout]") =>
        $"{dialogueLabel}{musicLabel}amix=inputs=2:duration=first:dropout_transition=0:normalize=0{finalLabel}";

    /// <summary>
    /// The <c>volume=</c> value alone — exposed so it can be asserted char-for-char in tests. Zero
    /// lift windows collapse to the bare ducked-gain constant (no trapezoid, no <c>max(</c>). One
    /// window is a single trapezoid. More than one window nests binary <c>max(...)</c> calls
    /// (ffmpeg's <c>eval</c> has no n-ary max) built right-to-left so the final expression reads
    /// <c>max(trap0, max(trap1, max(trap2, …)))</c>.
    /// </summary>
    internal static string BuildVolumeExpression(ResolvedMusic music)
    {
        string duck = Num(music.DuckGainLinear);
        if (music.LiftWindows.Count == 0)
            return duck;

        string delta = Num(music.BedGainLinear - music.DuckGainLinear);
        string trapChain = BuildTrapChain(music.LiftWindows, music.RampSec);
        return $"{duck}+{delta}*{trapChain}";
    }

    private static string BuildTrapChain(IReadOnlyList<MusicLiftWindow> windows, double rampSec)
    {
        string expr = Trapezoid(windows[^1], rampSec);
        for (int i = windows.Count - 2; i >= 0; i--)
            expr = $"max({Trapezoid(windows[i], rampSec)},{expr})";
        return expr;
    }

    /// <summary>0 outside <c>[start, end]</c>, ramping linearly to 1 across <paramref name="rampSec"/> INSIDE each end of the window — the ramp lives inside the window so a lift is never above the ducked level exactly at a boundary.</summary>
    private static string Trapezoid(MusicLiftWindow w, double rampSec)
    {
        string s = Num(w.StartSec);
        string e = Num(w.EndSec);
        string r = Num(Math.Max(0.01, rampSec));
        return $"clip((t-{s})/{r},0,1)*clip(({e}-t)/{r},0,1)";
    }

    private static string Num(double value) => FfmpegArgvFormat.Number(Math.Round(value, 5));
}
