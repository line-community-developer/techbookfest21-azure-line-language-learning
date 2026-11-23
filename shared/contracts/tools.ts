import { z } from 'zod/v4';
import { CefrLevel, ObservationCertainty, ObservationType, ReviewPriority, TargetLanguage } from './common';

// Tutor agent tool contracts. Inputs intentionally have no userId: the learner is always taken from the
// server-side authentication context, so a model cannot address another learner's data.

const TopicScore = z.object({ topicId: z.string(), title: z.string(), score: z.number() });

export const GetLearningProfileInput = z.object({});
export const GetLearningProfileOutput = z.object({
  learnerName: z.string(),
  currentLevel: CefrLevel,
  nativeLanguage: z.string(),
  targetLanguage: TargetLanguage,
  learningGoals: z.array(z.string()),
  weakTopics: z.array(TopicScore),
  strongTopics: z.array(TopicScore),
});

export const GetRecentMistakesInput = z.object({
  limit: z.number().int().min(1).max(20),
});
export const GetRecentMistakesOutput = z.object({
  mistakes: z.array(z.object({
    observationId: z.string(),
    topicId: z.string(),
    title: z.string(),
    sourceType: z.enum(['practice', 'conversation']),
    certainty: ObservationCertainty,
    evidenceText: z.string(),
    occurredAt: z.string(),
  })),
});

export const SearchGrammarRulesInput = z.object({
  query: z.string().min(1).max(200),
  topicId: z.string().nullable(),
  level: CefrLevel.nullable(),
  limit: z.number().int().min(1).max(5),
});
export const SearchGrammarRulesOutput = z.object({
  results: z.array(z.object({
    topicId: z.string(),
    title: z.string(),
    level: CefrLevel,
    summaryJa: z.string(),
    rules: z.array(z.object({ id: z.string(), summaryJa: z.string() })),
    examples: z.array(z.object({ text: z.string(), translationJa: z.string() })),
    score: z.number(),
    sourceIds: z.array(z.string()),
  })),
});

export const RecordLearningObservationInput = z.object({
  messageId: z.string(),
  grammarTopicId: z.string(),
  observationType: ObservationType.extract(['grammar-error', 'possible-misunderstanding', 'correct-use']),
  evidenceText: z.string().min(1).max(300),
  correction: z.string().nullable(),
  confidence: z.number().min(0).max(1),
});
export const RecordLearningObservationOutput = z.object({
  observationId: z.string(),
  created: z.boolean(),
});

export const CreatePracticeSessionInput = z.object({
  topicIds: z.array(z.string()).min(1).max(3),
  questionCount: z.number().int().min(3).max(8),
});
export const CreatePracticeSessionOutput = z.object({
  practiceSessionId: z.string(),
  questionCount: z.number().int(),
  topicIds: z.array(z.string()),
  created: z.boolean(),
});

export const GetReviewPlanInput = z.object({});
export const GetReviewPlanOutput = z.object({
  reviewPlan: z.object({
    id: z.string(),
    items: z.array(z.object({ topicId: z.string(), title: z.string(), priority: ReviewPriority, reasonJa: z.string() })),
    practiceSessionIds: z.array(z.string()),
  }).nullable(),
});

export const ToolSchemas = {
  get_learning_profile: { input: GetLearningProfileInput, output: GetLearningProfileOutput },
  get_recent_mistakes: { input: GetRecentMistakesInput, output: GetRecentMistakesOutput },
  search_grammar_rules: { input: SearchGrammarRulesInput, output: SearchGrammarRulesOutput },
  record_learning_observation: { input: RecordLearningObservationInput, output: RecordLearningObservationOutput },
  create_practice_session: { input: CreatePracticeSessionInput, output: CreatePracticeSessionOutput },
  get_review_plan: { input: GetReviewPlanInput, output: GetReviewPlanOutput },
} as const;
