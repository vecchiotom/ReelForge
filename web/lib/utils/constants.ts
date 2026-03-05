import type { StepType } from '../types/workflow';

export const STATUS_COLORS: Record<string, string> = {
  Queued: 'gray',
  Running: 'blue',
  Passed: 'green',
  Failed: 'red',
  Pending: 'gray',
  Processing: 'blue',
  Completed: 'green',
  Skipped: 'yellow',
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
  Custom: 'Custom',
};

export const AGENT_GROUP_COLORS: Record<string, string> = {
  Analysis: 'blue',
  Translation: 'cyan',
  Production: 'violet',
  Quality: 'orange',
  'File Processing': 'teal',
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
};

export const STEP_TYPE_COLORS: Record<StepType, string> = {
  Agent: 'violet',
  Conditional: 'orange',
  ForEach: 'cyan',
  ReviewLoop: 'green',
};

export const STEP_TYPE_DESCRIPTIONS: Record<StepType, string> = {
  Agent: 'Run a single AI agent to process data',
  Conditional: 'Branch execution based on a condition expression',
  ForEach: 'Iterate over a collection and run an agent for each item',
  ReviewLoop: 'Run an agent repeatedly until a quality score threshold is met',
};
