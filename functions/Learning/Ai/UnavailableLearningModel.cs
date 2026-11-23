using LineLanguageLearn.Learning.Domain;

namespace LineLanguageLearn.Learning.Ai;

public sealed class UnavailableLearningModel : ILearningModel
{
    public ProviderInfo Info { get; } = new(ProviderKind.Unavailable, "not-configured");

    public Task<ModelResponse> GenerateQuestionsAsync(QuestionGenerationPrompt prompt, CancellationToken ct) =>
        Task.FromException<ModelResponse>(Error());

    public Task<ModelResponse> AnalyzeConversationAsync(ConversationAnalysisPrompt prompt, CancellationToken ct) =>
        Task.FromException<ModelResponse>(Error());

    public Task<ModelResponse> JudgeQuestionAsync(QuestionJudgePrompt prompt, CancellationToken ct) =>
        Task.FromException<ModelResponse>(Error());

    public static DomainException Error() => new(DomainErrorKind.Unavailable, "ai-not-configured",
        "AIサービスに接続されていないため、この機能を利用できません。");
}
