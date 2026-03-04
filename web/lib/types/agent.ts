export type AgentCategory = 'Analysis' | 'Translation' | 'Production' | 'Quality' | 'FileProcessing' | 'Custom';

export interface AgentDefinition {
  id: string;
  name: string;
  description: string;
  agentType: string;
  systemPrompt: string;
  isBuiltIn: boolean;
  category: AgentCategory;
  userId: string | null;
  createdAt: string;
}

export interface CreateAgentRequest {
  name: string;
  description: string;
  systemPrompt: string;
}

export interface UpdateAgentRequest {
  name?: string;
  description?: string;
  systemPrompt?: string;
}
