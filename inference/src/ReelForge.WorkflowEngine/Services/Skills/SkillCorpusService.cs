using System.Collections.Concurrent;
using ReelForge.Shared.Skills;

namespace ReelForge.WorkflowEngine.Services.Skills;

/// <summary>
/// A skill successfully loaded from the vendored corpus.
/// </summary>
public sealed record LoadedSkillInfo(string Name, string DisplayName, string? Version, int BodyLength);

/// <summary>
/// A skill in <see cref="SkillCatalog.All"/> that failed to load or validate.
/// </summary>
public sealed record SkillLoadFailure(string Name, string Reason);

/// <summary>
/// Snapshot of the vendored skill corpus's load state — backs <c>GET /skills/status</c>.
/// </summary>
public sealed record SkillCorpusStatus(
    IReadOnlyList<LoadedSkillInfo> Loaded,
    IReadOnlyList<SkillLoadFailure> Failed);

public interface ISkillCorpusService
{
    /// <summary>
    /// Loads the full markdown body (frontmatter stripped) of a skill's SKILL.md, or null if the
    /// skill is unknown or failed to load/validate.
    /// </summary>
    Task<string?> LoadSkillBodyAsync(string skillName, CancellationToken ct);

    /// <summary>
    /// Reads a supplementary resource file belonging to a skill, or null if the skill is unknown,
    /// the resource does not exist, or <paramref name="resourcePath"/> would resolve outside that
    /// skill's own directory (path-traversal guard, re-applied on every call — never trust a
    /// caller-supplied path).
    /// </summary>
    Task<string?> ReadSkillResourceAsync(string skillName, string resourcePath, CancellationToken ct);

    /// <summary>Current load/validation snapshot, for the diagnostic endpoint.</summary>
    Task<SkillCorpusStatus> GetStatusAsync(CancellationToken ct);
}

/// <summary>
/// Loads and validates the vendored skill corpus (inference/skills/, copied into the container at
/// <c>Skills:CorpusPath</c>, default <c>/app/skills</c>). Singleton; loads once, lazily, on first
/// use — no TTL/cache-invalidation needed since the corpus is a bundled file tree, not a live
/// fetch (the whole point of replacing the old live-GitHub-fetch RemotionSkillsService).
/// </summary>
public sealed class SkillCorpusService : ISkillCorpusService
{
    private readonly string _corpusRoot;
    private readonly ILogger<SkillCorpusService> _logger;

    // name -> resolved, validated skill directory (absolute, normalized)
    private readonly ConcurrentDictionary<string, string> _skillDirs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, LoadedSkillInfo> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _bodies = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SkillLoadFailure> _failures = [];

    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private volatile bool _loaded_;

    public SkillCorpusService(IConfiguration configuration, ILogger<SkillCorpusService> logger)
    {
        _corpusRoot = configuration["Skills:CorpusPath"] ?? "/app/skills";
        _logger = logger;
    }

    public async Task<string?> LoadSkillBodyAsync(string skillName, CancellationToken ct)
    {
        await EnsureLoadedAsync(ct);
        return _bodies.TryGetValue(skillName, out string? body) ? body : null;
    }

    public async Task<string?> ReadSkillResourceAsync(string skillName, string resourcePath, CancellationToken ct)
    {
        await EnsureLoadedAsync(ct);

        if (string.IsNullOrWhiteSpace(resourcePath) || !_skillDirs.TryGetValue(skillName, out string? skillDir))
            return null;

        string? safePath = ResolveWithinSkillDir(skillDir, resourcePath);
        if (safePath == null)
        {
            _logger.LogWarning(
                "Rejected skill resource read outside skill directory: skill={SkillName} path={ResourcePath}",
                skillName, resourcePath);
            return null;
        }

        if (!File.Exists(safePath))
            return null;

        try
        {
            return await File.ReadAllTextAsync(safePath, ct);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to read skill resource {SkillName}/{ResourcePath}", skillName, resourcePath);
            return null;
        }
    }

    public async Task<SkillCorpusStatus> GetStatusAsync(CancellationToken ct)
    {
        await EnsureLoadedAsync(ct);
        return new SkillCorpusStatus(_loaded.Values.OrderBy(s => s.Name).ToList(), _failures.ToList());
    }

