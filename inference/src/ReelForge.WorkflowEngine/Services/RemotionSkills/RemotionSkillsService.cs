using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReelForge.WorkflowEngine.Services.RemotionSkills;

/// <summary>
/// Fetches and caches Remotion skill markdown files from the official
/// remotion-dev/skills GitHub repository. Files are cached in-memory with
/// a configurable TTL (default 24 hours) and refreshed lazily.
/// </summary>
public sealed class RemotionSkillsService
{
    private const string RepoOwner = "remotion-dev";
    private const string RepoName = "skills";
    private const string Branch = "main";

    // As of 2026-09, the upstream repo no longer has a single "skills/remotion/" directory —
    // content is spread across sibling "skills/{skill-name}/" directories, each with its own
    // SKILL.md plus flat and nested topic .md files (confirmed by fetching the live recursive
    // tree API). SkillsRootPath now matches the whole "skills/" tree; AllowedSkillDirs narrows
    // that down to the directories actually useful for a headless, automated Remotion codegen
    // pipeline. Deliberately excluded:
    //   - remotion-best-practices: a bundle directory that re-nests full COPIES of the other
    //     skill directories' content (e.g. skills/remotion-best-practices/remotion-markup/...),
    //     which would otherwise duplicate every topic and collide topic names.
    //   - remotion-maps: third-party mapping providers (Mapbox/Cesium/MapTiler) unrelated to
    //     promotional-video generation, and itself duplicated as a nested directory inside
    //     remotion-markup (guarded against separately below regardless of this exclusion).
    //   - remotion-saas, remotion-studio, remotion-upgrade, remotion-docs, remotion-interactivity:
    //     SaaS-platform building, interactive Studio UI, version-upgrade guides, and general docs
    //     indexing — none of it applies to agents doing automated, headless Remotion codegen.
    private const string SkillsRootPath = "skills/";

