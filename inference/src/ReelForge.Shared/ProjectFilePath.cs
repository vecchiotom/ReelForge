namespace ReelForge.Shared;

public static class ProjectFilePath
{
    public static string NormalizeRelativePath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            throw new ArgumentException("File path cannot be empty.", nameof(rawPath));

        string normalized = rawPath.Replace('\\', '/').Trim();

        while (normalized.StartsWith('/'))
            normalized = normalized[1..];

        string[] segments = normalized
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => segment.Trim())
            .Where(segment => !string.IsNullOrWhiteSpace(segment) && segment != ".")
            .ToArray();

        if (segments.Length == 0)
            throw new ArgumentException("File path cannot be empty.", nameof(rawPath));

        if (segments.Any(segment => segment == ".."))
            throw new InvalidOperationException("Path traversal is not allowed in project file paths.");

        return string.Join('/', segments);
    }

    public static string GetFileName(string normalizedPath)
    {
        int separatorIndex = normalizedPath.LastIndexOf('/');
        return separatorIndex >= 0 ? normalizedPath[(separatorIndex + 1)..] : normalizedPath;
    }

    public static string? GetDirectoryPath(string normalizedPath)
    {
        int separatorIndex = normalizedPath.LastIndexOf('/');
        if (separatorIndex <= 0)
            return null;

        return normalizedPath[..separatorIndex];
    }

    public static string? NormalizeDirectoryPath(string? rawDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(rawDirectoryPath))
            return null;

        string normalized = NormalizeRelativePath(rawDirectoryPath);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    public static string CombineDirectoryAndFileName(string? directoryPath, string fileName)
    {
        string normalizedFileName = NormalizeRelativePath(fileName);
        string? normalizedDirectory = NormalizeDirectoryPath(directoryPath);
        return string.IsNullOrWhiteSpace(normalizedDirectory)
            ? normalizedFileName
            : $"{normalizedDirectory}/{normalizedFileName}";
    }

    public static string BuildStorageFileName(Guid fileId, string originalFileName)
    {
        string extension = Path.GetExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(extension) || extension == ".")
            return fileId.ToString();

        return $"{fileId}{extension.ToLowerInvariant()}";
    }

    public static string BuildStoragePrefix(Guid projectId, string category)
        => $"projects/{projectId}/{category}/";

    public static string BuildStorageKey(Guid projectId, string category, string? directoryPath, string storageFileName)
    {
        string prefix = BuildStoragePrefix(projectId, category);
        string? normalizedDirectory = NormalizeDirectoryPath(directoryPath);
        return string.IsNullOrWhiteSpace(normalizedDirectory)
            ? $"{prefix}{storageFileName}"
            : $"{prefix}{normalizedDirectory}/{storageFileName}";
    }
}
