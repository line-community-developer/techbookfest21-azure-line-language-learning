import { z } from 'zod/v4';
import { CefrLevel, SchemaVersion } from '../contracts/common';
import { PracticeQuestion } from '../contracts/practice-question';

export const PracticeAnswer = z.object({
  questionId: z.string(),
  attemptId: z.string(),
  answer: z.string(),
  normalizedAnswer: z.string(),
  isCorrect: z.boolean(),
  feedbackCode: z.enum(['correct', 'incorrect', 'accent-mismatch']),
  masteryBefore: z.number().min(0).max(1),
  masteryAfter: z.number().min(0).max(1),
  answeredAt: z.string(),
});
export type PracticeAnswer = z.infer<typeof PracticeAnswer>;

export const PracticeSummary = z.object({
  total: z.number().int().min(0),
  answered: z.number().int().min(0),
  correct: z.number().int().min(0),
  accuracy: z.number().min(0).max(1),
});
export type PracticeSummary = z.infer<typeof PracticeSummary>;

export const PracticeSession = z.object({
  id: z.string(),
  userId: z.string(),
  schemaVersion: SchemaVersion,
  mode: z.enum(['recommended', 'topic', 'review']),
  level: CefrLevel,
  targetTopicIds: z.array(z.string()).min(1),
  reviewPlanId: z.string().nullable(),
  origin: z.object({
    kind: z.enum(['learner', 'review-workflow', 'tutor-tool', 'demo-seed']),
    refId: z.string().nullable(),
  }),
  status: z.enum(['in-progress', 'completed']),
  questions: z.array(PracticeQuestion).min(1),
  answers: z.array(PracticeAnswer),
  summary: PracticeSummary.nullable(),
  completedAt: z.string().nullable(),
  createdAt: z.string().optional(),
  updatedAt: z.string().optional(),
});
export type PracticeSession = z.infer<typeof PracticeSession>;

export const partitionKey = '/userId';
export const displayName = 'Practice Session';