    private static readonly HashSet<string> AllowedSkillDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "remotion-create",
        "remotion-markup",
        "remotion-render",
        "remotion-captions",
        "remotion-multimedia",
    };

    private const string RawBaseUrl = $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/{Branch}/";
    private const string TreeApiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/git/trees/{Branch}?recursive=1";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RemotionSkillsService> _logger;
    private readonly TimeSpan _cacheTtl;

    // Skill index: relative path → description (extracted from first line / filename)
    private readonly ConcurrentDictionary<string, SkillFileEntry> _index = new();
    // Full content cache: relative path → markdown content
    private readonly ConcurrentDictionary<string, CachedContent> _contentCache = new();

    // Tracks "the index has been successfully loaded at least once" independently of _index.Count,
    // so a load that legitimately produces zero entries isn't indistinguishable from "never
    // loaded" and defeat the cache guard below. Stays null until the first successful load.
    private DateTime? _indexLoadedAt;
    private readonly SemaphoreSlim _indexLock = new(1, 1);

    public RemotionSkillsService(
        IHttpClientFactory httpClientFactory,
        ILogger<RemotionSkillsService> logger,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _cacheTtl = TimeSpan.FromHours(
            configuration.GetValue("RemotionSkills:CacheTtlHours", 24));
    }

    /// <summary>
    /// Returns the skill index — a list of available skill files with their topic names.
    /// </summary>
    public async Task<IReadOnlyList<SkillFileEntry>> ListSkillsAsync(CancellationToken ct = default)
    {
        await EnsureIndexLoadedAsync(ct);
        return _index.Values.OrderBy(e => e.Topic).ToList();
    }

    /// <summary>
    /// Searches the skill index for entries matching the query (case-insensitive substring match
    /// against topic name and description).
    /// </summary>
    public async Task<IReadOnlyList<SkillFileEntry>> SearchSkillsAsync(string query, CancellationToken ct = default)
    {
        await EnsureIndexLoadedAsync(ct);

        if (string.IsNullOrWhiteSpace(query))
            return _index.Values.OrderBy(e => e.Topic).ToList();

        string q = query.Trim();
        return _index.Values
            .Where(e => e.Topic.Contains(q, StringComparison.OrdinalIgnoreCase)
                     || e.Description.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Topic)
            .ToList();
    }

    /// <summary>
    /// Reads the full markdown content of a skill file by topic name or relative path.
    /// </summary>
    public async Task<string?> ReadSkillAsync(string topicOrPath, CancellationToken ct = default)
    {
        await EnsureIndexLoadedAsync(ct);

        // Resolve by topic name first, then by path
        string? relativePath = ResolveSkillPath(topicOrPath);
        if (relativePath == null)
            return null;

        // Check content cache
        if (_contentCache.TryGetValue(relativePath, out CachedContent? cached)
            && DateTime.UtcNow - cached.FetchedAt < _cacheTtl)
        {
            return cached.Content;
        }

        // Fetch from GitHub raw
        string url = RawBaseUrl + SkillsRootPath + relativePath;
        try
        {
            using HttpClient client = CreateClient();
            string content = await client.GetStringAsync(url, ct);

            _contentCache[relativePath] = new CachedContent(content, DateTime.UtcNow);
            return content;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Remotion skill file: {Path}", relativePath);
            return null;
        }
    }

    private string? ResolveSkillPath(string topicOrPath)
    {
        // Direct path match (e.g. "rules/animations.md" or "SKILL.md")
        if (_index.ContainsKey(topicOrPath))
            return topicOrPath;

        // Topic name match (e.g. "animations", "3d", "transitions")
        SkillFileEntry? entry = _index.Values
            .FirstOrDefault(e => e.Topic.Equals(topicOrPath, StringComparison.OrdinalIgnoreCase));
        return entry?.RelativePath;
    }

    private async Task EnsureIndexLoadedAsync(CancellationToken ct)
    {
        if (IsIndexFresh())
            return;

        await _indexLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (IsIndexFresh())
                return;

            await LoadIndexFromGitHubAsync(ct);
        }
        finally
        {
            _indexLock.Release();
        }
    }

    // "Fresh" means loaded at least once, within the TTL — deliberately independent of
    // _index.Count so an index that legitimately loaded as empty still caches (and doesn't
    // hot-loop refetching the GitHub tree on every tool call).
    private bool IsIndexFresh() =>
        _indexLoadedAt.HasValue && DateTime.UtcNow - _indexLoadedAt.Value < _cacheTtl;

    private async Task LoadIndexFromGitHubAsync(CancellationToken ct)
    {
        _logger.LogInformation("Loading Remotion skills index from GitHub...");

        try
        {
            using HttpClient client = CreateClient();
            string json = await client.GetStringAsync(TreeApiUrl, ct);

            GitHubTreeResponse? tree = JsonSerializer.Deserialize<GitHubTreeResponse>(json);
            if (tree?.Tree == null)
            {
                _logger.LogWarning("Failed to deserialize GitHub tree response");
                return;
            }

            ConcurrentDictionary<string, SkillFileEntry> newIndex = new();

            foreach (GitHubTreeEntry entry in tree.Tree)
            {
                if (entry.Type != "blob" || !entry.Path.StartsWith(SkillsRootPath, StringComparison.Ordinal))
                    continue;

                string relativePath = entry.Path[SkillsRootPath.Length..];

                if (!relativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    continue;

                string[] segments = relativePath.Split('/');
                if (segments.Length < 2)
                    continue; // stray top-level file directly under skills/, not a skill doc

                string skillDir = segments[0];
                if (!AllowedSkillDirs.Contains(skillDir))
                    continue;

                // Guard against remotion-maps content leaking in nested under an allowed
                // directory (e.g. skills/remotion-markup/remotion-maps/...) — out of scope
                // regardless of nesting depth, same rationale as the top-level exclusion above.
                if (segments.Any(s => s.Equals("remotion-maps", StringComparison.OrdinalIgnoreCase)))
                    continue;

                string fileNameNoExt = Path.GetFileNameWithoutExtension(relativePath);

                // Every allowed directory has its own SKILL.md overview file, so the plain
                // filename ("SKILL") would collide across directories when used as a topic name.
                // Disambiguate by using the directory name itself as the topic for those files.
                string topic = fileNameNoExt.Equals("SKILL", StringComparison.OrdinalIgnoreCase)
                    ? skillDir
                    : fileNameNoExt;

                string description = GenerateDescription(topic, fileNameNoExt, skillDir);

                newIndex[relativePath] = new SkillFileEntry(relativePath, topic, description);
            }

            _index.Clear();
            foreach (KeyValuePair<string, SkillFileEntry> kvp in newIndex)
                _index[kvp.Key] = kvp.Value;

            _indexLoadedAt = DateTime.UtcNow;
            _logger.LogInformation("Loaded {Count} Remotion skill files", _index.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Remotion skills index from GitHub");
        }
    }

    private HttpClient CreateClient()
    {
        HttpClient client = _httpClientFactory.CreateClient("RemotionSkills");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ReelForge-WorkflowEngine/1.0");
        return client;
    }

    private static string GenerateDescription(string topic, string fileNameNoExt, string skillDir)
    {
        // Each allowed directory's own SKILL.md is the overview for that directory; give each
        // one a distinct, accurate blurb rather than one generic line shared across all five.
        if (fileNameNoExt.Equals("SKILL", StringComparison.OrdinalIgnoreCase))
        {
            return skillDir switch
            {
                "remotion-create" => "Scaffolding a new Remotion project and composition",
                "remotion-markup" => "Content, animation, and effects best practices — the core React markup skill (compositions, timing, transitions, audio, text, and more)",
                "remotion-render" => "Exporting/rendering a Remotion video — rendering strategy and transparent output",
                "remotion-captions" => "Transcribing, displaying, and animating captions",
                "remotion-multimedia" => "Reading audio/video duration and dimensions via Mediabunny",
                _ => $"Overview of the {skillDir} skill directory"
            };
        }

        return topic switch
        {
            "3d" => "3D content in Remotion using Three.js and React Three Fiber",
            "audio-visualization" => "Audio visualization — spectrum bars, waveforms, bass-reactive effects",
            "audio" => "Using audio and sound — importing, trimming, volume, speed, pitch",
            "calculate-metadata" => "Dynamically set composition duration, dimensions, and props",
            "compositions" => "Defining compositions, stills, folders, default props, dynamic metadata",
            "cropping" => "Cropping components with cropLeft/cropRight/cropTop/cropBottom props",
            "display-captions" => "Displaying captions and subtitles on video",
            "effects" => "Canvas/WebGL visual effects using effects arrays and createEffect()",
            "embedding-videos" => "Embedding videos — trimming, volume, speed, looping, pitch",
            "ffmpeg" => "FFmpeg operations for Remotion pipelines",
            "gifs" => "Displaying GIFs synchronized with Remotion's timeline",
            "google-fonts" => "Loading Google Fonts in Remotion",
            "html-in-canvas" => "Rendering children into a <canvas> for Canvas 2D/WebGL post-processing",
            "images" => "Embedding images using the Img component",
            "import-srt-captions" => "Importing SRT caption files",
            "get-audio-duration" => "Getting audio file duration in seconds",
            "get-video-dimensions" => "Getting video width and height",
            "get-video-duration" => "Getting video file duration in seconds",
            "light-leaks" => "Light leak overlay effects",
            "local-fonts" => "Loading local font files via @remotion/fonts",
            "lottie" => "Embedding Lottie animations in Remotion",
            "measuring-dom-nodes" => "Measuring DOM element dimensions in Remotion",
            "measuring-text" => "Measuring text dimensions, fitting text to containers",
            "multi-scene-video" => "Structuring a multi-scene video — one file per scene",
            "parameters" => "Making videos parametrizable with a Zod schema",
            "sequencing" => "Sequencing patterns — delay, trim, limit duration",
            "sfx" => "Sound effects in Remotion",
            "silence-detection" => "Adaptive silence detection for video/audio using FFmpeg loudnorm and silencedetect",
            "tailwind" => "Using TailwindCSS in Remotion",
            "text-highlights" => "Animated text highlights and hand-drawn annotations via @remotion/rough-notation",
            "timing" => "Interpolation curves — linear, easing, spring animations",
            "transcribe-captions" => "Transcribing and generating captions",
            "transitions" => "Scene transition patterns for Remotion",
            "transparent-videos" => "Rendering video with transparency",
            "video-editing" => "Bare-bones video editing in the Remotion Studio — independently positioned vs. ripple-edited clips",
            "video-layout" => "Designing video layouts — framing, safe areas, minimum on-screen content sizes",
            "voiceover" => "AI-generated voiceover using ElevenLabs TTS",
            _ => $"Remotion skill: {topic}"
        };
    }

    // --- GitHub API response models ---

    private sealed class GitHubTreeResponse
    {
        [JsonPropertyName("tree")]
        public List<GitHubTreeEntry> Tree { get; set; } = [];
    }

    private sealed class GitHubTreeEntry
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;
    }

    private sealed record CachedContent(string Content, DateTime FetchedAt);
}

/// <summary>
/// Represents an entry in the Remotion skills index.
/// </summary>
public sealed record SkillFileEntry(string RelativePath, string Topic, string Description);
