namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// Options for the ffmpeg/ffprobe-backed video editing pipeline (plan §2). Bound from the
/// <c>VideoEditing</c> configuration section (<c>VideoEditing__*</c> env vars in compose).
/// </summary>
public sealed class VideoEditingOptions
{
    public const string SectionName = "VideoEditing";

    /// <summary>
    /// Root directory for per-execution/per-step scratch working files (extracted audio,
    /// intermediate segment files). Must be a volume pre-owned by the container's non-root
    /// <c>$APP_UID</c> (R12) — see the WorkflowEngine Dockerfile.
    /// </summary>
    public string ScratchPath { get; set; } = "/var/tmp/reelforge-video";

    /// <summary>Executable name or path for ffmpeg. Resolved via PATH by default.</summary>
    public string FfmpegPath { get; set; } = "ffmpeg";

    /// <summary>Executable name or path for ffprobe. Resolved via PATH by default.</summary>
    public string FfprobePath { get; set; } = "ffprobe";

    /// <summary>
    /// Maximum number of ffmpeg/ffprobe invocations allowed to run concurrently across the whole
    /// process (R13). Enforced by a single process-wide semaphore in <see cref="IVideoToolRunner"/>.
    /// Default 1: video encoding is CPU-heavy and competes with WorkflowEngine:MaxConcurrency.
    /// </summary>
    public int MaxConcurrentJobs { get; set; } = 1;

    /// <summary>Hard wall-clock timeout, in seconds, for a single VideoAnalyze step's tool invocations.</summary>
    public int AnalyzeTimeoutSeconds { get; set; } = 900;

    /// <summary>Hard wall-clock timeout, in seconds, for a single VideoCompile step's tool invocations.</summary>
    public int CompileTimeoutSeconds { get; set; } = 1800;

    /// <summary>
    /// Font file path passed to drawtext's <c>fontfile=</c> for Phase 3 motion-graphics overlays
    /// (see docs/video-editing.md "Motion graphics (Phase 3)"). Must be a font actually installed
    /// in the image — the WorkflowEngine Dockerfile installs Alpine's <c>font-dejavu</c> package
    /// alongside ffmpeg specifically so this default resolves.
    /// </summary>
    public string FontFilePath { get; set; } = "/usr/share/fonts/dejavu/DejaVuSans.ttf";
}
