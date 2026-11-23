using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Identity;
using LineLanguageLearn.Learning.Tutor.Tools;

namespace LineLanguageLearn.Learning.Tutor;

public sealed record TutorTurnContext(
    UserContext User,
    TutorProfile Tutor,
    TutorSession Session,
    TutorMessage LearnerMessage,
    LearningProfile Profile,
    string SystemPrompt);

public sealed record TutorTurnResult(string ReplyText, List<TutorCorrection> Corrections,
    IReadOnlyList<ToolCallRecord>? ToolCalls = null, IReadOnlyList<string>? RetrievedSourceIds = null);

/// <summary>
/// Agent execution boundary. The runtime decides which tools to call; tool execution always goes through
/// the application's dispatcher, locally or through MCP (validation, authorization, budget, logging).
/// </summary>
public interface ITutorAgentRuntime
{
    ProviderInfo Info { get; }
    Task<TutorTurnResult> RunTurnAsync(TutorTurnContext context, IToolInvoker tools, CancellationToken ct);
}
