using LineLanguageLearn.Learning.Ai;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Tutor.Tools;

namespace LineLanguageLearn.Learning.Tutor;

public sealed class UnavailableTutorAgent : ITutorAgentRuntime
{
    public ProviderInfo Info { get; } = new(ProviderKind.Unavailable, "not-configured");

    public Task<TutorTurnResult> RunTurnAsync(TutorTurnContext context, IToolInvoker tools, CancellationToken ct) =>
        Task.FromException<TutorTurnResult>(UnavailableLearningModel.Error());
}
