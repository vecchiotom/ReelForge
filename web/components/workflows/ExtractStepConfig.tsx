'use client';

import { useState } from 'react';
import {
  Stack, Select, TextInput, NumberInput, TagsInput, Switch, Collapse, Button, Text, Divider, Code, Group, Paper,
} from '@mantine/core';
import { IconChevronDown, IconChevronUp, IconSettings } from '@tabler/icons-react';
import type {
  ExtractStepConfig as ExtractStepConfigValue,
  ExtractOperation,
  ExtractInputRef,
  ExtractInputSource,
  ExtractUnknownIdBehaviour,
} from '@/lib/types/workflow';
import type { StepData } from './WorkflowStepList';

/** Builds a sensible default config for a freshly-added or operation-switched Extract step. */
export function createDefaultExtractStepConfig(operation: ExtractOperation = 'Project'): ExtractStepConfigValue {
  const base: ExtractStepConfigValue = {
    version: 1,
    operation,
    inputs: {},
    path: null,
    fields: null,
    idField: null,
    idPrefix: 'i',
    sortBy: null,
    take: null,
    skip: 0,
    idsPath: null,
    recordsPath: null,
    onUnknownId: 'Fail',
    categories: null,
    includeExtensions: null,
    excludePathContains: null,
    includeSummaries: true,
    includeContent: false,
    maxCharsPerFile: 2000,
    maxOutputChars: 24_000,
    expect: null,
  };

  if (operation === 'Project') {
    return {
      ...base,
      inputs: { source: { from: 'Previous' } },
      path: '$.components',
      take: 40,
    };
  }
  if (operation === 'Resolve') {
    return {
      ...base,
      inputs: { ids: { from: 'Previous' }, records: { from: 'Step', stepOrder: null } },
      idsPath: '$.selectedIds',
      recordsPath: '$.view.items',
    };
  }
  // Files
  return {
    ...base,
    inputs: { source: { from: 'ProjectFiles' } },
  };
}

const INPUT_SOURCE_OPTIONS: { value: ExtractInputSource; label: string }[] = [
  { value: 'Previous', label: 'Previous step output' },
  { value: 'Step', label: 'Specific step' },
  { value: 'Accumulated', label: 'Accumulated workflow output' },
  { value: 'ProjectFiles', label: 'Project files' },
];

interface InputRefPickerProps {
  label: string;
  value: ExtractInputRef | undefined;
  onChange: (ref: ExtractInputRef) => void;
  priorStepOptions: { value: string; label: string }[];
}

function InputRefPicker({ label, value, onChange, priorStepOptions }: InputRefPickerProps) {
  const from = value?.from ?? 'Previous';
  return (
    <Group gap="xs" align="flex-end" grow onClick={(e) => e.stopPropagation()}>
      <Select
        label={label}
        size="xs"
        value={from}
        data={INPUT_SOURCE_OPTIONS}
        onChange={(v) => {
          if (!v) return;
          const nextFrom = v as ExtractInputSource;
          onChange({ from: nextFrom, stepOrder: nextFrom === 'Step' ? (value?.stepOrder ?? null) : null });
        }}
      />
      {from === 'Step' && (
        <Select
          label="Step"
          size="xs"
          data={priorStepOptions}
          value={value?.stepOrder != null ? String(value.stepOrder) : null}
          onChange={(v) => onChange({ from: 'Step', stepOrder: v ? Number(v) : null })}
          placeholder={priorStepOptions.length > 0 ? 'Select a step' : 'No earlier steps'}
          disabled={priorStepOptions.length === 0}
        />
      )}
    </Group>
  );
}

interface ExtractStepConfigProps {
  config: ExtractStepConfigValue;
  onChange: (config: ExtractStepConfigValue) => void;
  allSteps?: StepData[];
  currentStepIndex?: number;
}

/**
 * Configuration form for a Extract step (deterministic, non-LLM projection).
 * Operations mirror the backend `ExtractStepConfig` record exactly: `project`, `resolve`, `files`.
 */
