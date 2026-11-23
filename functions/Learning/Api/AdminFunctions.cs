using LineLanguageLearn.Learning.Ai;
using LineLanguageLearn.Learning.Contracts;
using LineLanguageLearn.Learning.Knowledge;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Practice;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LineLanguageLearn.Learning.Api;

public sealed record AdminQuestionGenerationRequest(string TopicId, int QuestionCount);
public sealed record AdminQuestionValidationRequest(string TopicId, GeneratedQuestion Question, string GenerationId);
public sealed record AdminQuestionPublishRequest(string TopicId, GeneratedQuestion Question, ProviderInfo Provider);

public sealed class AdminFunctions(ApiPipeline pipeline, IServiceProvider services)
{
    private const string RequestSchema = "api.AdminQuestionGenerationRequest";
    private const string ResponseSchema = "api.AdminQuestionGenerationResponse";

    [Function("adminGrammarList")]
    public Task<HttpResponseData> GrammarList(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "management/grammar")] HttpRequestData request) =>
        pipeline.HandleAdminAsync(request, SchemaNames.GrammarListView, _ =>
        {
            var topics = services.GetRequiredService<GrammarKnowledgeBase>().Topics;
            return Task.FromResult<object>(new GrammarListView(topics.OrderBy(t => t.Level).ThenBy(t => t.Id, StringComparer.Ordinal).ToList()));
        });

    [Function("adminGenerateQuestions")]
    public Task<HttpResponseData> GenerateQuestions(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "management/questions/generate")] HttpRequestData request) =>
        pipeline.HandleAdminAsync(request, ResponseSchema, async ct =>
        {
            var input = await ApiPipeline.ReadBodyAsync<AdminQuestionGenerationRequest>(request, RequestSchema);
            var generator = services.GetRequiredService<QuestionGenerationService>();
            var batch = await generator.GeneratePreviewAsync(input.TopicId, input.QuestionCount, ct);
            return new { input.TopicId, questions = batch.Questions.Select(question => new { question, batch.GenerationId, batch.Provider }) };
        });

    [Function("adminValidateQuestion")]
    public Task<HttpResponseData> ValidateQuestion(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "management/questions/validate")] HttpRequestData request) =>
        pipeline.HandleAdminAsync(request, "api.QuestionValidation", async ct =>
        {
            var input = await ApiPipeline.ReadBodyAsync<AdminQuestionValidationRequest>(request, "api.AdminQuestionValidationRequest");
            var generator = services.GetRequiredService<QuestionGenerationService>();
            return await generator.ValidatePreviewAsync(input.TopicId, input.Question, input.GenerationId, ct);
        });

    [Function("adminPublishQuestion")]
    public Task<HttpResponseData> PublishQuestion(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "management/questions/publish")] HttpRequestData request) =>
        pipeline.HandleAdminAsync(request, "api.AdminQuestionPublishResponse", async ct =>
        {
            var input = await ApiPipeline.ReadBodyAsync<AdminQuestionPublishRequest>(request, "api.AdminQuestionPublishRequest");
            var service = services.GetRequiredService<PublishedQuestionService>();
            return new { id = await service.PublishAsync(input.TopicId, input.Question, input.Provider, ct) };
        });
}
