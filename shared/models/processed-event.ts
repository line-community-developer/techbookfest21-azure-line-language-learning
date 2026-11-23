import { z } from 'zod/v4';
import { SchemaVersion } from '../contracts/common';

export const ProcessedEvent = z.object({
  id: z.string(),
  userId: z.string(),
  schemaVersion: SchemaVersion,
  channel: z.string(),
  externalEventId: z.string(),
  resultRef: z.string(),
  createdAt: z.string().optional(),
  updatedAt: z.string().optional(),
});
export type ProcessedEvent = z.infer<typeof ProcessedEvent>;

export const partitionKey = '/userId';
export const displayName = 'Processed Event';
