using LineLanguageLearn.Learning.Ai;
using LineLanguageLearn.Learning.Ai.Mock;
using LineLanguageLearn.Learning.Ai.Foundry;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Identity;
using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Knowledge;
using LineLanguageLearn.Learning.Messaging;
using LineLanguageLearn.Learning.Persistence;
using LineLanguageLearn.Learning.Tutor;
using LineLanguageLearn.Learning.Workflows;
using LineLanguageLearn.SeedData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LineLanguageLearn.Tests;

/// <summary>Deterministic clock: every read advances one second, so event ordering is stable.</summary>
public sealed class SteppingClock(DateTimeOffset start) : TimeProvider
{
    private long _ticks;
    public override DateTimeOffset GetUtcNow() => start.AddSeconds(Interlocked.Increment(ref _ticks));
}

/// <summary>Composes the real application services with in-memory storage and mock AI (no network).</summary>
public sealed class TestApp : IDisposable
{
    public static readonly UserContext Learner = new("test-learner", "Test Learner", "test");
    public static readonly UserContext OtherLearner = new("other-learner", "Other Learner", "test");

    private readonly ServiceProvider _provider;

    public TestApp(Action<IServiceCollection>? overrides = null, Action<LearningOptions>? configure = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Learning:Storage"] = "InMemory",
        }).Build();
        var services = new ServiceCollection();
        services.AddLearningServices(configuration, configure);
        services.AddReferenceDataFixtures();
        var selected = new LearningOptions();
        configure?.Invoke(selected);
        if (selected.AiProvider == AiProviderMode.Foundry)
            services.AddSingleton<ILearningModel, FoundryLearningModel>();
        if (selected.TutorAgent == TutorAgentMode.FoundryAgentService)
            services.AddSingleton<ITutorAgentRuntime, FoundryAgentTutorRuntime>();
        services.AddSingleton<ScenarioFixtureBuilder>();
        // Tests drive the same workflow steps synchronously instead of through the Durable host.
        services.AddSingleton<IWorkflowScheduler, InProcessWorkflowScheduler>();
        services.AddSingleton<TimeProvider>(new SteppingClock(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero)));
        overrides?.Invoke(services);
        _provider = services.BuildServiceProvider();
    }

    /// <summary>Replaces the model with a ScriptedModel around the mock model.</summary>
    public static Action<IServiceCollection> WithScriptedModel => services =>
    {
        services.AddSingleton(sp => new ScriptedModel(new MockLearningModel(sp.GetRequiredService<SampleQuestionBank>())));
        services.AddSingleton<ILearningModel>(sp => sp.GetRequiredService<ScriptedModel>());
    };

    public T Get<T>() where T : notnull => _provider.GetRequiredService<T>();

    public static string SourcePath(params string[] segments)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "swallowkit.config.js")))
            root = root.Parent;
        return Path.Combine([root?.FullName ?? throw new InvalidOperationException("Repository root not found."), .. segments]);
    }

    public LearningStore Store => Get<LearningStore>();
    public MockMessagingAdapter Outbox => Get<MockMessagingAdapter>();

    public void Dispose() => _provider.Dispose();
}

/// <summary>Wraps a model to inject failures or replace outputs in specific operations.</summary>
public sealed class ScriptedModel(ILearningModel inner) : ILearningModel
{
    public int AnalyzeFailuresRemaining { get; set; }
    public Func<QuestionGenerationPrompt, string>? QuestionOverride { get; set; }
    public Func<ConversationAnalysisPrompt, string>? AnalysisOverride { get; set; }
    public int AnalyzeCalls { get; private set; }

    public ProviderInfo Info => inner.Info;

    public Task<ModelResponse> GenerateQuestionsAsync(QuestionGenerationPrompt prompt, CancellationToken ct) =>
        QuestionOverride is null ? inner.GenerateQuestionsAsync(prompt, ct) : Task.FromResult(new ModelResponse(QuestionOverride(prompt), inner.Info, null));

    public Task<ModelResponse> AnalyzeConversationAsync(ConversationAnalysisPrompt prompt, CancellationToken ct)
    {
        AnalyzeCalls++;
        if (AnalyzeFailuresRemaining > 0)
        {
            AnalyzeFailuresRemaining--;
            throw new HttpRequestException("Simulated model outage");
        }
        return AnalysisOverride is null ? inner.AnalyzeConversationAsync(prompt, ct) : Task.FromResult(new ModelResponse(AnalysisOverride(prompt), inner.Info, null));
    }

    public int JudgeCalls { get; private set; }

    public Task<ModelResponse> JudgeQuestionAsync(QuestionJudgePrompt prompt, CancellationToken ct)
    {
        JudgeCalls++;
        return inner.JudgeQuestionAsync(prompt, ct);
    }
}
