namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>Result of one ffmpeg/ffprobe process invocation.</summary>
public sealed record VideoToolResult(
    int ExitCode,
    string StdOut,
    string StdErr,
    bool TimedOut)
{
    public bool Succeeded => !TimedOut && ExitCode == 0;
}

/// <summary>
/// The single seam every ffmpeg/ffprobe invocation in this feature goes through (plan §2). All
/// implementations must: build argv as a <c>string[]</c> (never a joined command-line string);
/// enforce the caller-supplied timeout on top of, but independent from, the caller's own
/// <see cref="CancellationToken"/>; kill the entire process tree on cancellation or timeout
/// (R16); gate concurrency through a single process-wide semaphore sized by
/// <see cref="VideoEditingOptions.MaxConcurrentJobs"/> (R13), with the semaphore wait itself
/// cancellable and excluded from the timeout; and never buffer unbounded stdout/stderr.
/// </summary>
public interface IVideoToolRunner
{
    Task<VideoToolResult> RunFfmpegAsync(IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct);

    /// <summary>
    /// Same as <see cref="RunFfmpegAsync(IReadOnlyList{string}, TimeSpan, CancellationToken)"/>,
    /// with <paramref name="onStdOutLine"/> invoked synchronously once per stdout line as ffmpeg
    /// produces it (in addition to — not instead of — the line still being captured into
    /// <see cref="VideoToolResult.StdOut"/> as always). A genuine overload rather than an optional
    /// parameter on the 3-arg method, deliberately: existing callers/mocks of the 3-arg overload
    /// are completely unaffected by this addition. Used by <c>VideoCompileStepExecutor</c> to
    /// parse <c>-progress pipe:1</c> key=value lines into a real encode percentage; the callback
    /// must stay cheap and non-throwing, since it runs on the process's async I/O callback thread.
    /// </summary>
    Task<VideoToolResult> RunFfmpegAsync(
        IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct, Action<string> onStdOutLine);

    Task<VideoToolResult> RunFfprobeAsync(IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct);
}
