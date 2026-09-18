using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ReelForge.WorkflowEngine.Services.RemotionSkills;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Regression tests for the 2026-09 remotion-dev/skills repo restructure, which broke
/// <see cref="RemotionSkillsService"/> in two ways:
///   1. The path filter (<c>skills/remotion/</c>) no longer matches ANYTHING — the upstream repo
///      now spreads content across sibling "skills/{skill-name}/" directories — so the index
///      loaded permanently empty and every search/read tool call returned "not found".
///   2. The cache guard (<c>_index.Count > 0 &amp;&amp; ...</c>) could never be true while the
///      index was permanently empty (bug 1), so the service re-fetched the entire GitHub tree on
///      EVERY tool call instead of once per TTL, guaranteeing GitHub API rate-limiting under any
///      real use (60 unauthenticated requests/hour/IP).
///
/// The fixture tree below is a representative subset of paths pulled from the live
/// GET https://api.github.com/repos/remotion-dev/skills/git/trees/main?recursive=1 response
/// (fetched 2026-09-17), covering:
///   - each of the five directories RemotionSkillsService now allows (remotion-create,
///     remotion-markup, remotion-render, remotion-captions, remotion-multimedia), including each
///     directory's own SKILL.md (real upstream shape: every skill directory has one),
///   - a nested "remotion-maps" directory duplicated inside remotion-markup (real upstream shape:
///     skills/remotion-markup/remotion-maps/... mirrors the top-level skills/remotion-maps/...),
///     which must be excluded regardless of nesting depth,
///   - the "remotion-best-practices" bundle directory, which re-nests full copies of the other
///     directories (real upstream shape) and must be excluded entirely,
///   - directories deliberately out of scope for a headless codegen pipeline (remotion-maps,
///     remotion-studio),
///   - a non-.md blob and a tree-type entry under skills/, and a stray top-level file — all of
///     which must be filtered out without special-casing.
/// </summary>
public class RemotionSkillsServiceTests
{
    private const string TreeApiUrl = "https://api.github.com/repos/remotion-dev/skills/git/trees/main?recursive=1";
    private const string RawBaseUrl = "https://raw.githubusercontent.com/remotion-dev/skills/main/";

    private const string FixtureTreeJson = """
        {
          "tree": [
            { "path": "README.md", "type": "blob" },
            { "path": "skills", "type": "tree" },
            { "path": "skills/remotion-create", "type": "tree" },
            { "path": "skills/remotion-create/SKILL.md", "type": "blob" },
            { "path": "skills/remotion-create/tailwind.md", "type": "blob" },
            { "path": "skills/remotion-create/video-layout.md", "type": "blob" },
            { "path": "skills/remotion-markup/SKILL.md", "type": "blob" },
            { "path": "skills/remotion-markup/compositions.md", "type": "blob" },
            { "path": "skills/remotion-markup/audio.md", "type": "blob" },
            { "path": "skills/remotion-markup/remotion-maps/REFERENCE.md", "type": "blob" },
            { "path": "skills/remotion-markup/remotion-maps/techniques/cesium/TECHNIQUE.md", "type": "blob" },
            { "path": "skills/remotion-render/SKILL.md", "type": "blob" },
            { "path": "skills/remotion-render/transparent-videos.md", "type": "blob" },
            { "path": "skills/remotion-captions/SKILL.md", "type": "blob" },
            { "path": "skills/remotion-captions/display-captions.md", "type": "blob" },
            { "path": "skills/remotion-multimedia/SKILL.md", "type": "blob" },
            { "path": "skills/remotion-multimedia/get-audio-duration.md", "type": "blob" },
            { "path": "skills/remotion-best-practices/SKILL.md", "type": "blob" },
            { "path": "skills/remotion-best-practices/remotion-markup/compositions.md", "type": "blob" },
            { "path": "skills/remotion-best-practices/assets/remotion-icon.svg", "type": "blob" },
            { "path": "skills/remotion-maps/SKILL.md", "type": "blob" },
            { "path": "skills/remotion-studio/SKILL.md", "type": "blob" }
          ]
        }
        """;

    [Fact]
    public async Task Index_loads_non_empty_from_the_current_upstream_layout()
    {
        (RemotionSkillsService service, FakeHttpMessageHandler _) = CreateService();

        IReadOnlyList<SkillFileEntry> entries = await service.ListSkillsAsync();

        // Exactly the 12 in-scope files from the fixture: 5 directories x (1 SKILL.md + topic
        // files), remotion-maps/best-practices/studio/nested-duplicate entries excluded.
        entries.Should().HaveCount(12);
    }

    [Fact]
    public async Task Search_for_a_real_current_topic_returns_a_result()
    {
        (RemotionSkillsService service, FakeHttpMessageHandler _) = CreateService();

        // "get-audio-duration" is chosen because it doesn't also appear as a substring of any
        // other entry's description (unlike e.g. "compositions", which also shows up inside the
        // remotion-markup SKILL.md overview description) — keeps this a precise single-match
        // assertion rather than an assertion about substring-search behavior in general.
        IReadOnlyList<SkillFileEntry> results = await service.SearchSkillsAsync("get-audio-duration");

        results.Should().ContainSingle();
        results[0].Topic.Should().Be("get-audio-duration");
        results[0].RelativePath.Should().Be("remotion-multimedia/get-audio-duration.md");
    }

