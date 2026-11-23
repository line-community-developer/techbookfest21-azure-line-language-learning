using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LineLanguageLearn.Learning.Api;
using LineLanguageLearn.Learning.Contracts;
using LineLanguageLearn.SeedData;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Identity;
using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Persistence;
using LineLanguageLearn.Learning.Practice;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var scenarios = new[] { "a1-new", "a2-review-ready", "a2-review-in-progress", "a2-review-completed" };
var user = new UserContext("local-demo-learner", "Demo Learner", "local-development");
var root = Path.GetFullPath(Path.Combine("dev-seeds"));
var containers = new Dictionary<string, string>
{
    ["learning-profile"] = Containers.LearningProfiles,
    ["learning-observation"] = Containers.LearningObservations,
    ["practice-session"] = Containers.PracticeSessions,
    ["tutor-session"] = Containers.TutorSessions,
    ["review-plan"] = Containers.ReviewPlans,
    ["workflow-run"] = Containers.WorkflowRuns,
    ["grammar-topic"] = Containers.GrammarTopics,
    ["tutor-profile"] = Containers.TutorProfiles,
    ["sample-question"] = Containers.SampleQuestions,
};
if (args.Length != 0) throw new ArgumentException("Run from the repository root without arguments.");

foreach (var scenario in scenarios)
{
    var services = new ServiceCollection();
    services.AddLearningServices(new ConfigurationBuilder().Build(), o =>
    {
        o.Storage = StorageMode.InMemory;
    });
    services.AddReferenceDataFixtures();
    services.AddSingleton<ScenarioFixtureBuilder>();
    services.AddSingleton<TimeProvider>(new SeedClock());
    using var provider = services.BuildServiceProvider();
    var store = provider.GetRequiredService<LearningStore>();
    if (scenario == "a1-new")
    {
        await provider.GetRequiredService<LearningProfileService>().GetOrCreateAsync(user, default);
    }
    else
    {
        await provider.GetRequiredService<ScenarioFixtureBuilder>().CreateIfMissingAsync(user, default);
        if (scenario is "a2-review-in-progress" or "a2-review-completed")
        {
            var plan = (await store.ReviewPlans.ListAsync(user.UserId, default)).Single();
            var practice = provider.GetRequiredService<PracticeService>();
            var view = await practice.StartAsync(user, new StartPracticeRequest(PracticeMode.Review, null, null, plan.Id, null), default);
            var session = await store.PracticeSessions.GetRequiredAsync(user.UserId, view.Id, "practice", default);
            var questions = scenario == "a2-review-in-progress" ? session.Questions.Take(2) : session.Questions;
            foreach (var question in questions)
                await practice.SubmitAnswerAsync(user, session.Id, new SubmitAnswerRequest(question.Id, question.CorrectAnswer, $"seed-{question.Id}"), default);
            if (scenario == "a2-review-completed")
                await practice.CompleteAsync(user, session.Id, default);
        }
    }

    var directory = Path.Combine(root, scenario);
    Directory.CreateDirectory(directory);
    var reference = ReferenceDataFixtures.Load();
    await ExportReferenceAsync("grammar-topic", reference.Grammar.Topics, SchemaNames.GrammarTopic);
    await ExportReferenceAsync("tutor-profile", reference.Tutors.Tutors, SchemaNames.TutorProfile);
    await ExportReferenceAsync("sample-question", reference.Questions.Questions, "model.SampleQuestion");
    await ExportAsync(store.Profiles, SchemaNames.LearningProfile);
    await ExportAsync(store.Observations, SchemaNames.LearningObservation);
    await ExportAsync(store.PracticeSessions, SchemaNames.PracticeSession);
    await ExportAsync(store.TutorSessions, SchemaNames.TutorSession);
    await ExportAsync(store.ReviewPlans, SchemaNames.ReviewPlan);
    await ExportAsync(store.WorkflowRuns, SchemaNames.WorkflowRun);

    var importedStore = new InMemoryDocumentStore();
    foreach (var file in Directory.GetFiles(directory, "*.json"))
    {
        var documents = JsonSerializer.Deserialize<JsonElement[]>(await File.ReadAllTextAsync(file))!;
        foreach (var document in documents)
        {
            var partition = document.TryGetProperty("userId", out var userId)
                ? userId.GetString()! : document.GetProperty("language").GetString()!;
            await importedStore.UpsertAsync(containers[Path.GetFileNameWithoutExtension(file)], partition, document.GetProperty("id").GetString()!, JsonNode.Parse(document.GetRawText())!.AsObject(), default);
        }
    }
    await VerifyStoreAsync(scenario, importedStore);
    Console.WriteLine($"Exported and verified {scenario}");

    async Task ExportReferenceAsync<T>(string stem, IReadOnlyList<T> documents, string schema)
    {
        foreach (var document in documents) ContractSchemas.EnsureValid(schema, LearningJson.ToElement(document));
        await File.WriteAllTextAsync(Path.Combine(directory, stem + ".json"),
            JsonSerializer.Serialize(documents, new JsonSerializerOptions(LearningJson.Options) { WriteIndented = true }) + Environment.NewLine);
    }

    async Task ExportAsync<T>(DocumentCollection<T> collection, string schema) where T : UserDocument
    {
        var documents = await collection.ListAsync(user.UserId, default);
        foreach (var session in documents.OfType<TutorSession>())
            foreach (var message in session.Messages)
                message.ToolCalls = message.ToolCalls.Select(call => call with { DurationMs = 0 }).ToList();
        foreach (var document in documents) ContractSchemas.EnsureValid(schema, LearningJson.ToElement(document));
        var filename = Regex.Replace(typeof(T).Name, "([a-z0-9])([A-Z])", "$1-$2").ToLowerInvariant();
        await File.WriteAllTextAsync(Path.Combine(directory, filename + ".json"),
            JsonSerializer.Serialize(documents, new JsonSerializerOptions(LearningJson.Options) { WriteIndented = true }) + Environment.NewLine);
    }
}

