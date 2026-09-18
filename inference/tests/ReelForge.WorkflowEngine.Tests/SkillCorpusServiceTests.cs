using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ReelForge.Shared.Skills;
using ReelForge.WorkflowEngine.Services.Skills;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

public class SkillCorpusServiceTests
{
    [Fact]
    public async Task Loads_all_five_vendored_skills_with_no_validation_failures()
    {
        ISkillCorpusService service = CreateServiceOverRealCorpus();

        SkillCorpusStatus status = await service.GetStatusAsync(CancellationToken.None);

        status.Failed.Should().BeEmpty(
            "the real vendored corpus under inference/skills/ should load cleanly — " +
            string.Join("; ", status.Failed.Select(f => $"{f.Name}: {f.Reason}")));
        status.Loaded.Select(s => s.Name).Should().BeEquivalentTo(
            SkillCatalog.All.Select(s => s.Name));
        status.Loaded.Should().OnlyContain(s => s.BodyLength > 0);
    }

    [Fact]
    public async Task LoadSkillBodyAsync_returns_the_markdown_body_with_frontmatter_stripped()
    {
        ISkillCorpusService service = CreateServiceOverRealCorpus();

        string? body = await service.LoadSkillBodyAsync("remotion-markup", CancellationToken.None);

        body.Should().NotBeNullOrWhiteSpace();
        body.Should().NotContain("name: remotion-markup", "the YAML frontmatter block should be stripped from the body");
        body.Should().NotStartWith("---");
    }

    [Fact]
    public async Task LoadSkillBodyAsync_returns_null_for_an_unknown_skill()
    {
        ISkillCorpusService service = CreateServiceOverRealCorpus();

        (await service.LoadSkillBodyAsync("not-a-real-skill", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task ReadSkillResourceAsync_reads_a_real_topic_file()
    {
        ISkillCorpusService service = CreateServiceOverRealCorpus();

        string? content = await service.ReadSkillResourceAsync("remotion-markup", "timing.md", CancellationToken.None);

        content.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("..\\..\\secret.txt")]
    [InlineData("../SKILL.md")]
    public async Task ReadSkillResourceAsync_rejects_a_path_that_escapes_the_skill_directory(string escapingPath)
    {
        ISkillCorpusService service = CreateServiceOverRealCorpus();

        (await service.ReadSkillResourceAsync("remotion-markup", escapingPath, CancellationToken.None))
            .Should().BeNull();
    }

    [Fact]
    public async Task ReadSkillResourceAsync_rejects_an_absolute_path()
    {
        ISkillCorpusService service = CreateServiceOverRealCorpus();

        (await service.ReadSkillResourceAsync("remotion-markup", "/etc/passwd", CancellationToken.None))
            .Should().BeNull();
    }

    [Fact]
    public async Task ReadSkillResourceAsync_returns_null_for_a_nonexistent_resource_under_a_valid_skill()
    {
        ISkillCorpusService service = CreateServiceOverRealCorpus();

        (await service.ReadSkillResourceAsync("remotion-markup", "does-not-exist.md", CancellationToken.None))
            .Should().BeNull();
    }

    [Fact]
    public async Task GetStatusAsync_reports_a_failure_for_a_missing_skill_directory()
    {
        string tempRoot = Directory.CreateTempSubdirectory("skillcorpus-missing-").FullName;
        try
        {
            // Deliberately create none of the five directories — every skill should fail to load.
            ISkillCorpusService service = CreateService(tempRoot);

            SkillCorpusStatus status = await service.GetStatusAsync(CancellationToken.None);

            status.Loaded.Should().BeEmpty();
            status.Failed.Should().HaveCount(SkillCatalog.All.Count);
            status.Failed.Select(f => f.Name).Should().BeEquivalentTo(SkillCatalog.All.Select(s => s.Name));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task GetStatusAsync_reports_a_failure_for_a_skill_missing_its_SKILL_md()
    {
        string tempRoot = Directory.CreateTempSubdirectory("skillcorpus-nomd-").FullName;
        try
        {
            foreach (SkillDescriptor descriptor in SkillCatalog.All)
            {
                string dir = Path.Combine(tempRoot, descriptor.RelativePath);
                Directory.CreateDirectory(dir);
                if (descriptor.Name != "remotion-markup")
                {
                    File.WriteAllText(Path.Combine(dir, "SKILL.md"),
                        $"---\nname: {descriptor.Name}\ndescription: test\nversion: 1.0.0\n---\n\nBody content for {descriptor.Name}.\n");
                }
                // remotion-markup deliberately has no SKILL.md at all.
            }

            ISkillCorpusService service = CreateService(tempRoot);
            SkillCorpusStatus status = await service.GetStatusAsync(CancellationToken.None);

            status.Failed.Should().ContainSingle(f => f.Name == "remotion-markup");
            status.Loaded.Should().HaveCount(SkillCatalog.All.Count - 1);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static ISkillCorpusService CreateService(string corpusRoot)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Skills:CorpusPath"] = corpusRoot })
            .Build();

        return new SkillCorpusService(configuration, NullLogger<SkillCorpusService>.Instance);
    }

    private static ISkillCorpusService CreateServiceOverRealCorpus() => CreateService(ResolveRealSkillsCorpusRoot());

    /// <summary>
    /// Walks up from the test assembly's output directory to find the repo's real
    /// inference/skills/ corpus (identified by its UPSTREAM.md marker file), so these tests
    /// exercise the actual vendored content rather than a synthetic fixture.
    /// </summary>
    private static string ResolveRealSkillsCorpusRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "skills", "UPSTREAM.md");
            if (File.Exists(candidate))
                return Path.Combine(dir.FullName, "skills");

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate inference/skills/UPSTREAM.md above {AppContext.BaseDirectory}.");
    }
}
