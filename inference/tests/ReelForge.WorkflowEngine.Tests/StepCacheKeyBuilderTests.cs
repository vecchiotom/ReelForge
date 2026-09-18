using System;
using System.Collections.Generic;
using FluentAssertions;
using ReelForge.Shared.Data.Models;
using ReelForge.WorkflowEngine.Execution.Caching;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Coverage for <see cref="StepCacheKeyBuilder"/>: determinism, sensitivity to every hashed field,
/// null/empty distinguishability, output shape, and the length-prefixed-framing collision
/// resistance the doc comment on <see cref="StepCacheKeyBuilder.Build"/> claims.
/// </summary>
public class StepCacheKeyBuilderTests
{
    private static StepCacheKeyInputs BaseInputs() => new()
    {
        ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        StepType = StepType.Agent,
        AgentType = AgentType.VideoStoryEditor,
        AgentDefinitionId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        AgentSystemPrompt = "You decide which shots to keep.",
        AgentOutputSchemaName = "VideoEditDecisionOutput",
        AgentInferenceProviderId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
        AgentAssignedSkillsJson = "[\"remotion-markup\"]",
        AgentInputContextMode = AgentInputContextMode.PreviousStepOnly,
        SelectedPriorStepOrdersJson = "[1,2]",
        InputMappingJson = "{\"a\":1}",
        ExtractConfigJson = "{\"op\":\"Project\"}",
        VideoAnalyzeConfigJson = "{\"source\":\"ProjectFile\"}",
        VideoCompileConfigJson = "{\"mode\":\"Reencode\"}",
        EditRoomConfigJson = "{\"seats\":3}",
        GraphicsRoomConfigJson = "{\"seats\":2}",
        ColorGradeRoomConfigJson = "{\"seats\":2}",
        ConditionExpression = "score >= 9",
        MaxIterations = 3,
        MinScore = 8,
        ResolvedInput = "[\"begin\"]",
        UserRequest = "make it punchy",
        ProjectFileFingerprint = "abc123",
        WorkflowDefinitionId = Guid.Parse("44444444-4444-4444-4444-444444444444")
    };

    private static readonly IStepCacheKeyBuilder Builder = new StepCacheKeyBuilder();

    [Fact]
    public void Build_is_deterministic_for_identical_inputs()
    {
        StepCacheKeyInputs inputs = BaseInputs();

        string key1 = Builder.Build(inputs);
        string key2 = Builder.Build(inputs with { });

        key1.Should().Be(key2);
    }

