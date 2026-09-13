'use client';

import { useState } from 'react';
import {
  Stack, Select, SegmentedControl, TextInput, NumberInput, Switch, Collapse, Button, Text, Divider, Code, Group, Paper,
} from '@mantine/core';
import { IconChevronDown, IconChevronUp, IconSettings } from '@tabler/icons-react';
import type {
  VideoAnalyzeStepConfig as VideoAnalyzeStepConfigValue,
  VideoSourceRef,
  VideoSourceKind,
  VideoTranscriptionMode,
} from '@/lib/types/workflow';
import { useProjectFiles } from '@/lib/hooks/use-files';
import { useInferenceProviders } from '@/lib/hooks/use-inference-providers';
import { useAuth } from '@/lib/hooks/use-auth';
import type { StepData } from './WorkflowStepList';

/** Builds a sensible default config for a freshly-added VideoAnalyze step. Mirrors the C# record defaults exactly. */
export function createDefaultVideoAnalyzeStepConfig(): VideoAnalyzeStepConfigValue {
  return {
    version: 1,
    source: { kind: 'PreviousStepOutput', projectFileId: null, stepOrder: null },
    detectSilence: true,
    silenceThresholdDb: -34.0,
    minSilenceMs: 350,
    detectShots: true,
    sceneThreshold: 0.3,
    transcription: 'Optional',
    transcriptionProviderId: null,
    language: null,
    wordTimestamps: true,
    maxAsrChunkBytes: 20_000_000,
    maxDurationSeconds: 1800,
    maxInputBytes: 2_000_000_000,
    maxOutputChars: 24_000,
    maxViewSegments: 400,
    maxSegmentTextChars: 160,
    expect: null,
  };
}

const SOURCE_KIND_OPTIONS: { value: VideoSourceKind; label: string }[] = [
  { value: 'PreviousStepOutput', label: 'Previous step output' },
  { value: 'StepOutput', label: 'Specific step' },
  { value: 'ProjectFile', label: 'Uploaded project file' },
];

interface VideoSourceRefPickerProps {
  value: VideoSourceRef;
  onChange: (ref: VideoSourceRef) => void;
  priorStepOptions: { value: string; label: string }[];
  projectId?: string;
}

/** Picks which video a VideoAnalyze step operates on — render output, a specific step, or an uploaded file. */
function VideoSourceRefPicker({ value, onChange, priorStepOptions, projectId }: VideoSourceRefPickerProps) {
  const { data: files } = useProjectFiles(projectId ?? '');
  const videoFiles = (files ?? []).filter((f) => f.mimeType?.startsWith('video/'));

  return (
    <Stack gap="xs" onClick={(e) => e.stopPropagation()}>
      <div>
        <Text size="xs" fw={500} mb={4}>Source video</Text>
        <SegmentedControl
          fullWidth
          size="xs"
          data={SOURCE_KIND_OPTIONS.map((o) => ({ value: o.value, label: o.label }))}
          value={value.kind}
          onChange={(v) => onChange({ kind: v as VideoSourceKind, projectFileId: null, stepOrder: null })}
        />
      </div>

      {value.kind === 'StepOutput' && (
        <Select
          label="Step"
          size="xs"
          data={priorStepOptions}
          value={value.stepOrder != null ? String(value.stepOrder) : null}
          onChange={(v) => onChange({ ...value, stepOrder: v ? Number(v) : null })}
          placeholder={priorStepOptions.length > 0 ? 'Select a step' : 'No earlier steps'}
          disabled={priorStepOptions.length === 0}
        />
      )}

      {value.kind === 'ProjectFile' && (
        <Select
          label="Video file"
          size="xs"
          data={videoFiles.map((f) => ({ value: f.id, label: f.originalFileName }))}
          value={value.projectFileId ?? null}
          onChange={(v) => onChange({ ...value, projectFileId: v })}
          placeholder={videoFiles.length > 0 ? 'Select a video file' : 'No video files uploaded to this project'}
          disabled={videoFiles.length === 0}
          searchable
        />
      )}
    </Stack>
  );
}

interface VideoAnalyzeStepConfigProps {
  config: VideoAnalyzeStepConfigValue;
  onChange: (config: VideoAnalyzeStepConfigValue) => void;
  allSteps?: StepData[];
  currentStepIndex?: number;
  projectId?: string;
}

/**
 * Configuration form for a VideoAnalyze step (deterministic ffmpeg-based derushing — no model call).
 * Mirrors the backend `VideoAnalyzeStepConfig` record field-for-field.
 */
