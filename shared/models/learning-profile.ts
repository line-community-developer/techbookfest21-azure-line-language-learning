import { z } from 'zod/v4';
import { CefrLevel, LanguageCode, MasteryStatus, SchemaVersion, TargetLanguage } from '../contracts/common';

export const TopicMastery = z.object({
  topicId: z.string(),
  score: z.number().min(0).max(1),
  status: MasteryStatus,
  attempts: z.number().int().min(0),
  correct: z.number().int().min(0),
  conversationErrors: z.number().int().min(0),
  conversationCorrectUses: z.number().int().min(0),
  lastObservedAt: z.string().nullable(),
});
export type TopicMastery = z.infer<typeof TopicMastery>;

// Read model projected from LearningObservation documents; never edited directly by clients.
export const LearningProfile = z.object({
  id: z.string(),
  userId: z.string(),
  schemaVersion: SchemaVersion,
  learnerName: z.string().min(1),
  nativeLanguage: LanguageCode,
  targetLanguage: TargetLanguage,
  currentLevel: CefrLevel,
  learningGoals: z.array(z.string()),
  masteryByTopic: z.array(z.object({
    key: z.string(),
    value: TopicMastery,
  })),
  weakTopics: z.array(z.string()),
  strongTopics: z.array(z.string()),
  observationCount: z.number().int().min(0),
  createdAt: z.string().optional(),
  updatedAt: z.string().optional(),
});
export type LearningProfile = z.infer<typeof LearningProfile>;

export const partitionKey = '/userId';
export const displayName = 'Learning Profile';
