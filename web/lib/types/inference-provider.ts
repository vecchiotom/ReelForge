export type InferenceProviderKind = 'AzureOpenAI' | 'OpenAICompatible';

/**
 * What an InferenceProvider row can be used for. Mirrors the backend `InferenceProviderCapability`
 * enum (ReelForge.Shared/Data/Models/Enums.cs) and `InferenceProviderResponse.Capability`
 * (Inference.Api/Controllers/Dto/InferenceProviderDtos.cs) exactly. `Chat`, `Transcription`, and
 * `Vision` (Phase 2 of video editing — shot captioning, see docs/video-editing.md) each participate
 * in their own independent "at most one default" constraint, so a chat-provider picker (e.g. the
 * per-agent override) must filter to `Chat` only — never merely "not Transcription", which would
 * incorrectly admit `Vision` rows too.
 */
export type InferenceProviderCapability = 'Chat' | 'Transcription' | 'Vision';

export interface InferenceProvider {
  id: string;
  name: string;
  kind: InferenceProviderKind;
  capability: InferenceProviderCapability;
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
  /** Omitted defaults to 'Chat' server-side, for backward compatibility. */
  capability?: InferenceProviderCapability;
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
  /** Omitted/null leaves the stored capability unchanged. */
  capability?: InferenceProviderCapability;
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
  capability?: InferenceProviderCapability | null;
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
