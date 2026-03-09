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
    private const string SkillsBasePath = "skills/remotion/";
    private const string RawBaseUrl = $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/{Branch}/";
    private const string TreeApiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/git/trees/{Branch}?recursive=1";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RemotionSkillsService> _logger;
    private readonly TimeSpan _cacheTtl;

    // Skill index: relative path → description (extracted from first line / filename)
    private readonly ConcurrentDictionary<string, SkillFileEntry> _index = new();
    // Full content cache: relative path → markdown content
    private readonly ConcurrentDictionary<string, CachedContent> _contentCache = new();

    private DateTime _indexLoadedAt = DateTime.MinValue;
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
        string url = RawBaseUrl + SkillsBasePath + relativePath;
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
        if (_index.Count > 0 && DateTime.UtcNow - _indexLoadedAt < _cacheTtl)
            return;

        await _indexLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (_index.Count > 0 && DateTime.UtcNow - _indexLoadedAt < _cacheTtl)
                return;

            await LoadIndexFromGitHubAsync(ct);
        }
        finally
        {
            _indexLock.Release();
        }
    }

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

            _index.Clear();

            foreach (GitHubTreeEntry entry in tree.Tree)
            {
                if (entry.Type != "blob" || !entry.Path.StartsWith(SkillsBasePath))
                    continue;

                string relativePath = entry.Path[SkillsBasePath.Length..];

                // Include .md files and .tsx example files
                if (!relativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    continue;

                string topic = Path.GetFileNameWithoutExtension(relativePath);
                string description = GenerateDescription(topic, relativePath);

                _index[relativePath] = new SkillFileEntry(relativePath, topic, description);
            }

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

    private static string GenerateDescription(string topic, string relativePath)
    {
        if (relativePath == "SKILL.md")
            return "Main Remotion skill overview — index of all available topics";

        return topic switch
        {
            "3d" => "3D content in Remotion using Three.js and React Three Fiber",
            "animations" => "Fundamental animation skills — interpolate, spring, useCurrentFrame",
            "assets" => "Importing images, videos, audio, and fonts into Remotion",
            "audio-visualization" => "Audio visualization — spectrum bars, waveforms, bass-reactive effects",
            "audio" => "Using audio and sound — importing, trimming, volume, speed, pitch",
            "calculate-metadata" => "Dynamically set composition duration, dimensions, and props",
            "can-decode" => "Check if a video can be decoded by the browser",
            "charts" => "Chart and data visualization patterns (bar, pie, line, stock charts)",
            "compositions" => "Defining compositions, stills, folders, default props, dynamic metadata",
            "display-captions" => "Displaying captions and subtitles on video",
            "extract-frames" => "Extract frames from videos at specific timestamps",
            "ffmpeg" => "FFmpeg operations — trimming, silence detection",
            "fonts" => "Loading Google Fonts and local fonts in Remotion",
            "get-audio-duration" => "Getting audio file duration in seconds",
            "get-video-dimensions" => "Getting video width and height",
            "get-video-duration" => "Getting video file duration in seconds",
            "gifs" => "Displaying GIFs synchronized with Remotion's timeline",
            "images" => "Embedding images using the Img component",
            "import-srt-captions" => "Importing SRT caption files",
            "light-leaks" => "Light leak overlay effects",
            "lottie" => "Embedding Lottie animations in Remotion",
            "maps" => "Adding maps using Mapbox with animation",
            "measuring-dom-nodes" => "Measuring DOM element dimensions in Remotion",
            "measuring-text" => "Measuring text dimensions, fitting text to containers",
            "parameters" => "Making videos parametrizable with Zod schema",
            "sequencing" => "Sequencing patterns — delay, trim, limit duration",
            "sfx" => "Sound effects in Remotion",
            "subtitles" => "Subtitle rendering patterns",
            "tailwind" => "Using TailwindCSS in Remotion",
            "text-animations" => "Typography and text animation patterns",
            "timing" => "Interpolation curves — linear, easing, spring animations",
            "transcribe-captions" => "Transcribing and generating captions",
            "transitions" => "Scene transition patterns for Remotion",
            "transparent-videos" => "Rendering video with transparency",
            "trimming" => "Trimming patterns — cut beginning or end of animations",
            "videos" => "Embedding videos — trimming, volume, speed, looping, pitch",
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
