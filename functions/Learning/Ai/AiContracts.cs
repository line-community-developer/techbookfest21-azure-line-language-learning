using System.Text.Json;
using LineLanguageLearn.Learning.Contracts;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Knowledge;

namespace LineLanguageLearn.Learning.Ai;

// C# shapes of shared/contracts/ai.ts (Structured Outputs).
public sealed record GeneratedQuestion(
    QuestionType Type,
    string GrammarTopicId,
    int Difficulty,
    string InstructionJa,
    string Prompt,
    List<string> Choices,
    string CorrectAnswer,
    List<string> AcceptedAnswers,
    string ExplanationJa,
    List<string> RuleIds,
    List<string> SourceIds);

public sealed record GeneratedQuestionBatch(List<GeneratedQuestion> Questions);

public sealed record ConversationFinding(
    FindingKind Kind,
    string? GrammarTopicId,
    string MessageId,
    string EvidenceText,
    string? Correction,
    string ExplanationJa,
    double Confidence);

public sealed record ConversationAnalysisOutput(List<ConversationFinding> Findings, string SummaryJa);

public sealed record JudgementIssue(string Code, string MessageJa);

public sealed record QuestionJudgementOutput(JudgementVerdict Verdict, double Score, List<JudgementIssue> Issues, string RationaleJa);

// Inputs handed to a model provider. Grammar context is already retrieved (RAG happens before the model call).
public sealed record LearnerSnapshot(CefrLevel Level, IReadOnlyList<string> WeakTopicIds, IReadOnlyList<string> RecentMistakeExamples);

public sealed record QuestionGenerationPrompt(
    TargetLanguage Language,
    CefrLevel Level,
    IReadOnlyList<string> TopicIds,
    int QuestionCount,
    string Purpose,
    IReadOnlyList<GrammarSearchHit> Grammar,
    LearnerSnapshot Learner,
    int Seed,
    IReadOnlyCollection<string> AvoidPrompts);

public sealed record ConversationAnalysisPrompt(TutorSession Session, IReadOnlyList<GrammarTopic> CandidateTopics, TargetLanguage Language, CefrLevel Level);

public sealed record QuestionJudgePrompt(GeneratedQuestion Question, GrammarTopic Topic, CefrLevel Level);

/// <summary>Raw structured output (JSON text) as returned by a provider, before local schema validation.</summary>
public sealed record ModelResponse(string Json, ProviderInfo Provider, string? ResponseId);

/// <summary>
/// Model boundary for the structured AI tasks. Implementations return JSON text that must satisfy the
/// shared contract; validation and domain checks happen in the application layer, never in the SDK adapter.
/// </summary>
public interface ILearningModel
{
    ProviderInfo Info { get; }
    Task<ModelResponse> GenerateQuestionsAsync(QuestionGenerationPrompt prompt, CancellationToken ct);
    Task<ModelResponse> AnalyzeConversationAsync(ConversationAnalysisPrompt prompt, CancellationToken ct);
    Task<ModelResponse> JudgeQuestionAsync(QuestionJudgePrompt prompt, CancellationToken ct);
}

public sealed class StructuredOutputException(string schemaName, string code, string message, IReadOnlyList<SchemaViolation>? violations = null)
    : Exception(message)
{
    public string SchemaName { get; } = schemaName;
    public string Code { get; } = code;
    public IReadOnlyList<SchemaViolation> Violations { get; } = violations ?? [];
}

/// <summary>Step 1 of output handling: syntactic/structural validation against the exported JSON Schema.</summary>
public static class StructuredOutput
{
    public static T Parse<T>(string schemaName, string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new StructuredOutputException(schemaName, "invalid-json", $"Model output is not valid JSON: {ex.Message}");
        }
        using (document)
        {
            var violations = ContractSchemas.Validate(schemaName, document.RootElement);
            if (violations.Count > 0)
            {
                throw new StructuredOutputException(schemaName, "schema-mismatch", $"Model output does not match {schemaName}.", violations);
            }
            return LearningJson.Deserialize<T>(document.RootElement);
        }
    }
}
