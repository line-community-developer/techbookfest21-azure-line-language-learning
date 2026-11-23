import { z } from 'zod/v4';
import { ObservationCertainty, ObservationType, SchemaVersion } from '../contracts/common';

// One learning-relevant fact about the learner. IDs are deterministic per source item so replays never double count.
export const LearningObservation = z.object({
  id: z.string(),
  userId: z.string(),
  schemaVersion: SchemaVersion,
  grammarTopicId: z.string().nullable(),
  sourceType: z.enum(['practice', 'conversation']),
  sourceId: z.string(),
  sourceItemId: z.string(),
  observationType: ObservationType,
  certainty: ObservationCertainty,
  confidence: z.number().min(0).max(1),
  evidence: z.object({
    text: z.string(),
    expected: z.string().nullable(),
    actual: z.string().nullable(),
  }),
  detectedBy: z.string(),
  occurredAt: z.string(),
  createdAt: z.string().optional(),
  updatedAt: z.string().optional(),
});
export type LearningObservation = z.infer<typeof LearningObservation>;

export const partitionKey = '/userId';
export const displayName = 'Learning Observation';