    [Fact]
    public void Build_returns_64_lowercase_hex_characters()
    {
        string key = Builder.Build(BaseInputs());

        key.Should().HaveLength(64);
        key.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    public static IEnumerable<object[]> FieldMutators()
    {
        yield return new object[] { "ProjectId", (Func<StepCacheKeyInputs, StepCacheKeyInputs>)(i => i with { ProjectId = Guid.NewGuid() }) };
        yield return new object[] { "StepType", (Func<StepCacheKeyInputs, StepCacheKeyInputs>)(i => i with { StepType = StepType.Extract }) };
        yield return new object[] { "AgentType", (Func<StepCacheKeyInputs, StepCacheKeyInputs>)(i => i with { AgentType = AgentType.Colorist }) };
        yield return new object[] { "AgentDefinitionId", (Func<StepCacheKeyInputs, StepCacheKeyInputs>)(i => i with { AgentDefinitionId = Guid.NewGuid() }) };
        yield return new object[] { "AgentSystemPrompt", (Func<StepCacheKeyInputs, StepCacheKeyInputs>)(i => i with { AgentSystemPrompt = "different prompt" }) };
        yield return new object[] { "AgentOutputSchemaName", (Func<StepCacheKeyInputs, StepCacheKeyInputs>)(i => i with { AgentOutputSchemaName = "SomethingElseOutput" }) };
        yield return new object[] { "AgentInferenceProviderId", (Func<StepCacheKeyInputs, StepCacheKeyInputs>)(i => i with { AgentInferenceProviderId = Guid.NewGuid() }) };
        yield return new object[] { "AgentAssignedSkillsJson", (Func<StepCacheKeyInputs, StepCacheKeyInputs>)(i => i with { AgentAssignedSkillsJson = "[\"different-skill\"]" }) };
        yield return new object[] { "AgentInputContextMode", (Func<StepCacheKeyInputs, StepCacheKeyInputs>)(i => i with { AgentInputContextMode = AgentInputContextMode.FullWorkflow }) };
        yield return new object[] { "SelectedPriorStepOrdersJson", (Func<StepCacheKeyInputs, StepCacheKeyInputs>)(i => i with { SelectedPriorStepOrdersJson = "[9]" }) };
        yield return new object[] { "InputMappingJson", (Func<StepCacheKeyInputs, StepCacheKeyInputs>)(i => i with { InputMappingJson = "{\"b\":2}" }) };
        yield return new object[] { "ExtractConfigJson", (Func<StepCacheKeyInputs, StepCacheKeyInputs>)(i => i with { ExtractConfigJson = "{\"op\":\"Resolve\"}" }) };
        yield return new object[] { "VideoAnalyzeConfigJson", (Func<StepCacheKeyInputs, StepCacheKeyInputs>)(i => i with { VideoAnalyzeConfigJson = "{\"source\":\"StepOutput\"}" }) };
        yield return new object[] { "VideoCompileConfigJson", (Func<StepCacheKeyInputs, StepCacheKeyInputs>)(i => i with { VideoCompileConfigJson = "{\"mode\":\"StreamCopy\"}" }) };
        yield return new object[] { "EditRoomConfigJson", (Func<StepCacheKeyInputs, StepCacheKeyInputs>)(i => i with { EditRoomConfigJson = "{\"seats\":5}" }) };
        yield return new object[] { "GraphicsRoomConfigJson", (Func<StepCacheKeyInputs, StepCacheKeyInputs>)(i => i with { GraphicsRoomConfigJson = "{\"seats\":5}" }) };
        yield return new object[] { "ColorGradeRoomConfigJson", (Func<StepCacheKeyInputs, StepCacheKeyInputs>)(i => i with { ColorGradeRoomConfigJson = "{\"seats\":5}" }) };
        yield return new object[] { "ConditionExpression", (Func<StepCacheKeyInputs, StepCacheKeyInputs>)(i => i with { ConditionExpression = "score >= 5" }) };
        yield return new object[] { "MaxIterations", (Func<StepCacheKeyInputs, StepCacheKeyInputs>)(i => i with { MaxIterations = 7 }) };
        yield return new object[] { "MinScore", (Func<StepCacheKeyInputs, StepCacheKeyInputs>)(i => i with { MinScore = 2 }) };
        yield return new object[] { "ResolvedInput", (Func<StepCacheKeyInputs, StepCacheKeyInputs>)(i => i with { ResolvedInput = "[\"different\"]" }) };
        yield return new object[] { "UserRequest", (Func<StepCacheKeyInputs, StepCacheKeyInputs>)(i => i with { UserRequest = "make it slower" }) };
        yield return new object[] { "ProjectFileFingerprint", (Func<StepCacheKeyInputs, StepCacheKeyInputs>)(i => i with { ProjectFileFingerprint = "def456" }) };
    }

    [Theory]
    [MemberData(nameof(FieldMutators))]
    public void Build_changes_when_a_single_hashed_field_changes(string fieldName, Func<StepCacheKeyInputs, StepCacheKeyInputs> mutate)
    {
        StepCacheKeyInputs original = BaseInputs();
        StepCacheKeyInputs mutated = mutate(original);

        string originalKey = Builder.Build(original);
        string mutatedKey = Builder.Build(mutated);

        mutatedKey.Should().NotBe(originalKey, because: $"changing {fieldName} must change the cache key");
    }

    [Fact]
    public void WorkflowDefinitionId_is_deliberately_excluded_from_the_hash()
    {
        // See StepCacheKeyInputs' own doc comment: WorkflowDefinitionId is metadata-only, carried
        // for WorkflowStepCacheEntry population, NOT part of the content that determines whether
        // two executions would produce the same output — a cache entry must survive an edit to the
        // workflow definition it was produced under.
        StepCacheKeyInputs original = BaseInputs();
        StepCacheKeyInputs mutated = original with { WorkflowDefinitionId = Guid.NewGuid() };

        Builder.Build(mutated).Should().Be(Builder.Build(original));
    }

    [Fact]
    public void Null_and_empty_string_are_distinguishable()
    {
        StepCacheKeyInputs withNull = BaseInputs() with { UserRequest = null };
        StepCacheKeyInputs withEmpty = BaseInputs() with { UserRequest = string.Empty };

        Builder.Build(withNull).Should().NotBe(Builder.Build(withEmpty));
    }

    [Fact]
    public void Two_different_null_optional_fields_still_produce_the_same_key_as_each_other()
    {
        StepCacheKeyInputs a = BaseInputs() with { UserRequest = null, MinScore = null };
        StepCacheKeyInputs b = BaseInputs() with { UserRequest = null, MinScore = null };

        Builder.Build(a).Should().Be(Builder.Build(b));
    }

    [Fact]
    public void Length_prefixed_framing_prevents_concatenation_collisions_across_adjacent_fields()
    {
        // The exact shape the doc comment on StepCacheKeyBuilder.Build calls out: {AgentSystemPrompt="ab", AgentOutputSchemaName="c"}
        // vs {AgentSystemPrompt="a", AgentOutputSchemaName="bc"} would hash identically under naive
        // newline-joined concatenation (both flatten to "...ab" + "c..." vs "...a" + "bc..." = the
        // same bytes once the field NAMES are fixed and only these two adjacent values move mass
        // between them) unless each value's own length is also part of the framing.
        StepCacheKeyInputs a = BaseInputs() with { AgentSystemPrompt = "ab", AgentOutputSchemaName = "c" };
        StepCacheKeyInputs b = BaseInputs() with { AgentSystemPrompt = "a", AgentOutputSchemaName = "bc" };

        Builder.Build(a).Should().NotBe(Builder.Build(b));
    }

    [Fact]
    public void Length_prefixed_framing_prevents_concatenation_collisions_via_embedded_separators()
    {
        // A value containing the field separator itself (a real system prompt or JSON config can
        // easily contain '=' or '\n') must not be able to forge a different field split.
        StepCacheKeyInputs a = BaseInputs() with { AgentSystemPrompt = "x\nAgentOutputSchemaName=5:hijack", AgentOutputSchemaName = "real" };
        StepCacheKeyInputs b = BaseInputs() with { AgentSystemPrompt = "x", AgentOutputSchemaName = "real" };

        Builder.Build(a).Should().NotBe(Builder.Build(b));
    }
}
