using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Data.OutputSchemas;
using ReelForge.Shared.Workflows;
using ReelForge.WorkflowEngine.Execution;
using ReelForge.WorkflowEngine.Execution.StepExecutors;
using ReelForge.WorkflowEngine.Services.Storage;
using ReelForge.WorkflowEngine.Services.Video;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Covers the <c>inEdit</c> marking on <c>view.placements</c> — the pre-decision signal that
/// replaces a silent post-hoc <c>cut_away</c> drop in <c>VideoCompileStepExecutor</c>.
///
/// <para>
/// Note WHERE this is tested from: <see cref="MotionGraphicsPlacementAnnotator"/> sits in the
/// AGENT step path, not in <c>VideoAnalyzeStepExecutor</c>. The analyze step builds the placement
/// candidates before the story editor's decision exists, so it structurally cannot know which
/// candidates survive; the annotator runs at the moment the planner's own prompt is assembled,
/// when the analysis artifact and the decision are both in step history.
/// </para>
/// </summary>
public class MotionGraphicsPlacementAnnotatorTests
{
    private const string AnalysisKey = "projects/p/agentFiles/video-analysis/analysis.json";

    private static readonly JsonSerializerOptions ArtifactOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    // =======================================================================
    // The core property: overlap with a kept span => inEdit true, no overlap => inEdit false.
    // =======================================================================

    [Fact]
    public void Annotate_marks_a_placement_overlapping_a_kept_span_inEdit_true_and_a_cut_away_one_false()
    {
        // Shot s0 [0,10) is kept; shot s1 [10,20) is cut. p0's window sits inside s0, p1's inside s1.
        VideoAnalysisPlacement kept = Placement("p0", "s0", startSec: 4.0, endSec: 6.0);
        VideoAnalysisPlacement cutAway = Placement("p1", "s1", startSec: 14.0, endSec: 16.0);

        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: [("s0", 0.0, 10.0), ("s1", 10.0, 20.0)],
            placements: [kept, cutAway]);

        VideoEditDecisionOutput decision = Decision(("s0", "s0"));

        string? annotated = MotionGraphicsPlacementAnnotator.Annotate(
            BuildEnvelope(artifact), artifact, decision);