async Task VerifyStoreAsync(string scenario, IDocumentStore documentStore)
{
    var verification = new ServiceCollection();
    verification.AddLearningServices(new ConfigurationBuilder().Build(), o => o.Storage = StorageMode.InMemory);
    verification.AddSingleton(documentStore);
    using var restarted = verification.BuildServiceProvider();
    await restarted.GetRequiredService<ReferenceDataLoader>().LoadAsync(default);
    var imported = restarted.GetRequiredService<LearningStore>();
    if (await imported.Profiles.GetAsync(user.UserId, LearningProfileService.ProfileId, default) is null)
        throw new InvalidOperationException($"Missing seed profile in {scenario}.");
    var dashboard = await restarted.GetRequiredService<LearnerInsightsService>().GetDashboardAsync(user, default);
    ContractSchemas.EnsureValid(SchemaNames.DashboardView, LearningJson.ToElement(dashboard));
    if (dashboard.Learner.CurrentLevel != (scenario == "a1-new" ? CefrLevel.A1 : CefrLevel.A2))
        throw new InvalidOperationException($"Wrong level in {scenario}.");
    if (scenario == "a1-new")
    {
        if ((await imported.PracticeSessions.ListAsync(user.UserId, default)).Count != 0 || (await imported.ReviewPlans.ListAsync(user.UserId, default)).Count != 0)
            throw new InvalidOperationException("New learner must have no practice history or review plan.");
    }
    else
    {
        var plan = (await imported.ReviewPlans.ListAsync(user.UserId, default)).Single();
        var review = (await imported.PracticeSessions.ListAsync(user.UserId, default)).Single(s => s.Mode == PracticeMode.Review);
        var expectedAnswers = scenario == "a2-review-ready" ? 0 : scenario == "a2-review-in-progress" ? 2 : review.Questions.Count;
        if (review.Answers.Count != expectedAnswers || plan.Status != (scenario == "a2-review-completed" ? ReviewPlanStatus.Completed : ReviewPlanStatus.Active))
            throw new InvalidOperationException($"Wrong review state in {scenario}.");
    }
}

sealed class SeedClock : TimeProvider
{
    private long _seconds;
    public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero).AddSeconds(Interlocked.Increment(ref _seconds));
}
