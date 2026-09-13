using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Workflows;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// The literal JSON strings seeded in <see cref="WorkflowTemplateCatalog"/> for
/// <c>lean-context-promo</c> and <c>video-derush-edit</c> were authored by hand alongside (but not
/// generated from) the config record types they are meant to deserialize into. This is exactly
/// the class of bug that slipped through once already in this initiative's precedent (enum JSON
/// casing, commit 8e4b6a5a) — a field name or enum value drifting between the type's author and
/// the template's author looks correct by eye but throws or silently no-ops at execution time.
/// These tests deserialize the actual seeded literals with the exact <see cref="JsonSerializerOptions"/>
/// the real step executors use, and assert the resulting values, so a future edit to either side
/// that breaks the pairing fails CI instead of a live workflow run.
/// </summary>
public class WorkflowTemplateCatalogConfigDeserializationTests
{
    // Mirrors VideoAnalyzeStepExecutor.ConfigJsonOptions / VideoCompileStepExecutor.ConfigJsonOptions
    // / ExtractStepExecutor.ConfigJsonOptions exactly: JsonSerializerDefaults.Web (camelCase
    // properties) + JsonStringEnumConverter (PascalCase enum member names, per R7).
    private static readonly JsonSerializerOptions ConfigJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static WorkflowTemplateStepDefinition GetStep(string templateKey, int index)
    {
        WorkflowTemplateDefinition? template = WorkflowTemplateCatalog.GetByKey(templateKey);
        template.Should().NotBeNull($"template '{templateKey}' must be registered in WorkflowTemplateCatalog");
        template!.Steps.Should().HaveCountGreaterThan(index);
        return template.Steps[index];
    }

    [Fact]
    public void VideoAnalyze_step_literal_deserializes_against_the_real_config_type()
    {
        WorkflowTemplateStepDefinition step = GetStep("video-derush-edit", 0);
        step.StepType.Should().Be(StepType.VideoAnalyze);
        step.VideoAnalyzeConfigJson.Should().NotBeNullOrWhiteSpace();

        VideoAnalyzeStepConfig? config = JsonSerializer.Deserialize<VideoAnalyzeStepConfig>(
            step.VideoAnalyzeConfigJson!, ConfigJsonOptions);

        config.Should().NotBeNull();
        config!.Version.Should().Be(1);
        config.Source.Should().NotBeNull();
        config.Source.Kind.Should().Be(VideoSourceKind.PreviousStepOutput);
        config.Source.ProjectFileId.Should().BeNull();
        config.Source.StepOrder.Should().BeNull();

        // Everything else in the literal was left to the record's own defaults — confirm those
        // defaults are the sane, safe values the config type documents, not silently nulled out
        // by a property-name mismatch (which System.Text.Json would do without throwing).
        config.DetectSilence.Should().BeTrue();
        config.DetectShots.Should().BeTrue();
        config.Transcription.Should().Be(VideoTranscriptionMode.Optional);
        config.MaxDurationSeconds.Should().Be(1800);
        config.MaxOutputChars.Should().Be(24_000);
    }

    [Fact]
    public void VideoCompile_step_literal_deserializes_against_the_real_config_type()
    {
        WorkflowTemplateStepDefinition step = GetStep("video-derush-edit", 2);
        step.StepType.Should().Be(StepType.VideoCompile);
        step.VideoCompileConfigJson.Should().NotBeNullOrWhiteSpace();

        VideoCompileStepConfig? config = JsonSerializer.Deserialize<VideoCompileStepConfig>(
            step.VideoCompileConfigJson!, ConfigJsonOptions);

        config.Should().NotBeNull();
        config!.Version.Should().Be(1);
        config.Decision.Should().NotBeNull();
        config.Decision.From.Should().Be(ExtractInputSource.Previous);
        config.Decision.StepOrder.Should().BeNull();
        config.AnalysisStepOrder.Should().Be(1);
        config.AnalysisStepResultId.Should().BeNull();

        // Defaults again: confirm the frame-accuracy-protecting default (Reencode) and the
        // allowlisted codec/preset defaults actually round-trip.
        config.Mode.Should().Be(VideoCompileMode.Reencode);
        config.VideoCodec.Should().Be("libx264");
        config.AudioCodec.Should().Be("aac");
        config.Preset.Should().Be("veryfast");
        config.AllowKeyframeSnapping.Should().BeFalse();
        config.RegisterProjectFile.Should().BeTrue();
    }

    [Fact]
    public void The_middle_video_derush_edit_step_is_the_story_editor_agent_with_no_deterministic_config()
    {
        WorkflowTemplateStepDefinition step = GetStep("video-derush-edit", 1);
        step.AgentType.Should().Be(AgentType.VideoStoryEditor);
        step.StepType.Should().Be(StepType.Agent);
        step.VideoAnalyzeConfigJson.Should().BeNull();
        step.VideoCompileConfigJson.Should().BeNull();
        step.ExtractConfigJson.Should().BeNull();
    }

    [Fact]
    public void Lean_context_promo_Extract_step_literal_still_deserializes_against_the_real_config_type()
    {
        // Precedent check (R7): this template predates video editing but is the same
        // hand-authored-literal-vs-type-author pairing risk, and had no dedicated
        // deserialization test until now either.
        WorkflowTemplateStepDefinition step = GetStep("lean-context-promo", 5);
        step.AgentType.Should().Be(AgentType.ExtractTransform);
        step.StepType.Should().Be(StepType.Extract);
        step.ExtractConfigJson.Should().NotBeNullOrWhiteSpace();

        ExtractStepConfig? config = JsonSerializer.Deserialize<ExtractStepConfig>(
            step.ExtractConfigJson!, ConfigJsonOptions);

        config.Should().NotBeNull();
        config!.Version.Should().Be(1);
        config.Operation.Should().Be(ExtractOperation.Project);
        config.Inputs.Should().ContainKey("source");
        config.Inputs["source"].From.Should().Be(ExtractInputSource.Step);
        config.Inputs["source"].StepOrder.Should().Be(3);
        config.Path.Should().Be("$.components");
        config.Fields.Should().BeEquivalentTo(new[] { "name", "filePath", "responsibility" });
        config.Take.Should().Be(40);
        config.MaxOutputChars.Should().Be(8000);
        config.Expect.Should().NotBeNull();
        config.Expect!.MinItems.Should().Be(1);
    }
}
