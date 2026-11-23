import { z } from 'zod/v4';
import {
  CefrLevel,
  Difficulty,
  MasteryStatus,
  ObservationCertainty,
  ObservationType,
  ProviderInfo,
  QuestionType,
  ReviewPriority,
} from './common';
import { GrammarTopic } from '../models/grammar-topic';
import { PracticeSummary } from '../models/practice-session';
import { TutorCorrection, ToolCallRecord } from './tutor-message';
import { WorkflowRun } from '../models/workflow-run';

// HTTP API contracts between the React SPA, the BFF and the Functions backend.
// The BFF validates requests and responses with these schemas; the backend validates its
// output against the exported JSON Schema in contract tests.

export const ComponentMode = z.object({
  component: z.enum(['ai-model', 'tutor-agent', 'retrieval', 'storage', 'identity', 'messaging', 'workflow']),
  mode: z.string(),
  isMock: z.boolean(),
  detail: z.string(),
});
export type ComponentMode = z.infer<typeof ComponentMode>;

export const SystemStatus = z.object({
  environment: z.enum(['local', 'cloud']),
  components: z.array(ComponentMode),
  learner: z.object({ userId: z.string(), learnerName: z.string(), identityMode: z.string() }),
});
export type SystemStatus = z.infer<typeof SystemStatus>;

export const TopicRef = z.object({
  topicId: z.string(),
  title: z.string(),
  titleJa: z.string(),
  level: CefrLevel,
});
export type TopicRef = z.infer<typeof TopicRef>;

export const MasteryView = TopicRef.extend({
  score: z.number().min(0).max(1),
  status: MasteryStatus,
  attempts: z.number().int(),
  correct: z.number().int(),
  conversationErrors: z.number().int(),
  lastObservedAt: z.string().nullable(),
});
export type MasteryView = z.infer<typeof MasteryView>;

export const ReviewPlanView = z.object({
  id: z.string(),
  sourceType: z.enum(['conversation', 'practice']),
  sourceId: z.string(),
  sourceLabel: z.string(),
  status: z.enum(['active', 'completed']),
  createdAt: z.string(),
  items: z.array(TopicRef.extend({
    priority: ReviewPriority,
    priorityScore: z.number(),
    reasonJa: z.string(),
    evidence: z.array(z.object({ observationId: z.string(), text: z.string(), certainty: ObservationCertainty })),
  })),
  practiceSessionId: z.string().nullable(),
  practiceSessionStatus: z.enum(['in-progress', 'completed']).nullable(),
});
export type ReviewPlanView = z.infer<typeof ReviewPlanView>;

export const Recommendation = z.object({
  mode: z.enum(['recommended', 'review']),
  topics: z.array(TopicRef),
  reasonJa: z.string(),
  reviewPlanId: z.string().nullable(),
  practiceSessionId: z.string().nullable(),
});
export type Recommendation = z.infer<typeof Recommendation>;

export const ActivityItem = z.object({
  kind: z.enum(['practice', 'conversation']),
  id: z.string(),
  title: z.string(),
  detail: z.string(),
  occurredAt: z.string(),
});
export type ActivityItem = z.infer<typeof ActivityItem>;

export const DashboardView = z.object({
  learner: z.object({
    learnerName: z.string(),
    currentLevel: CefrLevel,
    nativeLanguage: z.string(),
    targetLanguage: z.string(),
    learningGoals: z.array(z.string()),
  }),
  stats: z.object({ answered: z.number().int(), correct: z.number().int(), accuracy: z.number() }),
  recommendation: Recommendation,
  weakTopics: z.array(MasteryView),
  strongTopics: z.array(MasteryView),
  recentActivity: z.array(ActivityItem),
  reviewPlans: z.array(ReviewPlanView),
});
export type DashboardView = z.infer<typeof DashboardView>;

export const HistoryView = z.object({
  mastery: z.array(MasteryView),
  attempts: z.array(z.object({
    sessionId: z.string(),
    questionId: z.string(),
    topicId: z.string(),
    topicTitle: z.string(),
    prompt: z.string(),
    answer: z.string(),
    correctAnswer: z.string(),
    isCorrect: z.boolean(),
    answeredAt: z.string(),
  })),
  observations: z.array(z.object({
    id: z.string(),
    topicId: z.string().nullable(),
    topicTitle: z.string().nullable(),
    sourceType: z.enum(['practice', 'conversation']),
    observationType: ObservationType,
    certainty: ObservationCertainty,
    confidence: z.number(),
    evidenceText: z.string(),
    occurredAt: z.string(),
  })),
  sessions: z.array(z.object({
    id: z.string(),
    mode: z.enum(['recommended', 'topic', 'review']),
    status: z.enum(['in-progress', 'completed']),
    topics: z.array(TopicRef),
    createdAt: z.string(),
    completedAt: z.string().nullable(),
    summary: PracticeSummary.nullable(),
  })),
  reviewPriorities: z.array(TopicRef.extend({ priority: ReviewPriority, priorityScore: z.number(), reasonJa: z.string() })),
});
export type HistoryView = z.infer<typeof HistoryView>;

export const GrammarReference = TopicRef.extend({
  rules: z.array(z.object({ id: z.string(), summaryJa: z.string() })),
  example: z.object({ text: z.string(), translationJa: z.string() }).nullable(),
});
export type GrammarReference = z.infer<typeof GrammarReference>;

