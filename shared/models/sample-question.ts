import { z } from 'zod/v4';
import { Difficulty, ProviderInfo, QuestionType, SchemaVersion, TargetLanguage } from '../contracts/common';
import { QuestionValidation } from '../contracts/practice-question';

// Keep the nested publication schema separate so scaffold can resolve its fields
// without treating the provider/validation children as top-level model fields.
const QuestionPublication = z.object({
  instructionJa: z.string().min(1),
  sourceIds: z.array(z.string()).min(1),
  provider: ProviderInfo,
  validation: QuestionValidation,
});

export const SampleQuestion = z.object({
  id: z.string(),
  language: TargetLanguage,
  schemaVersion: SchemaVersion,
  grammarTopicId: z.string(),
  type: QuestionType,
  difficulty: Difficulty,
  prompt: z.string().min(1),
  choices: z.array(z.string()),
  correctAnswer: z.string().min(1),
  acceptedAnswers: z.array(z.string()).min(1),
  explanationJa: z.string().min(1),
  ruleIds: z.array(z.string()).min(1),
  publication: QuestionPublication.optional(),
  createdAt: z.string().optional(),
  updatedAt: z.string().optional(),
});
export type SampleQuestion = z.infer<typeof SampleQuestion>;

export const partitionKey = '/language';
export const displayName = 'Sample Question';
export const authPolicy = { policy: 'adminOnly' };
