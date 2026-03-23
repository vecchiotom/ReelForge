using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ReelForge.Shared.Data.Models;
using ReelForge.WorkflowEngine.Data;
using System;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

public class WorkflowEngineDbContextMappingTests
{
    [Fact]
    public void ProjectFile_IndexingStatus_is_mapped_with_string_conversion()
    {
        DbContextOptions<WorkflowEngineDbContext> options = new DbContextOptionsBuilder<WorkflowEngineDbContext>()
            .UseInMemoryDatabase("mapping-regression")
            .Options;

        using var db = new WorkflowEngineDbContext(options);

        var entityType = db.Model.FindEntityType(typeof(ProjectFile));
        var property = entityType?.FindProperty(nameof(ProjectFile.IndexingStatus));

        property.Should().NotBeNull();
        property!.GetProviderClrType().Should().Be(typeof(string));
    }

    [Fact]
    public void ProjectFile_SummaryStatus_is_mapped_with_string_conversion()
    {
        DbContextOptions<WorkflowEngineDbContext> options = new DbContextOptionsBuilder<WorkflowEngineDbContext>()
            .UseInMemoryDatabase("mapping-regression-summary")
            .Options;

        using var db = new WorkflowEngineDbContext(options);

        var entityType = db.Model.FindEntityType(typeof(ProjectFile));
        var property = entityType?.FindProperty(nameof(ProjectFile.SummaryStatus));

        property.Should().NotBeNull();
        property!.GetProviderClrType().Should().Be(typeof(string));
    }
}