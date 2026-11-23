import { z } from 'zod/v4';
import { ProviderInfo, SchemaVersion } from '../contracts/common';
import { TutorMessage } from '../contracts/tutor-message';

export const TutorSession = z.object({
  id: z.string(),
  userId: z.string(),
  schemaVersion: SchemaVersion,
  tutorProfileId: z.string(),
  channel: z.enum(['local-simulator', 'line', 'demo-seed']),
  theme: z.string(),
  focusTopicIds: z.array(z.string()),
  status: z.enum(['active', 'ended']),
  messages: z.array(TutorMessage),
  agent: ProviderInfo,
  endedAt: z.string().nullable(),
  analysisWorkflowRunId: z.string().nullable(),
  createdAt: z.string().optional(),
  updatedAt: z.string().optional(),
});
export type TutorSession = z.infer<typeof TutorSession>;

export const partitionKey = '/userId';
export const displayName = 'Tutor Session';
