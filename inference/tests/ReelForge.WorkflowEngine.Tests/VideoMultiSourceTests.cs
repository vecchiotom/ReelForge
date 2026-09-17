using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Inference;
using ReelForge.Shared.Workflows;
using ReelForge.WorkflowEngine.Execution;
using ReelForge.WorkflowEngine.Execution.StepExecutors;
using ReelForge.WorkflowEngine.Services.Storage;
using ReelForge.WorkflowEngine.Services.Video;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Tests for the multi-source addition to the video-editing pipeline (docs/video-editing.md
/// "Multiple source clips"): analyzing several source clips into one merged
/// <see cref="VideoAnalysisArtifact"/> with a globally-unique id space, and compiling a cut list
/// whose Keep spans reference more than one distinct physical clip. Deliberately kept in its own
/// file rather than folded into <see cref="VideoAnalyzeStepExecutorTests"/>/
/// <see cref="VideoCompileStepExecutorTests"/> — those already cover the single-source pipeline
/// exhaustively; this file covers only what changes when more than one source is involved.
/// </summary>
public class VideoMultiSourceTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();

    // ---------------------------------------------------------------------
    // VideoAnalyzeStepExecutor: id uniqueness/namespacing across sources
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Two_sources_produce_globally_unique_contiguous_ids_tagged_with_their_own_SourceIndex()
    {
        Guid fileA = Guid.NewGuid();
        Guid fileB = Guid.NewGuid();
        const string keyA = "projects/p/files/clipA.mp4";
        const string keyB = "projects/p/files/clipB.mp4";

        // Source A: 3 shots. Source B: 2 shots. Shot ids must come out as s0,s1,s2 (source 0) then
        // s3,s4 (source 1) — one continuous counter across both clips, never restarting per clip.
        var shotsA = new[] { (0.0, 1.0), (1.0, 2.0), (2.0, 3.0) };
        var shotsB = new[] { (0.0, 1.0), (1.0, 2.0) };

        var workspace = new Mock<IProjectFileWorkspace>();
        workspace
            .Setup(w => w.ListFilesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectWorkspaceFile>
            {
                new(fileA, ProjectId, "clipA.mp4", null, "uploads", keyA, "video/mp4", 1024, DateTime.UtcNow, null),
                new(fileB, ProjectId, "clipB.mp4", null, "uploads", keyB, "video/mp4", 1024, DateTime.UtcNow, null)
            });
        workspace
            .Setup(w => w.DownloadStorageKeyToFileAsync(It.IsAny<Guid>(), keyA, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, string, CancellationToken>((_, _, dest, _) => { File.WriteAllBytes(dest, new byte[10]); return Task.CompletedTask; });
        workspace
            .Setup(w => w.DownloadStorageKeyToFileAsync(It.IsAny<Guid>(), keyB, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, string, CancellationToken>((_, _, dest, _) => { File.WriteAllBytes(dest, new byte[10]); return Task.CompletedTask; });

        var probe = new Mock<IMediaProbe>();
        probe.Setup(p => p.ProbeAsync(It.Is<string>(s => s.Contains("src0-")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaProbeResult(3, 30, 1, 1920, 1080, "h264", "aac", 48000));
        probe.Setup(p => p.ProbeAsync(It.Is<string>(s => s.Contains("src1-")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaProbeResult(2, 25, 1, 1280, 720, "h264", "aac", 48000));

        var silence = new Mock<ISilenceDetector>();
        silence.Setup(s => s.DetectAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(double, double)>());

        var shotDetector = new Mock<IShotDetector>();
        shotDetector
            .Setup(s => s.DetectShotsAsync(It.Is<string>(p => p.Contains("src0-")), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(shotsA);
        shotDetector
            .Setup(s => s.DetectShotsAsync(It.Is<string>(p => p.Contains("src1-")), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(shotsB);

        VideoAnalyzeStepConfig config = new(
            Version: 1,
            Source: new VideoSourceRef(VideoSourceKind.ProjectFile, ProjectFileId: fileA), // ignored — Sources wins
            DetectSilence: false,
            Transcription: VideoTranscriptionMode.Off,
            Sources:
            [
                new VideoSourceRef(VideoSourceKind.ProjectFile, ProjectFileId: fileA),
                new VideoSourceRef(VideoSourceKind.ProjectFile, ProjectFileId: fileB)
            ]);

        StepExecutionContext context = CreateAnalyzeContext(config);
        VideoAnalyzeStepExecutor executor = CreateAnalyzeExecutor(workspace, probe, silence, shotDetector);

        string? capturedArtifactJson = null;
        workspace
            .Setup(w => w.UploadArtifactAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .Callback<Guid, string, string, string, CancellationToken, string>((_, path, _, _, _, _) =>
                capturedArtifactJson = File.ReadAllText(path))
            .ReturnsAsync("projects/p/agentFiles/video-analysis/e/step-1-analysis.json");

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        capturedArtifactJson.Should().NotBeNull();

        using JsonDocument artifact = JsonDocument.Parse(capturedArtifactJson!);
        JsonElement shots = artifact.RootElement.GetProperty("shots");
        shots.GetArrayLength().Should().Be(5);

        List<string> ids = shots.EnumerateArray().Select(s => s.GetProperty("id").GetString()!).ToList();
        ids.Should().OnlyHaveUniqueItems("ids must be globally unique across every source in one artifact");
        ids.Should().BeEquivalentTo(["s0", "s1", "s2", "s3", "s4"], because: "ids are assigned contiguously across sources, never restarting per source");

        List<int> sourceIndices = shots.EnumerateArray().Select(s => s.GetProperty("sourceIndex").GetInt32()).ToList();
        sourceIndices.Should().BeEquivalentTo([0, 0, 0, 1, 1], options => options.WithStrictOrdering());

        JsonElement sources = artifact.RootElement.GetProperty("sources");
        sources.GetArrayLength().Should().Be(2);
        sources[0].GetProperty("storageKey").GetString().Should().Be(keyA);
        sources[1].GetProperty("storageKey").GetString().Should().Be(keyB);
        sources[0].GetProperty("media").GetProperty("width").GetInt32().Should().Be(1920);
        sources[1].GetProperty("media").GetProperty("width").GetInt32().Should().Be(1280);

        // Every offered id in the bounded view must also carry its own "src" now that more than
        // one source exists (gated — a single-source view never gains this key, see
        // VideoAnalyzeStepExecutorTests for that guarantee).
        using JsonDocument outputDoc = JsonDocument.Parse(result.Output);
        JsonElement viewShots = outputDoc.RootElement.GetProperty("view").GetProperty("shots");
        viewShots.EnumerateArray().All(s => s.TryGetProperty("src", out _)).Should().BeTrue(
            "every shot in a multi-source view must carry its own 'src' key");
    }

    [Fact]
    public async Task Single_element_Sources_list_behaves_identically_to_the_legacy_Source_field()
    {
        // A one-element Sources list must be indistinguishable in outcome from the pre-multi-source
        // Source field: same ids, no "src"/"sources" keys in the view (isMultiSource gate).
        Guid fileA = Guid.NewGuid();
        const string keyA = "projects/p/files/clipA.mp4";

        var workspace = new Mock<IProjectFileWorkspace>();
        workspace
            .Setup(w => w.ListFilesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectWorkspaceFile>
            {
                new(fileA, ProjectId, "clipA.mp4", null, "uploads", keyA, "video/mp4", 1024, DateTime.UtcNow, null)
            });
        workspace
            .Setup(w => w.DownloadStorageKeyToFileAsync(It.IsAny<Guid>(), keyA, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, string, CancellationToken>((_, _, dest, _) => { File.WriteAllBytes(dest, new byte[10]); return Task.CompletedTask; });

        var probe = new Mock<IMediaProbe>();
        probe.Setup(p => p.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaProbeResult(3, 30, 1, 1920, 1080, "h264", "aac", 48000));

        var silence = new Mock<ISilenceDetector>();
        silence.Setup(s => s.DetectAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(double, double)>());

        var shotDetector = new Mock<IShotDetector>();
        shotDetector
            .Setup(s => s.DetectShotsAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([(0.0, 1.0), (1.0, 2.0)]);

        VideoAnalyzeStepConfig config = new(
            Version: 1,
            Source: new VideoSourceRef(VideoSourceKind.ProjectFile, ProjectFileId: fileA),
            DetectSilence: false,
            Transcription: VideoTranscriptionMode.Off,
            Sources: [new VideoSourceRef(VideoSourceKind.ProjectFile, ProjectFileId: fileA)]);

        StepExecutionContext context = CreateAnalyzeContext(config);
        VideoAnalyzeStepExecutor executor = CreateAnalyzeExecutor(workspace, probe, silence, shotDetector);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        JsonElement view = doc.RootElement.GetProperty("view");
        view.TryGetProperty("sources", out _).Should().BeFalse("a single source's view must not gain the multi-source 'sources' key");
        foreach (JsonElement shot in view.GetProperty("shots").EnumerateArray())
            shot.TryGetProperty("src", out _).Should().BeFalse("a single source's shots must not gain the multi-source 'src' key");
    }

    // ---------------------------------------------------------------------
    // VideoAnalyzeStepExecutor: Phase 4 §2.1 step-level vision hoist (multi-source)
    // ---------------------------------------------------------------------

    private static (Mock<IProjectFileWorkspace> Workspace, Mock<IMediaProbe> Probe, Mock<ISilenceDetector> Silence,
        Mock<IShotDetector> ShotDetector) TwoSourceFixture(
        Guid fileA, Guid fileB, string keyA, string keyB,
        IReadOnlyList<(double, double)> shotsA, IReadOnlyList<(double, double)> shotsB)
    {
        var workspace = new Mock<IProjectFileWorkspace>();
        workspace
            .Setup(w => w.ListFilesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectWorkspaceFile>
            {
                new(fileA, ProjectId, "clipA.mp4", null, "uploads", keyA, "video/mp4", 1024, DateTime.UtcNow, null),
                new(fileB, ProjectId, "clipB.mp4", null, "uploads", keyB, "video/mp4", 1024, DateTime.UtcNow, null)
            });
        workspace
            .Setup(w => w.DownloadStorageKeyToFileAsync(It.IsAny<Guid>(), keyA, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, string, CancellationToken>((_, _, dest, _) => { File.WriteAllBytes(dest, new byte[10]); return Task.CompletedTask; });
        workspace
            .Setup(w => w.DownloadStorageKeyToFileAsync(It.IsAny<Guid>(), keyB, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, string, CancellationToken>((_, _, dest, _) => { File.WriteAllBytes(dest, new byte[10]); return Task.CompletedTask; });

        var probe = new Mock<IMediaProbe>();
        probe.Setup(p => p.ProbeAsync(It.Is<string>(s => s.Contains("src0-")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaProbeResult(60, 30, 1, 1920, 1080, "h264", "aac", 48000));
        probe.Setup(p => p.ProbeAsync(It.Is<string>(s => s.Contains("src1-")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaProbeResult(60, 25, 1, 1280, 720, "h264", "aac", 48000));

        var silence = new Mock<ISilenceDetector>();
        silence.Setup(s => s.DetectAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(double, double)>());

        var shotDetector = new Mock<IShotDetector>();
        shotDetector
            .Setup(s => s.DetectShotsAsync(It.Is<string>(p => p.Contains("src0-")), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(shotsA);
        shotDetector
            .Setup(s => s.DetectShotsAsync(It.Is<string>(p => p.Contains("src1-")), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(shotsB);

        return (workspace, probe, silence, shotDetector);
    }

    private static ResolvedInferenceProvider VisionProviderFixture() => new(
        Guid.NewGuid(), "vision-test", InferenceProviderKind.OpenAICompatible, "http://localhost:9999", "vision-1", "", 30);

    private static VideoShotCaption CaptionFor(string shotId) => new(
        shotId, "a caption", [], "action", "setting", "mood", "Medium", "Eye level", [], [],
        "Unknown", "flat", "neutral", "centered", []);

    [Fact]
    public async Task MaxCaptionedShots_is_a_step_wide_cap_across_every_source_not_a_per_source_one()
    {
        Guid fileA = Guid.NewGuid(), fileB = Guid.NewGuid();
        const string keyA = "projects/p/files/clipA.mp4", keyB = "projects/p/files/clipB.mp4";

        var shotsA = Enumerable.Range(0, 20).Select(i => ((double)(i * 2), (double)(i * 2 + 2))).ToList();
        var shotsB = Enumerable.Range(0, 20).Select(i => ((double)(i * 2), (double)(i * 2 + 2))).ToList();

        (Mock<IProjectFileWorkspace> workspace, Mock<IMediaProbe> probe, Mock<ISilenceDetector> silence, Mock<IShotDetector> shotDetector) =
            TwoSourceFixture(fileA, fileB, keyA, keyB, shotsA, shotsB);

        var providerResolver = new Mock<IInferenceProviderResolver>();
        providerResolver.Setup(r => r.ResolveVisionAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(VisionProviderFixture());

        var shotCaptioner = new Mock<IShotCaptioner>();
        shotCaptioner
            .Setup(c => c.CaptionAsync(It.IsAny<ResolvedInferenceProvider>(), It.IsAny<ShotCaptionRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResolvedInferenceProvider _, ShotCaptionRequest r, int _, CancellationToken _) => CaptionFor(r.ShotId));

        VideoAnalyzeStepConfig config = new(
            Version: 1,
            Source: new VideoSourceRef(VideoSourceKind.ProjectFile, ProjectFileId: fileA),
            DetectSilence: false,
            Transcription: VideoTranscriptionMode.Off,
            AnalyzeVisuals: false,
            AnalyzeAudioLevels: false,
            Vision: VideoVisionMode.Optional,
            MaxCaptionedShots: 10,
            MinCaptionShotSeconds: 0.0,
            Sources:
            [
                new VideoSourceRef(VideoSourceKind.ProjectFile, ProjectFileId: fileA),
                new VideoSourceRef(VideoSourceKind.ProjectFile, ProjectFileId: fileB)
            ]);

        StepExecutionContext context = CreateAnalyzeContext(config);
        VideoAnalyzeStepExecutor executor = CreateAnalyzeExecutor(
            workspace, probe, silence, shotDetector, shotCaptioner: shotCaptioner, providerResolver: providerResolver);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        shotCaptioner.Invocations.Count(i => i.Method.Name == nameof(IShotCaptioner.CaptionAsync)).Should().Be(10);
    }

    [Fact]
    public async Task Captioning_runs_after_every_source_has_been_analyzed()
    {
        Guid fileA = Guid.NewGuid(), fileB = Guid.NewGuid();
        const string keyA = "projects/p/files/clipA.mp4", keyB = "projects/p/files/clipB.mp4";

        var shotsA = new List<(double, double)> { (0.0, 2.0) };
        var shotsB = new List<(double, double)> { (0.0, 2.0) };

        (Mock<IProjectFileWorkspace> workspace, Mock<IMediaProbe> probe, Mock<ISilenceDetector> silence, Mock<IShotDetector> shotDetector) =
            TwoSourceFixture(fileA, fileB, keyA, keyB, shotsA, shotsB);

        var callOrder = new List<string>();

        var frameGridSampler = new Mock<IFrameGridSampler>();
        frameGridSampler
            .Setup(g => g.SampleAsync(It.IsAny<string>(), It.IsAny<VideoScratchSpace>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { callOrder.Add("grid"); return new FrameGridResult(new byte[4 * 4 * 3], 4, 4, 1.0, 1); });

        var providerResolver = new Mock<IInferenceProviderResolver>();
        providerResolver.Setup(r => r.ResolveVisionAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(VisionProviderFixture());

        var shotCaptioner = new Mock<IShotCaptioner>();
        shotCaptioner
            .Setup(c => c.CaptionAsync(It.IsAny<ResolvedInferenceProvider>(), It.IsAny<ShotCaptionRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResolvedInferenceProvider _, ShotCaptionRequest r, int _, CancellationToken _) =>
            {
                callOrder.Add("caption");
                return CaptionFor(r.ShotId);
            });

        VideoAnalyzeStepConfig config = new(
            Version: 1,
            Source: new VideoSourceRef(VideoSourceKind.ProjectFile, ProjectFileId: fileA),
            DetectSilence: false,
            Transcription: VideoTranscriptionMode.Off,
            AnalyzeVisuals: true,
            AnalyzeAudioLevels: false,
            DetectNearDuplicates: false,
            Vision: VideoVisionMode.Optional,
            MinCaptionShotSeconds: 0.0,
            Sources:
            [
                new VideoSourceRef(VideoSourceKind.ProjectFile, ProjectFileId: fileA),
                new VideoSourceRef(VideoSourceKind.ProjectFile, ProjectFileId: fileB)
            ]);

        StepExecutionContext context = CreateAnalyzeContext(config);
        VideoAnalyzeStepExecutor executor = CreateAnalyzeExecutor(
            workspace, probe, silence, shotDetector,
            frameGridSampler: frameGridSampler, shotCaptioner: shotCaptioner, providerResolver: providerResolver);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);

        callOrder.Count(c => c == "grid").Should().Be(2);
        callOrder.Count(c => c == "caption").Should().BeGreaterThan(0);
        int lastGridIndex = callOrder.LastIndexOf("grid");
        int firstCaptionIndex = callOrder.IndexOf("caption");
        firstCaptionIndex.Should().BeGreaterThan(lastGridIndex,
            "every source's visual analysis must complete before step-level captioning begins");
    }

    [Fact]
    public async Task Keyframes_are_extracted_from_the_clip_each_shot_actually_came_from()
    {
        Guid fileA = Guid.NewGuid(), fileB = Guid.NewGuid();
        const string keyA = "projects/p/files/clipA.mp4", keyB = "projects/p/files/clipB.mp4";

        var shotsA = new List<(double, double)> { (0.0, 2.0) };
        var shotsB = new List<(double, double)> { (0.0, 2.0) };

        (Mock<IProjectFileWorkspace> workspace, Mock<IMediaProbe> probe, Mock<ISilenceDetector> silence, Mock<IShotDetector> shotDetector) =
            TwoSourceFixture(fileA, fileB, keyA, keyB, shotsA, shotsB);

        var providerResolver = new Mock<IInferenceProviderResolver>();
        providerResolver.Setup(r => r.ResolveVisionAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(VisionProviderFixture());

        var keyframeExtractor = new Mock<IKeyframeExtractor>();
        var capturedInputPaths = new List<string>();
        keyframeExtractor
            .Setup(k => k.ExtractKeyframeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, double, int, CancellationToken>((inputPath, _, _, _, _) =>
            {
                capturedInputPaths.Add(inputPath);
                return Task.CompletedTask;
            });

        var shotCaptioner = new Mock<IShotCaptioner>();
        shotCaptioner
            .Setup(c => c.CaptionAsync(It.IsAny<ResolvedInferenceProvider>(), It.IsAny<ShotCaptionRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResolvedInferenceProvider _, ShotCaptionRequest r, int _, CancellationToken _) => CaptionFor(r.ShotId));

        VideoAnalyzeStepConfig config = new(
            Version: 1,
            Source: new VideoSourceRef(VideoSourceKind.ProjectFile, ProjectFileId: fileA),
            DetectSilence: false,
            Transcription: VideoTranscriptionMode.Off,
            AnalyzeVisuals: false,
            AnalyzeAudioLevels: false,
            Vision: VideoVisionMode.Optional,
            MinCaptionShotSeconds: 0.0,
            Sources:
            [
                new VideoSourceRef(VideoSourceKind.ProjectFile, ProjectFileId: fileA),
                new VideoSourceRef(VideoSourceKind.ProjectFile, ProjectFileId: fileB)
            ]);

        StepExecutionContext context = CreateAnalyzeContext(config);
        VideoAnalyzeStepExecutor executor = CreateAnalyzeExecutor(
            workspace, probe, silence, shotDetector,
            keyframeExtractor: keyframeExtractor, shotCaptioner: shotCaptioner, providerResolver: providerResolver);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        capturedInputPaths.Should().HaveCount(2);
        capturedInputPaths.Should().ContainSingle(p => p.Contains("src0-"));
        capturedInputPaths.Should().ContainSingle(p => p.Contains("src1-"));
    }

    [Fact]
    public async Task Single_source_vision_behaviour_is_unchanged_by_the_step_level_hoist()
    {
        Guid fileA = Guid.NewGuid();
        const string keyA = "projects/p/files/clipA.mp4";

        var workspace = new Mock<IProjectFileWorkspace>();
        workspace
            .Setup(w => w.ListFilesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectWorkspaceFile>
            {
                new(fileA, ProjectId, "clipA.mp4", null, "uploads", keyA, "video/mp4", 1024, DateTime.UtcNow, null)
            });
        workspace
            .Setup(w => w.DownloadStorageKeyToFileAsync(It.IsAny<Guid>(), keyA, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, string, CancellationToken>((_, _, dest, _) => { File.WriteAllBytes(dest, new byte[10]); return Task.CompletedTask; });

        var probe = new Mock<IMediaProbe>();
        probe.Setup(p => p.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaProbeResult(10, 30, 1, 1920, 1080, "h264", "aac", 48000));

        var silence = new Mock<ISilenceDetector>();
        silence.Setup(s => s.DetectAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(double, double)>());

        var shotDetector = new Mock<IShotDetector>();
        shotDetector
            .Setup(s => s.DetectShotsAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([(0.0, 2.0)]);

        var providerResolver = new Mock<IInferenceProviderResolver>();
        providerResolver.Setup(r => r.ResolveVisionAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(VisionProviderFixture());

        var keyframeExtractor = new Mock<IKeyframeExtractor>();
        var capturedPaths = new List<(string InputPath, string OutputPath)>();
        keyframeExtractor
            .Setup(k => k.ExtractKeyframeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, double, int, CancellationToken>((inputPath, outputPath, _, _, _) =>
            {
                capturedPaths.Add((inputPath, outputPath));
                return Task.CompletedTask;
            });

        var shotCaptioner = new Mock<IShotCaptioner>();
        shotCaptioner
            .Setup(c => c.CaptionAsync(It.IsAny<ResolvedInferenceProvider>(), It.IsAny<ShotCaptionRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResolvedInferenceProvider _, ShotCaptionRequest r, int _, CancellationToken _) => CaptionFor(r.ShotId));

        VideoAnalyzeStepConfig config = new(
            Version: 1,
            Source: new VideoSourceRef(VideoSourceKind.ProjectFile, ProjectFileId: fileA),
            DetectSilence: false,
            Transcription: VideoTranscriptionMode.Off,
            AnalyzeVisuals: false,
            AnalyzeAudioLevels: false,
            Vision: VideoVisionMode.Optional,
            MinCaptionShotSeconds: 0.0);
            // Sources left null/empty — falls back to the single legacy Source field (one-element list).

        StepExecutionContext context = CreateAnalyzeContext(config);
        VideoAnalyzeStepExecutor executor = CreateAnalyzeExecutor(
            workspace, probe, silence, shotDetector,
            keyframeExtractor: keyframeExtractor, shotCaptioner: shotCaptioner, providerResolver: providerResolver);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        capturedPaths.Should().HaveCount(1);
        // No "srcN-" prefix at all for a genuinely single-source config (not even "src0-") — the
        // filename must be byte-identical to the pre-Phase-4/pre-multi-source single-frame path.
        Path.GetFileName(capturedPaths[0].OutputPath).Should().Be("keyframe-s0.jpg");

        using JsonDocument doc = JsonDocument.Parse(result.Output);
        JsonElement shots = doc.RootElement.GetProperty("view").GetProperty("shots");
        shots.GetArrayLength().Should().Be(1);
        shots[0].TryGetProperty("c", out _).Should().BeTrue("the single shot must have been captioned");
    }

    [Fact]
    public async Task MaxSharpnessShots_is_a_step_wide_cap_across_every_source()
    {
        Guid fileA = Guid.NewGuid(), fileB = Guid.NewGuid();
        const string keyA = "projects/p/files/clipA.mp4", keyB = "projects/p/files/clipB.mp4";

        var shotsA = Enumerable.Range(0, 10).Select(i => ((double)(i * 2), (double)(i * 2 + 2))).ToList();
        var shotsB = Enumerable.Range(0, 10).Select(i => ((double)(i * 2), (double)(i * 2 + 2))).ToList();

        (Mock<IProjectFileWorkspace> workspace, Mock<IMediaProbe> probe, Mock<ISilenceDetector> silence, Mock<IShotDetector> shotDetector) =
            TwoSourceFixture(fileA, fileB, keyA, keyB, shotsA, shotsB);

        var frameGridSampler = new Mock<IFrameGridSampler>();
        frameGridSampler
            .Setup(g => g.SampleAsync(It.IsAny<string>(), It.IsAny<VideoScratchSpace>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FrameGridResult(new byte[4 * 4 * 3 * 4], 4, 4, 1.0, 4));

        var sharpnessSampler = new Mock<ISharpnessSampler>();
        sharpnessSampler
            .Setup(s => s.MeasureAsync(It.IsAny<string>(), It.IsAny<VideoScratchSpace>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0.6);

        VideoAnalyzeStepConfig config = new(
            Version: 1,
            Source: new VideoSourceRef(VideoSourceKind.ProjectFile, ProjectFileId: fileA),
            DetectSilence: false,
            Transcription: VideoTranscriptionMode.Off,
            AnalyzeVisuals: true,
            AnalyzeAudioLevels: false,
            DetectNearDuplicates: false,
            DetectSharpness: true,
            MaxSharpnessShots: 8,
            Sources:
            [
                new VideoSourceRef(VideoSourceKind.ProjectFile, ProjectFileId: fileA),
                new VideoSourceRef(VideoSourceKind.ProjectFile, ProjectFileId: fileB)
            ]);

        StepExecutionContext context = CreateAnalyzeContext(config);
        VideoAnalyzeStepExecutor executor = CreateAnalyzeExecutor(
            workspace, probe, silence, shotDetector, frameGridSampler: frameGridSampler, sharpnessSampler: sharpnessSampler);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        sharpnessSampler.Invocations.Count(i => i.Method.Name == nameof(ISharpnessSampler.MeasureAsync)).Should().Be(8);
    }

    [Fact]
    public async Task MaxSharpnessShots_budget_is_consumed_on_every_attempt_even_when_measurement_fails()
    {
        // MeasureAsync returns null on every attempt (ffmpeg failure/missing output/short read).
        // Before the fix, Remaining was only decremented on a SUCCESSFUL measurement, so a source
        // that failed every attempt left the budget untouched and the next source started with it
        // still fully intact — a 2-source step with budget 4 would make 4 calls PER source (8
        // total) instead of 4 total. Each source here has far more shots than the budget so
        // exhausting the budget (not running out of shots) is what stops the attempts.
        Guid fileA = Guid.NewGuid(), fileB = Guid.NewGuid();
        const string keyA = "projects/p/files/clipA.mp4", keyB = "projects/p/files/clipB.mp4";

        var shotsA = Enumerable.Range(0, 10).Select(i => ((double)(i * 2), (double)(i * 2 + 2))).ToList();
        var shotsB = Enumerable.Range(0, 10).Select(i => ((double)(i * 2), (double)(i * 2 + 2))).ToList();

        (Mock<IProjectFileWorkspace> workspace, Mock<IMediaProbe> probe, Mock<ISilenceDetector> silence, Mock<IShotDetector> shotDetector) =
            TwoSourceFixture(fileA, fileB, keyA, keyB, shotsA, shotsB);

        var frameGridSampler = new Mock<IFrameGridSampler>();
        frameGridSampler
            .Setup(g => g.SampleAsync(It.IsAny<string>(), It.IsAny<VideoScratchSpace>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FrameGridResult(new byte[4 * 4 * 3 * 4], 4, 4, 1.0, 4));

        var sharpnessSampler = new Mock<ISharpnessSampler>();
        sharpnessSampler
            .Setup(s => s.MeasureAsync(It.IsAny<string>(), It.IsAny<VideoScratchSpace>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((double?)null);

        VideoAnalyzeStepConfig config = new(
            Version: 1,
            Source: new VideoSourceRef(VideoSourceKind.ProjectFile, ProjectFileId: fileA),
            DetectSilence: false,
            Transcription: VideoTranscriptionMode.Off,
            AnalyzeVisuals: true,
            AnalyzeAudioLevels: false,
            DetectNearDuplicates: false,
            DetectSharpness: true,
            MaxSharpnessShots: 4,
            Sources:
            [
                new VideoSourceRef(VideoSourceKind.ProjectFile, ProjectFileId: fileA),
                new VideoSourceRef(VideoSourceKind.ProjectFile, ProjectFileId: fileB)
            ]);

        StepExecutionContext context = CreateAnalyzeContext(config);
        VideoAnalyzeStepExecutor executor = CreateAnalyzeExecutor(
            workspace, probe, silence, shotDetector, frameGridSampler: frameGridSampler, sharpnessSampler: sharpnessSampler);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        sharpnessSampler.Invocations.Count(i => i.Method.Name == nameof(ISharpnessSampler.MeasureAsync)).Should().Be(4,
            because: "the budget must be consumed step-wide on every ATTEMPT, not only on a successful measurement");
    }

    // ---------------------------------------------------------------------
    // VideoAnalyzeStepExecutor: per-source guardrail enforcement
    // ---------------------------------------------------------------------

    [Fact]
    public async Task MaxInputBytes_guardrail_is_enforced_per_source_not_against_the_combined_total()
    {
        // Source 0 is small (well under the cap); source 1 alone exceeds MaxInputBytes. If the
        // guardrail were checked against the SUM across sources, source 0 being small wouldn't
        // matter (the combined total still exceeds the cap) — but this test's real point is that
        // the step fails specifically once IT REACHES source 1, proving the check runs
        // independently per source rather than only once at the very end against a grand total.
        Guid fileA = Guid.NewGuid();
        Guid fileB = Guid.NewGuid();
        const string keyA = "projects/p/files/clipA.mp4";
        const string keyB = "projects/p/files/clipB.mp4";

        var workspace = new Mock<IProjectFileWorkspace>();
        workspace
            .Setup(w => w.ListFilesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectWorkspaceFile>
            {
                new(fileA, ProjectId, "clipA.mp4", null, "uploads", keyA, "video/mp4", 1024, DateTime.UtcNow, null),
                new(fileB, ProjectId, "clipB.mp4", null, "uploads", keyB, "video/mp4", 1024, DateTime.UtcNow, null)
            });
        workspace
            .Setup(w => w.DownloadStorageKeyToFileAsync(It.IsAny<Guid>(), keyA, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, string, CancellationToken>((_, _, dest, _) => { File.WriteAllBytes(dest, new byte[50]); return Task.CompletedTask; });
        workspace
            .Setup(w => w.DownloadStorageKeyToFileAsync(It.IsAny<Guid>(), keyB, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, string, CancellationToken>((_, _, dest, _) => { File.WriteAllBytes(dest, new byte[500]); return Task.CompletedTask; });

        var probe = new Mock<IMediaProbe>();
        probe.Setup(p => p.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaProbeResult(3, 30, 1, 1920, 1080, "h264", "aac", 48000));

        var silence = new Mock<ISilenceDetector>();
        silence.Setup(s => s.DetectAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(double, double)>());
        var shotDetector = new Mock<IShotDetector>();
        shotDetector
            .Setup(s => s.DetectShotsAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(double, double)>());

        VideoAnalyzeStepConfig config = new(
            Version: 1,
            Source: new VideoSourceRef(VideoSourceKind.ProjectFile, ProjectFileId: fileA),
            DetectSilence: false,
            DetectShots: false,
            Transcription: VideoTranscriptionMode.Off,
            MaxInputBytes: 100, // source A (50 bytes) passes; source B (500 bytes) must fail
            Sources:
            [
                new VideoSourceRef(VideoSourceKind.ProjectFile, ProjectFileId: fileA),
                new VideoSourceRef(VideoSourceKind.ProjectFile, ProjectFileId: fileB)
            ]);

        StepExecutionContext context = CreateAnalyzeContext(config);
        VideoAnalyzeStepExecutor executor = CreateAnalyzeExecutor(workspace, probe, silence, shotDetector);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        string code = doc.RootElement.GetProperty("error").GetProperty("code").GetString()!;
        code.Should().Be("INPUT_TOO_LARGE");
        doc.RootElement.GetProperty("error").GetProperty("message").GetString()!
            .Should().Contain("Source 1", because: "the failure must identify WHICH source exceeded the guardrail");
    }

    [Fact]
    public async Task MaxDurationSeconds_guardrail_is_enforced_per_source()
    {
        // Source 0's duration is fine; source 1's duration alone exceeds MaxDurationSeconds. Proves
        // the duration guardrail (checked before any decode) runs independently for each source,
        // not only once against some combined figure.
        Guid fileA = Guid.NewGuid();
        Guid fileB = Guid.NewGuid();
        const string keyA = "projects/p/files/clipA.mp4";
        const string keyB = "projects/p/files/clipB.mp4";

        var workspace = new Mock<IProjectFileWorkspace>();
        workspace
            .Setup(w => w.ListFilesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectWorkspaceFile>
            {
                new(fileA, ProjectId, "clipA.mp4", null, "uploads", keyA, "video/mp4", 1024, DateTime.UtcNow, null),
                new(fileB, ProjectId, "clipB.mp4", null, "uploads", keyB, "video/mp4", 1024, DateTime.UtcNow, null)
            });
        workspace
            .Setup(w => w.DownloadStorageKeyToFileAsync(It.IsAny<Guid>(), It.IsIn(keyA, keyB), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, string, CancellationToken>((_, _, dest, _) => { File.WriteAllBytes(dest, new byte[10]); return Task.CompletedTask; });

        var probe = new Mock<IMediaProbe>();
        probe.Setup(p => p.ProbeAsync(It.Is<string>(s => s.Contains("src0-")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaProbeResult(5, 30, 1, 1920, 1080, "h264", "aac", 48000));
        probe.Setup(p => p.ProbeAsync(It.Is<string>(s => s.Contains("src1-")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaProbeResult(500, 30, 1, 1920, 1080, "h264", "aac", 48000));

        var silence = new Mock<ISilenceDetector>();
        silence.Setup(s => s.DetectAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(double, double)>());
        var shotDetector = new Mock<IShotDetector>();
        shotDetector
            .Setup(s => s.DetectShotsAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(double, double)>());

        VideoAnalyzeStepConfig config = new(
            Version: 1,
            Source: new VideoSourceRef(VideoSourceKind.ProjectFile, ProjectFileId: fileA),
            DetectSilence: false,
            DetectShots: false,
            Transcription: VideoTranscriptionMode.Off,
            MaxDurationSeconds: 60,
            Sources:
            [
                new VideoSourceRef(VideoSourceKind.ProjectFile, ProjectFileId: fileA),
                new VideoSourceRef(VideoSourceKind.ProjectFile, ProjectFileId: fileB)
            ]);

        StepExecutionContext context = CreateAnalyzeContext(config);
        VideoAnalyzeStepExecutor executor = CreateAnalyzeExecutor(workspace, probe, silence, shotDetector);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("DURATION_EXCEEDED");
    }

    private static StepExecutionContext CreateAnalyzeContext(VideoAnalyzeStepConfig config)
    {
        string configJson = JsonSerializer.Serialize(config, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        });

        var step = new WorkflowStep
        {
            Id = Guid.NewGuid(),
            StepOrder = 1,
            StepType = StepType.VideoAnalyze,
            VideoAnalyzeConfigJson = configJson
        };

        return new StepExecutionContext
        {
            Execution = new WorkflowExecution { Id = Guid.NewGuid(), ProjectId = ProjectId },
            Step = step,
            AllSteps = [step],
            AccumulatedOutput = string.Empty,
            StepOutputHistory = [],
            CurrentStepIndex = 0,
            IterationCount = 0,
            CorrelationId = "test",
            CancellationToken = CancellationToken.None
        };
    }

    private static VideoAnalyzeStepExecutor CreateAnalyzeExecutor(
        Mock<IProjectFileWorkspace> workspace, Mock<IMediaProbe> probe, Mock<ISilenceDetector> silence, Mock<IShotDetector> shotDetector,
        Mock<IFrameGridSampler>? frameGridSampler = null, Mock<IKeyframeExtractor>? keyframeExtractor = null,
        Mock<IShotCaptioner>? shotCaptioner = null, Mock<IInferenceProviderResolver>? providerResolver = null,
        Mock<ISharpnessSampler>? sharpnessSampler = null)
    {
        var audioExtractor = new Mock<IAudioExtractor>();
        audioExtractor
            .Setup(a => a.ExtractWavAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, CancellationToken>((_, outPath, _) => { File.WriteAllBytes(outPath, new byte[100]); return Task.CompletedTask; });

        frameGridSampler ??= new Mock<IFrameGridSampler>();
        keyframeExtractor ??= new Mock<IKeyframeExtractor>();
        shotCaptioner ??= new Mock<IShotCaptioner>();
        sharpnessSampler ??= new Mock<ISharpnessSampler>();
        var transcriptionFactory = new Mock<ITranscriptionClientFactory>();
        providerResolver ??= new Mock<IInferenceProviderResolver>();

        string tempScratchRoot = Path.Combine(Path.GetTempPath(), "video-multisource-tests", Guid.NewGuid().ToString("N"));
        var options = Options.Create(new VideoEditingOptions
        {
            ScratchPath = tempScratchRoot,
            MaxConcurrentJobs = 1,
            AnalyzeTimeoutSeconds = 30,
            CompileTimeoutSeconds = 30
        });

        workspace
            .Setup(w => w.UploadArtifactAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .ReturnsAsync("projects/p/agentFiles/video-analysis/e/step-1-analysis.json");

        return new VideoAnalyzeStepExecutor(
            probe.Object, silence.Object, shotDetector.Object, audioExtractor.Object,
            frameGridSampler.Object, keyframeExtractor.Object, sharpnessSampler.Object, shotCaptioner.Object,
            workspace.Object, transcriptionFactory.Object, providerResolver.Object,
            options, NullLogger<VideoAnalyzeStepExecutor>.Instance);
    }

    // ---------------------------------------------------------------------
    // VideoCompileStepExecutor: cross-source Keep-span validation
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Keep_span_mixing_ids_from_two_different_sources_is_rejected()
    {
        VideoAnalysisArtifact artifact = BuildTwoSourceArtifact(out string keyA, out string keyB);
        // s0 is source 0 (clip A), s1 is source 1 (clip B) — a single span may never bridge them.
        string decisionJson = BuildDecisionJson(("s0", "s1", "cross-clip (invalid)"));

        StepExecutionContext context = CreateCompileContext(artifact, decisionJson, keyA, keyB, out Mock<IProjectFileWorkspace> workspace);
        VideoCompileStepExecutor executor = CreateCompileExecutor(workspace);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        ErrorCode(result).Should().Be("MIXED_SOURCE_SPAN");
    }

    [Fact]
    public async Task Multi_source_StreamCopy_is_rejected_with_a_clear_error()
    {
        VideoAnalysisArtifact artifact = BuildTwoSourceArtifact(out string keyA, out string keyB);
        // Two separate, valid single-clip spans — one per source — is a legitimate cross-clip edit,
        // but StreamCopy has no multi-input equivalent.
        string decisionJson = BuildDecisionJson(("s0", "s0", "from clip A"), ("s1", "s1", "from clip B"));

        StepExecutionContext context = CreateCompileContext(
            artifact, decisionJson, keyA, keyB, out Mock<IProjectFileWorkspace> workspace,
            cfg => cfg with { Mode = VideoCompileMode.StreamCopy, AllowKeyframeSnapping = true });
        VideoCompileStepExecutor executor = CreateCompileExecutor(workspace);

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Failed);
        ErrorCode(result).Should().Be("MULTI_SOURCE_REQUIRES_REENCODE");
    }

    [Fact]
    public async Task Multi_source_reencode_downloads_both_clips_and_builds_a_multi_input_concat_filtergraph()
    {
        VideoAnalysisArtifact artifact = BuildTwoSourceArtifact(out string keyA, out string keyB);
        string decisionJson = BuildDecisionJson(("s0", "s0", "from clip A"), ("s1", "s1", "from clip B"));

        List<string>? capturedArgs = null;
        StepExecutionContext context = CreateCompileContext(artifact, decisionJson, keyA, keyB, out Mock<IProjectFileWorkspace> workspace);
        VideoCompileStepExecutor executor = CreateCompileExecutor(workspace, args => capturedArgs = args.ToList());

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        capturedArgs.Should().NotBeNull();

        // Two distinct source inputs, one per clip, in deterministic (sorted-by-source-index) order.
        List<int> inputPositions = capturedArgs!
            .Select((a, i) => (a, i))
            .Where(t => t.a == "-i")
            .Select(t => t.i + 1)
            .ToList();
        inputPositions.Count.Should().BeGreaterThanOrEqualTo(2);
        capturedArgs![inputPositions[0]].Should().Contain("source-0");
        capturedArgs![inputPositions[1]].Should().Contain("source-1");

        // Multi-source always scripts the filtergraph to a file (never inline -filter_complex).
        capturedArgs.Should().Contain("-filter_complex_script");
        capturedArgs.Should().NotContain("-filter_complex");

        int scriptArgIndex = capturedArgs.IndexOf("-filter_complex_script") + 1;
        string scriptPath = capturedArgs[scriptArgIndex];
        File.Exists(scriptPath).Should().BeTrue();
        string filterComplex = File.ReadAllText(scriptPath);

        // Each span trims from ITS OWN input index: span 0 (source 0, ffmpeg input 0) must trim
        // "[0:v]"/"[0:a]"; span 1 (source 1, ffmpeg input 1) must trim "[1:v]"/"[1:a]".
        filterComplex.Should().Contain("[0:v]trim=");
        filterComplex.Should().Contain("[0:a]atrim=");
        filterComplex.Should().Contain("[1:v]trim=");
        filterComplex.Should().Contain("[1:a]atrim=");
        filterComplex.Should().Contain("concat=n=2:v=1:a=1");

        capturedArgs.Should().Contain("-map");
        capturedArgs.Should().Contain("[vout]");
        capturedArgs.Should().Contain("[aout]");
    }

    [Fact]
    public async Task Multi_source_compile_with_music_mixes_after_the_concat_stage_and_music_is_the_last_input()
    {
        // Background music (see docs/video-editing.md "Background music") interacting with the
        // multi-source concat path: the concat stage's audio output flips to [adial], music is
        // appended as the LAST ffmpeg input (after both source clips), and the mix stage produces
        // [aout]. A music-free multi-source compile (the test above) must remain unaffected.
        VideoAnalysisArtifact artifact = BuildTwoSourceArtifact(out string keyA, out string keyB);
        string decisionJson = BuildDecisionJson(("s0", "s0", "from clip A"), ("s1", "s1", "from clip B"));

        const string musicKey = "projects/p/userFiles/bed.mp3";
        Guid musicProjectFileId = Guid.NewGuid();

        List<string>? capturedArgs = null;
        StepExecutionContext context = CreateCompileContext(
            artifact, decisionJson, keyA, keyB, out Mock<IProjectFileWorkspace> workspace,
            cfg => cfg with { EnableMusic = true, MusicTrackProjectFileId = musicProjectFileId });

        workspace
            .Setup(w => w.DownloadStorageKeyToFileAsync(It.IsAny<Guid>(), musicKey, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, string, CancellationToken>((_, _, dest, _) => { File.WriteAllBytes(dest, new byte[] { 0x00 }); return Task.CompletedTask; });
        workspace
            .Setup(w => w.ListFilesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectWorkspaceFile>
            {
                new(musicProjectFileId, ProjectId, "bed.mp3", null, "userFiles", musicKey, "audio/mpeg", 4096, DateTime.UtcNow, null)
            });

        VideoCompileStepExecutor executor = CreateCompileExecutor(workspace, args => capturedArgs = args.ToList());

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        capturedArgs.Should().NotBeNull();

        // Three inputs: source clip 0, source clip 1, then music LAST.
        List<int> inputPositions = capturedArgs!
            .Select((a, i) => (a, i))
            .Where(t => t.a == "-i")
            .Select(t => t.i + 1)
            .ToList();
        inputPositions.Should().HaveCount(3);
        capturedArgs![inputPositions[0]].Should().Contain("source-0");
        capturedArgs[inputPositions[1]].Should().Contain("source-1");
        capturedArgs[inputPositions[^1]].Should().Contain("music", "the music track must be the LAST ffmpeg input, after both source clips");

        int scriptArgIndex = capturedArgs.IndexOf("-filter_complex_script") + 1;
        string filterComplex = File.ReadAllText(capturedArgs[scriptArgIndex]);

        filterComplex.Should().Contain("concat=n=2:v=1:a=1[vout][adial]", "the concat stage's audio output must flip to [adial] when music is enabled");
        filterComplex.Should().Contain("[2:a]atrim=end=", "music is ffmpeg input index 2 here (2 source clips + 0 asset overlays)");
        filterComplex.Should().Contain("[amus]");
        filterComplex.Should().Contain("[adial][amus]amix=inputs=2:duration=first:dropout_transition=0:normalize=0[aout]");
    }

    [Fact]
    public async Task Multi_source_compile_where_one_clip_has_no_audio_drops_audio_from_the_whole_output()
    {
        // concat's own "a=" stream count must be uniform across every concatenated segment, so a
        // mix of audio-having and audio-less source clips can't produce a per-segment audio
        // branch for only some of them. The correct, safe degrade is to drop audio for the WHOLE
        // compiled output — exactly mirroring the single-source path's own per-clip behavior —
        // rather than crashing ffmpeg on the audio-less clip's "[N:a]" (the pre-fix bug) or
        // fabricating silence to paper over the gap.
        VideoAnalysisArtifact artifact = BuildTwoSourceArtifact(out string keyA, out string keyB);
        string decisionJson = BuildDecisionJson(("s0", "s0", "from clip A"), ("s1", "s1", "from clip B"));

        List<string>? capturedArgs = null;
        string? capturedFilterComplex = null;
        StepExecutionContext context = CreateCompileContext(artifact, decisionJson, keyA, keyB, out Mock<IProjectFileWorkspace> workspace);
        VideoCompileStepExecutor executor = CreateCompileExecutor(
            workspace,
            args =>
            {
                // Read the scripted filter_complex file INSIDE the callback, while the ffmpeg
                // mock is invoked — not after ExecuteAsync returns, at which point the executor's
                // own `finally { scratch?.Dispose(); }` has already recursively deleted the whole
                // scratch directory (by design, for a real run) and the file would be gone.
                capturedArgs = args.ToList();
                int idx = capturedArgs.IndexOf("-filter_complex_script");
                if (idx >= 0) capturedFilterComplex = File.ReadAllText(capturedArgs[idx + 1]);
            },
            configureMediaProbe: mock => mock
                .Setup(p => p.ProbeAsync(It.Is<string>(path => path.Contains("source-1")), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MediaProbeResult(10, 30, 1, 1920, 1080, "h264", null, null)));

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        capturedArgs.Should().NotBeNull();
        capturedFilterComplex.Should().NotBeNull();
        string filterComplex = capturedFilterComplex!;

        filterComplex.Should().Contain("[0:v]trim=", "source 0's video still concatenates normally");
        filterComplex.Should().Contain("[1:v]trim=", "source 1's video still concatenates normally");
        filterComplex.Should().NotContain("[0:a]atrim=", "audio is dropped for the whole output, not just the audio-less clip");
        filterComplex.Should().NotContain("[1:a]atrim=");
        filterComplex.Should().Contain("concat=n=2:v=1:a=0", "the concat filter itself must declare zero audio streams");

        capturedArgs.Should().NotContain("[aout]");
        capturedArgs.Should().NotContain("-c:a");
    }

    [Fact]
    public async Task Single_distinct_source_referenced_by_a_multi_source_artifact_uses_the_original_single_input_path()
    {
        // The artifact declares two sources, but the decision only keeps material from one of
        // them — this must take the UNCHANGED single-input EncodeReencodeAsync path (only one -i),
        // never the multi-source concat path, even though the artifact itself is multi-source-capable.
        VideoAnalysisArtifact artifact = BuildTwoSourceArtifact(out string keyA, out string keyB);
        string decisionJson = BuildDecisionJson(("s0", "s0", "from clip A only"));

        List<string>? capturedArgs = null;
        StepExecutionContext context = CreateCompileContext(artifact, decisionJson, keyA, keyB, out Mock<IProjectFileWorkspace> workspace);
        VideoCompileStepExecutor executor = CreateCompileExecutor(workspace, args => capturedArgs = args.ToList());

        StepExecutionResult result = await executor.ExecuteAsync(context);

        result.Status.Should().Be(StepStatus.Completed, because: result.ErrorDetails ?? result.Output);
        capturedArgs.Should().NotBeNull();
        capturedArgs!.Count(a => a == "-i").Should().Be(1);
        capturedArgs.Should().NotContain("-filter_complex_script");
        capturedArgs.Should().Contain("-filter_complex");
    }

    // ---------------------------------------------------------------------
    // Shared multi-source compile fixtures
    // ---------------------------------------------------------------------

    private static VideoAnalysisArtifact BuildTwoSourceArtifact(out string keyA, out string keyB)
    {
        keyA = "projects/p/files/clipA.mp4";
        keyB = "projects/p/files/clipB.mp4";

        var mediaA = new VideoAnalysisMedia(10, 30, 1, 1920, 1080);
        var mediaB = new VideoAnalysisMedia(8, 25, 1, 1280, 720);

        List<VideoAnalysisShot> shots =
        [
            new VideoAnalysisShot("s0", 0.0, 4.0, SourceIndex: 0),
            new VideoAnalysisShot("s1", 0.0, 3.0, SourceIndex: 1)
        ];

        return new VideoAnalysisArtifact(
            Version: 2,
            Media: mediaA,
            Shots: shots,
            SilenceSpans: [],
            Segments: [],
            Words: [],
            OfferedIds: ["s0", "s1"],
            Provenance: new VideoAnalysisProvenance(VideoTranscriptionMode.Off, false, false),
            Sources:
            [
                new VideoAnalysisSourceInfo(0, keyA, mediaA),
                new VideoAnalysisSourceInfo(1, keyB, mediaB)
            ]);
    }

    private static StepExecutionContext CreateCompileContext(
        VideoAnalysisArtifact artifact, string decisionJson, string keyA, string keyB,
        out Mock<IProjectFileWorkspace> workspace,
        Func<VideoCompileStepConfig, VideoCompileStepConfig>? configOverride = null)
    {
        workspace = new Mock<IProjectFileWorkspace>();
        const string analysisKey = "projects/p/agentFiles/video-analysis/e/step-1-analysis.json";

        string artifactJson = JsonSerializer.Serialize(artifact, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        });

        workspace
            .Setup(w => w.DownloadStorageKeyToFileAsync(It.IsAny<Guid>(), analysisKey, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, string, CancellationToken>((_, _, dest, _) => { File.WriteAllText(dest, artifactJson); return Task.CompletedTask; });
        workspace
            .Setup(w => w.DownloadStorageKeyToFileAsync(It.IsAny<Guid>(), keyA, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, string, CancellationToken>((_, _, dest, _) => { File.WriteAllBytes(dest, new byte[] { 0x00, 0x01 }); return Task.CompletedTask; });
        workspace
            .Setup(w => w.DownloadStorageKeyToFileAsync(It.IsAny<Guid>(), keyB, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, string, CancellationToken>((_, _, dest, _) => { File.WriteAllBytes(dest, new byte[] { 0x02, 0x03 }); return Task.CompletedTask; });

        VideoCompileStepConfig config = new(
            Version: 1,
            Decision: new ExtractInputRef(ExtractInputSource.Previous),
            AnalysisStepOrder: 1);
        if (configOverride is not null)
            config = configOverride(config);

        string configJson = JsonSerializer.Serialize(config, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        });

        var step = new WorkflowStep
        {
            Id = Guid.NewGuid(),
            StepOrder = 3,
            StepType = StepType.VideoCompile,
            VideoCompileConfigJson = configJson
        };

        List<StepOutputHistoryEntry> history =
        [
            new StepOutputHistoryEntry(1, "Analyze", "{}", OutputStorageKey: null, ArtifactStorageKey: analysisKey),
            new StepOutputHistoryEntry(2, "StoryEditor", decisionJson, OutputStorageKey: null, ArtifactStorageKey: null)
        ];

        return new StepExecutionContext
        {
            Execution = new WorkflowExecution { Id = Guid.NewGuid(), ProjectId = ProjectId },
            Step = step,
            AllSteps = [step],
            AccumulatedOutput = decisionJson,
            StepOutputHistory = history,
            CurrentStepIndex = 2,
            IterationCount = 0,
            CorrelationId = "test",
            CancellationToken = CancellationToken.None
        };
    }

    private static VideoCompileStepExecutor CreateCompileExecutor(
        Mock<IProjectFileWorkspace> workspace, Action<IReadOnlyList<string>>? ffmpegArgsCaptured = null,
        bool amixNormalizeAvailable = true, Action<Mock<IMediaProbe>>? configureMediaProbe = null)
    {
        VideoCompileStepExecutor.ResetDrawtextAvailabilityCacheForTests();
        VideoCompileStepExecutor.ResetAmixNormalizeCacheForTests();

        var toolRunner = new Mock<IVideoToolRunner>();
        toolRunner
            .Setup(t => t.RunFfmpegAsync(
                It.Is<IReadOnlyList<string>>(a => !a.Contains("-filters")), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<string>, TimeSpan, CancellationToken>((args, _, _) => ffmpegArgsCaptured?.Invoke(args))
            .ReturnsAsync(new VideoToolResult(0, string.Empty, string.Empty, false));
        toolRunner
            .Setup(t => t.RunFfmpegAsync(
                It.Is<IReadOnlyList<string>>(a => !a.Contains("-filters")), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>(),
                It.IsAny<Action<string>>()))
            .Callback<IReadOnlyList<string>, TimeSpan, CancellationToken, Action<string>>((args, _, _, _) => ffmpegArgsCaptured?.Invoke(args))
            .ReturnsAsync(new VideoToolResult(0, string.Empty, string.Empty, false));
        // Applied after the two catch-all setups above so it wins for the amix probe specifically
        // (background music — see VideoCompileStepExecutorTests.CreateExecutor's identical setup).
        toolRunner
            .Setup(t => t.RunFfmpegAsync(
                It.Is<IReadOnlyList<string>>(a => a.Contains("filter=amix")), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VideoToolResult(0, amixNormalizeAvailable ? "... normalize ..." : "... (no normalize) ...", string.Empty, false));

        var mediaProbe = new Mock<IMediaProbe>();
        mediaProbe
            .Setup(p => p.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaProbeResult(10, 30, 1, 1920, 1080, "h264", "aac", 48000));
        // Applied after the catch-all above, mirroring VideoCompileStepExecutorTests.CreateExecutor's
        // identical override precedence — a test-supplied probe (e.g. "this one source has no audio
        // stream") wins over the default success response.
        configureMediaProbe?.Invoke(mediaProbe);

        workspace
            .Setup(w => w.UploadArtifactAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .ReturnsAsync("projects/p/agentFiles/video-analysis/e/step-3-edl.json");

        workspace
            .Setup(w => w.UploadBinaryFileAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<SummaryStatus>(), It.IsAny<FileIndexingStatus>(), It.IsAny<CancellationToken>(),
                It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync((Guid projectId, string _, string fileName, string mime, SummaryStatus _, FileIndexingStatus _,
                CancellationToken _, string category, string? _) =>
                new ProjectWorkspaceFile(
                    Guid.NewGuid(), projectId, fileName, null, category,
                    $"projects/{projectId}/{category}/{Guid.NewGuid():D}.mp4", mime, 1024, DateTime.UtcNow, null));

        string tempScratchRoot = Path.Combine(Path.GetTempPath(), "video-multisource-compile-tests", Guid.NewGuid().ToString("N"));
        var options = Options.Create(new VideoEditingOptions
        {
            ScratchPath = tempScratchRoot,
            MaxConcurrentJobs = 1,
            AnalyzeTimeoutSeconds = 30,
            CompileTimeoutSeconds = 30
        });

        var scopeFactory = new Mock<IServiceScopeFactory>();

        return new VideoCompileStepExecutor(
            toolRunner.Object, mediaProbe.Object, workspace.Object, scopeFactory.Object, options,
            NullLogger<VideoCompileStepExecutor>.Instance);
    }

    private static string ErrorCode(StepExecutionResult result)
    {
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        return doc.RootElement.GetProperty("error").GetProperty("code").GetString() ?? string.Empty;
    }

    private static string BuildDecisionJson(params (string FromId, string ToId, string Reason)[] spans)
    {
        var keep = spans.Select(s => new { fromId = s.FromId, toId = s.ToId, reason = s.Reason });
        return JsonSerializer.Serialize(new { keep, editRationale = "test", suggestedTitle = "Test Edit" });
    }
}
