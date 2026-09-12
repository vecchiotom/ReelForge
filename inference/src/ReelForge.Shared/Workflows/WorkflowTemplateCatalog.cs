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
    string? ExtractConfigJson = null);

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
                        {"version":1,"operation":"project","inputs":{"source":{"from":"step","stepOrder":3}},"path":"$.components","fields":["name","filePath","responsibility"],"take":40,"maxOutputChars":8000,"expect":{"minItems":1}}
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