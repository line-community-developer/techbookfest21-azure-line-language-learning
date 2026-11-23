using System.Net;
using LineLanguageLearn.Learning.Contracts;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Knowledge;
using LineLanguageLearn.Learning.Practice;
using LineLanguageLearn.Learning.Tutor;
using LineLanguageLearn.Learning.Workflows;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.DependencyInjection;

namespace LineLanguageLearn.Learning.Api;

/// <summary>
/// AI-authored HTTP endpoints for the learning app (not SwallowKit-generated CRUD). The learner is always
/// resolved server-side by ApiPipeline; no endpoint accepts a user id from the client.
/// </summary>
public sealed class LearningFunctions(
    ApiPipeline pipeline,
    IServiceProvider services)
{
    // Resolve the learning graph only inside the action, after ApiPipeline has loaded the masters.
    private SystemStatusService status => services.GetRequiredService<SystemStatusService>();
    private LearnerInsightsService insights => services.GetRequiredService<LearnerInsightsService>();
    private PracticeService practice => services.GetRequiredService<PracticeService>();
    private GrammarKnowledgeBase knowledgeBase => services.GetRequiredService<GrammarKnowledgeBase>();
    private IGrammarRetriever retriever => services.GetRequiredService<IGrammarRetriever>();
    private TutorService tutor => services.GetRequiredService<TutorService>();
    private WorkflowRunner workflows => services.GetRequiredService<WorkflowRunner>();
    private ViewMapper views => services.GetRequiredService<ViewMapper>();

    [Function("learningStatus")]
    public Task<HttpResponseData> Status([HttpTrigger(AuthorizationLevel.Function, "get", Route = "learning/status")] HttpRequestData req) =>
        pipeline.HandleAsync(req, SchemaNames.SystemStatus, (user, _) => Task.FromResult<object>(status.Get(user)));

    [Function("learningDashboard")]
    public Task<HttpResponseData> Dashboard([HttpTrigger(AuthorizationLevel.Function, "get", Route = "learning/dashboard")] HttpRequestData req) =>
        pipeline.HandleAsync(req, SchemaNames.DashboardView, async (user, ct) => await insights.GetDashboardAsync(user, ct));

    [Function("learningHistory")]
    public Task<HttpResponseData> History([HttpTrigger(AuthorizationLevel.Function, "get", Route = "learning/history")] HttpRequestData req) =>
        pipeline.HandleAsync(req, SchemaNames.HistoryView, async (user, ct) => await insights.GetHistoryAsync(user, ct));

    [Function("learningGrammarList")]
    public Task<HttpResponseData> GrammarList([HttpTrigger(AuthorizationLevel.Function, "get", Route = "learning/grammar")] HttpRequestData req) =>
        pipeline.HandleAsync(req, SchemaNames.GrammarListView, (_, _) =>
            Task.FromResult<object>(new GrammarListView(knowledgeBase.Topics.OrderBy(t => t.Level).ThenBy(t => t.Id, StringComparer.Ordinal).ToList())));

    [Function("learningGrammarSearch")]
    public Task<HttpResponseData> GrammarSearch([HttpTrigger(AuthorizationLevel.Function, "get", Route = "learning/grammar-search")] HttpRequestData req) =>
        pipeline.HandleAsync(req, SchemaNames.GrammarSearchView, async (user, ct) =>
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var q = (query["q"] ?? "").Trim();
            if (q.Length is 0 or > 200) throw DomainException.Invalid("invalid-query", "q must be 1-200 characters.");
            CefrLevel? level = Enum.TryParse<CefrLevel>(query["level"], ignoreCase: true, out var parsed) ? parsed : null;
            var limit = int.TryParse(query["limit"], out var l) ? Math.Clamp(l, 1, 10) : 5;
            var hits = await retriever.SearchAsync(new GrammarQuery(TargetLanguage.It, q, level, query["topicId"], limit), ct);
            return new GrammarSearchView(q, retriever.Name, hits.Select(h => new GrammarSearchResultView(h.Topic, h.Score, h.MatchedTerms.ToList(), h.SourceIds.ToList())).ToList());
        });

    [Function("learningGrammarGet")]
    public Task<HttpResponseData> GrammarGet([HttpTrigger(AuthorizationLevel.Function, "get", Route = "learning/grammar/{topicId}")] HttpRequestData req, string topicId) =>
        pipeline.HandleAsync(req, SchemaNames.GrammarTopic, (_, _) => Task.FromResult<object>(knowledgeBase.Get(topicId)));

    [Function("learningPracticeStart")]
    public Task<HttpResponseData> PracticeStart([HttpTrigger(AuthorizationLevel.Function, "post", Route = "learning/practice/sessions")] HttpRequestData req) =>
        pipeline.HandleAsync(req, SchemaNames.PracticeSessionView, async (user, ct) =>
            await practice.StartAsync(user, await ApiPipeline.ReadBodyAsync<StartPracticeRequest>(req, SchemaNames.StartPracticeRequest), ct), HttpStatusCode.Created);

    [Function("learningPracticeGet")]
    public Task<HttpResponseData> PracticeGet([HttpTrigger(AuthorizationLevel.Function, "get", Route = "learning/practice/sessions/{sessionId}")] HttpRequestData req, string sessionId) =>
        pipeline.HandleAsync(req, SchemaNames.PracticeSessionView, async (user, ct) => await practice.GetAsync(user, sessionId, ct));

    [Function("learningPracticeAnswer")]
    public Task<HttpResponseData> PracticeAnswer([HttpTrigger(AuthorizationLevel.Function, "post", Route = "learning/practice/sessions/{sessionId}/answers")] HttpRequestData req, string sessionId) =>
        pipeline.HandleAsync(req, SchemaNames.AnswerResult, async (user, ct) =>
            await practice.SubmitAnswerAsync(user, sessionId, await ApiPipeline.ReadBodyAsync<SubmitAnswerRequest>(req, SchemaNames.SubmitAnswerRequest), ct));

    [Function("learningPracticeComplete")]
    public Task<HttpResponseData> PracticeComplete([HttpTrigger(AuthorizationLevel.Function, "post", Route = "learning/practice/sessions/{sessionId}/complete")] HttpRequestData req, string sessionId) =>
        pipeline.HandleAsync(req, SchemaNames.PracticeSessionView, async (user, ct) => await practice.CompleteAsync(user, sessionId, ct));

    [Function("learningTutorList")]
    public Task<HttpResponseData> TutorList([HttpTrigger(AuthorizationLevel.Function, "get", Route = "learning/tutor/sessions")] HttpRequestData req) =>
        pipeline.HandleAsync(req, SchemaNames.TutorSessionListView, async (user, ct) => await tutor.ListAsync(user, ct));

    // Conversations take place in the LINE talk (LineBotFunctions); the mini app only reads their results.
    [Function("learningTutorGet")]
    public Task<HttpResponseData> TutorGet([HttpTrigger(AuthorizationLevel.Function, "get", Route = "learning/tutor/sessions/{sessionId}")] HttpRequestData req, string sessionId) =>
        pipeline.HandleAsync(req, SchemaNames.TutorSessionView, async (user, ct) => await tutor.GetViewAsync(user, sessionId, ct));

    [Function("learningWorkflowGet")]
    public Task<HttpResponseData> WorkflowGet([HttpTrigger(AuthorizationLevel.Function, "get", Route = "learning/workflows/{runId}")] HttpRequestData req, string runId) =>
        pipeline.HandleAsync(req, SchemaNames.WorkflowRun, async (user, ct) => await workflows.GetAsync(user, runId, ct));

    [Function("learningWorkflowRetry")]
    public Task<HttpResponseData> WorkflowRetry(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "learning/workflows/{runId}/retry")] HttpRequestData req, string runId,
        [DurableClient] DurableTaskClient durable) =>
        pipeline.HandleAsync(req, SchemaNames.WorkflowRun, async (user, ct) =>
            await workflows.RetryAsync(user, runId, new DurableWorkflowScheduler(durable, workflows), ct));
}
