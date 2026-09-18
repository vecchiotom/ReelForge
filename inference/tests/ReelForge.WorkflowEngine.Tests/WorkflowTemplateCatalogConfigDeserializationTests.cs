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
        config.Source.Kind.Should().Be(VideoSourceKind.ProjectFile);
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

        // Phase 1 (scene/visual analysis) defaults — the seeded literal predates these fields, so
        // this confirms they fall back to sane, on-by-default values rather than being silently
        // nulled/zeroed by a future property-name mismatch.
        config.AnalyzeVisuals.Should().BeTrue();
        config.VisualDetail.Should().Be(VideoVisualDetail.Compact);
        config.AnalyzeAudioLevels.Should().BeTrue();
        config.DetectNearDuplicates.Should().BeTrue();
        config.DuplicateSimilarityThreshold.Should().Be(0.90);

        // Phase 2 (vision shot captioning) defaults — the seeded literal predates these fields
        // too. Vision must default OFF (unlike Transcription's Optional default — see
        // VideoVisionMode's doc comment) so this opt-in template's cost/behavior doesn't change
        // until a workflow author explicitly turns captioning on.
        config.Vision.Should().Be(VideoVisionMode.Off);
        config.VisionProviderId.Should().BeNull();
        config.CaptionSelection.Should().Be(VideoCaptionSelection.PerDuplicateGroup);
        config.MaxCaptionedShots.Should().Be(50);
        config.MinCaptionShotSeconds.Should().Be(1.0);
        config.KeyframeMaxWidth.Should().Be(512);
        config.VisionTimeoutSeconds.Should().Be(120);
        config.MaxCaptionChars.Should().Be(320);
        config.PersistKeyframes.Should().BeFalse();
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

        // Seam-transition / program-envelope config (see docs/video-editing.md "Seam transitions
        // and the program envelope"). Only MinSegmentMs exists on VideoCompileStepConfig as of
        // this template edit — TransitionPolicy/ProgramFadeInMs/ProgramFadeOutMs/
        // ProgramAudioFadeInMs/ProgramAudioFadeOutMs are seeded in the literal for the sibling
        // transition-system effort to pick up once its VideoCompileStepConfig fields land; System.Text.Json
        // ignores unmapped JSON properties by default, so the extra keys are harmless today.
        config.MinSegmentMs.Should().Be(800);
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
    public void VideoAnalyze_graphics_step_literal_deserializes_against_the_real_config_type()
    {
        WorkflowTemplateStepDefinition step = GetStep("video-derush-edit-graphics", 0);
        step.StepType.Should().Be(StepType.VideoAnalyze);
        step.VideoAnalyzeConfigJson.Should().NotBeNullOrWhiteSpace();

        VideoAnalyzeStepConfig? config = JsonSerializer.Deserialize<VideoAnalyzeStepConfig>(
            step.VideoAnalyzeConfigJson!, ConfigJsonOptions);

        config.Should().NotBeNull();
        config!.Version.Should().Be(1);
        config.Source.Kind.Should().Be(VideoSourceKind.ProjectFile);
        config.EmitOverlayPlacements.Should().BeTrue();

        // Phase 3 defaults not set by the literal — confirm they fall back to sane values rather
        // than being silently nulled/zeroed by a future property-name mismatch.
        config.MaxPlacementsPerShot.Should().Be(2);
        config.MaxPlacements.Should().Be(40);
    }

    [Fact]
    public void The_second_video_derush_edit_graphics_step_is_the_story_editor_agent()
    {
        WorkflowTemplateStepDefinition step = GetStep("video-derush-edit-graphics", 1);
        step.AgentType.Should().Be(AgentType.VideoStoryEditor);
        step.StepType.Should().Be(StepType.Agent);
    }

    [Fact]
    public void The_third_video_derush_edit_graphics_step_is_the_motion_graphics_planner_agent_with_no_deterministic_config()
    {
        WorkflowTemplateStepDefinition step = GetStep("video-derush-edit-graphics", 2);
        step.AgentType.Should().Be(AgentType.MotionGraphicsPlanner);
        step.StepType.Should().Be(StepType.Agent);
        step.VideoAnalyzeConfigJson.Should().BeNull();
        step.VideoCompileConfigJson.Should().BeNull();
        step.ExtractConfigJson.Should().BeNull();
    }

    [Fact]
    public void VideoCompile_graphics_step_literal_deserializes_against_the_real_config_type()
    {
        WorkflowTemplateStepDefinition step = GetStep("video-derush-edit-graphics", 3);
        step.StepType.Should().Be(StepType.VideoCompile);
        step.VideoCompileConfigJson.Should().NotBeNullOrWhiteSpace();

        VideoCompileStepConfig? config = JsonSerializer.Deserialize<VideoCompileStepConfig>(
            step.VideoCompileConfigJson!, ConfigJsonOptions);

        config.Should().NotBeNull();
        config!.Version.Should().Be(1);
        config.Decision.From.Should().Be(ExtractInputSource.Step);
        config.Decision.StepOrder.Should().Be(2);
        config.AnalysisStepOrder.Should().Be(1);
        config.EnableGraphics.Should().BeTrue();
        config.GraphicsPlan.Should().NotBeNull();
        config.GraphicsPlan!.From.Should().Be(ExtractInputSource.Step);
        config.GraphicsPlan.StepOrder.Should().Be(3);

        // Phase 3 graphics defaults not set by the literal.
        config.MaxOverlays.Should().Be(20);
        config.OverlayShortMs.Should().Be(1500);
        config.OverlayMediumMs.Should().Be(3000);
        config.OverlayHoldMs.Should().Be(6000);
        config.OverlayFontColor.Should().Be("white");
        config.OverlayBoxColor.Should().Be("black@0.45");

        // See the seam-transition/program-envelope comment on
        // VideoCompile_step_literal_deserializes_against_the_real_config_type above — only
        // MinSegmentMs round-trips today.
        config.MinSegmentMs.Should().Be(800);
    }

    [Fact]
    public void The_fourth_video_derush_edit_step_is_a_ReviewLoop_looping_back_to_the_story_editor()
    {
        WorkflowTemplateStepDefinition step = GetStep("video-derush-edit", 3);
        step.AgentType.Should().Be(AgentType.VideoReviewAgent);
        step.StepType.Should().Be(StepType.ReviewLoop);
        step.LoopTargetStepOrder.Should().Be(2, "must loop back to the story-editor step, not the deterministic VideoAnalyze step");
        step.MaxIterations.Should().Be(3);
        step.MinScore.Should().Be(8);
        step.AgentInputContextMode.Should().Be(AgentInputContextMode.FullWorkflow);
    }

    [Fact]
    public void The_fifth_video_derush_edit_graphics_step_is_a_ReviewLoop_looping_back_to_the_story_editor()
    {
        WorkflowTemplateStepDefinition step = GetStep("video-derush-edit-graphics", 4);
        step.AgentType.Should().Be(AgentType.VideoReviewAgent);
        step.StepType.Should().Be(StepType.ReviewLoop);
        step.LoopTargetStepOrder.Should().Be(2, "must loop back to the story-editor step so the story editor, motion-graphics planner, and compile all re-run");
        step.MaxIterations.Should().Be(3);
        step.MinScore.Should().Be(8);
        step.AgentInputContextMode.Should().Be(AgentInputContextMode.FullWorkflow);
    }

    [Fact]
    public void VideoAnalyze_music_step_literal_deserializes_against_the_real_config_type()
    {
        WorkflowTemplateStepDefinition step = GetStep("video-derush-edit-music", 0);
        step.StepType.Should().Be(StepType.VideoAnalyze);
        step.VideoAnalyzeConfigJson.Should().NotBeNullOrWhiteSpace();

        VideoAnalyzeStepConfig? config = JsonSerializer.Deserialize<VideoAnalyzeStepConfig>(
            step.VideoAnalyzeConfigJson!, ConfigJsonOptions);

        config.Should().NotBeNull();
        config!.Version.Should().Be(1);
        config.Source.Kind.Should().Be(VideoSourceKind.ProjectFile);
        config.OfferMusicTracks.Should().BeTrue();

        // Defaults not set by the literal — confirm they fall back to sane values rather than
        // being silently nulled/zeroed by a future property-name mismatch.
        config.MaxMusicTracks.Should().Be(20);
    }

    [Fact]
    public void The_second_video_derush_edit_music_step_is_the_story_editor_agent()
    {
        WorkflowTemplateStepDefinition step = GetStep("video-derush-edit-music", 1);
        step.AgentType.Should().Be(AgentType.VideoStoryEditor);
        step.StepType.Should().Be(StepType.Agent);
    }

    [Fact]
    public void The_third_video_derush_edit_music_step_is_the_music_supervisor_agent_with_no_deterministic_config()
    {
        WorkflowTemplateStepDefinition step = GetStep("video-derush-edit-music", 2);
        step.AgentType.Should().Be(AgentType.MusicSupervisor);
        step.StepType.Should().Be(StepType.Agent);
        step.VideoAnalyzeConfigJson.Should().BeNull();
        step.VideoCompileConfigJson.Should().BeNull();
        step.ExtractConfigJson.Should().BeNull();
        step.AgentInputContextMode.Should().Be(AgentInputContextMode.FullWorkflow);
    }

    [Fact]
    public void VideoCompile_music_step_literal_deserializes_against_the_real_config_type()
    {
        WorkflowTemplateStepDefinition step = GetStep("video-derush-edit-music", 3);
        step.StepType.Should().Be(StepType.VideoCompile);
        step.VideoCompileConfigJson.Should().NotBeNullOrWhiteSpace();

        VideoCompileStepConfig? config = JsonSerializer.Deserialize<VideoCompileStepConfig>(
            step.VideoCompileConfigJson!, ConfigJsonOptions);

        config.Should().NotBeNull();
        config!.Version.Should().Be(1);
        config.Decision.From.Should().Be(ExtractInputSource.Step);
        config.Decision.StepOrder.Should().Be(2);
        config.AnalysisStepOrder.Should().Be(1);
        config.EnableMusic.Should().BeTrue();
        config.MusicPlan.Should().NotBeNull();
        config.MusicPlan!.From.Should().Be(ExtractInputSource.Step);
        config.MusicPlan.StepOrder.Should().Be(3);

        // Untouched defaults — confirm they really landed rather than being silently
        // nulled/zeroed by a future property-name mismatch.
        config.MusicDucking.Should().Be(MusicDuckingMode.SpeechEnvelope);
        config.MusicFitPolicy.Should().Be(MusicFit.LoopToFit);
        config.MusicFadeInMs.Should().Be(1500);
        config.MusicFadeOutMs.Should().Be(2500);
        config.MusicBedQuietDb.Should().Be(-26);
        config.MusicBedBalancedDb.Should().Be(-20);
        config.MusicBedFeatureDb.Should().Be(-14);
        config.MusicDuckLightDb.Should().Be(-6);
        config.MusicDuckNormalDb.Should().Be(-11);
        config.MusicDuckHeavyDb.Should().Be(-18);

        // See the seam-transition/program-envelope comment on
        // VideoCompile_step_literal_deserializes_against_the_real_config_type above — only
        // MinSegmentMs round-trips today.
        config.MinSegmentMs.Should().Be(800);
    }

    [Fact]
    public void The_fifth_video_derush_edit_music_step_is_a_ReviewLoop_looping_back_to_the_story_editor()
    {
        WorkflowTemplateStepDefinition step = GetStep("video-derush-edit-music", 4);
        step.AgentType.Should().Be(AgentType.VideoReviewAgent);
        step.StepType.Should().Be(StepType.ReviewLoop);
        step.LoopTargetStepOrder.Should().Be(2, "must loop back to the story-editor step so the story editor, music supervisor, and compile all re-run");
        step.MaxIterations.Should().Be(3);
        step.MinScore.Should().Be(8);
        step.AgentInputContextMode.Should().Be(AgentInputContextMode.FullWorkflow);
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
