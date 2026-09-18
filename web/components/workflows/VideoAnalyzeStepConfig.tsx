'use client';

import { useState } from 'react';
import {
  Stack, Select, SegmentedControl, TextInput, NumberInput, Switch, Collapse, Button, Text, Divider, Code, Group, Paper, ActionIcon, SimpleGrid,
} from '@mantine/core';
import { IconChevronDown, IconChevronUp, IconSettings, IconPlus, IconTrash } from '@tabler/icons-react';
import type {
  VideoAnalyzeStepConfig as VideoAnalyzeStepConfigValue,
  VideoSourceRef,
  VideoSourceKind,
  VideoTranscriptionMode,
  VideoVisualDetail,
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
    analyzeVisuals: true,
    visualSampleFps: 2.0,
    visualGridWidth: 32,
    visualGridHeight: 18,
    maxVisualSampleFrames: 4000,
    stillMotionThreshold: 0.02,
    minStillWindowMs: 400,
    maxStillWindowsPerShot: 3,
    detectLetterbox: true,
    detectSharpness: false,
    analyzeAudioLevels: true,
    detectNearDuplicates: true,
    duplicateSimilarityThreshold: 0.90,
    duplicateWindowShots: 20,
    visualDetail: 'Compact',
    maxViewDuplicateGroups: 20,
    analyzeColorGrading: true,
    detectLookGroups: true,
    lookSimilarityThreshold: 0.88,
    maxViewLookGroups: 12,
    keyframesPerShot: 1,
    maxSharpnessShots: 24,
    expect: null,
  };
}

const SOURCE_KIND_OPTIONS: { value: VideoSourceKind; label: string }[] = [
  { value: 'PreviousStepOutput', label: 'Previous step output' },
  { value: 'StepOutput', label: 'Specific step' },
  { value: 'ProjectFile', label: 'Uploaded project file' },
];

const DEFAULT_SOURCE_REF: VideoSourceRef = { kind: 'PreviousStepOutput', projectFileId: null, stepOrder: null };

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

interface VideoMultiSourceRefPickerProps {
  value: VideoSourceRef[];
  onChange: (refs: VideoSourceRef[]) => void;
  priorStepOptions: { value: string; label: string }[];
  projectId?: string;
}

