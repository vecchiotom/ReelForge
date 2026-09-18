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
}

export interface SkillDetail extends Skill {
  /** The SKILL.md content, markdown. */
  body: string;
}

export interface SetAgentSkillsRequest {
  skills: string[];
}
