'use client';

import { useState } from 'react';
import {
  Stack, Select, TextInput, NumberInput, Switch, Collapse, Button, Text, Divider, Group,
} from '@mantine/core';
import { IconChevronDown, IconChevronUp, IconSettings } from '@tabler/icons-react';
import type {
  VideoCompileStepConfig as VideoCompileStepConfigValue,
  VideoCompileMode,
} from '@/lib/types/workflow';
import { InputRefPicker } from './ExtractStepConfig';
import type { StepData } from './WorkflowStepList';

/** Fixed allowlist — these config values reach ffmpeg argv, so they must never be free text (plan R11). */
const VIDEO_CODEC_OPTIONS = ['libx264', 'libx265'];
const AUDIO_CODEC_OPTIONS = ['aac'];
const PRESET_OPTIONS = ['ultrafast', 'veryfast', 'fast', 'medium'];

/** Builds a sensible default config for a freshly-added VideoCompile step. Mirrors the C# record defaults exactly. */
export function createDefaultVideoCompileStepConfig(): VideoCompileStepConfigValue {
  return {
    version: 1,
    decision: { from: 'Previous' },
    analysisStepOrder: 1,
    analysisStepResultId: null,
    mode: 'Reencode',
    prePaddingMs: 80,
    postPaddingMs: 120,
    minSegmentMs: 250,
    maxSegments: 200,
    allowKeyframeSnapping: false,
    outputFileName: 'edited.mp4',
    videoCodec: 'libx264',
    audioCodec: 'aac',
    crf: 20,
    preset: 'veryfast',
    registerProjectFile: true,
    expect: null,
  };
}

interface VideoCompileStepConfigProps {
  config: VideoCompileStepConfigValue;
  onChange: (config: VideoCompileStepConfigValue) => void;
  allSteps?: StepData[];
  currentStepIndex?: number;
}

/**
 * Configuration form for a VideoCompile step (deterministic ffmpeg-based cutting — no model call).
 * Mirrors the backend `VideoCompileStepConfig` record field-for-field.
 */
