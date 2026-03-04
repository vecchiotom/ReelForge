'use client';

import { useState } from 'react';
import { Modal, TextInput, Textarea, Button, Stack } from '@mantine/core';
import { useForm } from '@mantine/form';
import { notifications } from '@mantine/notifications';
import { createProject, updateProject } from '@/lib/api/projects';
import type { Project } from '@/lib/types/project';

interface ProjectFormProps {
  opened: boolean;
  onClose: () => void;
  onSuccess: () => void;
  project?: Project;
}

export function ProjectForm({ opened, onClose, onSuccess, project }: ProjectFormProps) {
  const [loading, setLoading] = useState(false);
  const isEdit = !!project;

  const form = useForm({
    initialValues: {
      name: project?.name || '',
      description: project?.description || '',
    },
    validate: {
      name: (v) => (!v.trim() ? 'Name is required' : null),
    },
  });

  const handleSubmit = form.onSubmit(async (values) => {
    setLoading(true);
    try {
      if (isEdit) {
        await updateProject(project.id, values);
        notifications.show({ title: 'Updated', message: 'Project updated successfully', color: 'green' });
      } else {
        await createProject(values);
        notifications.show({ title: 'Created', message: 'Project created successfully', color: 'green' });
      }
      form.reset();
      onSuccess();
      onClose();
    } catch (err: unknown) {
      notifications.show({
        title: 'Error',
        message: err instanceof Error ? err.message : 'Operation failed',
        color: 'red',
      });
    } finally {
      setLoading(false);
    }
  });

  return (
    <Modal opened={opened} onClose={onClose} title={isEdit ? 'Edit Project' : 'Create Project'} centered>
      <form onSubmit={handleSubmit}>
        <Stack>
          <TextInput label="Name" placeholder="My Project" {...form.getInputProps('name')} />
          <Textarea label="Description" placeholder="Optional description" rows={3} {...form.getInputProps('description')} />
          <Button type="submit" loading={loading} fullWidth>
            {isEdit ? 'Update' : 'Create'}
          </Button>
        </Stack>
      </form>
    </Modal>
  );
}
