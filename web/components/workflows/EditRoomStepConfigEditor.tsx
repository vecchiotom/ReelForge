'use client';

import { Stack, NumberInput, Switch, Text, SimpleGrid } from '@mantine/core';
import type { ExtractInputRef } from '@/lib/types/workflow';
import { InputRefPicker } from './ExtractStepConfig';
import type { StepData } from './WorkflowStepList';

/**
 * The known fields of the backend `EditRoomStepConfig` record this minimal editor exposes. The
 * config is carried on `StepData.editRoomConfigJson` as a RAW JSON string, and this editor
 * parses/edits/re-serializes it while spread-preserving any field it does not know about — so a
 * template-provisioned config with fields beyond these (seats, temperatures, termination mode,
 * timeouts…) round-trips losslessly through the builder, exactly like the pre-editor opaque
 * passthrough did.
 */
interface EditRoomKnownFields {
  version?: number;
  view?: ExtractInputRef | null;
  rounds?: number;
  maxTurns?: number;
  fallbackToSoloEditor?: boolean;
  persistTranscript?: boolean;
}

type EditRoomConfigObject = EditRoomKnownFields & Record<string, unknown>;

/** The default config a freshly-added EditRoom step gets — mirrors the C# record defaults for the fields it names. */
export function createDefaultEditRoomConfigJson(): string {
  return JSON.stringify({ version: 1, view: { from: 'Previous' } });
}

function parseConfig(json: string | null | undefined): EditRoomConfigObject {
  if (!json) return { version: 1, view: { from: 'Previous' } };
  try {
    const parsed = JSON.parse(json);
    if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) return parsed as EditRoomConfigObject;
  } catch {
    // Malformed JSON (hand-edited?) — fall through to the default rather than crashing the builder.
  }
  return { version: 1, view: { from: 'Previous' } };
}

interface EditRoomStepConfigEditorProps {
  /** The raw JSON string from `StepData.editRoomConfigJson`. */
  configJson: string | null | undefined;
  onChange: (configJson: string) => void;
  allSteps?: StepData[];
  currentStepIndex?: number;
}

/**
 * Minimal configuration form for an EditRoom step (multi-agent group-chat deliberation emitting
 * one `VideoEditDecisionOutput`, identical in shape to a solo VideoStoryEditor step's). Exposes
 * the fields a workflow author composing pipelines actually needs — which step's bounded view the
 * room deliberates over, its size/ceiling, and the degrade toggles — while preserving every other
 * (defaulted or template-provisioned) field untouched.
 */
export function EditRoomStepConfigEditor({
  configJson, onChange, allSteps = [], currentStepIndex = 0,
}: EditRoomStepConfigEditorProps) {
  const config = parseConfig(configJson);

  const priorStepOptions = allSteps
    .slice(0, currentStepIndex)
    .map((previousStep, index) => {
      const stepOrder = index + 1;
      const label = previousStep.label?.trim() || `Step ${stepOrder}`;
      return { value: String(stepOrder), label: `${stepOrder}. ${label}` };
    });

  const patch = (updates: Partial<EditRoomKnownFields>) =>
    onChange(JSON.stringify({ ...config, ...updates }));

  return (
    <Stack gap="md" onClick={(e) => e.stopPropagation()}>
      <InputRefPicker
        label="Analysis view"
        value={config.view ?? { from: 'Previous' }}
        onChange={(view) => patch({ view })}
        priorStepOptions={priorStepOptions}
        allowedSources={['Previous', 'Step']}
      />
      <Text size="xs" c="dimmed" mt={-8}>
        Which step&apos;s bounded {'{view, meta}'} envelope every editor seat and the director see —
        normally the VideoAnalyze step. The room emits the same decision shape a solo story-editor
        step would, so a VideoCompile step&apos;s decision reference works unchanged.
      </Text>

      <SimpleGrid cols={2} spacing="sm">
        <NumberInput
          label="Rounds"
          description="Round-robin passes over every seat before the director speaks"
          value={config.rounds ?? 2}
          onChange={(v) => patch({ rounds: typeof v === 'number' ? v : 2 })}
          min={1}
          max={6}
          size="sm"
        />
        <NumberInput
          label="Max turns"
          description="Hard turn ceiling (clamped 2–20 server-side)"
          value={config.maxTurns ?? 8}
          onChange={(v) => patch({ maxTurns: typeof v === 'number' ? v : 8 })}
          min={2}
          max={20}
          size="sm"
        />
      </SimpleGrid>

      <SimpleGrid cols={2} spacing="sm">
        <Switch
          label="Solo-editor fallback"
          description="A failed/empty room degrades to one ordinary VideoStoryEditor call"
          checked={config.fallbackToSoloEditor ?? true}
          onChange={(e) => patch({ fallbackToSoloEditor: e.currentTarget.checked })}
          size="sm"
        />
        <Switch
          label="Persist transcript"
          description="Upload the full room discussion as a non-authoritative audit artifact"
          checked={config.persistTranscript ?? true}
          onChange={(e) => patch({ persistTranscript: e.currentTarget.checked })}
          size="sm"
        />
      </SimpleGrid>
    </Stack>
  );
}
