export type SkillCategory = 'Remotion' | 'ReelForge' | 'Custom';

export interface Skill {
  name: string;
  displayName: string;
  description: string;
  category: SkillCategory;
  version: string | null;
  /** Which built-in AgentTypes get this skill by default. */
  defaultForAgentTypes: string[];
  /** How many custom agents currently have this skill assigned. */
  assignedAgentCount: number;
  /**
   * Link to the exact vendored directory/commit this skill's content was pulled from (e.g.
   * a GitHub tree URL pinned to a commit SHA). Null for a skill with no upstream source, such
   * as a future ReelForge-authored skill.
   */
  sourceUrl?: string | null;
  /** Pinned upstream commit SHA this skill was vendored from. Null alongside sourceUrl. */
  sourceCommit?: string | null;
}

export interface SkillDetail extends Skill {
  /** The SKILL.md content, markdown. */
  body: string;
}

export interface SetAgentSkillsRequest {
  skills: string[];
}
