namespace ReelForge.WorkflowEngine.Services.Video;

/// <summary>
/// A per-execution/per-step working directory under <see cref="VideoEditingOptions.ScratchPath"/>
/// for intermediate ffmpeg artifacts (extracted WAVs, ASR chunk audio, encoded segment files) that
/// never need to be referenced again once the step finishes (plan §1.5 — "working files" have
/// nothing referencing them and are deleted in a <c>finally</c>).
/// </summary>
/// <remarks>
/// The containment check in <see cref="ValidateScratchScope"/> mirrors
/// <c>ProjectFileWorkspace.ValidateProjectScope</c>'s pattern — a hard assertion that every path a
/// step executor touches stays under the scratch root — applied to local filesystem paths instead
/// of S3 storage keys. It guards the same class of bug: a caller-supplied file name containing
/// <c>..</c> or an absolute path escaping the intended sandbox directory.
/// </remarks>
public sealed class VideoScratchSpace : IDisposable
{
    private readonly ILogger? _logger;
    private bool _disposed;

    private VideoScratchSpace(string directoryPath, ILogger? logger)
    {
        DirectoryPath = directoryPath;
        _logger = logger;
    }

    /// <summary>The absolute, fully-resolved path of this scratch directory.</summary>
    public string DirectoryPath { get; }

    /// <summary>
    /// Creates (if necessary) and returns the scratch directory for one step attempt within one
    /// workflow execution: <c>{ScratchPath}/{executionId}/{stepId}/</c>. Guids are used verbatim
    /// as path segments — they cannot contain <c>..</c> or a path separator, so no further
    /// sanitization is needed for these two segments specifically.
    /// </summary>
    public static VideoScratchSpace Create(
        VideoEditingOptions options, Guid executionId, Guid stepId, ILogger? logger = null)
    {
        string root = Path.GetFullPath(options.ScratchPath);
        string directory = Path.GetFullPath(
            Path.Combine(root, executionId.ToString("D"), stepId.ToString("D")));

        ValidateScratchScope(root, directory);
        Directory.CreateDirectory(directory);

        return new VideoScratchSpace(directory, logger);
    }

    /// <summary>
    /// Resolves <paramref name="fileName"/> against this scratch directory and asserts the result
    /// still lies within it — the local-filesystem equivalent of
    /// <c>ProjectFileWorkspace.ValidateProjectScope</c>. Use this for every intermediate file path
    /// (extracted WAV, ASR chunk, encoded segment) rather than concatenating strings directly.
    /// </summary>
    public string GetPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("File name cannot be empty.", nameof(fileName));
        }

        string candidate = Path.GetFullPath(Path.Combine(DirectoryPath, fileName));
        ValidateScratchScope(DirectoryPath, candidate);
        return candidate;
    }

    /// <summary>
    /// Asserts <paramref name="candidatePath"/> is <paramref name="root"/> itself or lies strictly
    /// beneath it. Throws <see cref="UnauthorizedAccessException"/> otherwise — the same exception
    /// type <c>ProjectFileWorkspace.ValidateProjectScope</c> uses for the analogous S3-key check.
    /// </summary>
    public static void ValidateScratchScope(string root, string candidatePath)
    {
        string normalizedRoot = Path.GetFullPath(root);
        string normalizedCandidate = Path.GetFullPath(candidatePath);

        string rootWithSeparator = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;

        bool isRootItself = string.Equals(normalizedCandidate, normalizedRoot, StringComparison.Ordinal);
        bool isUnderRoot = normalizedCandidate.StartsWith(rootWithSeparator, StringComparison.Ordinal);

        if (!isRootItself && !isUnderRoot)
        {
            throw new UnauthorizedAccessException(
                $"Path '{normalizedCandidate}' is outside the video scratch scope '{normalizedRoot}'.");
        }
    }

    /// <summary>
    /// Recursively deletes this scratch directory, swallowing filesystem races (another process
    /// already removed a file, a handle briefly still open) rather than letting cleanup itself
    /// fail a step that otherwise completed successfully.
    /// </summary>
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
        catch (IOException ex)
        {
            _logger?.LogWarning(ex, "Failed to clean up video scratch directory {Directory}", DirectoryPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogWarning(ex, "Failed to clean up video scratch directory {Directory}", DirectoryPath);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Cleanup();
    }
}
