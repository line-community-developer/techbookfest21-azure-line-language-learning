using LineLanguageLearn.Learning.Domain;

namespace LineLanguageLearn.Learning.Api;

// C# shapes of shared/contracts/api.ts. Contract tests serialize these and validate them against the
// exported JSON Schemas, so a field added on only one side fails the build's test step.

public sealed record ComponentMode(string Component, string Mode, bool IsMock, string Detail);
public sealed record LearnerIdentityView(string UserId, string LearnerName, string IdentityMode);
public sealed record SystemStatusView(string Environment, List<ComponentMode> Components, LearnerIdentityView Learner);

public sealed record TopicRef(string TopicId, string Title, string TitleJa, CefrLevel Level);

public sealed record MasteryView(
    string TopicId, string Title, string TitleJa, CefrLevel Level,
    double Score, MasteryStatus Status, int Attempts, int Correct, int ConversationErrors, string? LastObservedAt);

public sealed record EvidenceView(string ObservationId, string Text, ObservationCertainty Certainty);

public sealed record ReviewPlanItemView(
    string TopicId, string Title, string TitleJa, CefrLevel Level,
    ReviewPriority Priority, double PriorityScore, string ReasonJa, List<EvidenceView> Evidence);

public sealed record ReviewPlanView(
    string Id,
    ObservationSource SourceType,
    string SourceId,
    string SourceLabel,
    ReviewPlanStatus Status,
    string CreatedAt,
    List<ReviewPlanItemView> Items,
    string? PracticeSessionId,
    PracticeStatus? PracticeSessionStatus);

public sealed record RecommendationView(string Mode, List<TopicRef> Topics, string ReasonJa, string? ReviewPlanId, string? PracticeSessionId);

public sealed record ActivityItem(string Kind, string Id, string Title, string Detail, string OccurredAt);

public sealed record DashboardLearner(string LearnerName, CefrLevel CurrentLevel, string NativeLanguage, string TargetLanguage, List<string> LearningGoals);
public sealed record DashboardStats(int Answered, int Correct, double Accuracy);

public sealed record DashboardView(
    DashboardLearner Learner,
    DashboardStats Stats,
    RecommendationView Recommendation,
    List<MasteryView> WeakTopics,
    List<MasteryView> StrongTopics,
    List<ActivityItem> RecentActivity,
    List<ReviewPlanView> ReviewPlans);

public sealed record AttemptView(string SessionId, string QuestionId, string TopicId, string TopicTitle, string Prompt, string Answer, string CorrectAnswer, bool IsCorrect, string AnsweredAt);

public sealed record ObservationView(
    string Id, string? TopicId, string? TopicTitle, ObservationSource SourceType, ObservationType ObservationType,
    ObservationCertainty Certainty, double Confidence, string EvidenceText, string OccurredAt);

public sealed record SessionHistoryView(string Id, PracticeMode Mode, PracticeStatus Status, List<TopicRef> Topics, string CreatedAt, string? CompletedAt, PracticeSummary? Summary);

public sealed record ReviewPriorityView(string TopicId, string Title, string TitleJa, CefrLevel Level, ReviewPriority Priority, double PriorityScore, string ReasonJa);

public sealed record HistoryView(
    List<MasteryView> Mastery,
    List<AttemptView> Attempts,
    List<ObservationView> Observations,
    List<SessionHistoryView> Sessions,
    List<ReviewPriorityView> ReviewPriorities);

public sealed record RuleView(string Id, string SummaryJa);
public sealed record ExampleView(string Text, string TranslationJa);

public sealed record GrammarReferenceView(string TopicId, string Title, string TitleJa, CefrLevel Level, List<RuleView> Rules, ExampleView? Example);

public sealed record MasteryChange(string TopicId, double Before, double After);

public sealed record AnswerResultView(
    string QuestionId,
    bool IsCorrect,
    FeedbackCode FeedbackCode,
    string SubmittedAnswer,
    string CorrectAnswer,
    List<string> AcceptedAnswers,
    string ExplanationJa,
    List<GrammarReferenceView> GrammarReferences,
    MasteryChange Mastery,
    bool AlreadyAnswered);

public sealed record PracticeQuestionView(
    string Id, int Order, QuestionType Type, TopicRef Topic, int Difficulty, string InstructionJa, string Prompt, List<string> Choices, AnswerResultView? Result);

public sealed record TopicResultView(string TopicId, string Title, string TitleJa, CefrLevel Level, int Total, int Correct, double MasteryAfter);

public sealed record PracticeSummaryView(int Total, int Answered, int Correct, double Accuracy, List<TopicResultView> ByTopic);

public sealed record PracticeSessionView(
    string Id,
    PracticeMode Mode,
    CefrLevel Level,
    PracticeStatus Status,
    List<TopicRef> Topics,
    string? ReviewPlanId,
    List<PracticeQuestionView> Questions,
    PracticeSummaryView? Summary,
    ProviderInfo GeneratedBy,
    string CreatedAt);

public sealed record StartPracticeRequest(PracticeMode Mode, List<string>? TopicIds, CefrLevel? Level, string? ReviewPlanId, int? QuestionCount);

public sealed record SubmitAnswerRequest(string QuestionId, string Answer, string? AttemptId);

public sealed record GrammarListView(List<GrammarTopic> Topics);

public sealed record GrammarSearchResultView(GrammarTopic Topic, double Score, List<string> MatchedTerms, List<string> SourceIds);
public sealed record GrammarSearchView(string Query, string Retriever, List<GrammarSearchResultView> Results);

public sealed record TutorRef(string Id, string Name, string AvatarEmoji);

public sealed record TutorMessageView(
    string Id, MessageRole Role, string Text, string CreatedAt, List<TutorCorrection> Corrections, List<string> RetrievedSourceIds, List<ToolCallRecord> ToolCalls);

public sealed record TutorSessionView(
    string Id,
    TutorRef Tutor,
    TutorChannel Channel,
    TutorSessionStatus Status,
    string Theme,
    List<TopicRef> FocusTopics,
    List<TutorMessageView> Messages,
    ProviderInfo Agent,
    string? AnalysisWorkflowRunId,
    string CreatedAt);

public sealed record TutorSessionSummaryView(
    string Id,
    TutorRef Tutor,
    TutorChannel Channel,
    TutorSessionStatus Status,
    string Theme,
    List<TopicRef> FocusTopics,
    ProviderInfo Agent,
    string? AnalysisWorkflowRunId,
    string CreatedAt,
    int MessageCount);

public sealed record TutorSessionListView(List<TutorSessionSummaryView> Sessions);

public sealed record StartTutorSessionRequest(string? Theme);
public sealed record SendTutorMessageRequest(string EventId, string Text);
public sealed record SendTutorMessageResponse(TutorSessionView Session, List<TutorMessageView> Replies, bool Duplicate);
public sealed record EndTutorSessionResponse(TutorSessionView Session, WorkflowRun WorkflowRun);
