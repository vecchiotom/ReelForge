using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ReelForge.Shared.Data.Models;
using ReelForge.WorkflowEngine.Data;
using ReelForge.WorkflowEngine.Execution.Caching;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Coverage for <see cref="ProjectFileFingerprintProvider"/>: identical inventories fingerprint
/// identically regardless of insertion order, and any change to a hashed field (id set, storage
/// key, size, upload time, summary/indexing status) changes the fingerprint.
/// </summary>
public class ProjectFileFingerprintProviderTests
{
    private static WorkflowEngineDbContext NewDb() =>
        new(new DbContextOptionsBuilder<WorkflowEngineDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static ProjectFile MakeFile(Guid id, Guid projectId, string storageKey = "k1", long sizeBytes = 100) => new()
    {
        Id = id,
        ProjectId = projectId,
        OriginalFileName = "a.txt",
        StorageKey = storageKey,
        StorageBucket = "reelforge",
        MimeType = "text/plain",
        SizeBytes = sizeBytes,
        UploadedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        SummaryStatus = SummaryStatus.Done,
        IndexingStatus = FileIndexingStatus.Indexed
    };

    [Fact]
    public async Task Same_files_produce_the_same_fingerprint()
    {
        Guid projectId = Guid.NewGuid();
        Guid fileId = Guid.NewGuid();

        using WorkflowEngineDbContext db1 = NewDb();
        db1.ProjectFiles.Add(MakeFile(fileId, projectId));
        await db1.SaveChangesAsync();
        string fp1 = await new ProjectFileFingerprintProvider(db1).GetFingerprintAsync(projectId, CancellationToken.None);

        using WorkflowEngineDbContext db2 = NewDb();
        db2.ProjectFiles.Add(MakeFile(fileId, projectId));
        await db2.SaveChangesAsync();
        string fp2 = await new ProjectFileFingerprintProvider(db2).GetFingerprintAsync(projectId, CancellationToken.None);

        fp1.Should().Be(fp2);
    }

    [Fact]
    public async Task Fingerprint_is_insertion_order_independent()
    {
        Guid projectId = Guid.NewGuid();
        Guid idA = Guid.Parse("00000000-0000-0000-0000-000000000001");
        Guid idB = Guid.Parse("00000000-0000-0000-0000-000000000002");

        using WorkflowEngineDbContext dbAscending = NewDb();
        dbAscending.ProjectFiles.Add(MakeFile(idA, projectId, "a"));
        dbAscending.ProjectFiles.Add(MakeFile(idB, projectId, "b"));
        await dbAscending.SaveChangesAsync();
        string ascending = await new ProjectFileFingerprintProvider(dbAscending).GetFingerprintAsync(projectId, CancellationToken.None);

        using WorkflowEngineDbContext dbDescending = NewDb();
        dbDescending.ProjectFiles.Add(MakeFile(idB, projectId, "b"));
        dbDescending.ProjectFiles.Add(MakeFile(idA, projectId, "a"));
        await dbDescending.SaveChangesAsync();
        string descending = await new ProjectFileFingerprintProvider(dbDescending).GetFingerprintAsync(projectId, CancellationToken.None);

        ascending.Should().Be(descending);
    }

    [Fact]
    public async Task A_changed_StorageKey_changes_the_fingerprint()
    {
        Guid projectId = Guid.NewGuid();
        Guid fileId = Guid.NewGuid();

        using WorkflowEngineDbContext dbOriginal = NewDb();
        dbOriginal.ProjectFiles.Add(MakeFile(fileId, projectId, storageKey: "original-key"));
        await dbOriginal.SaveChangesAsync();
        string original = await new ProjectFileFingerprintProvider(dbOriginal).GetFingerprintAsync(projectId, CancellationToken.None);

        using WorkflowEngineDbContext dbChanged = NewDb();
        dbChanged.ProjectFiles.Add(MakeFile(fileId, projectId, storageKey: "replaced-key"));
        await dbChanged.SaveChangesAsync();
        string changed = await new ProjectFileFingerprintProvider(dbChanged).GetFingerprintAsync(projectId, CancellationToken.None);

        changed.Should().NotBe(original);
    }

    [Fact]
    public async Task A_changed_SizeBytes_changes_the_fingerprint()
    {
        Guid projectId = Guid.NewGuid();
        Guid fileId = Guid.NewGuid();

        using WorkflowEngineDbContext dbOriginal = NewDb();
        dbOriginal.ProjectFiles.Add(MakeFile(fileId, projectId, sizeBytes: 100));
        await dbOriginal.SaveChangesAsync();
        string original = await new ProjectFileFingerprintProvider(dbOriginal).GetFingerprintAsync(projectId, CancellationToken.None);

        using WorkflowEngineDbContext dbChanged = NewDb();
        dbChanged.ProjectFiles.Add(MakeFile(fileId, projectId, sizeBytes: 999));
        await dbChanged.SaveChangesAsync();
        string changed = await new ProjectFileFingerprintProvider(dbChanged).GetFingerprintAsync(projectId, CancellationToken.None);

        changed.Should().NotBe(original);
    }

    [Fact]
    public async Task A_changed_UploadedAt_changes_the_fingerprint()
    {
        Guid projectId = Guid.NewGuid();
        Guid fileId = Guid.NewGuid();

        using WorkflowEngineDbContext dbOriginal = NewDb();
        ProjectFile original = MakeFile(fileId, projectId);
        dbOriginal.ProjectFiles.Add(original);
        await dbOriginal.SaveChangesAsync();
        string fpOriginal = await new ProjectFileFingerprintProvider(dbOriginal).GetFingerprintAsync(projectId, CancellationToken.None);

        using WorkflowEngineDbContext dbChanged = NewDb();
        ProjectFile changedFile = MakeFile(fileId, projectId);
        changedFile.UploadedAt = original.UploadedAt.AddDays(1);
        dbChanged.ProjectFiles.Add(changedFile);
        await dbChanged.SaveChangesAsync();
        string fpChanged = await new ProjectFileFingerprintProvider(dbChanged).GetFingerprintAsync(projectId, CancellationToken.None);

        fpChanged.Should().NotBe(fpOriginal);
    }

    [Fact]
    public async Task A_changed_SummaryStatus_changes_the_fingerprint()
    {
        Guid projectId = Guid.NewGuid();
        Guid fileId = Guid.NewGuid();

        using WorkflowEngineDbContext dbOriginal = NewDb();
        ProjectFile original = MakeFile(fileId, projectId);
        original.SummaryStatus = SummaryStatus.Pending;
        dbOriginal.ProjectFiles.Add(original);
        await dbOriginal.SaveChangesAsync();
        string fpOriginal = await new ProjectFileFingerprintProvider(dbOriginal).GetFingerprintAsync(projectId, CancellationToken.None);

        using WorkflowEngineDbContext dbChanged = NewDb();
        ProjectFile changedFile = MakeFile(fileId, projectId);
        changedFile.SummaryStatus = SummaryStatus.Done;
        dbChanged.ProjectFiles.Add(changedFile);
        await dbChanged.SaveChangesAsync();
        string fpChanged = await new ProjectFileFingerprintProvider(dbChanged).GetFingerprintAsync(projectId, CancellationToken.None);

        fpChanged.Should().NotBe(fpOriginal);
    }

    [Fact]
    public async Task A_changed_IndexingStatus_changes_the_fingerprint()
    {
        Guid projectId = Guid.NewGuid();
        Guid fileId = Guid.NewGuid();

        using WorkflowEngineDbContext dbOriginal = NewDb();
        ProjectFile original = MakeFile(fileId, projectId);
        original.IndexingStatus = FileIndexingStatus.NotIndexed;
        dbOriginal.ProjectFiles.Add(original);
        await dbOriginal.SaveChangesAsync();
        string fpOriginal = await new ProjectFileFingerprintProvider(dbOriginal).GetFingerprintAsync(projectId, CancellationToken.None);

        using WorkflowEngineDbContext dbChanged = NewDb();
        ProjectFile changedFile = MakeFile(fileId, projectId);
        changedFile.IndexingStatus = FileIndexingStatus.Indexed;
        dbChanged.ProjectFiles.Add(changedFile);
        await dbChanged.SaveChangesAsync();
        string fpChanged = await new ProjectFileFingerprintProvider(dbChanged).GetFingerprintAsync(projectId, CancellationToken.None);

        fpChanged.Should().NotBe(fpOriginal);
    }

    [Fact]
    public async Task An_added_file_changes_the_fingerprint()
    {
        Guid projectId = Guid.NewGuid();

        using WorkflowEngineDbContext dbOneFile = NewDb();
        dbOneFile.ProjectFiles.Add(MakeFile(Guid.NewGuid(), projectId, "only-file"));
        await dbOneFile.SaveChangesAsync();
        string fpOneFile = await new ProjectFileFingerprintProvider(dbOneFile).GetFingerprintAsync(projectId, CancellationToken.None);

        using WorkflowEngineDbContext dbTwoFiles = NewDb();
        dbTwoFiles.ProjectFiles.Add(MakeFile(Guid.NewGuid(), projectId, "first-file"));
        dbTwoFiles.ProjectFiles.Add(MakeFile(Guid.NewGuid(), projectId, "second-file"));
        await dbTwoFiles.SaveChangesAsync();
        string fpTwoFiles = await new ProjectFileFingerprintProvider(dbTwoFiles).GetFingerprintAsync(projectId, CancellationToken.None);

        fpTwoFiles.Should().NotBe(fpOneFile);
    }

    [Fact]
    public async Task An_empty_project_has_a_stable_fingerprint()
    {
        using WorkflowEngineDbContext db = NewDb();
        Guid projectId = Guid.NewGuid();

        string fp1 = await new ProjectFileFingerprintProvider(db).GetFingerprintAsync(projectId, CancellationToken.None);
        string fp2 = await new ProjectFileFingerprintProvider(db).GetFingerprintAsync(projectId, CancellationToken.None);

        fp1.Should().Be(fp2);
        fp1.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public async Task Result_is_memoized_within_the_same_provider_instance()
    {
        Guid projectId = Guid.NewGuid();
        using WorkflowEngineDbContext db = NewDb();
        db.ProjectFiles.Add(MakeFile(Guid.NewGuid(), projectId));
        await db.SaveChangesAsync();

        var provider = new ProjectFileFingerprintProvider(db);
        string first = await provider.GetFingerprintAsync(projectId, CancellationToken.None);

        // Add another file directly to the context WITHOUT going through the provider again — a
        // memoized provider must keep returning the value it already computed for this scope.
        db.ProjectFiles.Add(MakeFile(Guid.NewGuid(), projectId));
        await db.SaveChangesAsync();
        string second = await provider.GetFingerprintAsync(projectId, CancellationToken.None);

        second.Should().Be(first);
    }
}