export const AnswerResult = z.object({
  questionId: z.string(),
  isCorrect: z.boolean(),
  feedbackCode: z.enum(['correct', 'incorrect', 'accent-mismatch']),
  submittedAnswer: z.string(),
  correctAnswer: z.string(),
  acceptedAnswers: z.array(z.string()),
  explanationJa: z.string(),
  grammarReferences: z.array(GrammarReference),
  mastery: z.object({ topicId: z.string(), before: z.number(), after: z.number() }),
  alreadyAnswered: z.boolean(),
});
export type AnswerResult = z.infer<typeof AnswerResult>;

// Pre-answer view of a question: no answer key, no explanation.
export const PracticeQuestionView = z.object({
  id: z.string(),
  order: z.number().int(),
  type: QuestionType,
  topic: TopicRef,
  difficulty: Difficulty,
  instructionJa: z.string(),
  prompt: z.string(),
  choices: z.array(z.string()),
  result: AnswerResult.nullable(),
});
export type PracticeQuestionView = z.infer<typeof PracticeQuestionView>;

export const PracticeSessionView = z.object({
  id: z.string(),
  mode: z.enum(['recommended', 'topic', 'review']),
  level: CefrLevel,
  status: z.enum(['in-progress', 'completed']),
  topics: z.array(TopicRef),
  reviewPlanId: z.string().nullable(),
  questions: z.array(PracticeQuestionView),
  summary: PracticeSummary.extend({
    byTopic: z.array(TopicRef.extend({ total: z.number().int(), correct: z.number().int(), masteryAfter: z.number() })),
  }).nullable(),
  generatedBy: ProviderInfo,
  createdAt: z.string(),
});
export type PracticeSessionView = z.infer<typeof PracticeSessionView>;

export const StartPracticeRequest = z.object({
  mode: z.enum(['recommended', 'topic', 'review']),
  topicIds: z.array(z.string()).max(3).optional(),
  level: CefrLevel.optional(),
  reviewPlanId: z.string().optional(),
  questionCount: z.number().int().min(3).max(10).optional(),
});
export type StartPracticeRequest = z.infer<typeof StartPracticeRequest>;

export const SubmitAnswerRequest = z.object({
  questionId: z.string().min(1),
  answer: z.string().max(200),
  attemptId: z.string().max(100).optional(),
});
export type SubmitAnswerRequest = z.infer<typeof SubmitAnswerRequest>;

export const GrammarListView = z.object({ topics: z.array(GrammarTopic) });
export type GrammarListView = z.infer<typeof GrammarListView>;

export const GrammarSearchView = z.object({
  query: z.string(),
  retriever: z.string(),
  results: z.array(z.object({
    topic: GrammarTopic,
    score: z.number(),
    matchedTerms: z.array(z.string()),
    sourceIds: z.array(z.string()),
  })),
});
export type GrammarSearchView = z.infer<typeof GrammarSearchView>;

export const TutorMessageView = z.object({
  id: z.string(),
  role: z.enum(['learner', 'tutor']),
  text: z.string(),
  createdAt: z.string(),
  corrections: z.array(TutorCorrection),
  retrievedSourceIds: z.array(z.string()),
  toolCalls: z.array(ToolCallRecord),
});
export type TutorMessageView = z.infer<typeof TutorMessageView>;

export const TutorSessionView = z.object({
  id: z.string(),
  tutor: z.object({ id: z.string(), name: z.string(), avatarEmoji: z.string() }),
  channel: z.enum(['local-simulator', 'line', 'demo-seed']),
  status: z.enum(['active', 'ended']),
  theme: z.string(),
  focusTopics: z.array(TopicRef),
  messages: z.array(TutorMessageView),
  agent: ProviderInfo,
  analysisWorkflowRunId: z.string().nullable(),
  createdAt: z.string(),
});
export type TutorSessionView = z.infer<typeof TutorSessionView>;

export const TutorSessionListView = z.object({
  sessions: z.array(TutorSessionView.omit({ messages: true }).extend({ messageCount: z.number().int() })),
});
export type TutorSessionListView = z.infer<typeof TutorSessionListView>;

export const StartTutorSessionRequest = z.object({
  theme: z.string().max(100).optional(),
});
export type StartTutorSessionRequest = z.infer<typeof StartTutorSessionRequest>;

export const SendTutorMessageRequest = z.object({
  eventId: z.string().min(1).max(100),
  text: z.string().trim().min(1).max(500),
});
export type SendTutorMessageRequest = z.infer<typeof SendTutorMessageRequest>;

export const SendTutorMessageResponse = z.object({
  session: TutorSessionView,
  replies: z.array(TutorMessageView),
  duplicate: z.boolean(),
});
export type SendTutorMessageResponse = z.infer<typeof SendTutorMessageResponse>;

export const EndTutorSessionResponse = z.object({
  session: TutorSessionView,
  workflowRun: WorkflowRun,
});
export type EndTutorSessionResponse = z.infer<typeof EndTutorSessionResponse>;

export const ApiError = z.object({
  error: z.string(),
  code: z.string().optional(),
  details: z.unknown().optional(),
});
export type ApiError = z.infer<typeof ApiError>;
