using System.Text.Json.Serialization;

namespace LineLanguageLearn.Learning.Domain;

// Enum values serialize as kebab-case strings (see LearningJson) to match shared/contracts.

public enum LanguageCode { Ja, It, En }

public enum TargetLanguage { It, En }

public enum CefrLevel
{
    [JsonStringEnumMemberName("A1")] A1,
    [JsonStringEnumMemberName("A2")] A2,
    [JsonStringEnumMemberName("B1")] B1,
    [JsonStringEnumMemberName("B2")] B2,
    [JsonStringEnumMemberName("C1")] C1,
    [JsonStringEnumMemberName("C2")] C2,
}

public enum QuestionType { MultipleChoice, Cloze }

public enum MasteryStatus { Unknown, Weak, Developing, Strong }

public enum ObservationType { CorrectAnswer, IncorrectAnswer, GrammarError, PossibleMisunderstanding, CorrectUse, NewExpression }

public enum ObservationCertainty { Deterministic, AiInferred }

public enum ObservationSource { Practice, Conversation }

public enum ReviewPriority { High, Medium, Low }

public enum ProviderKind { Mock, Foundry, Unavailable }

public enum GrammarCategory { Article, Preposition, VerbPattern, Tense, Pronoun, Construction }

public enum SourceKind { OriginalNote, Textbook, Web, NativeReview }

public enum ReviewStatus { Unreviewed, Reviewed }

public enum ValidationCheckKind { Deterministic, Semantic }

public enum ValidationSeverity { Error, Warning }

public enum ValidationStatus { Passed, Failed }

public enum JudgementVerdict { Pass, Fail, Uncertain }

public enum PracticeMode { Recommended, Topic, Review }

public enum PracticeStatus { InProgress, Completed }

public enum PracticeOriginKind { Learner, ReviewWorkflow, TutorTool, DemoSeed }

public enum FeedbackCode { Correct, Incorrect, AccentMismatch }

public enum TutorChannel { LocalSimulator, Line, DemoSeed }

public enum TutorSessionStatus { Active, Ended }

public enum MessageRole { Learner, Tutor }

public enum ToolCallStatus { Succeeded, Failed, Rejected }

public enum CorrectionStyle { Recast, Explicit }

public enum ReviewPlanStatus { Active, Completed }

public enum WorkflowStatus { Pending, Running, Completed, Failed }

public enum WorkflowStepStatus { Pending, Running, Completed, Failed, Skipped }

public enum FindingKind { GrammarError, PossibleMisunderstanding, CorrectUse, NewExpression, NeedsReview }

public static class CefrLevels
{
    public static int Rank(this CefrLevel level) => (int)level;
}
