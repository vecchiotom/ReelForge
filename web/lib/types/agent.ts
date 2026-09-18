export interface AgentDefinition {
  id: string;
  name: string;
  description: string;
  agentType: string;
  systemPrompt: string;
  isBuiltIn: boolean;
  ownerId: string | null;
  createdAt: string;
  color: string | null;
  outputSchemaJson: string | null;
  availableTools: string[] | null;
  generatesOutput: boolean;
  outputSchemaName: string | null;
  inferenceProviderId: string | null;
  inferenceProviderName: string | null;
  /** Raw stored value — null/empty for built-ins always, may be set for custom agents. */
  assignedSkills: string[] | null;
  /** The resolved set to DISPLAY — the SkillCatalog default for built-ins, `assignedSkills` (or []) for custom agents. */
  effectiveSkills: string[];
}

export interface CreateAgentRequest {
  name: string;
  description: string;
  systemPrompt: string;
  color?: string;
}

export interface UpdateAgentRequest {
  name?: string;
  description?: string;
  systemPrompt?: string;
  color?: string;
}
