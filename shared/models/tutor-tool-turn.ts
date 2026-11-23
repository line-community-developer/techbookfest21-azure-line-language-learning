import { z } from 'zod/v4';
import { SchemaVersion } from '../contracts/common';
import { ToolCallRecord } from '../contracts/tutor-message';

export const TutorToolTurn = z.object({
  id: z.string(),
  userId: z.string(),
  schemaVersion: SchemaVersion,
  sessionId: z.string(),
  learnerMessageId: z.string(),
  expiresAt: z.iso.datetime({ offset: true }),
  closed: z.boolean(),
  maxCalls: z.number().int().min(1).max(32),
  calls: z.array(z.object({
    id: z.string(),
    name: z.string(),
    completed: z.boolean(),
    record: ToolCallRecord.nullable(),
    sourceIds: z.array(z.string()),
  })).max(32),
  createdAt: z.string().optional(),
  updatedAt: z.string().optional(),
});
export type TutorToolTurn = z.infer<typeof TutorToolTurn>;

export const partitionKey = '/userId';
export const displayName = 'Tutor Tool Turn';
export const references = { sessionId: 'TutorSession', learnerMessageId: false };
