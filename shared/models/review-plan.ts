import { z } from 'zod/v4';
import { ReviewPriority, SchemaVersion } from '../contracts/common';

export const ReviewPlanItem = z.object({
  topicId: z.string(),
  priority: ReviewPriority,
  priorityScore: z.number().min(0).max(1),
  reasonJa: z.string(),
  evidenceObservationIds: z.array(z.string()),
});
export type ReviewPlanItem = z.infer<typeof ReviewPlanItem>;

export const ReviewPlan = z.object({
  id: z.string(),
  userId: z.string(),
  schemaVersion: SchemaVersion,
  sourceType: z.enum(['conversation', 'practice']),
  sourceId: z.string(),
  status: z.enum(['active', 'completed']),
  items: z.array(ReviewPlanItem).min(1),
  practiceSessionIds: z.array(z.string()),
  createdAt: z.string().optional(),
  updatedAt: z.string().optional(),
});
export type ReviewPlan = z.infer<typeof ReviewPlan>;

export const partitionKey = '/userId';
export const displayName = 'Review Plan';
