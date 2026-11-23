using LineLanguageLearn.Learning.Ai;
using LineLanguageLearn.Learning.Api;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Persistence;
using LineLanguageLearn.Learning.Practice;
using LineLanguageLearn.Learning.Tutor;
using LineLanguageLearn.Learning.Tutor.Tools;
using LineLanguageLearn.SeedData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace LineLanguageLearn.Tests;

public class ReferenceDataAndUnavailableAiTests
{
    [Fact]
    public async Task Reference_snapshot_reads_changed_database_content_without_embedded_fallback()
    {
        var fixture = ReferenceDataFixtures.Load();
        fixture.Grammar.Topics[0].TitleJa = "Database grammar";
        fixture.Tutors.Tutors[0].Name = "Database tutor";
        fixture.Questions.Questions[0].Prompt = "Database question ___";
        var store = new InMemoryDocumentStore();
        foreach (var topic in fixture.Grammar.Topics)
            await store.UpsertAsync(Containers.GrammarTopics, "it", topic.Id, topic, default);
        foreach (var tutor in fixture.Tutors.Tutors)
            await store.UpsertAsync(Containers.TutorProfiles, "it", tutor.Id, tutor, default);
        foreach (var question in fixture.Questions.Questions)
            await store.UpsertAsync(Containers.SampleQuestions, "it", question.Id, question, default);
        var loader = new ReferenceDataLoader(store, NullLogger<ReferenceDataLoader>.Instance);
        await loader.LoadAsync(default);

        Assert.Equal("Database grammar", loader.Snapshot.Grammar.Get(fixture.Grammar.Topics[0].Id).TitleJa);
        Assert.Equal("Database tutor", loader.Snapshot.Tutors.Get(fixture.Tutors.Tutors[0].Id).Name);
        Assert.Equal("Database question ___", loader.Snapshot.Questions.Questions.Single(
            question => question.Id == fixture.Questions.Questions[0].Id).Prompt);
        Assert.Equal(fixture.Grammar.Topics.Count, loader.Snapshot.Grammar.Topics.Count);
    }

    [Fact]
    public async Task Empty_reference_database_fails_instead_of_loading_bundled_data()
    {
        var loader = new ReferenceDataLoader(new InMemoryDocumentStore(), NullLogger<ReferenceDataLoader>.Instance);
        Assert.Throws<InvalidOperationException>(() => loader.Snapshot);
        var error = await Assert.ThrowsAsync<DomainException>(() => loader.LoadAsync(default));
        Assert.Equal(DomainErrorKind.Unavailable, error.Kind);
        Assert.Equal("reference-data-unavailable", error.Code);
        Assert.Throws<InvalidOperationException>(() => loader.Snapshot);
    }