/** Same as VideoSourceRefPicker, but for the plural `sources` field — one clip per row, reorderable only by position, add/remove freely. A one-element list is functionally identical to the singular `source` field on the backend. */
function VideoMultiSourceRefPicker({ value, onChange, priorStepOptions, projectId }: VideoMultiSourceRefPickerProps) {
  const sources = value.length > 0 ? value : [DEFAULT_SOURCE_REF];

  const updateAt = (index: number, ref: VideoSourceRef) => {
    onChange(sources.map((s, i) => (i === index ? ref : s)));
  };
  const removeAt = (index: number) => {
    onChange(sources.filter((_, i) => i !== index));
  };
  const add = () => onChange([...sources, DEFAULT_SOURCE_REF]);

  return (
    <Stack gap="xs" onClick={(e) => e.stopPropagation()}>
      <Text size="xs" fw={500}>Source clips ({sources.length})</Text>
      {sources.map((ref, index) => (
        <Paper key={index} withBorder p="xs" radius="md">
          <Group justify="space-between" align="flex-start" wrap="nowrap" mb={4}>
            <Text size="xs" c="dimmed" fw={600}>Clip {index + 1}</Text>
            <ActionIcon
              size="xs"
              color="red"
              variant="subtle"
              disabled={sources.length <= 1}
              onClick={() => removeAt(index)}
              aria-label={`Remove clip ${index + 1}`}
            >
              <IconTrash size={14} />
            </ActionIcon>
          </Group>
          <VideoSourceRefPicker
            value={ref}
            onChange={(next) => updateAt(index, next)}
            priorStepOptions={priorStepOptions}
            projectId={projectId}
          />
        </Paper>
      ))}
      <Button
        size="xs"
        variant="light"
        leftSection={<IconPlus size={14} />}
        onClick={add}
      >
        Add source clip
      </Button>
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

  // Multi-source mode is "on" whenever the step is currently saved with the plural `sources`
  // field (even a one-element list) rather than the singular `source` — mirrors the backend's
  // own two-representation record exactly, so toggling never silently drops the other field.
  const isMultiSource = !config.source;

  const toggleMultiSource = (multi: boolean) => {
    if (multi) {
      patch({ source: undefined, sources: config.source ? [config.source] : (config.sources ?? [DEFAULT_SOURCE_REF]) });
    } else {
      patch({ source: config.sources?.[0] ?? DEFAULT_SOURCE_REF, sources: null });
    }
  };

  return (
    <Stack gap="md" onClick={(e) => e.stopPropagation()}>
      <Switch
        label="Multiple source clips"
        description="Analyze several clips as one step, cut together in Keep-span order."
        checked={isMultiSource}
        onChange={(e) => toggleMultiSource(e.currentTarget.checked)}
      />

      {isMultiSource ? (
        <VideoMultiSourceRefPicker
          value={config.sources ?? []}
          onChange={(sources) => patch({ sources })}
          priorStepOptions={priorStepOptions}
          projectId={projectId}
        />
      ) : (
        <VideoSourceRefPicker
          value={config.source ?? DEFAULT_SOURCE_REF}
          onChange={(source) => patch({ source })}
          priorStepOptions={priorStepOptions}
          projectId={projectId}
        />
      )}

      <Divider label="Silence detection" labelPosition="left" />
      <SimpleGrid cols={{ base: 1, sm: 2, md: 3 }}>
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
      </SimpleGrid>

      <Divider label="Shot detection" labelPosition="left" />
      <SimpleGrid cols={{ base: 1, sm: 2 }}>
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
      </SimpleGrid>

      <Divider label="Scene and visual analysis" labelPosition="left" />
      <Text size="xs" c="dimmed">
        Deterministic ffmpeg + pure C# descriptors (motion, camera move, exposure, dominant
        colors, safe zones, near-duplicate takes, audio loudness). No LLM call, no new external
        dependency. See docs/video-editing.md.
      </Text>
      <SimpleGrid cols={{ base: 1, sm: 2, md: 3 }}>
        <Switch
          label="Analyze visuals"
          checked={config.analyzeVisuals}
          onChange={(e) => patch({ analyzeVisuals: e.currentTarget.checked })}
        />
        <Switch
          label="Analyze audio levels"
          checked={config.analyzeAudioLevels}
          onChange={(e) => patch({ analyzeAudioLevels: e.currentTarget.checked })}
        />
        <Switch
          label="Detect near-duplicate takes"
          checked={config.detectNearDuplicates}
          onChange={(e) => patch({ detectNearDuplicates: e.currentTarget.checked })}
          disabled={!config.analyzeVisuals}
        />
      </SimpleGrid>
      <SimpleGrid cols={{ base: 1, sm: 2, md: 3 }}>
        <Switch
          label="Analyze color grading"
          description="Colour temperature, tone curve, and saturation. No extra processing cost."
          checked={config.analyzeColorGrading}
          onChange={(e) => patch({ analyzeColorGrading: e.currentTarget.checked })}
          disabled={!config.analyzeVisuals}
        />
        <Switch
          label="Detect look groups"
          description="Groups shots that share a similar grade. No extra processing cost."
          checked={config.detectLookGroups}
          onChange={(e) => patch({ detectLookGroups: e.currentTarget.checked })}
          disabled={!config.analyzeVisuals}
        />
        <Switch
          label="Detect letterbox/pillarbox"
          description="No extra processing cost. May under-report soft or gradient bars."
          checked={config.detectLetterbox}
          onChange={(e) => patch({ detectLetterbox: e.currentTarget.checked })}
          disabled={!config.analyzeVisuals}
        />
      </SimpleGrid>
      {config.analyzeVisuals && (
        <Select
          label="Per-shot detail in the prompt view"
          description="Degrades Full -> Compact -> None to fit the char budget below, before any shot/silence/segment is ever dropped"
          size="xs"
          value={config.visualDetail}
          data={[
            { value: 'None', label: 'None: no visual/audio data in the view' },
            { value: 'Compact', label: 'Compact: motion, camera move, safe zone, dup/best, exposure' },
            { value: 'Full', label: 'Full: adds all regions, all still windows, motion std-dev/peak' },
          ]}
          onChange={(v) => v && patch({ visualDetail: v as VideoVisualDetail })}
        />
      )}

      <Divider label="Transcription (ASR)" labelPosition="left" />
      <Select
        label="Mode"
        size="xs"
        value={config.transcription}
        data={[
          { value: 'Off', label: 'Off: silence + shot analysis only' },
          { value: 'Optional', label: 'Optional: attempt ASR, degrade cleanly if unavailable' },
          { value: 'Required', label: 'Required: fail the step if ASR is unavailable' },
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
          <SimpleGrid cols={{ base: 1, sm: 2 }}>
            <TextInput
              label="Language"
              description="ISO 639-1 code, e.g. en (blank auto-detects)"
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
          </SimpleGrid>
          <Switch
            label="Word-level timestamps"
            checked={config.wordTimestamps}
            onChange={(e) => patch({ wordTimestamps: e.currentTarget.checked })}
          />
        </Stack>
      )}

      <Divider label="Guardrails (checked before any decode)" labelPosition="left" />
      <SimpleGrid cols={{ base: 1, sm: 2 }}>
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
      </SimpleGrid>

      <Divider label="Prompt-view budget" labelPosition="left" />
      <SimpleGrid cols={{ base: 1, sm: 2, md: 3 }}>
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
      </SimpleGrid>

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
        <SimpleGrid cols={{ base: 1, sm: 2, md: 3 }}>
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
        </SimpleGrid>
      </Collapse>

      <Divider />

      <Paper p="xs" withBorder>
        <Text size="xs" fw={600} c="dimmed" mb={4}>Output shape (always this envelope, always valid JSON)</Text>
        <Code block style={{ fontSize: 11 }}>
{`{
  "view": {
    "media": { "durationSec": 184.32, "fpsNum": 30, "fpsDen": 1, "width": 1920, "height": 1080 },
    "shots": [ { "id": "s0", "startSec": 0.0, "endSec": 12.4, "durationSec": 12.4${
      config.analyzeVisuals && config.visualDetail !== 'None'
        ? ',\n      "v": { "motion": 12, "move": "Pan", "cutIn": "still", "cutOut": "moving",\n             "bright": 41, "contrast": 22, "colors": ["#2b3a4f", "#c9b48a"],\n             "safe": { "region": "LowerThird", "fit": 88, "text": "Light" },\n             "dup": "d2", "best": true }' + (config.analyzeAudioLevels ? ',\n      "a": { "rms": -21, "speech": 82 }' : '')
        : ''
    } } ],
    "silences": [ { "id": "g3", "startSec": 12.1, "endSec": 13.9, "durationSec": 1.8, "afterShot": "s0" } ],
    "segments": [ { "id": "t7", "shot": "s0", "startSec": 1.2, "endSec": 4.9, "text": "so what we built here is" } ]${
      config.analyzeVisuals && config.visualDetail !== 'None'
        ? ',\n    "pacing": { "meanShotSec": 4.1, "medianShotSec": 3.8, "cutsPerMinute": 14.6, "motionTimeline": [12, 30, 8], "timelineBinSec": 5.0 },\n    "duplicateGroups": [ { "id": "d2", "shotIds": ["s4", "s7"], "bestShotId": "s7", "similarity": 94 } ]'
        : ''
    }
  },
  "meta": {
    "operation": "videoAnalyze",
    "artifactStorageKey": "projects/.../step-1-analysis.json",
    "offeredIdCount": 412, "truncated": false, "droppedItems": 0,
    "transcription": { "mode": "${config.transcription}", "applied": true, "provider": "whisper-local", "degraded": false },
    "visual": { "applied": ${config.analyzeVisuals}, "degraded": false, "sampleFps": ${config.visualSampleFps}, "gridWidth": ${config.visualGridWidth}, "gridHeight": ${config.visualGridHeight}, "detail": "${config.analyzeVisuals ? config.visualDetail : 'None'}" },
    "audioLevels": { "applied": ${config.analyzeAudioLevels} },
    "sourceChars": 1843201, "outputChars": 23117
  }
}`}
        </Code>
      </Paper>
    </Stack>
  );
}
