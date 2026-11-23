using System.Text.Json.Serialization;
using LineLanguageLearn.Learning.Infrastructure;

namespace LineLanguageLearn.Learning.Domain;

// C# shapes of the persisted documents defined in shared/models (the source of truth).
// Contract tests validate serialized instances against the exported JSON Schemas so drift is caught.

public abstract class Document
{
    public string Id { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CreatedAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UpdatedAt { get; set; }
}

public abstract class UserDocument : Document
{
    public string UserId { get; set; } = "";
    public int SchemaVersion { get; set; } = DomainConstants.SchemaVersion;
}

public static class DomainConstants
{
    public const int SchemaVersion = 1;
}

public sealed record ProviderInfo(ProviderKind Kind, string Model)
{
    [JsonIgnore]
    public string Label => $"{Kind.ToString().ToLowerInvariant()}:{Model}";
}

// ---- LearningProfile ----
public sealed class TopicMastery
{
    public string TopicId { get; set; } = "";
    public double Score { get; set; }
    public MasteryStatus Status { get; set; }
    public int Attempts { get; set; }
    public int Correct { get; set; }
    public int ConversationErrors { get; set; }
    public int ConversationCorrectUses { get; set; }
    public string? LastObservedAt { get; set; }
}

public sealed class LearningProfile : UserDocument
{
    public string LearnerName { get; set; } = "";
    public LanguageCode NativeLanguage { get; set; } = LanguageCode.Ja;
    public TargetLanguage TargetLanguage { get; set; } = TargetLanguage.It;
    public CefrLevel CurrentLevel { get; set; } = CefrLevel.A1;
    public List<string> LearningGoals { get; set; } = [];
    [JsonConverter(typeof(DictionaryEntriesConverter<TopicMastery>))]
    public Dictionary<string, TopicMastery> MasteryByTopic { get; set; } = [];
    public List<string> WeakTopics { get; set; } = [];
    public List<string> StrongTopics { get; set; } = [];
    public int ObservationCount { get; set; }
}

// ---- GrammarTopic ----
public sealed record GrammarRule(string Id, string SummaryJa, string? Pattern);
public sealed record GrammarExample(string Id, string Text, string TranslationJa, string? NoteJa);
public sealed record CommonMistake(string Id, string Incorrect, string Correct, string ExplanationJa);
public sealed record SourceReference(string Id, SourceKind Kind, string Label, string? Url);

public sealed class GrammarTopic : Document
{
    public TargetLanguage Language { get; set; }
    public int SchemaVersion { get; set; } = DomainConstants.SchemaVersion;
    public string Title { get; set; } = "";
    public string TitleJa { get; set; } = "";
    public CefrLevel Level { get; set; }
    public GrammarCategory Category { get; set; }
    public string Description { get; set; } = "";
    public string UsageJa { get; set; } = "";
    public List<GrammarRule> Rules { get; set; } = [];
    public List<GrammarExample> Examples { get; set; } = [];
    public List<CommonMistake> CommonMistakes { get; set; } = [];
    public List<string> RelatedTopics { get; set; } = [];
    public List<string> Keywords { get; set; } = [];
    public List<SourceReference> SourceReferences { get; set; } = [];
    public ReviewStatus ReviewStatus { get; set; }
    public string? ReviewNoteJa { get; set; }
}

public sealed record QuestionPublication(string InstructionJa, List<string> SourceIds, ProviderInfo Provider, QuestionValidation Validation);

public sealed class SampleQuestion : Document
{
    public TargetLanguage Language { get; set; } = TargetLanguage.It;
    public int SchemaVersion { get; set; } = DomainConstants.SchemaVersion;
    public string GrammarTopicId { get; set; } = "";
    public QuestionType Type { get; set; }
    public int Difficulty { get; set; }
    public string Prompt { get; set; } = "";
    public List<string> Choices { get; set; } = [];
    public string CorrectAnswer { get; set; } = "";
    public List<string> AcceptedAnswers { get; set; } = [];
    public string ExplanationJa { get; set; } = "";
    public List<string> RuleIds { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public QuestionPublication? Publication { get; set; }
}

// ---- PracticeQuestion / PracticeSession ----
public sealed record QuestionValidationCheck(string Id, ValidationCheckKind Kind, bool Passed, ValidationSeverity Severity, string MessageJa);

public sealed record SemanticJudgement(JudgementVerdict Verdict, double Score, string RationaleJa, ProviderInfo Evaluator);

public sealed record QuestionValidation(ValidationStatus Status, List<QuestionValidationCheck> Checks, SemanticJudgement? Semantic, string EvaluatedAt);

public sealed class PracticeQuestion : Document
{
    public string SessionId { get; set; } = "";
    public int Order { get; set; }
    public QuestionType Type { get; set; }
    public string GrammarTopicId { get; set; } = "";
    public int Difficulty { get; set; }
    public string InstructionJa { get; set; } = "";
    public string Prompt { get; set; } = "";
    public List<string> Choices { get; set; } = [];
    public string CorrectAnswer { get; set; } = "";
    public List<string> AcceptedAnswers { get; set; } = [];
    public string ExplanationJa { get; set; } = "";
    public List<string> RuleIds { get; set; } = [];
    public List<string> SourceIds { get; set; } = [];
    public ProviderInfo GeneratedBy { get; set; } = new(ProviderKind.Unavailable, "unknown");
    public string GenerationId { get; set; } = "";
    public QuestionValidation Validation { get; set; } = new(ValidationStatus.Failed, [], null, "");
}

public sealed record PracticeAnswer(
    string QuestionId,
    string AttemptId,
    string Answer,
    string NormalizedAnswer,
    bool IsCorrect,
    FeedbackCode FeedbackCode,
    double MasteryBefore,
    double MasteryAfter,
    string AnsweredAt);

public sealed record PracticeSummary(int Total, int Answered, int Correct, double Accuracy);

public sealed record PracticeOrigin(PracticeOriginKind Kind, string? RefId);

public sealed class PracticeSession : UserDocument
{
    public PracticeMode Mode { get; set; }
    public CefrLevel Level { get; set; }
    public List<string> TargetTopicIds { get; set; } = [];
    public string? ReviewPlanId { get; set; }
    public PracticeOrigin Origin { get; set; } = new(PracticeOriginKind.Learner, null);
    public PracticeStatus Status { get; set; }
    public List<PracticeQuestion> Questions { get; set; } = [];
    public List<PracticeAnswer> Answers { get; set; } = [];
    public PracticeSummary? Summary { get; set; }
    public string? CompletedAt { get; set; }
}

// ---- LearningObservation ----
public sealed record ObservationEvidence(string Text, string? Expected, string? Actual);

public sealed class LearningObservation : UserDocument
{
    public string? GrammarTopicId { get; set; }
    public ObservationSource SourceType { get; set; }
    public string SourceId { get; set; } = "";
    public string SourceItemId { get; set; } = "";
    public ObservationType ObservationType { get; set; }
    public ObservationCertainty Certainty { get; set; }
    public double Confidence { get; set; }
    public ObservationEvidence Evidence { get; set; } = new("", null, null);
    public string DetectedBy { get; set; } = "";
    public string OccurredAt { get; set; } = "";
}

// ---- Tutor ----
public sealed record CorrectionPolicy(int MaxCorrectionsPerTurn, CorrectionStyle Style, bool ExplainInJapanese);

public sealed class TutorProfile : Document
{
    public TargetLanguage Language { get; set; }
    public int SchemaVersion { get; set; } = DomainConstants.SchemaVersion;
    public string Name { get; set; } = "";
    public string AvatarEmoji { get; set; } = "";
    public string PersonaJa { get; set; } = "";
    public string SpeakingStyle { get; set; } = "";
    public string Greeting { get; set; } = "";
    public CorrectionPolicy CorrectionPolicy { get; set; } = new(1, CorrectionStyle.Recast, true);
    public string PromptTemplateId { get; set; } = "";
}

public sealed record TutorCorrection(string GrammarTopicId, string Incorrect, string Suggestion, string ExplanationJa);

public sealed record ToolCallRecord(string CallId, string Name, ToolCallStatus Status, double DurationMs, string? ErrorCode);

public sealed class TutorMessage : Document
{
    public string SessionId { get; set; } = "";
    public MessageRole Role { get; set; }
    public string Text { get; set; } = "";
    public string? ExternalEventId { get; set; }
    public List<TutorCorrection> Corrections { get; set; } = [];
    public List<string> RetrievedSourceIds { get; set; } = [];
    public List<ToolCallRecord> ToolCalls { get; set; } = [];
}

public sealed class TutorSession : UserDocument
{
    public string TutorProfileId { get; set; } = "";
    public TutorChannel Channel { get; set; }
    public string Theme { get; set; } = "";
    public List<string> FocusTopicIds { get; set; } = [];
    public TutorSessionStatus Status { get; set; }
    public List<TutorMessage> Messages { get; set; } = [];
    public ProviderInfo Agent { get; set; } = new(ProviderKind.Unavailable, "unknown");
    public string? EndedAt { get; set; }
    public string? AnalysisWorkflowRunId { get; set; }
}

// ---- ReviewPlan ----
public sealed record ReviewPlanItem(string TopicId, ReviewPriority Priority, double PriorityScore, string ReasonJa, List<string> EvidenceObservationIds);

public sealed class ReviewPlan : UserDocument
{
    public ObservationSource SourceType { get; set; }
    public string SourceId { get; set; } = "";
    public ReviewPlanStatus Status { get; set; }
    public List<ReviewPlanItem> Items { get; set; } = [];
    public List<string> PracticeSessionIds { get; set; } = [];
}

// ---- WorkflowRun ----
public sealed record WorkflowError(string Code, string Message);

public sealed class WorkflowStepState
{
    public string Name { get; set; } = "";
    public WorkflowStepStatus Status { get; set; }
    public int Attempts { get; set; }
    public string? StartedAt { get; set; }
    public string? CompletedAt { get; set; }
    [JsonConverter(typeof(DictionaryEntriesConverter<string>))]
    public Dictionary<string, string> Output { get; set; } = [];
    public WorkflowError? Error { get; set; }
}

public sealed class WorkflowRun : UserDocument
{
    public string WorkflowName { get; set; } = "";
    public string SubjectId { get; set; } = "";
    public WorkflowStatus Status { get; set; }
    public int RunCount { get; set; }
    public List<WorkflowStepState> Steps { get; set; } = [];
    public WorkflowError? Error { get; set; }
}

// ---- Internal (not exposed by the API) ----

/// <summary>Idempotency marker for externally delivered events (e.g. LINE webhook event ids).</summary>
public sealed class ProcessedEvent : UserDocument
{
    public string Channel { get; set; } = "";
    public string ExternalEventId { get; set; } = "";
    public string ResultRef { get; set; } = "";
}
