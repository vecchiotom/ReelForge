'use client';

import { Stack, Button } from '@mantine/core';
import { IconPlus } from '@tabler/icons-react';
import { DndContext, closestCenter, KeyboardSensor, PointerSensor, useSensor, useSensors } from '@dnd-kit/core';
import type { DragEndEvent } from '@dnd-kit/core';
import { SortableContext, sortableKeyboardCoordinates, verticalListSortingStrategy, arrayMove } from '@dnd-kit/sortable';
import { StepCard } from './StepCard';

export interface StepData {
  id: string;
  label: string;
  agentDefinitionId: string;
}

interface WorkflowStepListProps {
  steps: StepData[];
  onChange: (steps: StepData[]) => void;
}

let nextId = 1;

export function WorkflowStepList({ steps, onChange }: WorkflowStepListProps) {
  const sensors = useSensors(
    useSensor(PointerSensor),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  );

  const handleDragEnd = (event: DragEndEvent) => {
    const { active, over } = event;
    if (over && active.id !== over.id) {
      const oldIndex = steps.findIndex((s) => s.id === active.id);
      const newIndex = steps.findIndex((s) => s.id === over.id);
      onChange(arrayMove(steps, oldIndex, newIndex));
    }
  };

  const addStep = () => {
    onChange([...steps, { id: `new-${nextId++}`, label: '', agentDefinitionId: '' }]);
  };

  const updateStep = (index: number, updates: Partial<StepData>) => {
    const newSteps = [...steps];
    newSteps[index] = { ...newSteps[index], ...updates };
    onChange(newSteps);
  };

  const removeStep = (index: number) => {
    onChange(steps.filter((_, i) => i !== index));
  };

  return (
    <Stack gap="sm">
      <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={handleDragEnd}>
        <SortableContext items={steps.map((s) => s.id)} strategy={verticalListSortingStrategy}>
          {steps.map((step, index) => (
            <StepCard
              key={step.id}
              id={step.id}
              label={step.label}
              agentDefinitionId={step.agentDefinitionId}
              onLabelChange={(label) => updateStep(index, { label })}
              onAgentChange={(agentDefinitionId) => updateStep(index, { agentDefinitionId })}
              onRemove={() => removeStep(index)}
            />
          ))}
        </SortableContext>
      </DndContext>
      <Button variant="outline" leftSection={<IconPlus size={16} />} onClick={addStep}>
        Add Step
      </Button>
    </Stack>
  );
}
