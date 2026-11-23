import { z } from 'zod/v4';
import { SchemaVersion } from '../contracts/common';

export const WorkflowStatus = z.enum(['pending', 'running', 'completed', 'failed']);
export type WorkflowStatus = z.infer<typeof WorkflowStatus>;

export const WorkflowStepState = z.object({
  name: z.string(),
  status: z.enum(['pending', 'running', 'completed', 'failed', 'skipped']),
  attempts: z.number().int().min(0),
  startedAt: z.string().nullable(),
  completedAt: z.string().nullable(),
  // Step outputs (ids, counts, serialized intermediate results) needed to resume from the failed step.
  output: z.array(z.object({
    key: z.string(),
    value: z.string(),
  })),
  error: z.object({ code: z.string(), message: z.string() }).nullable(),
});
export type WorkflowStepState = z.infer<typeof WorkflowStepState>;

export const WorkflowRun = z.object({
  id: z.string(),
  userId: z.string(),
  schemaVersion: SchemaVersion,
  workflowName: z.string(),
  subjectId: z.string(),
  status: WorkflowStatus,
  runCount: z.number().int().min(0),
  steps: z.array(WorkflowStepState),
  error: z.object({ code: z.string(), message: z.string() }).nullable(),
  createdAt: z.string().optional(),
  updatedAt: z.string().optional(),
});
export type WorkflowRun = z.infer<typeof WorkflowRun>;

export const partitionKey = '/userId';
export const displayName = 'Workflow Run';