    [Fact]
    public async Task Learning_entry_point_can_be_created_without_masters_and_recovers_after_seeding()
    {
        var services = new ServiceCollection();
        services.AddLearningServices(new ConfigurationBuilder().Build(), options => options.Storage = StorageMode.InMemory);
        services.AddSingleton<ApiPipeline>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        Assert.NotNull(ActivatorUtilities.CreateInstance<LearningFunctions>(provider));
        var loader = provider.GetRequiredService<ReferenceDataLoader>();
        await Assert.ThrowsAsync<DomainException>(() => loader.LoadAsync(default));
        var store = provider.GetRequiredService<IDocumentStore>();
        Assert.Empty(await store.ListAsync<LearningProfile>(Containers.LearningProfiles, TestApp.Learner.UserId, default));
        var fixture = ReferenceDataFixtures.Load();
        foreach (var topic in fixture.Grammar.Topics)
            await store.UpsertAsync(Containers.GrammarTopics, "it", topic.Id, topic, default);
        foreach (var tutor in fixture.Tutors.Tutors)
            await store.UpsertAsync(Containers.TutorProfiles, "it", tutor.Id, tutor, default);
        foreach (var question in fixture.Questions.Questions)
            await store.UpsertAsync(Containers.SampleQuestions, "it", question.Id, question, default);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => loader.LoadAsync(default)));
        var snapshot = loader.Snapshot;
        Assert.Equal(15, snapshot.Grammar.Topics.Count);
        Assert.NotNull(provider.GetRequiredService<PracticeService>());
        await store.UpsertAsync(Containers.GrammarTopics, "it", fixture.Grammar.Topics[0].Id,
            new GrammarTopic(), default);
        await loader.LoadAsync(default);
        Assert.Same(snapshot, loader.Snapshot);
    }

    [Fact]
    public void Broken_reference_links_are_rejected()
    {
        var fixture = ReferenceDataFixtures.Load();
        fixture.Questions.Questions[0].RuleIds = ["not-a-real-rule"];
        Assert.Throws<InvalidOperationException>(() => ReferenceDataSnapshot.Create(
            fixture.Grammar.Topics, fixture.Tutors.Tutors, fixture.Questions.Questions, true));
        Assert.Throws<InvalidOperationException>(() => ReferenceDataSnapshot.Create(
            fixture.Grammar.Topics, fixture.Tutors.Tutors, [], true));
        fixture.Grammar.Topics[0].RelatedTopics = ["not-a-real-topic"];
        Assert.Throws<InvalidOperationException>(() => ReferenceDataSnapshot.Create(
            fixture.Grammar.Topics, fixture.Tutors.Tutors, [], false));
    }

    [Fact]
    public void Runtime_assembly_contains_neither_reference_json_nor_fixed_response_implementations()
    {
        var assembly = typeof(LearningOptions).Assembly;
        Assert.DoesNotContain(assembly.GetManifestResourceNames(), name =>
            name.Contains("grammar.it.json") || name.Contains("tutors.json") || name.Contains("mock-question-bank"));
        Assert.Null(assembly.GetType("LineLanguageLearn.Learning.Ai.Mock.MockLearningModel"));
        Assert.Null(assembly.GetType("LineLanguageLearn.Learning.Ai.Mock.MockGrammarHeuristics"));
        Assert.Null(assembly.GetType("LineLanguageLearn.Learning.Tutor.MockTutorAgent"));
    }

    [Theory]
    [InlineData("AiProvider")]
    [InlineData("TutorAgent")]
    public void Runtime_does_not_accept_old_mock_provider_settings(string setting)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Learning:Storage"] = "InMemory",
            [$"Learning:{setting}"] = "Mock",
        }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddLearningServices(configuration));
    }

    [Fact]
    public async Task Unconfigured_runtime_rejects_new_ai_operations_without_creating_phantom_sessions()
    {
        using var provider = Runtime();
        Assert.IsType<UnavailableLearningModel>(provider.GetRequiredService<ILearningModel>());
        Assert.IsType<UnavailableTutorAgent>(provider.GetRequiredService<ITutorAgentRuntime>());
        var options = new LearningOptions();
        Assert.Equal(AiProviderMode.Unavailable, options.AiProvider);
        Assert.Equal(TutorAgentMode.Unavailable, options.TutorAgent);
        var practice = provider.GetRequiredService<PracticeService>();
        await AssertUnavailable(() => practice.StartAsync(TestApp.Learner,
            new StartPracticeRequest(PracticeMode.Recommended, null, null, null, null), default));
        var tutor = provider.GetRequiredService<TutorService>();
        await AssertUnavailable(() => tutor.StartSessionAsync(TestApp.Learner, null, TutorChannel.LocalSimulator, default));
        var store = provider.GetRequiredService<LearningStore>();
        Assert.Empty(await store.PracticeSessions.ListAsync(TestApp.Learner.UserId, default));
        Assert.Empty(await store.TutorSessions.ListAsync(TestApp.Learner.UserId, default));
    }

    [Fact]
    public async Task Unconfigured_tutor_does_not_save_a_new_message_or_canned_reply()
    {
        using var provider = Runtime();
        var store = provider.GetRequiredService<LearningStore>();
        var session = new TutorSession
        {
            Id = "existing-conversation", UserId = TestApp.Learner.UserId, Status = TutorSessionStatus.Active,
            TutorProfileId = provider.GetRequiredService<TutorProfileCatalog>().Tutors[0].Id,
        };
        await store.TutorSessions.UpsertAsync(session, default);
        await AssertUnavailable(() => provider.GetRequiredService<TutorService>().SendMessageAsync(
            TestApp.Learner, session.Id, "new-event", "Ciao!", default));
        Assert.Empty((await store.TutorSessions.GetAsync(TestApp.Learner.UserId, session.Id, default))!.Messages);
    }

    [Fact]
    public async Task Unconfigured_analysis_and_judge_fail_explicitly()
    {
        var fixture = ReferenceDataFixtures.Load();
        var model = new UnavailableLearningModel();
        await AssertUnavailable(() => model.AnalyzeConversationAsync(
            new ConversationAnalysisPrompt(new TutorSession(), fixture.Grammar.Topics, TargetLanguage.It, CefrLevel.A1), default));
        var question = new GeneratedQuestion(QuestionType.Cloze, fixture.Grammar.Topics[0].Id, 1, "Complete", "___", [],
            "a", ["a"], "Explanation", [], []);
        await AssertUnavailable(() => model.JudgeQuestionAsync(
            new QuestionJudgePrompt(question, fixture.Grammar.Topics[0], CefrLevel.A1), default));
    }

    [Fact]
    public async Task Empty_ai_reply_is_an_error_not_a_placeholder_message()
    {
        using var app = new TestApp(services => services.AddSingleton<ITutorAgentRuntime, EmptyReplyAgent>());
        var tutor = app.Get<TutorService>();
        var session = await tutor.StartSessionAsync(TestApp.Learner, null, TutorChannel.LocalSimulator, default);
        var error = await Assert.ThrowsAsync<DomainException>(() =>
            tutor.SendMessageAsync(TestApp.Learner, session.Id, "empty-turn", "Ciao!", default));
        Assert.Equal(DomainErrorKind.Unavailable, error.Kind);
        Assert.Equal("empty-ai-response", error.Code);
        var stored = await app.Store.TutorSessions.GetAsync(TestApp.Learner.UserId, session.Id, default);
        Assert.Equal(2, stored!.Messages.Count);
        Assert.Equal(MessageRole.Learner, stored.Messages[^1].Role);
    }

    private sealed class EmptyReplyAgent : ITutorAgentRuntime
    {
        public ProviderInfo Info { get; } = new(ProviderKind.Foundry, "empty-response-test");
        public Task<TutorTurnResult> RunTurnAsync(TutorTurnContext context, IToolInvoker tools, CancellationToken ct) =>
            Task.FromResult(new TutorTurnResult(" ", []));
    }

    private static ServiceProvider Runtime()
    {
        var services = new ServiceCollection();
        services.AddLearningServices(new ConfigurationBuilder().Build(), options => options.Storage = StorageMode.InMemory);
        services.AddReferenceDataFixtures(includeAi: false);
        return services.BuildServiceProvider();
    }

    private static async Task AssertUnavailable(Func<Task> operation)
    {
        var error = await Assert.ThrowsAsync<DomainException>(operation);
        Assert.Equal(DomainErrorKind.Unavailable, error.Kind);
        Assert.Equal("ai-not-configured", error.Code);
    }
}
