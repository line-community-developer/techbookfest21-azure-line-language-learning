import { z } from 'zod/v4';
import { CefrLevel, Difficulty, QuestionType } from './common';

// Structured Outputs contracts. Every property is required (nullable instead of optional) so the
// exported JSON Schema can be sent as a strict json_schema response format to Foundry models.

export const GeneratedQuestion = z.object({
  type: QuestionType,
  grammarTopicId: z.string(),
  difficulty: Difficulty,
  instructionJa: z.string(),
  prompt: z.string(),
  choices: z.array(z.string()),
  correctAnswer: z.string(),
  acceptedAnswers: z.array(z.string()),
  explanationJa: z.string(),
  ruleIds: z.array(z.string()),
  sourceIds: z.array(z.string()),
});
export type GeneratedQuestion = z.infer<typeof GeneratedQuestion>;

export const GeneratedQuestionBatch = z.object({
  questions: z.array(GeneratedQuestion),
});
export type GeneratedQuestionBatch = z.infer<typeof GeneratedQuestionBatch>;

export const ConversationFinding = z.object({
  kind: z.enum(['grammar-error', 'possible-misunderstanding', 'correct-use', 'new-expression', 'needs-review']),
  grammarTopicId: z.string().nullable(),
  messageId: z.string(),
  evidenceText: z.string(),
  correction: z.string().nullable(),
  explanationJa: z.string(),
  confidence: z.number().min(0).max(1),
});
export type ConversationFinding = z.infer<typeof ConversationFinding>;

export const ConversationAnalysis = z.object({
  findings: z.array(ConversationFinding),
  summaryJa: z.string(),
});
export type ConversationAnalysis = z.infer<typeof ConversationAnalysis>;

export const QuestionJudgement = z.object({
  verdict: z.enum(['pass', 'fail', 'uncertain']),
  score: z.number().min(0).max(1),
  issues: z.array(z.object({ code: z.string(), messageJa: z.string() })),
  rationaleJa: z.string(),
});
export type QuestionJudgement = z.infer<typeof QuestionJudgement>;

export const QuestionGenerationRequest = z.object({
  targetLanguage: z.string(),
  level: CefrLevel,
  topicIds: z.array(z.string()).min(1),
  questionCount: z.number().int().min(1).max(10),
  purpose: z.enum(['practice', 'review']),
});
export type QuestionGenerationRequest = z.infer<typeof QuestionGenerationRequest>;