    /// <summary>
    /// Resolves <paramref name="resourcePath"/> relative to <paramref name="skillDir"/>, requiring
    /// the fully-resolved path to still live under that skill's own directory. Returns null (never
    /// throws) on any escape attempt — a rooted path, "..", a symlink-style escape, etc.
    /// </summary>
    private static string? ResolveWithinSkillDir(string skillDir, string resourcePath)
    {
        string combined = Path.Combine(skillDir, resourcePath);
        string fullPath = Path.GetFullPath(combined);
        string normalizedRoot = Path.GetFullPath(skillDir);
        string rootWithSeparator = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;

        return fullPath.StartsWith(rootWithSeparator, StringComparison.Ordinal)
            ? fullPath
            : null;
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_loaded_)
            return;

        await _loadLock.WaitAsync(ct);
        try
        {
            if (_loaded_)
                return;

            LoadCorpus();
            _loaded_ = true;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private void LoadCorpus()
    {
        string normalizedRoot = Path.GetFullPath(_corpusRoot);

        foreach (SkillDescriptor descriptor in SkillCatalog.All)
        {
            try
            {
                string? skillDir = ResolveWithinSkillDir(normalizedRoot, descriptor.RelativePath);
                if (skillDir == null)
                {
                    RecordFailure(descriptor.Name, $"RelativePath '{descriptor.RelativePath}' resolves outside the corpus root.");
                    continue;
                }

                if (!Directory.Exists(skillDir))
                {
                    RecordFailure(descriptor.Name, $"Skill directory not found: {skillDir}");
                    continue;
                }

                string skillMdPath = Path.Combine(skillDir, "SKILL.md");
                if (!File.Exists(skillMdPath))
                {
                    RecordFailure(descriptor.Name, $"SKILL.md not found: {skillMdPath}");
                    continue;
                }

                string raw = File.ReadAllText(skillMdPath);
                (Dictionary<string, string> frontmatter, string body) = ParseFrontmatter(raw);

                if (string.IsNullOrWhiteSpace(body))
                {
                    RecordFailure(descriptor.Name, "SKILL.md has no body content after frontmatter.");
                    continue;
                }

                string? version = frontmatter.GetValueOrDefault("version");

                _skillDirs[descriptor.Name] = skillDir;
                _bodies[descriptor.Name] = body;
                _loaded[descriptor.Name] = new LoadedSkillInfo(descriptor.Name, descriptor.DisplayName, version, body.Length);

                _logger.LogInformation(
                    "Loaded skill '{SkillName}' ({BodyLength} chars, version={Version})",
                    descriptor.Name, body.Length, version ?? "unknown");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                RecordFailure(descriptor.Name, $"Failed to read SKILL.md: {ex.Message}");
            }
        }
    }

    private void RecordFailure(string skillName, string reason)
    {
        _logger.LogWarning("Skill '{SkillName}' failed to load: {Reason}", skillName, reason);
        _failures.Add(new SkillLoadFailure(skillName, reason));
    }

    /// <summary>
    /// Minimal hand-rolled YAML-frontmatter parser: the file must start with a "---" line, end
    /// the frontmatter block with another "---" line, and each frontmatter line in between must be
    /// a simple "key: value" pair (no nesting, lists, or multi-line scalars — SKILL.md frontmatter
    /// never needs any of that). Anything not matching this shape is treated as "malformed" by the
    /// caller (an empty frontmatter dictionary is still valid — SKILL.md files always carry
    /// name/description/version in practice, but nothing here hard-requires a specific key).
    /// </summary>
    private static (Dictionary<string, string> Frontmatter, string Body) ParseFrontmatter(string raw)
    {
        Dictionary<string, string> frontmatter = new(StringComparer.OrdinalIgnoreCase);

        string normalized = raw.Replace("\r\n", "\n");
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
            return (frontmatter, normalized.Trim());

        int closingIndex = normalized.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (closingIndex < 0)
            return (frontmatter, normalized.Trim());

        string frontmatterBlock = normalized[4..closingIndex];
        int bodyStart = normalized.IndexOf('\n', closingIndex + 1);
        string body = bodyStart >= 0 ? normalized[(bodyStart + 1)..] : string.Empty;

        foreach (string line in frontmatterBlock.Split('\n'))
        {
            int colon = line.IndexOf(':');
            if (colon <= 0)
                continue;

            string key = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();
            if (key.Length > 0)
                frontmatter[key] = value;
        }

        return (frontmatter, body.Trim());
    }
}
