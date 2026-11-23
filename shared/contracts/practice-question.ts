import { z } from 'zod/v4';
import { Difficulty, ProviderInfo, QuestionType } from './common';

export const QuestionValidationCheck = z.object({
  id: z.string(),
  kind: z.enum(['deterministic', 'semantic']),
  passed: z.boolean(),
  severity: z.enum(['error', 'warning']),
  messageJa: z.string(),
});
export type QuestionValidationCheck = z.infer<typeof QuestionValidationCheck>;

export const SemanticJudgement = z.object({
  verdict: z.enum(['pass', 'fail', 'uncertain']),
  score: z.number().min(0).max(1),
  rationaleJa: z.string(),
  evaluator: ProviderInfo,
});
export type SemanticJudgement = z.infer<typeof SemanticJudgement>;

export const QuestionValidation = z.object({
  status: z.enum(['passed', 'failed']),
  checks: z.array(QuestionValidationCheck),
  semantic: SemanticJudgement.nullable(),
  evaluatedAt: z.string(),
});
export type QuestionValidation = z.infer<typeof QuestionValidation>;

// Stored inside PracticeSession. Contains the answer key, so it must never be returned to clients before answering.
export const PracticeQuestion = z.object({
  id: z.string(),
  sessionId: z.string(),
  order: z.number().int().min(0),
  type: QuestionType,
  grammarTopicId: z.string(),
  difficulty: Difficulty,
  instructionJa: z.string().min(1),
  prompt: z.string().min(1),
  choices: z.array(z.string()),
  correctAnswer: z.string().min(1),
  acceptedAnswers: z.array(z.string().min(1)).min(1),
  explanationJa: z.string().min(1),
  ruleIds: z.array(z.string()),
  sourceIds: z.array(z.string()).min(1),
  generatedBy: ProviderInfo,
  generationId: z.string(),
  validation: QuestionValidation,
  createdAt: z.string().optional(),
  updatedAt: z.string().optional(),
});
export type PracticeQuestion = z.infer<typeof PracticeQuestion>;

export const displayName = 'Practice Question';
