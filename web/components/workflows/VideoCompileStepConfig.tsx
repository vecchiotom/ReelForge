'use client';

import { useState } from 'react';
import {
  Stack, Select, TextInput, NumberInput, Switch, Collapse, Button, Text, Divider, SimpleGrid,
} from '@mantine/core';
import { IconChevronDown, IconChevronUp, IconSettings } from '@tabler/icons-react';
import type {
  VideoCompileStepConfig as VideoCompileStepConfigValue,
  VideoCompileMode,
  VideoTransitionPolicy,
} from '@/lib/types/workflow';
import { InputRefPicker } from './ExtractStepConfig';
import type { StepData } from './WorkflowStepList';

/** Fixed allowlist — these config values reach ffmpeg argv, so they must never be free text (plan R11). */
const VIDEO_CODEC_OPTIONS = ['libx264', 'libx265'];
const AUDIO_CODEC_OPTIONS = ['aac'];
const PRESET_OPTIONS = ['ultrafast', 'veryfast', 'fast', 'medium'];
const PROGRAM_FADE_COLOR_OPTIONS = ['black', 'white'];
const TRANSITION_POLICY_OPTIONS: { value: VideoTransitionPolicy; label: string }[] = [
  { value: 'Off', label: 'Off: hard cuts (default)' },
  { value: 'AudioOnly', label: 'Audio only: declick every splice' },
  { value: 'Auto', label: 'Auto: pick a treatment per cut' },
  { value: 'Expressive', label: 'Expressive: allow noticeable transitions' },
];

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
    graphicsPlan: null,
    enableGraphics: false,
    maxOverlays: 20,
    overlayShortMs: 1500,
    overlayMediumMs: 3000,
    overlayHoldMs: 6000,
    overlayFadeMs: 300,
    overlayFontSizePct: 5,
    overlayFontColor: 'white',
    overlayBoxColor: 'black@0.45',
    maxOverlayTextChars: 80,
    maxOverlaySubtextChars: 60,
    programFadeInMs: 0,
    programFadeOutMs: 0,
    programAudioFadeInMs: 0,
    programAudioFadeOutMs: 0,
    programFadeColor: 'black',
    transitionPolicy: 'Off',
    audioSeamRampMs: 24,
    softCutMs: 200,
    dissolveMs: 500,
    dipToBlackMs: 600,
    dipCutMs: 220,
    maxTransitionMs: 1200,
    maxTransitionRatioPct: 35,
    maxTransitionSegments: 80,
    sectionBreakGapMs: 8000,
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
          { value: 'Reencode', label: 'Re-encode: frame-accurate (default)' },
          { value: 'StreamCopy', label: 'Stream copy: fast, cuts snap to keyframes' },
        ]}
        onChange={(v) => {
          if (!v) return;
          const mode = v as VideoCompileMode;
          patch({
            mode,
            allowKeyframeSnapping: mode === 'StreamCopy' ? config.allowKeyframeSnapping : false,
            // Graphics overlays require Re-encode mode (drawtext/drawbox have no stream-copy equivalent).
            enableGraphics: mode === 'StreamCopy' ? false : config.enableGraphics,
          });
        }}
      />
      {config.mode === 'StreamCopy' && (
        <Switch
          label="Allow keyframe snapping"
          description="Required for stream copy. Cut points drift to the nearest keyframe."
          checked={config.allowKeyframeSnapping}
          onChange={(e) => patch({ allowKeyframeSnapping: e.currentTarget.checked })}
        />
      )}

      <SimpleGrid cols={{ base: 1, sm: 2, md: 4 }}>
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
      </SimpleGrid>

      <Divider label="Cut assembly" labelPosition="left" />
      <SimpleGrid cols={{ base: 1, sm: 2, md: 4 }}>
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
      </SimpleGrid>

      <Divider label="Output" labelPosition="left" />
      <SimpleGrid cols={{ base: 1, sm: 2 }}>
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
      </SimpleGrid>

      <Divider label="Motion graphics (optional)" labelPosition="left" />
      <Switch
        label="Enable graphics overlays"
        description="Applies a motion-graphics plan (lower-thirds/titles/callouts) during this same encode. Requires Re-encode mode."
        checked={config.enableGraphics}
        onChange={(e) => {
          const enableGraphics = e.currentTarget.checked;
          patch({
            enableGraphics,
            mode: enableGraphics && config.mode === 'StreamCopy' ? 'Reencode' : config.mode,
          });
        }}
      />
      {config.enableGraphics && (
        <InputRefPicker
          label="Motion graphics plan"
          value={config.graphicsPlan ?? { from: 'Previous' }}
          onChange={(graphicsPlan) => patch({ graphicsPlan })}
          priorStepOptions={priorStepOptions}
          allowedSources={['Previous', 'Step']}
        />
      )}

      <Divider label="Transitions & program fades" labelPosition="left" />
      <Select
        label="Transition policy"
        description="How much transition treatment to apply between cuts. Off = today's hard cuts. Audio only = a short declick on every splice, no visual change. Auto = the compile step picks a treatment per cut from measured shot data. Expressive = allows more noticeable transitions."
        size="xs"
        data={TRANSITION_POLICY_OPTIONS}
        value={config.transitionPolicy ?? 'Off'}
        onChange={(v) => v && patch({ transitionPolicy: v as VideoTransitionPolicy })}
      />

      <Text size="xs" fw={500}>Program open/close fade</Text>
      <SimpleGrid cols={{ base: 1, sm: 2, md: 4 }}>
        <NumberInput
          label="Video fade in (ms)"
          size="xs"
          min={0}
          value={config.programFadeInMs ?? 0}
          onChange={(v) => patch({ programFadeInMs: typeof v === 'number' ? v : 0 })}
        />
        <NumberInput
          label="Video fade out (ms)"
          size="xs"
          min={0}
          value={config.programFadeOutMs ?? 0}
          onChange={(v) => patch({ programFadeOutMs: typeof v === 'number' ? v : 0 })}
        />
        <NumberInput
          label="Audio fade in (ms)"
          size="xs"
          min={0}
          value={config.programAudioFadeInMs ?? 0}
          onChange={(v) => patch({ programAudioFadeInMs: typeof v === 'number' ? v : 0 })}
        />
        <NumberInput
          label="Audio fade out (ms)"
          size="xs"
          min={0}
          value={config.programAudioFadeOutMs ?? 0}
          onChange={(v) => patch({ programAudioFadeOutMs: typeof v === 'number' ? v : 0 })}
        />
      </SimpleGrid>
      <Select
        label="Fade color"
        description="Color the video fades to/from (allowlisted at execution time)"
        size="xs"
        data={PROGRAM_FADE_COLOR_OPTIONS}
        value={config.programFadeColor ?? 'black'}
        onChange={(v) => v && patch({ programFadeColor: v })}
      />

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

        <Text size="xs" fw={500} mt="sm">Seam transition tuning</Text>
        <Text size="xs" c="dimmed" mb="xs">
          Only take effect when the transition policy above is not Off.
        </Text>
        <SimpleGrid cols={{ base: 1, sm: 2, md: 3 }}>
          <NumberInput
            label="Audio seam ramp (ms)"
            description="Half-width of the audio declick ramp an audio-only seam treatment applies around the cut"
            size="xs"
            min={0}
            value={config.audioSeamRampMs ?? 24}
            onChange={(v) => patch({ audioSeamRampMs: typeof v === 'number' ? v : 24 })}
          />
          <NumberInput
            label="Soft cut (ms)"
            description="Duration of a soft-cut (short crossfade) treatment"
            size="xs"
            min={0}
            value={config.softCutMs ?? 200}
            onChange={(v) => patch({ softCutMs: typeof v === 'number' ? v : 200 })}
          />
          <NumberInput
            label="Dissolve (ms)"
            description="Duration of a dissolve treatment"
            size="xs"
            min={0}
            value={config.dissolveMs ?? 500}
            onChange={(v) => patch({ dissolveMs: typeof v === 'number' ? v : 500 })}
          />
        </SimpleGrid>
        <SimpleGrid cols={{ base: 1, sm: 2, md: 3 }} mt="xs">
          <NumberInput
            label="Dip to black (ms)"
            description="Duration of a dip-to-black treatment"
            size="xs"
            min={0}
            value={config.dipToBlackMs ?? 600}
            onChange={(v) => patch({ dipToBlackMs: typeof v === 'number' ? v : 600 })}
          />
          <NumberInput
            label="Dip cut (ms)"
            description="Duration of a dip-cut (brief dip without a full dip-to-black) treatment"
            size="xs"
            min={0}
            value={config.dipCutMs ?? 220}
            onChange={(v) => patch({ dipCutMs: typeof v === 'number' ? v : 220 })}
          />
          <NumberInput
            label="Section break gap (ms)"
            description="Silence gap that qualifies as a section break rather than a plain cut"
            size="xs"
            min={0}
            value={config.sectionBreakGapMs ?? 8000}
            onChange={(v) => patch({ sectionBreakGapMs: typeof v === 'number' ? v : 8000 })}
          />
        </SimpleGrid>
        <SimpleGrid cols={{ base: 1, sm: 2, md: 3 }} mt="xs">
          <NumberInput
            label="Max transition (ms)"
            description="Hard ceiling on any single transition's duration"
            size="xs"
            min={0}
            value={config.maxTransitionMs ?? 1200}
            onChange={(v) => patch({ maxTransitionMs: typeof v === 'number' ? v : 1200 })}
          />
          <NumberInput
            label="Max transition ratio (%)"
            description="Caps what percent of all cuts may carry a crossfade-style treatment; excess cuts downgrade to audio-only"
            size="xs"
            min={0}
            max={100}
            value={config.maxTransitionRatioPct ?? 35}
            onChange={(v) => patch({ maxTransitionRatioPct: typeof v === 'number' ? v : 35 })}
          />
          <NumberInput
            label="Max transition segments"
            description="Caps how many cuts in the whole edit may receive a non-hard-cut treatment"
            size="xs"
            min={0}
            value={config.maxTransitionSegments ?? 80}
            onChange={(v) => patch({ maxTransitionSegments: typeof v === 'number' ? v : 80 })}
          />
        </SimpleGrid>
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
        <SimpleGrid cols={{ base: 1, sm: 2, md: 4 }}>
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
        </SimpleGrid>
      </Collapse>

      <Text size="xs" c="dimmed">
        The editorial decision references opaque ids only, never a timestamp. Ids are resolved to
        frame-accurate times entirely server-side from the analysis step&apos;s full artifact.
      </Text>
    </Stack>
  );
}
