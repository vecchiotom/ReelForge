using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ReelForge.Shared.Data.Models;
using ReelForge.WorkflowEngine.Data;
using ReelForge.WorkflowEngine.Execution;
using ReelForge.WorkflowEngine.Execution.Caching;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Coverage for <see cref="StepResultCache"/> against an in-memory <see cref="WorkflowEngineDbContext"/>,
/// following the same EFCore.InMemory pattern <see cref="WorkflowExecutorServiceTests"/> already
/// uses. No S3 client is wired (the constructor's <c>s3Client</c> parameter is optional), so
/// <see cref="StepCacheOptions.ValidateStorageArtifacts"/> degrades to "trust the row" throughout —
/// exactly the documented behavior when no S3 client is available.
/// </summary>
public class StepResultCacheTests
{
    private static WorkflowEngineDbContext NewDb() =>
        new(new DbContextOptionsBuilder<WorkflowEngineDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static StepResultCache NewCache(WorkflowEngineDbContext db, StepCacheOptions? options = null) =>
        new(
            db,
            new StepCacheKeyBuilder(),
            new StepCachePolicy(),
            Options.Create(options ?? new StepCacheOptions()),
            NullLogger<StepResultCache>.Instance);

    private static StepCacheKeyInputs Inputs(Guid projectId, string resolvedInput = "input-1") => new()
    {
        ProjectId = projectId,
        StepType = StepType.Extract,
        AgentType = AgentType.ExtractTransform,
        AgentDefinitionId = Guid.NewGuid(),
        AgentInputContextMode = AgentInputContextMode.FullWorkflow,
        MaxIterations = 3,
        ResolvedInput = resolvedInput,
        ProjectFileFingerprint = "fingerprint-1",
        WorkflowDefinitionId = Guid.NewGuid()
    };

    private static StepExecutionResult CompletedResult(string output = "{\"result\":\"ok\"}", int tokensUsed = 42) => new()
    {
        Output = output,
        NextStepIndex = 1,
        Status = StepStatus.Completed,
        TokensUsed = tokensUsed,
        InputTokens = 10,
        OutputTokens = 32,
        DurationMs = 1234
    };

    [Fact]
    public async Task Store_then_get_round_trips_the_result()
    {
        using WorkflowEngineDbContext db = NewDb();
        StepResultCache cache = NewCache(db);
        Guid projectId = Guid.NewGuid();
        StepCacheKeyInputs inputs = Inputs(projectId);
        StepExecutionResult result = CompletedResult();

        await cache.StoreAsync(inputs, result, CancellationToken.None);
        StepCacheLookupResult lookup = await cache.TryGetAsync(inputs, CancellationToken.None);

        lookup.Hit.Should().BeTrue();
        lookup.Entry.Should().NotBeNull();
        lookup.Entry!.Output.Should().Be(result.Output);
        lookup.Entry.TokensUsed.Should().Be(result.TokensUsed);
        lookup.Entry.InputTokens.Should().Be(result.InputTokens);
        lookup.Entry.OutputTokens.Should().Be(result.OutputTokens);
        lookup.Entry.DurationMs.Should().Be(result.DurationMs);
        lookup.Entry.ProjectId.Should().Be(projectId);
    }

    [Fact]
    public async Task A_different_ResolvedInput_produces_a_miss()
    {
        using WorkflowEngineDbContext db = NewDb();
        StepResultCache cache = NewCache(db);
        Guid projectId = Guid.NewGuid();

        await cache.StoreAsync(Inputs(projectId, "input-A"), CompletedResult(), CancellationToken.None);
        StepCacheLookupResult lookup = await cache.TryGetAsync(Inputs(projectId, "input-B"), CancellationToken.None);

        lookup.Hit.Should().BeFalse();
        lookup.Entry.Should().BeNull();
    }

    [Fact]
    public async Task A_different_ProjectId_produces_a_miss_even_with_an_identical_cache_key()
    {
        using WorkflowEngineDbContext db = NewDb();
        StepResultCache cache = NewCache(db);
        StepCacheKeyInputs inputsA = Inputs(Guid.NewGuid());
        StepCacheKeyInputs inputsB = inputsA with { ProjectId = Guid.NewGuid() };

        await cache.StoreAsync(inputsA, CompletedResult(), CancellationToken.None);
        StepCacheLookupResult lookup = await cache.TryGetAsync(inputsB, CancellationToken.None);

        lookup.Hit.Should().BeFalse();
    }

    [Fact]
    public async Task An_expired_entry_is_reported_as_a_miss_and_evicted()
    {
        using WorkflowEngineDbContext db = NewDb();
        StepResultCache cache = NewCache(db);
        Guid projectId = Guid.NewGuid();
        StepCacheKeyInputs inputs = Inputs(projectId);

        // Inserted directly (bypassing StoreAsync, whose TtlHours<=0 means "never expires", not
        // "already expired") so the row's ExpiresAt is unambiguously in the past.
        string cacheKey = new StepCacheKeyBuilder().Build(inputs);
        db.WorkflowStepCacheEntries.Add(new WorkflowStepCacheEntry
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            CacheKey = cacheKey,
            WorkflowDefinitionId = inputs.WorkflowDefinitionId,
            StepType = inputs.StepType,
            AgentType = inputs.AgentType,
            Output = "{\"stale\":true}",
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            ExpiresAt = DateTime.UtcNow.AddHours(-1)
        });
        await db.SaveChangesAsync();

        StepCacheLookupResult lookup = await cache.TryGetAsync(inputs, CancellationToken.None);

        lookup.Hit.Should().BeFalse();
        (await db.WorkflowStepCacheEntries.CountAsync()).Should().Be(0, because: "an expired entry must be evicted, not just skipped");
    }

