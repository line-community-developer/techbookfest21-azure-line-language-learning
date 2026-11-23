using System.Text.Json;
using LineLanguageLearn.Learning.Ai;
using LineLanguageLearn.Learning.Ai.Mock;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Persistence;
using LineLanguageLearn.Learning.Tutor;
using Microsoft.Extensions.DependencyInjection;

namespace LineLanguageLearn.SeedData;

/// <summary>Offline fixture input only; this source is not compiled into Functions.</summary>
public static class ReferenceDataFixtures
{
    public static ReferenceDataSnapshot Load()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ReferenceData");
        return ReferenceDataSnapshot.Create(
            Read<GrammarTopic>("grammar-topic"),
            Read<TutorProfile>("tutor-profile"),
            Read<SampleQuestion>("sample-question"),
            requireSampleQuestions: true);

        List<T> Read<T>(string stem) =>
            JsonSerializer.Deserialize<List<T>>(File.ReadAllText(Path.Combine(directory, stem + ".json")), LearningJson.Options)
            ?? throw new InvalidOperationException($"Invalid reference fixture {stem}.");
    }

    public static void AddReferenceDataFixtures(this IServiceCollection services, bool includeAi = true)
    {
        var data = Load();
        services.AddSingleton(data.Grammar);
        services.AddSingleton(data.Tutors);
        services.AddSingleton(data.Questions);
        if (includeAi)
        {
            services.AddSingleton<ILearningModel, MockLearningModel>();
            services.AddSingleton<ITutorAgentRuntime, MockTutorAgent>();
        }
    }
}
