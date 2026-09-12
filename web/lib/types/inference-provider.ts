export type InferenceProviderKind = 'AzureOpenAI' | 'OpenAICompatible';

export interface InferenceProvider {
  id: string;
  name: string;
  kind: InferenceProviderKind;
  endpoint: string;
  modelName: string;
  hasApiKey: boolean;
  apiKeyLastFour: string | null;
  isDefault: boolean;
  isEnabled: boolean;
  timeoutSeconds: number | null;
  createdAt: string;
  updatedAt: string;
  lastTestAt: string | null;
  lastTestOk: boolean | null;
  lastTestError: string | null;
}

export interface CreateInferenceProviderRequest {
  name: string;
  kind: InferenceProviderKind;
  endpoint: string;
  modelName: string;
  apiKey?: string | null;
  isDefault: boolean;
  isEnabled: boolean;
  timeoutSeconds?: number | null;
}

export interface UpdateInferenceProviderRequest {
  name?: string;
  kind?: InferenceProviderKind;
  endpoint?: string;
  modelName?: string;
  apiKey?: string | null;
  isDefault?: boolean;
  isEnabled?: boolean;
  timeoutSeconds?: number | null;
}

export interface TestInferenceProviderRequest {
  id?: string | null;
  kind?: InferenceProviderKind | null;
  endpoint?: string | null;
  modelName?: string | null;
  apiKey?: string | null;
}

export interface TestInferenceProviderResponse {
  ok: boolean;
  latencyMs: number;
  error: string | null;
  responsePreview: string | null;
}