    [Fact]
    public async Task Index_excludes_remotion_maps_content_even_when_nested_inside_an_allowed_directory()
    {
        (RemotionSkillsService service, FakeHttpMessageHandler _) = CreateService();

        IReadOnlyList<SkillFileEntry> entries = await service.ListSkillsAsync();

        entries.Should().NotContain(e => e.RelativePath.Contains("remotion-maps"));
    }

    [Fact]
    public async Task Index_excludes_the_remotion_best_practices_bundle_directory()
    {
        (RemotionSkillsService service, FakeHttpMessageHandler _) = CreateService();

        IReadOnlyList<SkillFileEntry> entries = await service.ListSkillsAsync();

        entries.Should().NotContain(e => e.RelativePath.StartsWith("remotion-best-practices"));
    }

    [Fact]
    public async Task Each_allowed_directorys_SKILL_md_gets_a_distinct_topic_no_collision()
    {
        (RemotionSkillsService service, FakeHttpMessageHandler _) = CreateService();

        IReadOnlyList<SkillFileEntry> entries = await service.ListSkillsAsync();

        IReadOnlyList<SkillFileEntry> skillOverviews = entries
            .Where(e => e.RelativePath.EndsWith("/SKILL.md", StringComparison.Ordinal))
            .ToList();

        skillOverviews.Should().HaveCount(5);
        skillOverviews.Select(e => e.Topic).Should().OnlyHaveUniqueItems();
        skillOverviews.Select(e => e.Topic).Should().BeEquivalentTo(
            "remotion-create", "remotion-markup", "remotion-render", "remotion-captions", "remotion-multimedia");
    }

    [Fact]
    public async Task ReadSkill_fetches_content_from_the_correct_raw_url_for_a_nested_topic()
    {
        (RemotionSkillsService service, FakeHttpMessageHandler handler) = CreateService();
        handler.SetRawContent("remotion-markup/compositions.md", "# Compositions\n\nDefine compositions here.");

        string? content = await service.ReadSkillAsync("compositions");

        content.Should().Be("# Compositions\n\nDefine compositions here.");
        handler.RequestedUrls.Should().Contain(RawBaseUrl + "skills/remotion-markup/compositions.md");
    }

    [Fact]
    public async Task Second_call_within_the_TTL_does_not_refetch_the_GitHub_tree()
    {
        // Regression for bug 2: the old cache guard (_index.Count > 0 && ...) could never be true
        // while the index was permanently empty from bug 1, so every tool call re-fetched the
        // whole tree. This proves the fixed guard (an independent "loaded at least once"
        // timestamp) actually caches once the index has successfully loaded.
        (RemotionSkillsService service, FakeHttpMessageHandler handler) = CreateService();

        await service.ListSkillsAsync();
        await service.SearchSkillsAsync("audio");
        await service.ListSkillsAsync();

        handler.TreeApiCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_first_calls_only_fetch_the_tree_once()
    {
        (RemotionSkillsService service, FakeHttpMessageHandler handler) = CreateService();

        await Task.WhenAll(
            service.ListSkillsAsync(),
            service.ListSkillsAsync(),
            service.SearchSkillsAsync("audio"),
            service.ListSkillsAsync());

        handler.TreeApiCallCount.Should().Be(1);
    }

    private static (RemotionSkillsService Service, FakeHttpMessageHandler Handler) CreateService()
    {
        FakeHttpMessageHandler handler = new(TreeApiUrl, FixtureTreeJson);

        Mock<IHttpClientFactory> factoryMock = new();
        factoryMock
            .Setup(f => f.CreateClient("RemotionSkills"))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        RemotionSkillsService service = new(
            factoryMock.Object,
            NullLogger<RemotionSkillsService>.Instance,
            configuration);

        return (service, handler);
    }

    /// <summary>
    /// Minimal <see cref="HttpMessageHandler"/> stub: serves the fixture tree JSON for the
    /// GitHub tree API URL and per-path raw content registered via <see cref="SetRawContent"/>;
    /// anything else 404s. Tracks every requested URL and the tree API's call count specifically.
    /// </summary>
    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _treeApiUrl;
        private readonly string _treeApiResponseJson;
        private readonly Dictionary<string, string> _rawContent = new();

        public List<string> RequestedUrls { get; } = [];
        public int TreeApiCallCount { get; private set; }

        public FakeHttpMessageHandler(string treeApiUrl, string treeApiResponseJson)
        {
            _treeApiUrl = treeApiUrl;
            _treeApiResponseJson = treeApiResponseJson;
        }

        public void SetRawContent(string relativePath, string content) =>
            _rawContent[RawBaseUrl + "skills/" + relativePath] = content;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            RequestedUrls.Add(url);

            if (url == _treeApiUrl)
            {
                TreeApiCallCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_treeApiResponseJson)
                });
            }

            if (_rawContent.TryGetValue(url, out string? content))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(content)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
