import { z } from 'zod/v4';
import { SchemaVersion, TargetLanguage } from '../contracts/common';

// Tutor character. Prompt text lives in versioned templates referenced by promptTemplateId.
export const TutorProfile = z.object({
  id: z.string(),
  language: TargetLanguage,
  schemaVersion: SchemaVersion,
  name: z.string().min(1),
  avatarEmoji: z.string(),
  personaJa: z.string(),
  speakingStyle: z.string(),
  greeting: z.string().min(1),
  correctionPolicy: z.object({
    maxCorrectionsPerTurn: z.number().int().min(0).max(3),
    style: z.enum(['recast', 'explicit']),
    explainInJapanese: z.boolean(),
  }),
  promptTemplateId: z.string(),
  createdAt: z.string().optional(),
  updatedAt: z.string().optional(),
});
export type TutorProfile = z.infer<typeof TutorProfile>;

export const partitionKey = '/language';
export const displayName = 'Tutor Profile';
export const authPolicy = { policy: 'adminOnly' };
