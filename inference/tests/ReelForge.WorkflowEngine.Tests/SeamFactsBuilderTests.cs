using System.Collections.Generic;
using FluentAssertions;
using ReelForge.Shared.Workflows;
using ReelForge.WorkflowEngine.Execution.StepExecutors;
using ReelForge.WorkflowEngine.Services.Video;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

using ResolvedSpan = VideoCompileStepExecutor.ResolvedSpan;

/// <summary>
/// Tests for <see cref="SeamFactsBuilder"/>. See docs/video-editing.md "Cut transitions".
/// </summary>
public class SeamFactsBuilderTests
{
    private static VideoAnalysisShotVisual Visual(
        string motionClass = "Dynamic", string cameraMove = "Unknown", double cameraMoveConf = 0,
        IReadOnlyList<VideoAnalysisStillWindow>? stillWindows = null, string? dupGroupId = null,
        string? lookGroupId = null) =>
        new(
            MotionMean: 0, MotionPeak: 0, MotionStdDev: 0, MotionClass: motionClass,
            CameraMove: cameraMove, CameraMoveConfidence: cameraMoveConf,
            HeadMotion: 0, TailMotion: 0, StillWindows: stillWindows ?? [],
            BrightnessMean: 0, BrightnessStdDev: 0, ContrastRms: 0, ClippedHighlightRatio: 0, CrushedBlackRatio: 0,
            SaturationMean: 0, DominantColors: [], Regions: [], BestOverlayRegion: null, ActiveCrop: null,
            Sharpness: null, KenBurnsCandidate: false, KenBurnsReason: null, DuplicateGroupId: dupGroupId,
            GroupRank: null, IsBestTake: false, LookGroupId: lookGroupId);

    private static VideoAnalysisShotAudio Audio(string? audioCharacterClass) =>
        new(RmsDbfs: -20, PeakDbfs: -10, SpeechRatio: 0.5, LoudnessClass: "Normal", AudioCharacterClass: audioCharacterClass);

    private static VideoAnalysisArtifact Artifact(IReadOnlyList<VideoAnalysisShot> shots, IReadOnlyList<VideoAnalysisSegment>? segments = null) =>
        new(
            Version: 2,
            Media: new VideoAnalysisMedia(20, 30, 1, 1920, 1080),
            Shots: shots,
            SilenceSpans: [],
            Segments: segments ?? [],
            Words: [],
            OfferedIds: [],
            Provenance: new VideoAnalysisProvenance(VideoTranscriptionMode.Off, false, false));

    [Fact]
    public void Different_shots_same_source_report_look_jump_and_still_facts_correctly()
    {
        VideoAnalysisShot s0 = new(
            "s0", 0, 10,
            Visual: Visual("Static", "Pan", 0.9, [new VideoAnalysisStillWindow(8, 10, 0)], lookGroupId: "k0"),
            Audio: Audio("Dialogue"));
        VideoAnalysisShot s1 = new(
            "s1", 10, 20,
            Visual: Visual("Dynamic", "Tilt", 0.5, lookGroupId: "k1"),
            Audio: Audio("Silent"));

        VideoAnalysisSegment seg = new("t0", "s0", 9.0, 9.4, "Hello.");

        VideoAnalysisArtifact artifact = Artifact([s0, s1], [seg]);

        List<ResolvedSpan> spans =
        [
            new(0, 9.5, 0, 9.5, 0, 0),
            new(10.5, 20, 10.5, 20, 0, 0)
        ];

        IReadOnlyList<SeamFacts> facts = SeamFactsBuilder.Build(artifact, spans);

        facts.Should().HaveCount(1);
        SeamFacts f = facts[0];

        f.SameSource.Should().BeTrue();
        f.RemovedGapSec.Should().BeApproximately(1.0, 1e-9);
        f.OutShotId.Should().Be("s0");
        f.InShotId.Should().Be("s1");
        f.SameShot.Should().BeFalse();
        f.OutLook.Should().Be("k0");
        f.InLook.Should().Be("k1");
        f.LookJump.Should().BeTrue();
        f.CutOutStill.Should().BeTrue(); // 9.5 falls inside s0's still window [8,10]
        f.CutInStill.Should().BeFalse(); // s1 has no still windows and MotionClass is Dynamic
        f.DupPair.Should().BeFalse();
        f.OutAudioChar.Should().Be("Dialogue");
        f.InAudioChar.Should().Be("Silent");
        f.OutMove.Should().Be("Pan");
        f.InMove.Should().Be("Tilt");
        f.OutMoveConfidence.Should().Be(0.9);
        f.InMoveConfidence.Should().Be(0.5);
        f.EndsSentenceAtSeam.Should().BeTrue();
    }

