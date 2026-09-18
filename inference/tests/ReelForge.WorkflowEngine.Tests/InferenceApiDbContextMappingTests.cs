using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ReelForge.Inference.Api.Data;
using ReelForge.Shared.Data.Models;
using System.Linq;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Regression coverage for the WS1 InferenceProvider.Capability addition (plan §1.4 / R4).
/// Mirrors WorkflowEngineDbContextMappingTests' approach: a relational provider (Sqlite, no
/// connection ever opened) is used purely to build the model and read its annotations, since
/// GetColumnType()/index metadata is not available on the InMemory provider's runtime model.
/// </summary>
public class InferenceApiDbContextMappingTests
{
    private static InferenceApiDbContext CreateContext()
    {
        DbContextOptions<InferenceApiDbContext> options = new DbContextOptionsBuilder<InferenceApiDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        return new InferenceApiDbContext(options);
    }

    [Fact]
    public void InferenceProvider_Capability_is_mapped_with_string_conversion_and_defaults_to_Chat()
    {
        using InferenceApiDbContext db = CreateContext();

        var entityType = db.Model.FindEntityType(typeof(InferenceProvider));
        var property = entityType?.FindProperty(nameof(InferenceProvider.Capability));

        property.Should().NotBeNull();
        property!.GetProviderClrType().Should().Be(typeof(string));
        property.GetDefaultValue().Should().Be(InferenceProviderCapability.Chat);
    }

    [Fact]
    public void AgentDefinition_AssignedSkillsJson_is_mapped_as_jsonb()
    {
        using InferenceApiDbContext db = CreateContext();

        var entityType = db.Model.FindEntityType(typeof(AgentDefinition));
        var property = entityType?.FindProperty(nameof(AgentDefinition.AssignedSkillsJson));

        property.Should().NotBeNull();
        property!.GetColumnType().Should().Be("jsonb");
    }

    [Theory]
    [InlineData(nameof(WorkflowStep.EditRoomConfigJson))]
    [InlineData(nameof(WorkflowStep.GraphicsRoomConfigJson))]
    public void WorkflowStep_room_config_columns_are_mapped_as_jsonb(string propertyName)
    {
        // The exact mistake that broke a live deploy once: a new WorkflowStep config-json column
        // mapped as jsonb on the engine's DbContext but forgotten on this one (or vice versa).
        // This test and its WorkflowEngineDbContextMappingTests twin pin BOTH sides.
        using InferenceApiDbContext db = CreateContext();

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
        using InferenceApiDbContext db = CreateContext();

        var entityType = db.Model.FindEntityType(typeof(WorkflowStepResult));
        var property = entityType?.FindProperty(propertyName);

        property.Should().NotBeNull();
        property!.GetColumnType().Should().Be("jsonb");
    }

    [Fact]
    public void InferenceProvider_has_a_composite_unique_index_on_capability_and_is_default_not_is_default_alone()
    {
        using InferenceApiDbContext db = CreateContext();

        var entityType = db.Model.FindEntityType(typeof(InferenceProvider));
        entityType.Should().NotBeNull();

        var indexes = entityType!.GetIndexes().ToList();

        // R4: the old UNIQUE(is_default) index must be gone entirely, not merely supplemented -
        // otherwise a Transcription default and a Chat default could never coexist.
        indexes.Should().NotContain(
            i => i.IsUnique && i.Properties.Count == 1 && i.Properties[0].Name == nameof(InferenceProvider.IsDefault));

        indexes.Should().ContainSingle(i =>
            i.IsUnique &&
            i.Properties.Select(p => p.Name).SequenceEqual(new[]
            {
                nameof(InferenceProvider.Capability),
                nameof(InferenceProvider.IsDefault)
            }));
    }
}