export function VideoCompileStepConfig({
  config, onChange, allSteps = [], currentStepIndex = 0,
}: VideoCompileStepConfigProps) {
  const [expectOpen, setExpectOpen] = useState(Boolean(config.expect));
  const [advancedOpen, setAdvancedOpen] = useState(Boolean(config.analysisStepResultId));

  const priorStepOptions = allSteps
    .slice(0, currentStepIndex)
    .map((previousStep, index) => {
      const stepOrder = index + 1;
      const label = previousStep.label?.trim() || `Step ${stepOrder}`;
      return { value: String(stepOrder), label: `${stepOrder}. ${label}` };
    });

  const analyzeStepOptions = allSteps
    .slice(0, currentStepIndex)
    .map((previousStep, index) => ({ step: previousStep, stepOrder: index + 1 }))
    .filter(({ step }) => step.stepType === 'VideoAnalyze')
    .map(({ step, stepOrder }) => ({
      value: String(stepOrder),
      label: `${stepOrder}. ${step.label?.trim() || `Step ${stepOrder}`}`,
    }));

  const patch = (updates: Partial<VideoCompileStepConfigValue>) => onChange({ ...config, ...updates });

  return (
    <Stack gap="md" onClick={(e) => e.stopPropagation()}>
      <InputRefPicker
        label="Editorial decision"
        value={config.decision}
        onChange={(decision) => patch({ decision })}
        priorStepOptions={priorStepOptions}
        allowedSources={['Previous', 'Step']}
      />

      {analyzeStepOptions.length > 0 ? (
        <Select
          label="Analysis step"
          description="Which VideoAnalyze step's full artifact to resolve the decision's ids against"
          size="xs"
          data={analyzeStepOptions}
          value={String(config.analysisStepOrder)}
          onChange={(v) => v && patch({ analysisStepOrder: Number(v) })}
        />
      ) : (
        <NumberInput
          label="Analysis step order"
          description="Step order of the VideoAnalyze step whose artifact this decision resolves against"
          size="xs"
          min={1}
          value={config.analysisStepOrder}
          onChange={(v) => patch({ analysisStepOrder: typeof v === 'number' ? v : 1 })}
        />
      )}

      <Divider label="Encoding" labelPosition="left" />
      <Select
        label="Mode"
        size="xs"
        value={config.mode}
        data={[
          { value: 'Reencode', label: 'Re-encode — frame-accurate (default)' },
          { value: 'StreamCopy', label: 'Stream copy — fast, cuts snap to keyframes' },
        ]}
        onChange={(v) => {
          if (!v) return;
          const mode = v as VideoCompileMode;
          patch({ mode, allowKeyframeSnapping: mode === 'StreamCopy' ? config.allowKeyframeSnapping : false });
        }}
      />
      {config.mode === 'StreamCopy' && (
        <Switch
          label="Allow keyframe snapping"
          description="Required for stream copy — cut points drift to the nearest keyframe"
          checked={config.allowKeyframeSnapping}
          onChange={(e) => patch({ allowKeyframeSnapping: e.currentTarget.checked })}
        />
      )}

      <Group grow>
        <Select
          label="Video codec"
          size="xs"
          data={VIDEO_CODEC_OPTIONS}
          value={config.videoCodec}
          onChange={(v) => v && patch({ videoCodec: v })}
          disabled={config.mode === 'StreamCopy'}
        />
        <Select
          label="Audio codec"
          size="xs"
          data={AUDIO_CODEC_OPTIONS}
          value={config.audioCodec}
          onChange={(v) => v && patch({ audioCodec: v })}
          disabled={config.mode === 'StreamCopy'}
        />
        <Select
          label="Preset"
          size="xs"
          data={PRESET_OPTIONS}
          value={config.preset}
          onChange={(v) => v && patch({ preset: v })}
          disabled={config.mode === 'StreamCopy'}
        />
        <NumberInput
          label="CRF"
          description="0 (lossless) - 51 (worst)"
          size="xs"
          min={0}
          max={51}
          value={config.crf}
          onChange={(v) => patch({ crf: typeof v === 'number' ? Math.min(51, Math.max(0, v)) : 20 })}
          disabled={config.mode === 'StreamCopy'}
        />
      </Group>

      <Divider label="Cut assembly" labelPosition="left" />
      <Group grow>
        <NumberInput
          label="Pre-padding (ms)"
          size="xs"
          min={0}
          value={config.prePaddingMs}
          onChange={(v) => patch({ prePaddingMs: typeof v === 'number' ? v : 80 })}
        />
        <NumberInput
          label="Post-padding (ms)"
          size="xs"
          min={0}
          value={config.postPaddingMs}
          onChange={(v) => patch({ postPaddingMs: typeof v === 'number' ? v : 120 })}
        />
        <NumberInput
          label="Min segment (ms)"
          size="xs"
          min={0}
          value={config.minSegmentMs}
          onChange={(v) => patch({ minSegmentMs: typeof v === 'number' ? v : 250 })}
        />
        <NumberInput
          label="Max segments"
          size="xs"
          min={1}
          value={config.maxSegments}
          onChange={(v) => patch({ maxSegments: typeof v === 'number' ? v : 200 })}
        />
      </Group>

      <Divider label="Output" labelPosition="left" />
      <Group grow align="flex-end">
        <TextInput
          label="Output file name"
          size="xs"
          value={config.outputFileName}
          onChange={(e) => patch({ outputFileName: e.target.value || 'edited.mp4' })}
        />
        <Switch
          label="Register as project file"
          description="Makes the edited video re-editable and visible in the files UI"
          checked={config.registerProjectFile}
          onChange={(e) => patch({ registerProjectFile: e.currentTarget.checked })}
        />
      </Group>

      <Button
        variant="subtle"
        size="compact-xs"
        leftSection={<IconSettings size={14} />}
        rightSection={advancedOpen ? <IconChevronUp size={14} /> : <IconChevronDown size={14} />}
        onClick={() => setAdvancedOpen(!advancedOpen)}
      >
        Advanced
      </Button>
      <Collapse in={advancedOpen}>
        <TextInput
          label="Analysis step result ID"
          description="Cross-execution override: resolve a prior run's artifact instead of this execution's"
          placeholder="Leave blank to use this execution's analysis step"
          size="xs"
          value={config.analysisStepResultId ?? ''}
          onChange={(e) => patch({ analysisStepResultId: e.target.value || null })}
        />
      </Collapse>

      <Button
        variant="subtle"
        size="compact-xs"
        leftSection={<IconSettings size={14} />}
        rightSection={expectOpen ? <IconChevronUp size={14} /> : <IconChevronDown size={14} />}
        onClick={() => setExpectOpen(!expectOpen)}
      >
        Expectations (fail fast before encoding starts)
      </Button>
      <Collapse in={expectOpen}>
        <Group grow>
          <NumberInput
            label="Min output seconds"
            min={0}
            size="xs"
            value={config.expect?.minOutputSeconds ?? undefined}
            onChange={(v) => patch({ expect: { ...config.expect, minOutputSeconds: typeof v === 'number' ? v : null } })}
          />
          <NumberInput
            label="Max output seconds"
            min={0}
            size="xs"
            value={config.expect?.maxOutputSeconds ?? undefined}
            onChange={(v) => patch({ expect: { ...config.expect, maxOutputSeconds: typeof v === 'number' ? v : null } })}
          />
          <NumberInput
            label="Min retained ratio"
            description="Refuses an edit that discards nearly everything"
            min={0}
            max={1}
            step={0.01}
            decimalScale={2}
            size="xs"
            value={config.expect?.minRetainedRatio ?? undefined}
            onChange={(v) => patch({ expect: { ...config.expect, minRetainedRatio: typeof v === 'number' ? v : null } })}
          />
          <NumberInput
            label="Max retained ratio"
            min={0}
            max={1}
            step={0.01}
            decimalScale={2}
            size="xs"
            value={config.expect?.maxRetainedRatio ?? undefined}
            onChange={(v) => patch({ expect: { ...config.expect, maxRetainedRatio: typeof v === 'number' ? v : null } })}
          />
        </Group>
      </Collapse>

      <Text size="xs" c="dimmed">
        The editorial decision references opaque ids only — no timestamps. Ids are resolved to frame-accurate
        times entirely server-side from the analysis step&apos;s full artifact.
      </Text>
    </Stack>
  );
}