export function ExtractStepConfig({ config, onChange, allSteps = [], currentStepIndex = 0 }: ExtractStepConfigProps) {
  const [expectOpen, setExpectOpen] = useState(Boolean(config.expect));

  const priorStepOptions = allSteps
    .slice(0, currentStepIndex)
    .map((previousStep, index) => {
      const stepOrder = index + 1;
      const label = previousStep.label?.trim() || `Step ${stepOrder}`;
      return { value: String(stepOrder), label: `${stepOrder}. ${label}` };
    });

  const setInput = (name: string, ref: ExtractInputRef) => {
    onChange({ ...config, inputs: { ...config.inputs, [name]: ref } });
  };

  const patch = (updates: Partial<ExtractStepConfigValue>) => onChange({ ...config, ...updates });

  return (
    <Stack gap="md" onClick={(e) => e.stopPropagation()}>
      <Select
        label="Operation"
        description="What this Extract step does — a fixed set of deterministic, code-only operations (no model call)"
        size="sm"
        value={config.operation}
        data={[
          { value: 'Project', label: 'Project — reduce an array to a bounded, whitelisted view' },
          { value: 'Resolve', label: 'Resolve — look up full records for a list of ids, in order' },
          { value: 'Files', label: 'Files — list/summarize project files' },
        ]}
        onChange={(v) => {
          if (!v) return;
          onChange(createDefaultExtractStepConfig(v as ExtractOperation));
        }}
      />

      {config.operation === 'Project' && (
        <Stack gap="xs">
          <InputRefPicker
            label="Source"
            value={config.inputs.source}
            onChange={(ref) => setInput('source', ref)}
            priorStepOptions={priorStepOptions}
          />
          <TextInput
            label="Path"
            description="JSON path resolved against the source, e.g. $.components"
            placeholder="$.components"
            size="xs"
            value={config.path ?? ''}
            onChange={(e) => patch({ path: e.target.value || null })}
          />
          <TagsInput
            label="Fields"
            description="Whitelist of fields to keep per item — leave empty to keep the whole element"
            placeholder="name, filePath, responsibility"
            size="xs"
            value={config.fields ?? []}
            onChange={(fields) => patch({ fields: fields.length > 0 ? fields : null })}
          />
          <Group grow>
            <TextInput
              label="ID field"
              description='Element key used as a stable id; blank uses "{idPrefix}{index}"'
              placeholder="id"
              size="xs"
              value={config.idField ?? ''}
              onChange={(e) => patch({ idField: e.target.value || null })}
            />
            <TextInput
              label="ID prefix"
              size="xs"
              value={config.idPrefix}
              onChange={(e) => patch({ idPrefix: e.target.value || 'i' })}
            />
          </Group>
          <Group grow>
            <TextInput
              label="Sort by"
              placeholder="name"
              size="xs"
              value={config.sortBy ?? ''}
              onChange={(e) => patch({ sortBy: e.target.value || null })}
            />
            <NumberInput
              label="Skip"
              min={0}
              size="xs"
              value={config.skip}
              onChange={(v) => patch({ skip: typeof v === 'number' ? v : 0 })}
            />
            <NumberInput
              label="Take"
              min={1}
              size="xs"
              placeholder="No limit"
              value={config.take ?? undefined}
              onChange={(v) => patch({ take: typeof v === 'number' ? v : null })}
            />
          </Group>
        </Stack>
      )}

      {config.operation === 'Resolve' && (
        <Stack gap="xs">
          <InputRefPicker
            label="IDs"
            value={config.inputs.ids}
            onChange={(ref) => setInput('ids', ref)}
            priorStepOptions={priorStepOptions}
          />
          <TextInput
            label="IDs path"
            description="JSON path inside the ids input, e.g. $.selectedIds"
            placeholder="$.selectedIds"
            size="xs"
            value={config.idsPath ?? ''}
            onChange={(e) => patch({ idsPath: e.target.value || null })}
          />
          <Divider variant="dashed" />
          <InputRefPicker
            label="Records"
            value={config.inputs.records}
            onChange={(ref) => setInput('records', ref)}
            priorStepOptions={priorStepOptions}
          />
          <TextInput
            label="Records path"
            description="JSON path inside the records input, e.g. $.view.items"
            placeholder="$.view.items"
            size="xs"
            value={config.recordsPath ?? ''}
            onChange={(e) => patch({ recordsPath: e.target.value || null })}
          />
          <Select
            label="On unknown id"
            size="xs"
            value={config.onUnknownId}
            data={[
              { value: 'Fail', label: 'Fail the step' },
              { value: 'Skip', label: 'Skip the id' },
            ]}
            onChange={(v) => v && patch({ onUnknownId: v as ExtractUnknownIdBehaviour })}
          />
        </Stack>
      )}

      {config.operation === 'Files' && (
        <Stack gap="xs">
          <TagsInput
            label="Categories"
            placeholder="component, route, style"
            size="xs"
            value={config.categories ?? []}
            onChange={(v) => patch({ categories: v.length > 0 ? v : null })}
          />
          <TagsInput
            label="Include extensions"
            placeholder=".tsx, .ts"
            size="xs"
            value={config.includeExtensions ?? []}
            onChange={(v) => patch({ includeExtensions: v.length > 0 ? v : null })}
          />
          <TagsInput
            label="Exclude path contains"
            placeholder="node_modules, .test."
            size="xs"
            value={config.excludePathContains ?? []}
            onChange={(v) => patch({ excludePathContains: v.length > 0 ? v : null })}
          />
          <Group grow>
            <Switch
              label="Include summaries"
              checked={config.includeSummaries}
              onChange={(e) => patch({ includeSummaries: e.currentTarget.checked })}
            />
            <Switch
              label="Include content"
              checked={config.includeContent}
              onChange={(e) => patch({ includeContent: e.currentTarget.checked })}
            />
          </Group>
          <NumberInput
            label="Max chars per file"
            min={1}
            size="xs"
            value={config.maxCharsPerFile}
            onChange={(v) => patch({ maxCharsPerFile: typeof v === 'number' ? v : 2000 })}
          />
        </Stack>
      )}

      <NumberInput
        label="Max output chars"
        description="Hard cap on the serialized output; trailing items are dropped and re-serialized rather than truncated mid-JSON"
        min={256}
        max={200_000}
        size="sm"
        value={config.maxOutputChars}
        onChange={(v) => patch({ maxOutputChars: typeof v === 'number' ? v : 24_000 })}
      />

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
        <Stack gap="xs">
          <TagsInput
            label="Required paths"
            description="JSON paths that must be present in the output view"
            placeholder="$.view.items"
            size="xs"
            value={config.expect?.requiredPaths ?? []}
            onChange={(v) => patch({ expect: { ...config.expect, requiredPaths: v.length > 0 ? v : null } })}
          />
          <Group grow>
            <NumberInput
              label="Min items"
              min={0}
              size="xs"
              value={config.expect?.minItems ?? undefined}
              onChange={(v) => patch({ expect: { ...config.expect, minItems: typeof v === 'number' ? v : null } })}
            />
            <NumberInput
              label="Max items"
              min={0}
              size="xs"
              value={config.expect?.maxItems ?? undefined}
              onChange={(v) => patch({ expect: { ...config.expect, maxItems: typeof v === 'number' ? v : null } })}
            />
          </Group>
          <TagsInput
            label="Non-empty string paths"
            description="Paths that must resolve to a non-empty string"
            size="xs"
            value={config.expect?.nonEmptyStringPaths ?? []}
            onChange={(v) => patch({ expect: { ...config.expect, nonEmptyStringPaths: v.length > 0 ? v : null } })}
          />
        </Stack>
      </Collapse>

      <Divider />

      <Paper p="xs" withBorder>
        <Text size="xs" fw={600} c="dimmed" mb={4}>Output shape (always this envelope, always valid JSON)</Text>
        <Code block style={{ fontSize: 11 }}>
{`{
  "view": { "items": [ { "id": "c1", "name": "..." } ] },
  "meta": {
    "operation": "${config.operation.toLowerCase()}",
    "shape": "items",
    "itemCount": 40,
    "totalItemCount": 137,
    "truncated": true,
    "droppedItems": 97
  }
}`}
        </Code>
      </Paper>
    </Stack>
  );
}