    [Fact]
    public void Same_shot_on_both_sides_reports_SameShot_true()
    {
        VideoAnalysisShot s0 = new("s0", 0, 20, Visual: Visual("Static", stillWindows: [new VideoAnalysisStillWindow(9, 11, 0)]));
        VideoAnalysisArtifact artifact = Artifact([s0]);

        List<ResolvedSpan> spans =
        [
            new(0, 10, 0, 10, 0, 0),
            new(10, 20, 10, 20, 0, 0)
        ];

        IReadOnlyList<SeamFacts> facts = SeamFactsBuilder.Build(artifact, spans);

        facts[0].SameShot.Should().BeTrue();
        facts[0].OutShotId.Should().Be("s0");
        facts[0].InShotId.Should().Be("s0");
    }

    [Fact]
    public void Duplicate_group_membership_is_reported()
    {
        VideoAnalysisShot s0 = new("s0", 0, 10, Visual: Visual(dupGroupId: "d0"));
        VideoAnalysisShot s1 = new("s1", 10, 20, Visual: Visual(dupGroupId: "d0"));
        VideoAnalysisArtifact artifact = Artifact([s0, s1]);

        List<ResolvedSpan> spans =
        [
            new(0, 10, 0, 10, 0, 0),
            new(10, 20, 10, 20, 0, 0)
        ];

        IReadOnlyList<SeamFacts> facts = SeamFactsBuilder.Build(artifact, spans);

        facts[0].DupPair.Should().BeTrue();
    }

    [Fact]
    public void Different_source_reports_SameSource_false_and_null_gap()
    {
        VideoAnalysisShot s0 = new("s0", 0, 10, SourceIndex: 0);
        VideoAnalysisShot s1 = new("s1", 0, 10, SourceIndex: 1);
        VideoAnalysisArtifact artifact = Artifact([s0, s1]);

        List<ResolvedSpan> spans =
        [
            new(0, 8, 0, 8, 0, 0, SourceIndex: 0),
            new(0, 8, 0, 8, 0, 0, SourceIndex: 1)
        ];

        IReadOnlyList<SeamFacts> facts = SeamFactsBuilder.Build(artifact, spans);

        facts[0].SameSource.Should().BeFalse();
        facts[0].RemovedGapSec.Should().BeNull();
    }

    [Fact]
    public void Missing_visual_data_leaves_facts_null_or_false_without_throwing()
    {
        VideoAnalysisShot s0 = new("s0", 0, 10);
        VideoAnalysisShot s1 = new("s1", 10, 20);
        VideoAnalysisArtifact artifact = Artifact([s0, s1]);

        List<ResolvedSpan> spans =
        [
            new(0, 10, 0, 10, 0, 0),
            new(10, 20, 10, 20, 0, 0)
        ];

        IReadOnlyList<SeamFacts> facts = SeamFactsBuilder.Build(artifact, spans);

        facts[0].OutLook.Should().BeNull();
        facts[0].InLook.Should().BeNull();
        facts[0].LookJump.Should().BeFalse();
        facts[0].CutOutStill.Should().BeFalse();
        facts[0].CutInStill.Should().BeFalse();
        facts[0].DupPair.Should().BeFalse();
        facts[0].OutAudioChar.Should().BeNull();
    }

    [Fact]
    public void Single_span_produces_no_seams()
    {
        VideoAnalysisArtifact artifact = Artifact([new VideoAnalysisShot("s0", 0, 10)]);
        List<ResolvedSpan> spans = [new(0, 10, 0, 10, 0, 0)];

        SeamFactsBuilder.Build(artifact, spans).Should().BeEmpty();
    }
}