        annotated.Should().NotBeNull();
        InEditFlags(annotated!).Should().Equal(new Dictionary<string, bool?>
        {
            ["p0"] = true,
            ["p1"] = false
        });
    }

    [Fact]
    public void Annotate_marks_a_placement_only_partially_overlapping_a_kept_span_inEdit_true()
    {
        // The kept span ends mid-placement — the overlay would still be visible for part of its
        // window, which is exactly what MapSourceWindowToOutput reports as surviving (it clips to
        // the kept portion rather than dropping the overlay).
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: [("s0", 0.0, 5.0), ("s1", 5.0, 20.0)],
            placements: [Placement("p0", "s1", startSec: 4.0, endSec: 8.0)]);

        string? annotated = MotionGraphicsPlacementAnnotator.Annotate(
            BuildEnvelope(artifact), artifact, Decision(("s0", "s0")));

        InEditFlags(annotated!)["p0"].Should().Be(true);
    }

    [Fact]
    public void Annotate_is_display_only_it_never_drops_or_rewrites_a_placement_candidate()
    {
        // The whole point of marking rather than filtering: an inEdit:false candidate is still
        // fully offered, with every original field intact, and remains legal for the model to pick.
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: [("s0", 0.0, 10.0), ("s1", 10.0, 20.0)],
            placements:
            [
                Placement("p0", "s0", startSec: 4.0, endSec: 6.0),
                Placement("p1", "s1", startSec: 14.0, endSec: 16.0)
            ]);

        string envelope = BuildEnvelope(artifact);
        string? annotated = MotionGraphicsPlacementAnnotator.Annotate(envelope, artifact, Decision(("s0", "s0")));

        JsonArray before = (JsonArray)JsonNode.Parse(envelope)!["view"]!["placements"]!;
        JsonArray after = (JsonArray)JsonNode.Parse(annotated!)!["view"]!["placements"]!;

        after.Count.Should().Be(before.Count, "no candidate may be filtered out — inEdit is a hint, not a gate");
        for (int i = 0; i < before.Count; i++)
        {
            JsonObject original = (JsonObject)before[i]!;
            JsonObject marked = (JsonObject)after[i]!;

            marked.Count.Should().Be(original.Count + 1, "only the inEdit key may be added");
            foreach (KeyValuePair<string, JsonNode?> property in original)
                marked[property.Key]!.ToJsonString().Should().Be(property.Value!.ToJsonString());
        }
    }

    [Fact]
    public void Annotate_never_marks_a_placement_inEdit_using_another_source_clips_kept_spans()
    {
        // Multi-source discipline (inherited verbatim from MapSourceWindowToOutput): source 1's
        // placement window numerically falls inside source 0's kept span, but those are two
        // different clocks — it must still read as cut away.
        var shotA = new VideoAnalysisShot("s0", 0.0, 10.0, SourceIndex: 0);
        var shotB = new VideoAnalysisShot("s1", 0.0, 10.0, SourceIndex: 1);

        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: [],
            placements:
            [
                Placement("p0", "s0", startSec: 4.0, endSec: 6.0, sourceIndex: 0),
                Placement("p1", "s1", startSec: 4.0, endSec: 6.0, sourceIndex: 1)
            ]) with
        {
            Shots = [shotA, shotB],
            OfferedIds = ["s0", "s1"]
        };

        string? annotated = MotionGraphicsPlacementAnnotator.Annotate(
            BuildEnvelope(artifact), artifact, Decision(("s0", "s0")));

        InEditFlags(annotated!).Should().Equal(new Dictionary<string, bool?>
        {
            ["p0"] = true,
            ["p1"] = false
        });
    }

    // =======================================================================
    // Kept-span resolution reuses VideoCompileStepExecutor's own id index and offered-id rules.
    // =======================================================================

    [Fact]
    public void ResolveKeptSpans_ignores_ids_that_were_never_offered_and_ids_outside_the_cut_anchor_namespace()
    {
        VideoAnalysisPlacement placement = Placement("p0", "s1", startSec: 14.0, endSec: 16.0);
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: [("s0", 0.0, 10.0), ("s1", 10.0, 20.0)],
            placements: [placement],
            // s1 exists in the artifact but was trimmed out of the bounded view.
            offeredIds: ["s0"]);

        // A span naming the un-offered s1, plus one naming a placement id (a namespace
        // BuildIdTimeIndex deliberately cannot resolve) — neither may become a kept span.
        MotionGraphicsPlacementAnnotator.ResolveKeptSpans(artifact, Decision(("s1", "s1"), ("p0", "p0")))
            .Should().BeEmpty();

        MotionGraphicsPlacementAnnotator.IsInEdit([], placement).Should().BeFalse(
            "with no resolvable kept spans nothing survives the cut");
    }

    [Fact]
    public void ResolveKeptSpans_orders_spans_by_source_then_start_time()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: [("s0", 0.0, 5.0), ("s1", 5.0, 10.0), ("s2", 10.0, 15.0)],
            placements: []);

        MotionGraphicsPlacementAnnotator
            .ResolveKeptSpans(artifact, Decision(("s2", "s2"), ("s0", "s0")))
            .Select(s => s.SnappedStart)
            .Should().Equal(0.0, 10.0);
    }

    // =======================================================================
    // Decision discovery out of raw agent completion text.
    // =======================================================================

    [Fact]
    public void TryParseDecision_tolerates_trailing_prose_and_rejects_an_empty_keep_list()
    {
        MotionGraphicsPlacementAnnotator
            .TryParseDecision("""{"keep":[{"fromId":"s0","toId":"s0","reason":"good"}],"editRationale":""} done!""")
            !.Keep.Should().HaveCount(1);

        MotionGraphicsPlacementAnnotator.TryParseDecision("""{"keep":[]}""").Should().BeNull();
        MotionGraphicsPlacementAnnotator.TryParseDecision("""{"keep":null}""").Should().BeNull();
        MotionGraphicsPlacementAnnotator.TryParseDecision("no json here").Should().BeNull();
    }

    [Fact]
    public void TryParseDecision_accepts_an_EditRoom_step_output_with_its_additive_room_metadata_block()
    {
        // FindDecision is deliberately duck-typed by SHAPE (a non-empty "keep" array), not by the
        // producing step's type — so a StepType.EditRoom predecessor works exactly like a solo
        // Agent(VideoStoryEditor) one. The room's ADDITIVE "room" sibling object must be ignored,
        // never a parse failure.
        var decision = MotionGraphicsPlacementAnnotator.TryParseDecision(
            """
            {"keep":[{"fromId":"s0","toId":"s1","reason":"room agreed"}],
             "editRationale":"room synthesis","suggestedTitle":"Room Edit",
             "room":{"seats":["PacingEditor","StoryEditor"],"turnCount":6,"terminationReason":"converged","degraded":false}}
            """);

        decision.Should().NotBeNull();
        decision!.Keep.Should().HaveCount(1);
        decision.Keep[0].FromId.Should().Be("s0");
        decision.Keep[0].ToId.Should().Be("s1");
    }

    // =======================================================================
    // End-to-end through the real StepExecutionContext prompt assembly.
    // =======================================================================

    [Fact]
    public async Task AnnotateAsync_puts_inEdit_into_the_actual_FullWorkflow_prompt_the_planner_receives()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: [("s0", 0.0, 10.0), ("s1", 10.0, 20.0)],
            placements:
            [
                Placement("p0", "s0", startSec: 4.0, endSec: 6.0),
                Placement("p1", "s1", startSec: 14.0, endSec: 16.0)
            ]);

        StepExecutionContext context = CreateContext(
            analyzeOutput: BuildEnvelope(artifact),
            decisionOutput: """{"keep":[{"fromId":"s0","toId":"s0","reason":"keeper"}],"editRationale":"r"}""");

        string before = context.BuildAgentInput();
        before.Should().NotContain("inEdit");

        await CreateAnnotator(artifact).AnnotateAsync(context, CancellationToken.None);

        string after = context.BuildAgentInput();
        after.Should().Contain("\"inEdit\":true").And.Contain("\"inEdit\":false");
        after.Should().Contain("\"p0\"").And.Contain("\"p1\"", "both candidates stay in the prompt");
        after.Should().Contain("keeper", "the story editor's decision is still part of the FullWorkflow context");
    }

    [Fact]
    public async Task AnnotateAsync_leaves_the_prompt_untouched_when_no_story_editor_decision_is_in_history()
    {
        // The template variant with no story-editor step (or a planner placed before it) must
        // simply get an un-annotated prompt, never a failure.
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: [("s0", 0.0, 10.0)],
            placements: [Placement("p0", "s0", startSec: 4.0, endSec: 6.0)]);

        StepExecutionContext context = CreateContext(
            analyzeOutput: BuildEnvelope(artifact),
            decisionOutput: "not a decision at all");

        string expected = context.BuildAgentInput();

        await CreateAnnotator(artifact).AnnotateAsync(context, CancellationToken.None);

        context.BuildAgentInput().Should().Be(expected);
    }

    [Fact]
    public async Task AnnotateAsync_swallows_a_storage_failure_and_leaves_the_prompt_untouched()
    {
        VideoAnalysisArtifact artifact = BuildArtifact(
            shots: [("s0", 0.0, 10.0)],
            placements: [Placement("p0", "s0", startSec: 4.0, endSec: 6.0)]);

        StepExecutionContext context = CreateContext(
            analyzeOutput: BuildEnvelope(artifact),
            decisionOutput: """{"keep":[{"fromId":"s0","toId":"s0","reason":"keeper"}]}""");

        string expected = context.BuildAgentInput();

        var workspace = new Mock<IProjectFileWorkspace>();
        workspace
            .Setup(w => w.DownloadStorageKeyToFileAsync(
                It.IsAny<Guid>(), AnalysisKey, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("MinIO is down"));

        Func<Task> act = () => CreateAnnotator(workspace).AnnotateAsync(context, CancellationToken.None);

        await act.Should().NotThrowAsync();
        context.BuildAgentInput().Should().Be(expected);
    }

    // =======================================================================
    // Fixtures
    // =======================================================================

    private static VideoAnalysisPlacement Placement(
        string id, string shotId, double startSec, double endSec, int sourceIndex = 0) =>
        new(id, shotId, "LowerThird", new VideoAnalysisRect(0.1, 0.8, 0.6, 0.15),
            "ShotMiddle", startSec, endSec, 0.8, "Light", sourceIndex);

    private static VideoAnalysisArtifact BuildArtifact(
        (string Id, double Start, double End)[] shots,
        IReadOnlyList<VideoAnalysisPlacement> placements,
        string[]? offeredIds = null) =>
        new(
            Version: 2,
            Media: new VideoAnalysisMedia(shots.Length > 0 ? shots.Max(s => s.End) : 60.0, 30, 1, 1920, 1080),
            Shots: shots.Select(s => new VideoAnalysisShot(s.Id, s.Start, s.End)).ToList(),
            SilenceSpans: [],
            Segments: [],
            Words: [],
            OfferedIds: offeredIds?.ToList() ?? shots.Select(s => s.Id).ToList(),
            Provenance: new VideoAnalysisProvenance(VideoTranscriptionMode.Off, false, false),
            Placements: placements,
            OfferedPlacementIds: placements.Select(p => p.Id).ToList());

    private static VideoEditDecisionOutput Decision(params (string FromId, string ToId)[] keep) =>
        new()
        {
            Keep = keep.Select(k => new VideoEditKeepSpan { FromId = k.FromId, ToId = k.ToId, Reason = "r" }).ToList(),
            EditRationale = "r"
        };

    /// <summary>
    /// The relevant slice of what <c>VideoAnalyzeStepExecutor</c> persists as its step output — a
    /// <c>{view, meta}</c> envelope whose <c>view.placements</c> entries carry only the small
    /// <c>{id, shotId, region, fit, text}</c> shape (no rect, no time window).
    /// </summary>
    private static string BuildEnvelope(VideoAnalysisArtifact artifact)
    {
        var placements = new JsonArray((artifact.Placements ?? []).Select(p => (JsonNode)new JsonObject
        {
            ["id"] = p.Id,
            ["shotId"] = p.ShotId,
            ["region"] = p.Region,
            ["fit"] = (int)Math.Round(p.Suitability * 100),
            ["text"] = p.TextColor
        }).ToArray());

        var view = new JsonObject
        {
            ["shots"] = new JsonArray(artifact.Shots.Select(s => (JsonNode)new JsonObject
            {
                ["id"] = s.Id,
                ["startSec"] = s.StartSec,
                ["endSec"] = s.EndSec
            }).ToArray()),
            ["placements"] = placements
        };

        return new JsonObject
        {
            ["view"] = view,
            ["meta"] = new JsonObject { ["artifactStorageKey"] = AnalysisKey }
        }.ToJsonString();
    }

    /// <summary>Placement id -> its <c>inEdit</c> flag (null when the key is absent).</summary>
    private static Dictionary<string, bool?> InEditFlags(string envelopeJson) =>
        ((JsonArray)JsonNode.Parse(envelopeJson)!["view"]!["placements"]!)
        .ToDictionary(
            n => n!["id"]!.GetValue<string>(),
            n => n!.AsObject().TryGetPropertyValue("inEdit", out JsonNode? flag) ? flag!.GetValue<bool>() : (bool?)null);

    /// <summary>
    /// A planner step at StepOrder 3 with StepOrder-1 (VideoAnalyze, carrying the artifact key)
    /// and StepOrder-2 (VideoStoryEditor) history entries — the exact
    /// <c>video-derush-edit-graphics</c> template layout.
    /// </summary>
    private static StepExecutionContext CreateContext(string analyzeOutput, string decisionOutput)
    {
        var step = new WorkflowStep
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = Guid.NewGuid(),
            AgentDefinitionId = Guid.NewGuid(),
            StepOrder = 3,
            StepType = StepType.Agent,
            AgentInputContextMode = AgentInputContextMode.FullWorkflow,
            AgentDefinition = new AgentDefinition
            {
                Id = Guid.NewGuid(),
                Name = "MotionGraphicsPlanner",
                AgentType = AgentType.MotionGraphicsPlanner,
                SystemPrompt = "prompt"
            }
        };

        return new StepExecutionContext
        {
            Execution = new WorkflowExecution { Id = Guid.NewGuid(), ProjectId = Guid.NewGuid() },
            Step = step,
            AllSteps = [step],
            AccumulatedOutput = analyzeOutput,
            StepOutputHistory =
            [
                new StepOutputHistoryEntry(1, "Analyze source clips", analyzeOutput, null, AnalysisKey),
                new StepOutputHistoryEntry(2, "Decide which spans to keep", decisionOutput)
            ],
            CurrentStepIndex = 2,
            IterationCount = 0,
            CorrelationId = "test",
            CancellationToken = CancellationToken.None
        };
    }

    private static MotionGraphicsPlacementAnnotator CreateAnnotator(VideoAnalysisArtifact artifact)
    {
        var workspace = new Mock<IProjectFileWorkspace>();
        workspace
            .Setup(w => w.DownloadStorageKeyToFileAsync(
                It.IsAny<Guid>(), AnalysisKey, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, string, CancellationToken>((_, _, destPath, _) =>
            {
                File.WriteAllText(destPath, JsonSerializer.Serialize(artifact, ArtifactOptions));
                return Task.CompletedTask;
            });

        return CreateAnnotator(workspace);
    }

    private static MotionGraphicsPlacementAnnotator CreateAnnotator(Mock<IProjectFileWorkspace> workspace)
    {
        string scratchRoot = Path.Combine(Path.GetTempPath(), "reelforge-graphics-annotator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratchRoot);

        return new MotionGraphicsPlacementAnnotator(
            workspace.Object,
            Options.Create(new VideoEditingOptions { ScratchPath = scratchRoot, MaxConcurrentJobs = 1 }),
            NullLogger<MotionGraphicsPlacementAnnotator>.Instance);
    }
}
