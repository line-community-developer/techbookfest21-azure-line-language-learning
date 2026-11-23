import { z } from 'zod/v4';
import { GeneratedQuestion } from './ai';
import { ProviderInfo } from './common';

export const AdminQuestionGenerationRequest = z.object({
  topicId: z.string().min(1).max(120),
  questionCount: z.number().int().min(1).max(10),
});
export type AdminQuestionGenerationRequest = z.infer<typeof AdminQuestionGenerationRequest>;

const AdminQuestionCandidate = z.object({
  question: GeneratedQuestion,
  generationId: z.string(),
  provider: ProviderInfo,
});

export const AdminQuestionValidationRequest = AdminQuestionCandidate.pick({ question: true, generationId: true })
  .extend(AdminQuestionGenerationRequest.pick({ topicId: true }).shape);

export const AdminQuestionPublishRequest = AdminQuestionCandidate.omit({ generationId: true })
  .extend(AdminQuestionGenerationRequest.pick({ topicId: true }).shape);

export const AdminQuestionPublishResponse = z.object({ id: z.string() });

export const AdminQuestionGenerationResponse = z.object({
  topicId: z.string(),
  questions: z.array(AdminQuestionCandidate),
});
export type AdminQuestionGenerationResponse = z.infer<typeof AdminQuestionGenerationResponse>;
