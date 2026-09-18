import type { StepType } from '../types/workflow';

export const STATUS_COLORS: Record<string, string> = {
  Queued: 'gray',
  Running: 'blue',
  Passed: 'green',
  Failed: 'red',
  Pending: 'gray',
  Processing: 'blue',
  Completed: 'green', // StepStatus
  Done: 'green', // SummaryStatus — a distinct enum from StepStatus/ExecutionStatus that uses 'Done'
  Skipped: 'yellow',
  NotIndexed: 'gray',
  Indexed: 'green',
};

/** Maps an AgentType enum value to a display group name. */
export const AGENT_TYPE_GROUP: Record<string, string> = {
  CodeStructureAnalyzer: 'Analysis',
  DependencyAnalyzer: 'Analysis',
  ComponentInventoryAnalyzer: 'Analysis',
  RouteAndApiAnalyzer: 'Analysis',
  StyleAndThemeExtractor: 'Analysis',
  RemotionComponentTranslator: 'Translation',
  AnimationStrategyAgent: 'Translation',
  DirectorAgent: 'Production',
  ScriptwriterAgent: 'Production',
  AuthorAgent: 'Production',
  ReviewAgent: 'Quality',
  FileSummarizerAgent: 'File Processing',
  ExtractTransform: 'Extract',
  VideoStoryEditor: 'Video',
  VideoTransform: 'Video',
  Custom: 'Custom',
};

export const AGENT_GROUP_COLORS: Record<string, string> = {
  Analysis: 'blue',
  Translation: 'cyan',
  Production: 'violet',
  Quality: 'orange',
  'File Processing': 'teal',
  Extract: 'grape',
  Video: 'indigo',
  Custom: 'pink',
};

export function getAgentGroup(agentType: string): string {
  return AGENT_TYPE_GROUP[agentType] || 'Custom';
}

export const STEP_TYPE_LABELS: Record<StepType, string> = {
  Agent: 'Agent',
  Conditional: 'Conditional Branch',
  ForEach: 'For Each',
  ReviewLoop: 'Review Loop',
  Parallel: 'Parallel',
  Extract: 'Extract',
  VideoAnalyze: 'Analyze Video',
  VideoCompile: 'Compile Video',
  EditRoom: 'Edit Room',
  GraphicsRoom: 'Graphics Room',
};

// Note: 'cyan'/'teal' were the plan's suggested colors for VideoAnalyze/VideoCompile, but both are
// already taken by ForEach/Parallel in this exact map — picked 'blue'/'indigo' instead so every
// step type badge in the flowchart builder is visually distinct, not just distinct from Extract.
export const STEP_TYPE_COLORS: Record<StepType, string> = {
  Agent: 'violet',
  Conditional: 'orange',
  ForEach: 'cyan',
  ReviewLoop: 'green',
  Parallel: 'teal',
  Extract: 'grape',
  VideoAnalyze: 'blue',
  VideoCompile: 'indigo',
  EditRoom: 'pink',
  GraphicsRoom: 'lime',
};

export const STEP_TYPE_DESCRIPTIONS: Record<StepType, string> = {
  Agent: 'Run a single AI agent to process data',
  Conditional: 'Branch execution based on a condition expression',
  ForEach: 'Iterate a step over each item in a collection',
  ReviewLoop: 'Loop back to a target step until a quality score is met',
  Parallel: 'Run multiple agents in parallel and merge their outputs',
  Extract: 'Deterministically reduce prior outputs or project files into a bounded view (no AI call)',
  VideoAnalyze: 'Deterministic ffmpeg-based derushing — silence, shot, and transcript analysis (no AI call)',
  VideoCompile: 'Deterministic ffmpeg-based cutting from an editorial decision (no AI call)',
  EditRoom: 'Multi-agent group-chat deliberation producing one editorial decision (template-provisioned; no builder editor yet)',
  GraphicsRoom: 'Multi-agent group-chat deliberation producing one motion-graphics plan (template-provisioned; no builder editor yet)',
};
