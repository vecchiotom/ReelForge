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
