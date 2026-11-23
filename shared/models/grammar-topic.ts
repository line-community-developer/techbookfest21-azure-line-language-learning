import { z } from 'zod/v4';
import { CefrLevel, SchemaVersion, TargetLanguage } from '../contracts/common';

export const GrammarRule = z.object({
  id: z.string(),
  summaryJa: z.string().min(1),
  pattern: z.string().nullable(),
});
export type GrammarRule = z.infer<typeof GrammarRule>;

export const GrammarExample = z.object({
  id: z.string(),
  text: z.string().min(1),
  translationJa: z.string().min(1),
  noteJa: z.string().nullable(),
});
export type GrammarExample = z.infer<typeof GrammarExample>;

export const CommonMistake = z.object({
  id: z.string(),
  incorrect: z.string().min(1),
  correct: z.string().min(1),
  explanationJa: z.string().min(1),
});
export type CommonMistake = z.infer<typeof CommonMistake>;

export const SourceReference = z.object({
  id: z.string(),
  kind: z.enum(['original-note', 'textbook', 'web', 'native-review']),
  label: z.string(),
  url: z.string().nullable(),
});
export type SourceReference = z.infer<typeof SourceReference>;

// Grammar master data stored in Cosmos DB; Foundry IQ references these topic IDs.
export const GrammarTopic = z.object({
  id: z.string(),
  language: TargetLanguage,
  schemaVersion: SchemaVersion,
  title: z.string().min(1),
  titleJa: z.string().min(1),
  level: CefrLevel,
  category: z.enum(['article', 'preposition', 'verb-pattern', 'tense', 'pronoun', 'construction']),
  description: z.string().min(1),
  usageJa: z.string().min(1),
  rules: z.array(GrammarRule).min(1),
  examples: z.array(GrammarExample).min(1),
  commonMistakes: z.array(CommonMistake),
  relatedTopics: z.array(z.string()),
  keywords: z.array(z.string()),
  sourceReferences: z.array(SourceReference).min(1),
  reviewStatus: z.enum(['unreviewed', 'reviewed']),
  reviewNoteJa: z.string().nullable(),
  createdAt: z.string().optional(),
  updatedAt: z.string().optional(),
});
export type GrammarTopic = z.infer<typeof GrammarTopic>;

export const partitionKey = '/language';
export const displayName = 'Grammar Topic';
export const authPolicy = { policy: 'adminOnly' };
