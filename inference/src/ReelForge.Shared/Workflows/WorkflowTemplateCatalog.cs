using ReelForge.Shared.Data.Models;

namespace ReelForge.Shared.Workflows;

public sealed record WorkflowTemplateStepDefinition(
    AgentType AgentType,
    string Label,
    StepType StepType = StepType.Agent,
    string? ConditionExpression = null,
    string? LoopSourceExpression = null,
    int? LoopTargetStepOrder = null,
    int MaxIterations = 3,
    int? MinScore = null,
    string? InputMappingJson = null,
    AgentInputContextMode? AgentInputContextMode = null,
    IReadOnlyList<int>? SelectedPriorStepOrders = null,
    string? TrueBranchStepOrder = null,
    string? FalseBranchStepOrder = null,
    IReadOnlyList<AgentType>? ParallelAgentTypes = null,
    string? ExtractConfigJson = null,
    string? VideoAnalyzeConfigJson = null,
    string? VideoCompileConfigJson = null,
    string? EditRoomConfigJson = null,
    string? GraphicsRoomConfigJson = null);

public sealed record WorkflowTemplateDefinition(
    string Key,
    string Name,
    string Description,
    int Version,
    bool AutoCreateOnProject,
    bool RequiresUserInput,
    IReadOnlyList<WorkflowTemplateStepDefinition> Steps);

public static class WorkflowTemplateCatalog
{
    public const string StarterTemplateKey = "quick-win-promo";

