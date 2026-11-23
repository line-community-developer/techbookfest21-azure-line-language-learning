import { z } from 'zod/v4';

// Bump when a persisted document shape changes incompatibly.
export const SCHEMA_VERSION = 1 as const;
export const SchemaVersion = z.literal(SCHEMA_VERSION);

export const LanguageCode = z.enum(['ja', 'it', 'en']);
export type LanguageCode = z.infer<typeof LanguageCode>;

// Languages that can be learned. Reference data for enabled languages lives in Cosmos DB.
export const TargetLanguage = z.enum(['it', 'en']);
export type TargetLanguage = z.infer<typeof TargetLanguage>;

export const CefrLevel = z.enum(['A1', 'A2', 'B1', 'B2', 'C1', 'C2']);
export type CefrLevel = z.infer<typeof CefrLevel>;

export const QuestionType = z.enum(['multiple-choice', 'cloze']);
export type QuestionType = z.infer<typeof QuestionType>;

export const Difficulty = z.number().int().min(1).max(5);

export const MasteryStatus = z.enum(['unknown', 'weak', 'developing', 'strong']);
export type MasteryStatus = z.infer<typeof MasteryStatus>;

export const ObservationType = z.enum([
  'correct-answer',
  'incorrect-answer',
  'grammar-error',
  'possible-misunderstanding',
  'correct-use',
  'new-expression',
]);
export type ObservationType = z.infer<typeof ObservationType>;

// deterministic: established by application logic (e.g. graded answer).
// ai-inferred: proposed by a model and accepted only after evidence validation.
export const ObservationCertainty = z.enum(['deterministic', 'ai-inferred']);
export type ObservationCertainty = z.infer<typeof ObservationCertainty>;

export const ReviewPriority = z.enum(['high', 'medium', 'low']);
export type ReviewPriority = z.infer<typeof ReviewPriority>;

export const ProviderInfo = z.object({
  kind: z.enum(['mock', 'foundry', 'unavailable']),
  model: z.string(),
});
export type ProviderInfo = z.infer<typeof ProviderInfo>;
