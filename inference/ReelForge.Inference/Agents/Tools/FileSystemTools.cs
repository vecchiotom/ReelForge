using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace ReelForge.Inference.Agents.Tools;

/// <summary>
/// Tool functions for file system operations used by analysis agents.
/// </summary>
public static class FileSystemTools
{
    /// <summary>Reads the directory tree structure from provided file listing data.</summary>
    [Description("Read the file tree structure of a project. Returns directory and file listing.")]
    public static string ReadFileTree([Description("The file listing data to parse")] string fileListingData)
    {
        return fileListingData;
    }

    /// <summary>Reads the content of a specific file from the provided project data.</summary>
    [Description("Read the content of a specific file from the project source.")]
    public static string ReadFileContent(
        [Description("The file path to read")] string filePath,
        [Description("The full project data containing all file contents")] string projectData)
    {
        return $"Reading file: {filePath}\n{projectData}";
    }

    /// <summary>Lists files matching a given extension pattern.</summary>
    [Description("List all files matching a given file extension (e.g., .tsx, .jsx, .css).")]
    public static string ListFilesByExtension(
        [Description("The file extension to filter by (e.g., .tsx)")] string extension,
        [Description("The file listing data to search through")] string fileListingData)
    {
        return $"Files matching *{extension}:\n{fileListingData}";
    }

    /// <summary>Reads package manifest files (package.json, .csproj, etc.).</summary>
    [Description("Read package manifest files to extract dependency information.")]
    public static string ReadPackageManifest(
        [Description("The content of the package manifest file")] string manifestContent)
    {
        return manifestContent;
    }

    /// <summary>Searches for patterns in source code using regex-like matching.</summary>
    [Description("Search for patterns in source code files using a search pattern.")]
    public static string SearchPatterns(
        [Description("The search pattern to look for")] string pattern,
        [Description("The source code content to search through")] string sourceContent)
    {
        return $"Searching for pattern: {pattern}\n{sourceContent}";
    }

    /// <summary>Reads CSS, SCSS, or Tailwind configuration files for style extraction.</summary>
    [Description("Read CSS, SCSS, or Tailwind configuration files to extract design tokens.")]
    public static string ReadStyleConfig(
        [Description("The content of the style configuration file")] string styleContent)
    {
        return styleContent;
    }

    /// <summary>Creates AIFunction instances for all file system tools.</summary>
    public static IEnumerable<AIFunction> CreateAll()
    {
        yield return AIFunctionFactory.Create(ReadFileTree);
        yield return AIFunctionFactory.Create(ReadFileContent);
        yield return AIFunctionFactory.Create(ListFilesByExtension);
        yield return AIFunctionFactory.Create(ReadPackageManifest);
        yield return AIFunctionFactory.Create(SearchPatterns);
        yield return AIFunctionFactory.Create(ReadStyleConfig);
    }
}