export function VideoAnalyzeStepConfig({
  config, onChange, allSteps = [], currentStepIndex = 0, projectId,
}: VideoAnalyzeStepConfigProps) {
  const [expectOpen, setExpectOpen] = useState(Boolean(config.expect));
  const { isAdmin } = useAuth();
  // /api/v1/inference-providers is admin-only — this step config is reachable by any project
  // member building a workflow, so a non-admin must never trigger this fetch (it would just 403).
  const { data: providers } = useInferenceProviders(isAdmin);
  const transcriptionProviders = (providers ?? [])
    .filter((p) => p.capability === 'Transcription' && (p.isEnabled || p.id === config.transcriptionProviderId));

  const priorStepOptions = allSteps
    .slice(0, currentStepIndex)
    .map((previousStep, index) => {
      const stepOrder = index + 1;
      const label = previousStep.label?.trim() || `Step ${stepOrder}`;
      return { value: String(stepOrder), label: `${stepOrder}. ${label}` };
    });

  const patch = (updates: Partial<VideoAnalyzeStepConfigValue>) => onChange({ ...config, ...updates });

  return (
    <Stack gap="md" onClick={(e) => e.stopPropagation()}>
      <VideoSourceRefPicker
        value={config.source}
        onChange={(source) => patch({ source })}
        priorStepOptions={priorStepOptions}
        projectId={projectId}
      />

      <Divider label="Silence detection" labelPosition="left" />
      <Group grow align="flex-end">
        <Switch
          label="Detect silence"
          checked={config.detectSilence}
          onChange={(e) => patch({ detectSilence: e.currentTarget.checked })}
        />
        <NumberInput
          label="Threshold (dB)"
          size="xs"
          value={config.silenceThresholdDb}
          onChange={(v) => patch({ silenceThresholdDb: typeof v === 'number' ? v : -34 })}
          disabled={!config.detectSilence}
        />
        <NumberInput
          label="Min silence (ms)"
          size="xs"
          min={0}
          value={config.minSilenceMs}
          onChange={(v) => patch({ minSilenceMs: typeof v === 'number' ? v : 350 })}
          disabled={!config.detectSilence}
        />
      </Group>

      <Divider label="Shot detection" labelPosition="left" />
      <Group grow align="flex-end">
        <Switch
          label="Detect shots"
          checked={config.detectShots}
          onChange={(e) => patch({ detectShots: e.currentTarget.checked })}
        />
        <NumberInput
          label="Scene threshold"
          description="0.0 - 1.0"
          size="xs"
          min={0}
          max={1}
          step={0.01}
          decimalScale={2}
          value={config.sceneThreshold}
          onChange={(v) => patch({ sceneThreshold: typeof v === 'number' ? v : 0.3 })}
          disabled={!config.detectShots}
        />
      </Group>

      <Divider label="Transcription (ASR)" labelPosition="left" />
      <Select
        label="Mode"
        size="xs"
        value={config.transcription}
        data={[
          { value: 'Off', label: 'Off — silence + shot analysis only' },
          { value: 'Optional', label: 'Optional — attempt ASR, degrade cleanly if unavailable' },
          { value: 'Required', label: 'Required — fail the step if ASR is unavailable' },
        ]}
        onChange={(v) => v && patch({ transcription: v as VideoTranscriptionMode })}
      />
      {config.transcription !== 'Off' && (
        <Stack gap="xs">
          <Select
            label="Transcription provider"
            size="xs"
            data={transcriptionProviders.map((p) => ({ value: p.id, label: p.name }))}
            value={config.transcriptionProviderId ?? null}
            onChange={(v) => patch({ transcriptionProviderId: v })}
            placeholder={
              !isAdmin
                ? 'Default transcription provider (admin required to pick a specific one)'
                : transcriptionProviders.length > 0
                ? 'Default transcription provider'
                : 'No transcription-capable provider configured'
            }
            clearable
            disabled={!isAdmin || transcriptionProviders.length === 0}
          />
          <Group grow>
            <TextInput
              label="Language"
              description="ISO 639-1 code, e.g. en — blank auto-detects"
              placeholder="en"
              size="xs"
              value={config.language ?? ''}
              onChange={(e) => patch({ language: e.target.value || null })}
            />
            <NumberInput
              label="Max ASR chunk bytes"
              size="xs"
              min={1_000_000}
              value={config.maxAsrChunkBytes}
              onChange={(v) => patch({ maxAsrChunkBytes: typeof v === 'number' ? v : 20_000_000 })}
            />
          </Group>
          <Switch
            label="Word-level timestamps"
            checked={config.wordTimestamps}
            onChange={(e) => patch({ wordTimestamps: e.currentTarget.checked })}
          />
        </Stack>
      )}

      <Divider label="Guardrails (checked before any decode)" labelPosition="left" />
      <Group grow>
        <NumberInput
          label="Max duration (seconds)"
          size="xs"
          min={1}
          value={config.maxDurationSeconds}
          onChange={(v) => patch({ maxDurationSeconds: typeof v === 'number' ? v : 1800 })}
        />
        <NumberInput
          label="Max input bytes"
          size="xs"
          min={1}
          value={config.maxInputBytes}
          onChange={(v) => patch({ maxInputBytes: typeof v === 'number' ? v : 2_000_000_000 })}
        />
      </Group>

      <Divider label="Prompt-view budget" labelPosition="left" />
      <Group grow>
        <NumberInput
          label="Max output chars"
          description="Trailing items are dropped and re-serialized, never truncated mid-JSON"
          size="xs"
          min={256}
          max={200_000}
          value={config.maxOutputChars}
          onChange={(v) => patch({ maxOutputChars: typeof v === 'number' ? v : 24_000 })}
        />
        <NumberInput
          label="Max view segments"
          size="xs"
          min={1}
          value={config.maxViewSegments}
          onChange={(v) => patch({ maxViewSegments: typeof v === 'number' ? v : 400 })}
        />
        <NumberInput
          label="Max segment text chars"
          size="xs"
          min={1}
          value={config.maxSegmentTextChars}
          onChange={(v) => patch({ maxSegmentTextChars: typeof v === 'number' ? v : 160 })}
        />
      </Group>

      <Button
        variant="subtle"
        size="compact-xs"
        leftSection={<IconSettings size={14} />}
        rightSection={expectOpen ? <IconChevronUp size={14} /> : <IconChevronDown size={14} />}
        onClick={() => setExpectOpen(!expectOpen)}
      >
        Expectations (fail fast before the next step runs)
      </Button>
      <Collapse in={expectOpen}>
        <Group grow>
          <NumberInput
            label="Min shots"
            min={0}
            size="xs"
            value={config.expect?.minShots ?? undefined}
            onChange={(v) => patch({ expect: { ...config.expect, minShots: typeof v === 'number' ? v : null } })}
          />
          <NumberInput
            label="Min transcript segments"
            min={0}
            size="xs"
            value={config.expect?.minTranscriptSegments ?? undefined}
            onChange={(v) =>
              patch({ expect: { ...config.expect, minTranscriptSegments: typeof v === 'number' ? v : null } })
            }
          />
          <NumberInput
            label="Max silence ratio"
            description="0.0 - 1.0"
            min={0}
            max={1}
            step={0.01}
            decimalScale={2}
            size="xs"
            value={config.expect?.maxSilenceRatio ?? undefined}
            onChange={(v) =>
              patch({ expect: { ...config.expect, maxSilenceRatio: typeof v === 'number' ? v : null } })
            }
          />
        </Group>
      </Collapse>

      <Divider />

      <Paper p="xs" withBorder>
        <Text size="xs" fw={600} c="dimmed" mb={4}>Output shape (always this envelope, always valid JSON)</Text>
        <Code block style={{ fontSize: 11 }}>
{`{
  "view": {
    "media": { "durationSec": 184.32, "fpsNum": 30, "fpsDen": 1, "width": 1920, "height": 1080 },
    "shots": [ { "id": "s0", "startSec": 0.0, "endSec": 12.4, "durationSec": 12.4 } ],
    "silences": [ { "id": "g3", "startSec": 12.1, "endSec": 13.9, "durationSec": 1.8, "afterShot": "s0" } ],
    "segments": [ { "id": "t7", "shot": "s0", "startSec": 1.2, "endSec": 4.9, "text": "so what we built here is" } ]
  },
  "meta": {
    "operation": "videoAnalyze",
    "artifactStorageKey": "projects/.../step-1-analysis.json",
    "offeredIdCount": 412, "truncated": false, "droppedItems": 0,
    "transcription": { "mode": "${config.transcription}", "applied": true, "provider": "whisper-local", "degraded": false },
    "sourceChars": 1843201, "outputChars": 23117
  }
}`}
        </Code>
      </Paper>
    </Stack>
  );
}
