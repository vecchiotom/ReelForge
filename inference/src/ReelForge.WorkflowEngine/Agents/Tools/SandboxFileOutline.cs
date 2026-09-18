using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReelForge.WorkflowEngine.Agents.Tools;

/// <summary>
/// Pure, deterministic locator aid for a sandbox source file: a compact JSON outline (imports plus
/// a capped list of top-level exported symbols with their source line) that lets an agent find
/// where to look before paying to read the whole file. Deliberately built on plain per-line
/// regexes rather than a real TS/TSX parser — it is a heuristic index into a file the agent
/// already trusts (its own prior writes, or project source), not a compiler front-end, and a wrong
/// or missed entry only costs an extra <c>ReadSandboxFileLines</c> call, never correctness. Like
/// <see cref="SandboxTextEditor"/>, this class has NO I/O of its own so it is cheap to unit-test
/// exhaustively.
/// </summary>
public static class SandboxFileOutline
{
    // "import X from 'y'", "import { X, Y } from 'y'", "import 'y'" (side-effect import) — all on
    // one line, which covers the overwhelming majority of real-world TSX import statements. A
    // multi-line import (rare, and still perfectly valid TS) is simply not indexed — again, this
    // is a locator aid, not a parser, so an occasional miss is an acceptable tradeoff for staying
    // a one-line-at-a-time regex.
    private static readonly Regex ImportRegex = new(
        """^\s*import\s+(?:[^;]*?\s+from\s+)?["']([^"']+)["']""",
        RegexOptions.Compiled);

    private static readonly Regex ExportTypeOrInterfaceRegex = new(
        @"^\s*export\s+(type|interface)\s+([A-Za-z_$][\w$]*)",
        RegexOptions.Compiled);

    private static readonly Regex ExportDefaultRegex = new(
        @"^\s*export\s+default\b",
        RegexOptions.Compiled);

    private static readonly Regex ExportConstOrFunctionRegex = new(
        @"^\s*export\s+(const|function)\s+([A-Za-z_$][\w$]*)",
        RegexOptions.Compiled);

    private static readonly Regex CompositionIdRegex = new(
        """id\s*=\s*["']([^"']+)["']""",
        RegexOptions.Compiled);

    /// <summary>Source lines are capped in the "signature" field so one very long line (a minified
    /// import, a huge inline object) cannot blow up the outline's own size.</summary>
    private const int MaxSignatureLength = 160;

    // camelCase so the emitted JSON matches the field names documented for this tool
    // ("totalLines"/"kind"/"signature"/...) rather than the PascalCase C# property names on
    // OutlineSymbol below.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string BuildOutline(string content, int maxEntries = 200)
    {
        content ??= string.Empty;

        List<string> imports = [];
        HashSet<string> seenImports = new(StringComparer.Ordinal);
        List<OutlineSymbol> symbols = [];
        bool truncated = false;

        string[] lines = content.Length == 0
            ? []
            : content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            int lineNumber = i + 1;

            // A single pathological line (adversarial input, a minified blob) must never take
            // down outline generation for the rest of the file — catch per line, not per file, so
            // one bad line costs only that one line's entry.
            try
            {
                Match importMatch = ImportRegex.Match(line);
                if (importMatch.Success)
                {
                    string specifier = importMatch.Groups[1].Value;
                    if (seenImports.Add(specifier))
                        imports.Add(specifier);
                    continue;
                }

                if (symbols.Count >= maxEntries)
                {
                    if (LineDeclaresSymbol(line))
                        truncated = true;
                    continue;
                }

                Match typeMatch = ExportTypeOrInterfaceRegex.Match(line);
                if (typeMatch.Success)
                {
                    symbols.Add(BuildSymbol(lineNumber, typeMatch.Groups[1].Value, typeMatch.Groups[2].Value, line));
                    continue;
                }

                if (ExportDefaultRegex.IsMatch(line))
                {
                    symbols.Add(BuildSymbol(lineNumber, "default", "default", line));
                    continue;
                }

                Match declMatch = ExportConstOrFunctionRegex.Match(line);
                if (declMatch.Success)
                {
                    string keyword = declMatch.Groups[1].Value;
                    string name = declMatch.Groups[2].Value;
                    // "function" is unambiguous; a "const" is only function-like when it is
                    // actually assigned an arrow function on this line — otherwise it is a plain
                    // exported value/constant.
                    bool isFunctionLike = keyword == "function" || line.Contains("=>", StringComparison.Ordinal);
                    string kind = isFunctionLike
                        ? (char.IsUpper(name[0]) ? "component" : "function")
                        : "const";
                    symbols.Add(BuildSymbol(lineNumber, kind, name, line));
                    continue;
                }

                if (line.Contains("<Composition", StringComparison.Ordinal))
                {
                    Match idMatch = CompositionIdRegex.Match(line);
                    string name = idMatch.Success ? idMatch.Groups[1].Value : string.Empty;
                    symbols.Add(BuildSymbol(lineNumber, "composition", name, line));
                }
            }
            catch
            {
                // Best-effort locator aid — skip whatever line broke the regex engine and move on
                // rather than failing the whole outline.
            }
        }

        if (symbols.Count > maxEntries)
        {
            symbols = symbols.Take(maxEntries).ToList();
            truncated = true;
        }

        var outline = new
        {
            totalLines = lines.Length,
            totalChars = content.Length,
            imports,
            symbols,
            truncated
        };

        return JsonSerializer.Serialize(outline, JsonOptions);
    }

    private static bool LineDeclaresSymbol(string line) =>
        ExportTypeOrInterfaceRegex.IsMatch(line)
        || ExportDefaultRegex.IsMatch(line)
        || ExportConstOrFunctionRegex.IsMatch(line)
        || line.Contains("<Composition", StringComparison.Ordinal);

    private static OutlineSymbol BuildSymbol(int line, string kind, string name, string sourceLine)
    {
        string trimmed = sourceLine.Trim();
        string signature = trimmed.Length > MaxSignatureLength
            ? trimmed[..MaxSignatureLength]
            : trimmed;

        return new OutlineSymbol(line, kind, name, signature);
    }

    private sealed record OutlineSymbol(int Line, string Kind, string Name, string Signature);
}
