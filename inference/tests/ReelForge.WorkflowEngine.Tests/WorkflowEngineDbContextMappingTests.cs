using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
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

    [Fact]
    public void WorkflowStep_ExtractConfigJson_is_mapped_as_jsonb()
    {
        // Relational metadata (GetColumnType) is not available on the InMemory provider's
        // runtime model, so a relational provider (Sqlite, no connection ever opened) is used
        // purely to build the model and read its annotations.
        DbContextOptions<WorkflowEngineDbContext> options = new DbContextOptionsBuilder<WorkflowEngineDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        using var db = new WorkflowEngineDbContext(options);

        var entityType = db.Model.FindEntityType(typeof(WorkflowStep));
        var property = entityType?.FindProperty(nameof(WorkflowStep.ExtractConfigJson));

        property.Should().NotBeNull();
        property!.GetColumnType().Should().Be("jsonb");
    }

    [Theory]
    [InlineData(nameof(WorkflowStep.EditRoomConfigJson))]
    [InlineData(nameof(WorkflowStep.GraphicsRoomConfigJson))]
    public void WorkflowStep_room_config_columns_are_mapped_as_jsonb(string propertyName)
    {
        // The exact mistake that broke a live deploy once: a new WorkflowStep config-json column
        // mapped as jsonb on one DbContext but not the other. This test and its
        // InferenceApiDbContextMappingTests twin pin BOTH sides.
        DbContextOptions<WorkflowEngineDbContext> options = new DbContextOptionsBuilder<WorkflowEngineDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        using var db = new WorkflowEngineDbContext(options);

        var entityType = db.Model.FindEntityType(typeof(WorkflowStep));
        var property = entityType?.FindProperty(propertyName);

        property.Should().NotBeNull();
        property!.GetColumnType().Should().Be("jsonb");
    }

    [Theory]
    [InlineData(nameof(WorkflowStepResult.ToolCallsJson))]
    [InlineData(nameof(WorkflowStepResult.ReasoningJson))]
    [InlineData(nameof(WorkflowStepResult.ChatTranscriptJson))]
    public void WorkflowStepResult_diagnostics_columns_are_mapped_as_jsonb(string propertyName)
    {
        DbContextOptions<WorkflowEngineDbContext> options = new DbContextOptionsBuilder<WorkflowEngineDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        using var db = new WorkflowEngineDbContext(options);

        var entityType = db.Model.FindEntityType(typeof(WorkflowStepResult));
        var property = entityType?.FindProperty(propertyName);

        property.Should().NotBeNull();
        property!.GetColumnType().Should().Be("jsonb");
    }

    [Fact]
    public void InferenceProvider_is_excluded_from_engine_migrations()
    {
        DbContextOptions<WorkflowEngineDbContext> options = new DbContextOptionsBuilder<WorkflowEngineDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        using var db = new WorkflowEngineDbContext(options);

        // IsTableExcludedFromMigrations is only populated on the design-time model.
        var designTimeModel = db.GetService<IDesignTimeModel>().Model;
        var entityType = designTimeModel.FindEntityType(typeof(InferenceProvider));

        entityType.Should().NotBeNull();
        entityType!.GetTableName().Should().Be("inference_providers");
        entityType.IsTableExcludedFromMigrations().Should().BeTrue(
            "InferenceProvider is owned by InferenceApiDbContext (Risk R2) - the WorkflowEngine must never try to create this table");
    }
}