    [Fact]
    public async Task Disabled_options_always_miss_and_never_write()
    {
        using WorkflowEngineDbContext db = NewDb();
        StepResultCache cache = NewCache(db, new StepCacheOptions { Enabled = false });
        Guid projectId = Guid.NewGuid();
        StepCacheKeyInputs inputs = Inputs(projectId);

        await cache.StoreAsync(inputs, CompletedResult(), CancellationToken.None);
        StepCacheLookupResult lookup = await cache.TryGetAsync(inputs, CancellationToken.None);

        lookup.Hit.Should().BeFalse();
        (await db.WorkflowStepCacheEntries.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Oversized_output_is_not_stored()
    {
        using WorkflowEngineDbContext db = NewDb();
        StepResultCache cache = NewCache(db, new StepCacheOptions { MaxEntryChars = 10 });
        Guid projectId = Guid.NewGuid();
        StepCacheKeyInputs inputs = Inputs(projectId);
        StepExecutionResult result = CompletedResult(output: new string('x', 11));

        await cache.StoreAsync(inputs, result, CancellationToken.None);

        (await db.WorkflowStepCacheEntries.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_failed_result_is_not_stored()
    {
        using WorkflowEngineDbContext db = NewDb();
        StepResultCache cache = NewCache(db);
        Guid projectId = Guid.NewGuid();
        StepCacheKeyInputs inputs = Inputs(projectId);
        StepExecutionResult failed = new()
        {
            Output = "{\"status\":\"failed\"}",
            NextStepIndex = 1,
            Status = StepStatus.Failed,
            ErrorDetails = "boom"
        };

        await cache.StoreAsync(inputs, failed, CancellationToken.None);

        (await db.WorkflowStepCacheEntries.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_null_empty_or_whitespace_output_is_not_stored(string? output)
    {
        using WorkflowEngineDbContext db = NewDb();
        StepResultCache cache = NewCache(db);
        Guid projectId = Guid.NewGuid();
        StepCacheKeyInputs inputs = Inputs(projectId);
        StepExecutionResult result = new()
        {
            Output = output ?? string.Empty,
            NextStepIndex = 1,
            Status = StepStatus.Completed
        };

        await cache.StoreAsync(inputs, result, CancellationToken.None);

        (await db.WorkflowStepCacheEntries.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Storing_twice_for_the_same_key_upserts_rather_than_duplicating()
    {
        using WorkflowEngineDbContext db = NewDb();
        StepResultCache cache = NewCache(db);
        Guid projectId = Guid.NewGuid();
        StepCacheKeyInputs inputs = Inputs(projectId);

        await cache.StoreAsync(inputs, CompletedResult(output: "{\"v\":1}", tokensUsed: 10), CancellationToken.None);
        await cache.StoreAsync(inputs, CompletedResult(output: "{\"v\":2}", tokensUsed: 20), CancellationToken.None);

        (await db.WorkflowStepCacheEntries.CountAsync()).Should().Be(1);
        StepCacheLookupResult lookup = await cache.TryGetAsync(inputs, CancellationToken.None);
        lookup.Entry!.Output.Should().Be("{\"v\":2}");
        lookup.Entry.TokensUsed.Should().Be(20);
    }

    [Fact]
    public async Task HitCount_increments_on_every_accepted_hit()
    {
        using WorkflowEngineDbContext db = NewDb();
        StepResultCache cache = NewCache(db);
        Guid projectId = Guid.NewGuid();
        StepCacheKeyInputs inputs = Inputs(projectId);

        await cache.StoreAsync(inputs, CompletedResult(), CancellationToken.None);

        // EF Core's identity map means every lookup below returns the SAME tracked entity
        // instance, so each HitCount is captured immediately rather than read later off a
        // (by-then-mutated) `.Entry` reference.
        StepCacheLookupResult first = await cache.TryGetAsync(inputs, CancellationToken.None);
        int firstHitCount = first.Entry!.HitCount;

        StepCacheLookupResult second = await cache.TryGetAsync(inputs, CancellationToken.None);
        int secondHitCount = second.Entry!.HitCount;

        StepCacheLookupResult third = await cache.TryGetAsync(inputs, CancellationToken.None);
        int thirdHitCount = third.Entry!.HitCount;

        firstHitCount.Should().Be(1);
        secondHitCount.Should().Be(2);
        thirdHitCount.Should().Be(3);
        third.Entry.LastHitAt.Should().NotBeNull();
    }

    [Fact]
    public async Task LastHitAt_is_null_until_the_first_hit()
    {
        using WorkflowEngineDbContext db = NewDb();
        StepResultCache cache = NewCache(db);
        Guid projectId = Guid.NewGuid();
        StepCacheKeyInputs inputs = Inputs(projectId);

        await cache.StoreAsync(inputs, CompletedResult(), CancellationToken.None);

        WorkflowStepCacheEntry stored = await db.WorkflowStepCacheEntries.SingleAsync();
        stored.LastHitAt.Should().BeNull();
        stored.HitCount.Should().Be(0);
    }
}
