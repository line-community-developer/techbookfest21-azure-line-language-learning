using System.Diagnostics;
using LineLanguageLearn.Learning.Contracts;
using LineLanguageLearn.Learning.Observability;
using Microsoft.Extensions.Logging;

namespace LineLanguageLearn.Learning.Ai;

public sealed record ModelResult<T>(T Value, ModelResponse Response, double DurationMs);

/// <summary>Calls the configured model, records telemetry and validates the structured output.</summary>
public sealed class LearningModelGateway(ILearningModel model, ILogger<LearningModelGateway> logger)
{
    public Domain.ProviderInfo Info => model.Info;

    public Task<ModelResult<GeneratedQuestionBatch>> GenerateQuestionsAsync(QuestionGenerationPrompt prompt, CancellationToken ct) =>
        InvokeAsync<GeneratedQuestionBatch>("generate-questions", SchemaNames.GeneratedQuestionBatch, () => model.GenerateQuestionsAsync(prompt, ct));

    public Task<ModelResult<ConversationAnalysisOutput>> AnalyzeConversationAsync(ConversationAnalysisPrompt prompt, CancellationToken ct) =>
        InvokeAsync<ConversationAnalysisOutput>("analyze-conversation", SchemaNames.ConversationAnalysis, () => model.AnalyzeConversationAsync(prompt, ct));

    public Task<ModelResult<QuestionJudgementOutput>> JudgeQuestionAsync(QuestionJudgePrompt prompt, CancellationToken ct) =>
        InvokeAsync<QuestionJudgementOutput>("judge-question", SchemaNames.QuestionJudgement, () => model.JudgeQuestionAsync(prompt, ct));

    private async Task<ModelResult<T>> InvokeAsync<T>(string operation, string schemaName, Func<Task<ModelResponse>> call)
    {
        using var activity = LearningTelemetry.StartActivity($"ai.{operation}");
        activity?.SetTag("ai.provider", model.Info.Kind.ToString());
        activity?.SetTag("ai.model", model.Info.Model);
        var started = Stopwatch.GetTimestamp();
        var outcome = "ok";
        try
        {
            var response = await call();
            var value = StructuredOutput.Parse<T>(schemaName, response.Json);
            return new ModelResult<T>(value, response, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (StructuredOutputException ex)
        {
            outcome = ex.Code;
            throw;
        }
        catch (Exception ex)
        {
            outcome = ex.GetType().Name;
            throw;
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            activity?.SetTag("ai.outcome", outcome);
            logger.AiCall(operation, model.Info.Kind.ToString().ToLowerInvariant(), model.Info.Model, elapsed, outcome);
        }
    }
}