    private static readonly IReadOnlyList<WorkflowTemplateDefinition> _templates =
    [
        new(
            Key: StarterTemplateKey,
            Name: "Quick Win Promo",
            Description: "Safe starter workflow optimized for deterministic renderability and clean visual output.",
            Version: 1,
            AutoCreateOnProject: true,
            RequiresUserInput: false,
            Steps:
            [
                new(AgentType.CodeStructureAnalyzer, "Analyze code structure", AgentInputContextMode: AgentInputContextMode.FullWorkflow),
                new(AgentType.DependencyAnalyzer, "Analyze UI dependencies", AgentInputContextMode: AgentInputContextMode.PreviousStepOnly),
                new(AgentType.ComponentInventoryAnalyzer, "Inventory components", AgentInputContextMode: AgentInputContextMode.PreviousStepOnly),
                new(AgentType.RouteAndApiAnalyzer, "Map routes and API", AgentInputContextMode: AgentInputContextMode.PreviousStepOnly),
                new(AgentType.StyleAndThemeExtractor, "Extract style tokens", AgentInputContextMode: AgentInputContextMode.PreviousStepOnly),
                new(AgentType.RemotionComponentTranslator, "Translate to Remotion scenes", AgentInputContextMode: AgentInputContextMode.FullWorkflow),
                new(AgentType.AnimationStrategyAgent, "Create animation strategy", AgentInputContextMode: AgentInputContextMode.PreviousStepOnly),
                new(AgentType.ScriptwriterAgent, "Draft narration and captions", AgentInputContextMode: AgentInputContextMode.PreviousStepOnly),
                new(AgentType.DirectorAgent, "Build shot direction", AgentInputContextMode: AgentInputContextMode.FullWorkflow),
                new(AgentType.AuthorAgent, "Assemble and render master composition", AgentInputContextMode: AgentInputContextMode.FullWorkflow),
                new(AgentType.ReviewAgent, "Review quality and compliance", StepType.ReviewLoop, LoopTargetStepOrder: 10, MaxIterations: 2, MinScore: 8, AgentInputContextMode: AgentInputContextMode.FullWorkflow)
            ]),
        new(
            Key: "lean-context-promo",
            Name: "Lean Context Promo",
            Description: "Opt-in variant of the starter workflow that inserts a deterministic Extract step to reduce the component inventory before it reaches downstream agents, cutting token usage. Demonstrates the Extract (op=project) pattern.",
            Version: 1,
            AutoCreateOnProject: false,
            RequiresUserInput: false,
            Steps:
            [
                new(AgentType.CodeStructureAnalyzer, "Analyze code structure", AgentInputContextMode: AgentInputContextMode.FullWorkflow),
                new(AgentType.DependencyAnalyzer, "Analyze UI dependencies", AgentInputContextMode: AgentInputContextMode.PreviousStepOnly),
                new(AgentType.ComponentInventoryAnalyzer, "Inventory components", AgentInputContextMode: AgentInputContextMode.PreviousStepOnly),
                new(AgentType.RouteAndApiAnalyzer, "Map routes and API", AgentInputContextMode: AgentInputContextMode.PreviousStepOnly),
                new(AgentType.StyleAndThemeExtractor, "Extract style tokens", AgentInputContextMode: AgentInputContextMode.PreviousStepOnly),
                new(
                    AgentType.ExtractTransform,
                    "Reduce component inventory to a lean view",
                    StepType.Extract,
                    ExtractConfigJson: """
                        {"version":1,"operation":"Project","inputs":{"source":{"from":"Step","stepOrder":3}},"path":"$.components","fields":["name","filePath","responsibility"],"take":40,"maxOutputChars":8000,"expect":{"minItems":1}}
                        """),
                new(AgentType.RemotionComponentTranslator, "Translate to Remotion scenes (lean context)", AgentInputContextMode: AgentInputContextMode.SelectedPriorSteps, SelectedPriorStepOrders: [1, 2, 4, 5, 6]),
                new(AgentType.AnimationStrategyAgent, "Create animation strategy (lean context)", AgentInputContextMode: AgentInputContextMode.SelectedPriorSteps, SelectedPriorStepOrders: [5, 6, 7]),
                new(AgentType.ScriptwriterAgent, "Draft narration and captions", AgentInputContextMode: AgentInputContextMode.PreviousStepOnly),
                new(AgentType.DirectorAgent, "Build shot direction (lean context)", AgentInputContextMode: AgentInputContextMode.SelectedPriorSteps, SelectedPriorStepOrders: [1, 4, 5, 6, 7, 8, 9]),
                new(AgentType.AuthorAgent, "Assemble and render master composition (lean context)", AgentInputContextMode: AgentInputContextMode.SelectedPriorSteps, SelectedPriorStepOrders: [6, 7, 8, 9, 10]),
                new(AgentType.ReviewAgent, "Review quality and compliance", StepType.ReviewLoop, LoopTargetStepOrder: 11, MaxIterations: 2, MinScore: 8, AgentInputContextMode: AgentInputContextMode.FullWorkflow)
            ]),
        new(
            Key: "cinematic-feature-spotlight",
            Name: "Cinematic Feature Spotlight",
            Description: "High-energy feature showcase with cinematic pacing, reveal transitions, and strong CTA ending.",
            Version: 1,
            AutoCreateOnProject: false,
            RequiresUserInput: true,
            Steps:
            [
                new(AgentType.CodeStructureAnalyzer, "Analyze cinematic candidate surfaces", AgentInputContextMode: AgentInputContextMode.FullWorkflow),
                new(AgentType.ComponentInventoryAnalyzer, "Find hero components", AgentInputContextMode: AgentInputContextMode.PreviousStepOnly),
                new(AgentType.StyleAndThemeExtractor, "Extract brand visuals", AgentInputContextMode: AgentInputContextMode.PreviousStepOnly),
                new(AgentType.RemotionComponentTranslator, "Translate hero scenes", AgentInputContextMode: AgentInputContextMode.FullWorkflow),
                new(AgentType.AnimationStrategyAgent, "Plan cinematic transitions", AgentInputContextMode: AgentInputContextMode.FullWorkflow),
                new(AgentType.ScriptwriterAgent, "Write emotional feature hooks", AgentInputContextMode: AgentInputContextMode.FullWorkflow),
                new(AgentType.DirectorAgent, "Define reveal sequence", AgentInputContextMode: AgentInputContextMode.FullWorkflow),
                new(AgentType.AuthorAgent, "Assemble cinematic master cut", AgentInputContextMode: AgentInputContextMode.FullWorkflow),
                new(AgentType.ReviewAgent, "Cinematic quality review", StepType.ReviewLoop, LoopTargetStepOrder: 8, MaxIterations: 3, MinScore: 9, AgentInputContextMode: AgentInputContextMode.FullWorkflow)
            ]),
        new(
            Key: "story-arc-launch-trailer",
            Name: "Story Arc Launch Trailer",
            Description: "Narrative trailer format with setup, tension build, payoff reveal, and final launch CTA.",
            Version: 1,
            AutoCreateOnProject: false,
            RequiresUserInput: true,
            Steps:
            [
                new(AgentType.CodeStructureAnalyzer, "Map narrative assets", AgentInputContextMode: AgentInputContextMode.FullWorkflow),
                new(AgentType.RouteAndApiAnalyzer, "Extract user journey flow", AgentInputContextMode: AgentInputContextMode.PreviousStepOnly),
                new(AgentType.StyleAndThemeExtractor, "Extract mood and tone tokens", AgentInputContextMode: AgentInputContextMode.PreviousStepOnly),
                new(AgentType.RemotionComponentTranslator, "Translate story scenes", AgentInputContextMode: AgentInputContextMode.FullWorkflow),
                new(AgentType.ScriptwriterAgent, "Write narrative arc script", AgentInputContextMode: AgentInputContextMode.FullWorkflow),
                new(AgentType.DirectorAgent, "Create trailer shotlist", AgentInputContextMode: AgentInputContextMode.FullWorkflow),
                new(AgentType.AnimationStrategyAgent, "Plan suspense pacing", AgentInputContextMode: AgentInputContextMode.FullWorkflow),
                new(AgentType.AuthorAgent, "Assemble and render trailer", AgentInputContextMode: AgentInputContextMode.FullWorkflow),
                new(AgentType.ReviewAgent, "Trailer impact review", StepType.ReviewLoop, LoopTargetStepOrder: 8, MaxIterations: 3, MinScore: 9, AgentInputContextMode: AgentInputContextMode.FullWorkflow)
            ]),
        new(
            Key: "video-derush-edit",
            Name: "Video Derush & Edit",
            Description: "Opt-in template that analyzes a real source video (silence, shots, optional ASR transcription), has a story-editor agent decide which spans to keep by referencing opaque ids only, then compiles the edit with ffmpeg. Demonstrates the VideoAnalyze/VideoCompile step types.",
            Version: 1,
            AutoCreateOnProject: false,
            RequiresUserInput: false,
            Steps:
            [
                new(
                    AgentType.VideoTransform,
                    "Analyze source video",
                    StepType.VideoAnalyze,
                    // Source.Kind=ProjectFile with no ProjectFileId, deliberately: this is the
                    // first step in the workflow, so Source.Kind=PreviousStepOutput would fail
                    // SOURCE_UNRESOLVED on every single execution (there is no prior step's
                    // output to resolve — found by Copilot review). ProjectFile fails the same
                    // way when unconfigured, but the workflow builder's source picker for
                    // ProjectFile visibly shows "no file selected", making it obvious the user
                    // needs to pick one before running — unlike PreviousStepOutput, which reads
                    // as already-configured.
                    VideoAnalyzeConfigJson: """
                        {"version":1,"source":{"kind":"ProjectFile"}}
                        """),
                new(
                    AgentType.VideoStoryEditor,
                    "Decide which spans to keep",
                    AgentInputContextMode: AgentInputContextMode.PreviousStepOnly),
                new(
                    AgentType.VideoTransform,
                    "Compile edited video",
                    StepType.VideoCompile,
                    VideoCompileConfigJson: """
                        {"version":1,"decision":{"from":"Previous"},"analysisStepOrder":1,"transitionPolicy":"Auto","programFadeInMs":500,"programFadeOutMs":800,"programAudioFadeInMs":300,"programAudioFadeOutMs":900,"minSegmentMs":800}
                        """),
                new(
                    AgentType.VideoReviewAgent,
                    "Review edit quality",
                    StepType.ReviewLoop,
                    // Loop back to step 2 (the story editor) — step 1 (VideoAnalyze) is
                    // deterministic and produces the same bounded view every time, so there is
                    // nothing for a retry to gain from re-running it. Mirrors the main pipeline's
                    // ReviewLoop wiring (quick-win-promo, StarterTemplateKey above) exactly:
                    // LoopTargetStepOrder/MaxIterations/MinScore, FullWorkflow context so the
                    // review agent sees the analysis view, the story editor's decision, and the
                    // compile step's own deterministic sentenceCheck.
                    LoopTargetStepOrder: 2,
                    MaxIterations: 3,
                    MinScore: 8,
                    AgentInputContextMode: AgentInputContextMode.FullWorkflow)
            ]),
        new(
            Key: "video-derush-edit-graphics",
            Name: "Video Derush, Edit & Graphics",
            Description: "Opt-in template extending Video Derush & Edit with Phase 3 motion graphics: the analyze step also derives overlay-placement candidates, a second agent plans zero or more lower-third/title/callout overlays anchored only to those offered placement ids (never a coordinate or timestamp), and the compile step applies them during the same ffmpeg encode. Demonstrates EmitOverlayPlacements/EnableGraphics end to end.",
            Version: 1,
            AutoCreateOnProject: false,
            RequiresUserInput: false,
            Steps:
            [
                new(
                    AgentType.VideoTransform,
                    "Analyze source video",
                    StepType.VideoAnalyze,
                    // Same ProjectFile-source rationale as video-derush-edit's first step above
                    // (this is the first step, so PreviousStepOutput would fail SOURCE_UNRESOLVED
                    // on every run) — plus emitOverlayPlacements:true to derive Phase 3's
                    // view.placements candidates alongside the usual cut-anchor ids.
                    VideoAnalyzeConfigJson: """
                        {"version":1,"source":{"kind":"ProjectFile"},"emitOverlayPlacements":true}
                        """),
                new(
                    AgentType.VideoStoryEditor,
                    "Decide which spans to keep",
                    AgentInputContextMode: AgentInputContextMode.PreviousStepOnly),
                new(
                    AgentType.MotionGraphicsPlanner,
                    "Plan motion graphics overlays",
                    // FullWorkflow (not PreviousStepOnly): this agent needs BOTH the analyze
                    // step's view.placements (step 1) and the story editor's decision (step 2),
                    // not merely the immediately-preceding step's output.
                    AgentInputContextMode: AgentInputContextMode.FullWorkflow),
                new(
                    AgentType.VideoTransform,
                    "Compile edited video with graphics",
                    StepType.VideoCompile,
                    // Decision/GraphicsPlan reference their source steps explicitly by
                    // StepOrder — "Previous" relative to THIS step would resolve to the
                    // MotionGraphicsPlanner step's output (step 3), not the story editor's
                    // decision (step 2).
                    VideoCompileConfigJson: """
                        {"version":1,"decision":{"from":"Step","stepOrder":2},"analysisStepOrder":1,"enableGraphics":true,"graphicsPlan":{"from":"Step","stepOrder":3},"transitionPolicy":"Auto","programFadeInMs":500,"programFadeOutMs":800,"programAudioFadeInMs":300,"programAudioFadeOutMs":900,"minSegmentMs":800}
                        """),
                new(
                    AgentType.VideoReviewAgent,
                    "Review edit quality",
                    StepType.ReviewLoop,
                    // Loop back to step 2 (the story editor) so a low score re-runs the story
                    // editor, the motion-graphics planner, and the compile step in sequence —
                    // step 1 (VideoAnalyze) is deterministic and need not rerun. Same
                    // MinScore/MaxIterations/FullWorkflow pattern as video-derush-edit above.
                    LoopTargetStepOrder: 2,
                    MaxIterations: 3,
                    MinScore: 8,
                    AgentInputContextMode: AgentInputContextMode.FullWorkflow)
            ]),
        new(
            Key: "video-derush-edit-music",
            Name: "Video Derush, Edit & Music",
            Description: "Opt-in template extending Video Derush & Edit with background music: the analyze step also offers candidate audio/* project files as music-track ids, a music-supervisor agent picks at most one track plus intensity/ducking/fit settings, and the compile step mixes it under the dialogue during the same ffmpeg encode. Demonstrates OfferMusicTracks/EnableMusic end to end.",
            Version: 1,
            AutoCreateOnProject: false,
            RequiresUserInput: false,
            Steps:
            [
                new(
                    AgentType.VideoTransform,
                    "Analyze source video",
                    StepType.VideoAnalyze,
                    // Same ProjectFile-source rationale as video-derush-edit's first step above
                    // (this is the first step, so PreviousStepOutput would fail SOURCE_UNRESOLVED
                    // on every run) — plus offerMusicTracks:true to derive the "m{n}" music-track
                    // candidates the music supervisor picks from.
                    VideoAnalyzeConfigJson: """
                        {"version":1,"source":{"kind":"ProjectFile"},"offerMusicTracks":true}
                        """),
                new(
                    AgentType.VideoStoryEditor,
                    "Decide which spans to keep",
                    AgentInputContextMode: AgentInputContextMode.PreviousStepOnly),
                new(
                    AgentType.MusicSupervisor,
                    "Pick background music",
                    // FullWorkflow (not PreviousStepOnly): this agent needs BOTH the analyze
                    // step's view.musicTracks (step 1) and the story editor's decision (step 2),
                    // not merely the immediately-preceding step's output.
                    AgentInputContextMode: AgentInputContextMode.FullWorkflow),
                new(
                    AgentType.VideoTransform,
                    "Compile edited video with music",
                    StepType.VideoCompile,
                    // Decision/MusicPlan reference their source steps explicitly by StepOrder —
                    // "Previous" relative to THIS step would resolve to the MusicSupervisor step's
                    // output (step 3), not the story editor's decision (step 2).
                    VideoCompileConfigJson: """
                        {"version":1,"decision":{"from":"Step","stepOrder":2},"analysisStepOrder":1,"enableMusic":true,"musicPlan":{"from":"Step","stepOrder":3},"transitionPolicy":"Auto","programFadeInMs":500,"programFadeOutMs":800,"programAudioFadeInMs":300,"programAudioFadeOutMs":900,"minSegmentMs":800}
                        """),
                new(
                    AgentType.VideoReviewAgent,
                    "Review edit quality",
                    StepType.ReviewLoop,
                    // Loop back to step 2 so a low score re-runs the story editor, the music
                    // supervisor, and the compile step in sequence — step 1 (VideoAnalyze) is
                    // deterministic and need not rerun. Same MinScore/MaxIterations/FullWorkflow
                    // pattern as video-derush-edit/video-derush-edit-graphics above.
                    LoopTargetStepOrder: 2,
                    MaxIterations: 3,
                    MinScore: 8,
                    AgentInputContextMode: AgentInputContextMode.FullWorkflow)
            ]),
        new(
            Key: "video-derush-edit-room",
            Name: "Video Derush & Edit Room",
            Description: "Opt-in template that replaces the single VideoStoryEditor decision step with a multi-agent 'edit room': several editor seats plus a director converse in a live group chat over the analyzed video's bounded view, then the director synthesizes ONE editorial decision the compile step consumes exactly like a solo VideoStoryEditor step would. Demonstrates the EditRoom step type end to end.",
            Version: 1,
            AutoCreateOnProject: false,
            RequiresUserInput: false,
            Steps:
            [
                new(
                    AgentType.VideoTransform,
                    "Analyze source video",
                    StepType.VideoAnalyze,
                    // Same ProjectFile-source rationale as video-derush-edit's first step above
                    // (this is the first step, so PreviousStepOutput would fail SOURCE_UNRESOLVED
                    // on every run).
                    VideoAnalyzeConfigJson: """
                        {"version":1,"source":{"kind":"ProjectFile"}}
                        """),
                new(
                    // The step's own AgentDefinitionId FK is satisfied by the same deterministic
                    // VideoTransform placeholder VideoAnalyze/VideoCompile steps use — the room's
                    // actual LLM seats/director are resolved independently by EditRoomStepExecutor
                    // from EditRoomConfigJson, never from this step's own AgentDefinitionId.
                    AgentType.VideoTransform,
                    "Edit room deliberation",
                    StepType.EditRoom,
                    EditRoomConfigJson: """
                        {"version":1,"view":{"from":"Previous"}}
                        """),
                new(
                    AgentType.VideoTransform,
                    "Compile edited video",
                    StepType.VideoCompile,
                    // Decision/AnalysisStepOrder reference their source steps explicitly by
                    // StepOrder — "Previous" relative to the compile step already resolves
                    // correctly here (the EditRoom step immediately precedes it and emits the
                    // exact same VideoEditDecisionOutput shape a solo VideoStoryEditor step
                    // would), kept explicit anyway for the same auditability the other video
                    // templates' compile steps already follow.
                    VideoCompileConfigJson: """
                        {"version":1,"decision":{"from":"Step","stepOrder":2},"analysisStepOrder":1,"transitionPolicy":"Auto","programFadeInMs":500,"programFadeOutMs":800,"programAudioFadeInMs":300,"programAudioFadeOutMs":900,"minSegmentMs":800}
                        """),
                new(
                    AgentType.VideoReviewAgent,
                    "Review edit quality",
                    StepType.ReviewLoop,
                    // Loop back to step 2 (the edit room) — step 1 (VideoAnalyze) is deterministic
                    // and produces the same bounded view every time, so there is nothing for a
                    // retry to gain from re-running it. Same MinScore/MaxIterations/FullWorkflow
                    // pattern as video-derush-edit above.
                    LoopTargetStepOrder: 2,
                    MaxIterations: 3,
                    MinScore: 8,
                    AgentInputContextMode: AgentInputContextMode.FullWorkflow)
            ]),
        new(
            Key: "video-derush-edit-graphics-room",
            Name: "Video Derush, Edit & Graphics Room",
            Description: "Opt-in template that replaces Video Derush, Edit & Graphics' single MotionGraphicsPlanner step with a multi-agent 'graphics room': several motion-graphics-artist seats plus a lead director converse in a live group chat over the analyzed video's offered overlay-placement candidates, then the director synthesizes ONE motion-graphics plan the compile step consumes exactly like a solo planner's. Demonstrates the GraphicsRoom step type end to end.",
            Version: 1,
            AutoCreateOnProject: false,
            RequiresUserInput: false,
            Steps:
            [
                new(
                    AgentType.VideoTransform,
                    "Analyze source video",
                    StepType.VideoAnalyze,
                    // Same ProjectFile-source rationale as video-derush-edit's first step above
                    // (this is the first step, so PreviousStepOutput would fail SOURCE_UNRESOLVED
                    // on every run) — plus emitOverlayPlacements:true to derive the view.placements
                    // candidates the graphics room deliberates over.
                    VideoAnalyzeConfigJson: """
                        {"version":1,"source":{"kind":"ProjectFile"},"emitOverlayPlacements":true}
                        """),
                new(
                    AgentType.VideoStoryEditor,
                    "Decide which spans to keep",
                    AgentInputContextMode: AgentInputContextMode.PreviousStepOnly),
                new(
                    // The step's own AgentDefinitionId FK is satisfied by the same deterministic
                    // VideoTransform placeholder VideoAnalyze/VideoCompile/EditRoom steps use — the
                    // room's actual LLM seats/director are resolved independently by
                    // GraphicsRoomStepExecutor from GraphicsRoomConfigJson, never from this step's
                    // own AgentDefinitionId.
                    AgentType.VideoTransform,
                    "Graphics room deliberation",
                    StepType.GraphicsRoom,
                    // View references the analyze step explicitly by StepOrder — "Previous"
                    // relative to THIS step would resolve to the story editor's decision (step 2),
                    // not the analyze envelope carrying view.placements (step 1). The story
                    // editor's decision still reaches the room: the executor annotates each
                    // placement with the same inEdit survival flag a solo planner's prompt gets.
                    GraphicsRoomConfigJson: """
                        {"version":1,"view":{"from":"Step","stepOrder":1}}
                        """),
                new(
                    AgentType.VideoTransform,
                    "Compile edited video with graphics",
                    StepType.VideoCompile,
                    // Decision/GraphicsPlan reference their source steps explicitly by StepOrder —
                    // "Previous" relative to THIS step would resolve to the graphics room's plan
                    // (step 3), not the story editor's decision (step 2). GraphicsPlan pointing at
                    // the GraphicsRoom step works UNCHANGED because that step emits the exact same
                    // MotionGraphicsPlanOutput shape a solo MotionGraphicsPlanner step would (the
                    // additive "room" metadata key is skipped by deserialization).
                    VideoCompileConfigJson: """
                        {"version":1,"decision":{"from":"Step","stepOrder":2},"analysisStepOrder":1,"enableGraphics":true,"graphicsPlan":{"from":"Step","stepOrder":3},"transitionPolicy":"Auto","programFadeInMs":500,"programFadeOutMs":800,"programAudioFadeInMs":300,"programAudioFadeOutMs":900,"minSegmentMs":800}
                        """),
                new(
                    AgentType.VideoReviewAgent,
                    "Review edit quality",
                    StepType.ReviewLoop,
                    // Loop back to step 2 (the story editor) so a low score re-runs the story
                    // editor, the graphics room, and the compile step in sequence — step 1
                    // (VideoAnalyze) is deterministic and need not rerun. Same
                    // MinScore/MaxIterations/FullWorkflow pattern as the other video templates.
                    LoopTargetStepOrder: 2,
                    MaxIterations: 3,
                    MinScore: 8,
                    AgentInputContextMode: AgentInputContextMode.FullWorkflow)
            ])
    ];

    public static IReadOnlyList<WorkflowTemplateDefinition> GetAll() => _templates;

    public static IReadOnlyList<WorkflowTemplateDefinition> GetAutoCreateTemplates() =>
        _templates.Where(template => template.AutoCreateOnProject).ToList();

    public static WorkflowTemplateDefinition? GetByKey(string key) =>
        _templates.FirstOrDefault(template =>
            string.Equals(template.Key, key, StringComparison.OrdinalIgnoreCase));

    public static string BuildVersionedWorkflowName(WorkflowTemplateDefinition template) =>
        $"{template.Name} v{template.Version}";
}