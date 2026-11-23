using LineLanguageLearn.Learning.Api;
using LineLanguageLearn.Learning.Contracts;
using LineLanguageLearn.SeedData;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Identity;
using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Persistence;
using LineLanguageLearn.Learning.Practice;

namespace LineLanguageLearn.Tests;

public class DemoAndContractTests
{
    private static readonly UserContext Demo = new("local-demo-learner", "Demo Learner", "local-development");

    [Fact]
    public async Task Offline_fixture_builder_creates_the_scenario_through_real_services()
    {
        using var app = new TestApp();
        await app.Get<ScenarioFixtureBuilder>().CreateIfMissingAsync(Demo, default);
        Assert.Equal(4, (await app.Store.PracticeSessions.ListAsync(Demo.UserId, default)).Count);
        Assert.Single(await app.Store.TutorSessions.ListAsync(Demo.UserId, default));
        Assert.Single(await app.Store.ReviewPlans.ListAsync(Demo.UserId, default));

        var dashboard = await app.Get<LearnerInsightsService>().GetDashboardAsync(Demo, default);
        ContractSchemas.EnsureValid(SchemaNames.DashboardView, LearningJson.ToElement(dashboard));
        Assert.Equal(CefrLevel.A2, dashboard.Learner.CurrentLevel);
        var weak = dashboard.WeakTopics.Select(w => w.TopicId).ToList();
        Assert.Contains("it-verb-a-infinitive", weak);
        Assert.Contains("it-verb-di-infinitive", weak);
        Assert.Contains("it-passato-prossimo-auxiliary", weak);
        var strong = dashboard.StrongTopics.Select(w => w.TopicId).ToList();
        Assert.Contains("it-definite-articles", strong);
        Assert.Contains("it-present-regular", strong);
        Assert.Equal("review", dashboard.Recommendation.Mode);
        Assert.NotNull(dashboard.Recommendation.PracticeSessionId);
        var plan = Assert.Single(dashboard.ReviewPlans);
        Assert.Equal(ObservationSource.Conversation, plan.SourceType);
        Assert.Contains(plan.Items, i => i.Evidence.Any(e => e.Certainty == ObservationCertainty.AiInferred));

        var history = await app.Get<LearnerInsightsService>().GetHistoryAsync(Demo, default);
        ContractSchemas.EnsureValid(SchemaNames.HistoryView, LearningJson.ToElement(history));
        Assert.NotEmpty(history.Attempts);
        Assert.Contains(history.Observations, o => o.Certainty == ObservationCertainty.Deterministic);
        Assert.Contains(history.Observations, o => o.Certainty == ObservationCertainty.AiInferred);

        // Step 9 of the demo: solve the review session generated from the conversation.
        var practice = app.Get<PracticeService>();
        var review = await practice.StartAsync(Demo, new StartPracticeRequest(PracticeMode.Review, null, null, plan.Id, null), default);
        Assert.Equal(dashboard.Recommendation.PracticeSessionId, review.Id);
        var stored = await app.Store.PracticeSessions.GetAsync(Demo.UserId, review.Id, default);
        foreach (var q in stored!.Questions) await practice.SubmitAnswerAsync(Demo, review.Id, new SubmitAnswerRequest(q.Id, q.CorrectAnswer, null), default);
        var done = await practice.CompleteAsync(Demo, review.Id, default);
        Assert.Equal(done.Summary!.Total, done.Summary.Correct);
        var after = await app.Get<LearnerInsightsService>().GetDashboardAsync(Demo, default);
        Assert.Equal(ReviewPlanStatus.Completed, after.ReviewPlans.Single().Status);
        Assert.Equal("recommended", after.Recommendation.Mode);
    }

    [Fact]
    public async Task Offline_fixture_generation_is_idempotent()
    {
        using var app = new TestApp();
        var fixture = app.Get<ScenarioFixtureBuilder>();
        await fixture.CreateIfMissingAsync(Demo, default);
        var count = (await app.Store.Observations.ListAsync(Demo.UserId, default)).Count;
        await fixture.CreateIfMissingAsync(Demo, default);
        Assert.Equal(count, (await app.Store.Observations.ListAsync(Demo.UserId, default)).Count);
    }

