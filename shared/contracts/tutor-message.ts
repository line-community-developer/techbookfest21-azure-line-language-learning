import { z } from 'zod/v4';

export const TutorCorrection = z.object({
  grammarTopicId: z.string(),
  incorrect: z.string(),
  suggestion: z.string(),
  explanationJa: z.string(),
});
export type TutorCorrection = z.infer<typeof TutorCorrection>;

export const ToolCallRecord = z.object({
  callId: z.string(),
  name: z.string(),
  status: z.enum(['succeeded', 'failed', 'rejected']),
  durationMs: z.number().min(0),
  errorCode: z.string().nullable(),
});
export type ToolCallRecord = z.infer<typeof ToolCallRecord>;

// Embedded inside TutorSession; not a standalone persistent entity.
export const TutorMessage = z.object({
  id: z.string(),
  sessionId: z.string(),
  role: z.enum(['learner', 'tutor']),
  text: z.string().min(1),
  externalEventId: z.string().nullable(),
  corrections: z.array(TutorCorrection),
  retrievedSourceIds: z.array(z.string()),
  toolCalls: z.array(ToolCallRecord),
  createdAt: z.string().optional(),
  updatedAt: z.string().optional(),
});
export type TutorMessage = z.infer<typeof TutorMessage>;

export const displayName = 'Tutor Message';
