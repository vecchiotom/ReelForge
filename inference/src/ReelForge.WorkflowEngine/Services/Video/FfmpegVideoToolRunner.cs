using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;

namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// Runs ffmpeg/ffprobe as child processes. Must be registered as a <b>singleton</b> — the
/// concurrency-limiting semaphore (R13) is only process-wide if exactly one instance exists for
/// the lifetime of the WorkflowEngine process.
/// </summary>
public sealed class FfmpegVideoToolRunner : IVideoToolRunner
{
    /// <summary>
    /// Bound on captured stdout/stderr per invocation, so a pathological input (e.g. thousands
    /// of silencedetect/showinfo lines) cannot grow this process's memory unboundedly. Only the
    /// tail is kept — the most recent output is what matters for diagnosing a failure.
    /// </summary>
    private const int MaxCapturedChars = 1_000_000;

    private readonly VideoEditingOptions _options;
    private readonly SemaphoreSlim _concurrencyGate;
    private readonly ILogger<FfmpegVideoToolRunner> _logger;

    public FfmpegVideoToolRunner(IOptions<VideoEditingOptions> options, ILogger<FfmpegVideoToolRunner> logger)
    {
        _options = options.Value;
        _logger = logger;
        int maxConcurrent = Math.Max(1, _options.MaxConcurrentJobs);
        _concurrencyGate = new SemaphoreSlim(maxConcurrent, maxConcurrent);
    }

    public Task<VideoToolResult> RunFfmpegAsync(IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct) =>
        RunAsync(_options.FfmpegPath, args, timeout, ct, onStdOutLine: null);

    public Task<VideoToolResult> RunFfmpegAsync(
        IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct, Action<string> onStdOutLine) =>
        RunAsync(_options.FfmpegPath, args, timeout, ct, onStdOutLine);

    public Task<VideoToolResult> RunFfprobeAsync(IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct) =>
        RunAsync(_options.FfprobePath, args, timeout, ct, onStdOutLine: null);

    private async Task<VideoToolResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> args,
        TimeSpan timeout,
        CancellationToken ct,
        Action<string>? onStdOutLine)
    {
        // The semaphore wait is cancellable via the caller's own token but is NOT counted
        // against the ffmpeg timeout below — a job queued behind MaxConcurrentJobs other jobs
        // must not time out just for having waited its turn (R13).
        await _concurrencyGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await RunProcessAsync(executablePath, args, timeout, ct, onStdOutLine).ConfigureAwait(false);
        }
        finally
        {
            _concurrencyGate.Release();
        }
    }

    private async Task<VideoToolResult> RunProcessAsync(
        string executablePath,
        IReadOnlyList<string> args,
        TimeSpan timeout,
        CancellationToken ct,
        Action<string>? onStdOutLine)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };
        foreach (string arg in args)
            startInfo.ArgumentList.Add(arg);

        using Process process = new() { StartInfo = startInfo, EnableRaisingEvents = true };

        BoundedTail stdout = new(MaxCapturedChars);
        BoundedTail stderr = new(MaxCapturedChars);
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
                return;

            stdout.AppendLine(e.Data);

            // Best-effort only (see IVideoToolRunner's doc comment) — a throwing/slow callback
            // must never be able to break the ffmpeg invocation itself.
            try
            {
                onStdOutLine?.Invoke(e.Data);
            }
            catch
            {
                // Swallowed deliberately.
            }
        };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        bool killedByTimeout = false;

        if (!process.Start())
            throw new InvalidOperationException($"Failed to start process '{executablePath}'.");

        // -nostdin already tells ffmpeg not to read stdin, but we also close our own end of the
        // redirected pipe immediately so nothing this process does can ever block on a prompt.
        process.StandardInput.Close();

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await using CancellationTokenRegistration registration = timeoutCts.Token.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    // If the external token itself was cancelled, this is a real cancellation,
                    // not a timeout — record which one actually happened for the caller.
                    killedByTimeout = !ct.IsCancellationRequested;
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // Process already exited between the HasExited check and Kill — benign race.
            }
        });

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Genuine external cancellation (not our internal timeout) — the process has already
            // been killed by the registration above; propagate so the caller sees a real cancel.
            throw;
        }
        catch (OperationCanceledException)
        {
            // Our own CancelAfter fired: the process was killed by the registration above.
            // Fall through and report TimedOut rather than throwing.
        }

        int exitCode = process.HasExited ? process.ExitCode : -1;
        if (killedByTimeout)
        {
            _logger.LogWarning(
                "ffmpeg/ffprobe invocation timed out after {TimeoutSeconds}s and was killed: {Executable} {Args}",
                timeout.TotalSeconds,
                executablePath,
                string.Join(' ', args));
        }

        return new VideoToolResult(exitCode, stdout.ToString(), stderr.ToString(), killedByTimeout);
    }

    /// <summary>
    /// Keeps only the last <c>maxChars</c> characters appended, dropping the oldest content
    /// first — a bounded tail rather than an unbounded in-memory buffer.
    /// </summary>
    private sealed class BoundedTail
    {
        private readonly int _maxChars;
        private readonly StringBuilder _builder = new();
        private readonly object _gate = new();

        public BoundedTail(int maxChars) => _maxChars = maxChars;

        public void AppendLine(string line)
        {
            lock (_gate)
            {
                _builder.Append(line).Append('\n');
                if (_builder.Length > _maxChars)
                    _builder.Remove(0, _builder.Length - _maxChars);
            }
        }

        public override string ToString()
        {
            lock (_gate)
            {
                return _builder.ToString();
            }
        }
    }
}