    [Fact]
    public void Status_reports_mock_components_honestly()
    {
        using var app = new TestApp();
        var status = app.Get<SystemStatusService>().Get(Demo);
        ContractSchemas.EnsureValid(SchemaNames.SystemStatus, LearningJson.ToElement(status));
        Assert.Equal("local", status.Environment);
        Assert.True(status.Components.Single(c => c.Component == "ai-model").IsMock);
        Assert.True(status.Components.Single(c => c.Component == "tutor-agent").IsMock);
        Assert.True(status.Components.Single(c => c.Component == "messaging").IsMock);
        Assert.Contains("AI接続なし", status.Components.Single(c => c.Component == "ai-model").Detail);
    }

    [Fact]
    public async Task Empty_database_creates_only_a_profile_without_demo_history()
    {
        using var app = new TestApp();
        var insights = app.Get<LearnerInsightsService>();
        var dashboard = await insights.GetDashboardAsync(Demo, default);
        var history = await insights.GetHistoryAsync(Demo, default);
        Assert.Equal(CefrLevel.A1, dashboard.Learner.CurrentLevel);
        Assert.Equal(0, dashboard.Stats.Answered);
        Assert.Empty(history.Attempts);
        Assert.Empty(await app.Store.Observations.ListAsync(Demo.UserId, default));
        Assert.Empty(await app.Store.PracticeSessions.ListAsync(Demo.UserId, default));
        Assert.Empty(await app.Store.TutorSessions.ListAsync(Demo.UserId, default));
        Assert.Empty(await app.Store.ReviewPlans.ListAsync(Demo.UserId, default));
        Assert.Empty(await app.Store.WorkflowRuns.ListAsync(Demo.UserId, default));
        Assert.Single(await app.Store.Profiles.ListAsync(Demo.UserId, default));
        Assert.Null(typeof(ApiPipeline).Assembly.GetType(typeof(ScenarioFixtureBuilder).FullName!));
    }

    [Fact]
    public async Task Stored_documents_match_shared_model_contracts()
    {
        using var app = new TestApp();
        await app.Get<ScenarioFixtureBuilder>().CreateIfMissingAsync(Demo, default);
        var s = app.Store;
        foreach (var d in await s.PracticeSessions.ListAsync(Demo.UserId, default)) ContractSchemas.EnsureValid(SchemaNames.PracticeSession, LearningJson.ToElement(d));
        foreach (var d in await s.Observations.ListAsync(Demo.UserId, default)) ContractSchemas.EnsureValid(SchemaNames.LearningObservation, LearningJson.ToElement(d));
        foreach (var d in await s.TutorSessions.ListAsync(Demo.UserId, default)) ContractSchemas.EnsureValid(SchemaNames.TutorSession, LearningJson.ToElement(d));
        foreach (var d in await s.ReviewPlans.ListAsync(Demo.UserId, default)) ContractSchemas.EnsureValid(SchemaNames.ReviewPlan, LearningJson.ToElement(d));
        foreach (var d in await s.WorkflowRuns.ListAsync(Demo.UserId, default)) ContractSchemas.EnsureValid(SchemaNames.WorkflowRun, LearningJson.ToElement(d));
        ContractSchemas.EnsureValid(SchemaNames.LearningProfile, LearningJson.ToElement((await s.Profiles.GetAsync(Demo.UserId, LearningProfileService.ProfileId, default))!));
        var view = await app.Get<LineLanguageLearn.Learning.Tutor.TutorService>().GetViewAsync(Demo, "ts-demo-weekend", default);
        ContractSchemas.EnsureValid(SchemaNames.TutorSessionView, LearningJson.ToElement(view));
    }

    [Fact]
    public async Task Test_store_creates_once_and_isolates_partitions()
    {
        var store = new InMemoryDocumentStore();
        var doc = new ReviewPlan { Id = "rp-1", UserId = "u1", SourceId = "s", Items = [] };
        Assert.True(await store.TryCreateAsync(Containers.ReviewPlans, "u1", doc.Id, doc, default));
        Assert.False(await store.TryCreateAsync(Containers.ReviewPlans, "u1", doc.Id, doc, default));
        var loaded = await store.GetAsync<ReviewPlan>(Containers.ReviewPlans, "u1", "rp-1", default);
        Assert.Equal("s", loaded!.SourceId);
        Assert.Empty(await store.ListAsync<ReviewPlan>(Containers.ReviewPlans, "u2", default));
    }
}
