using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Knowledge;
using LineLanguageLearn.Learning.Tutor;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Cosmos;
using System.Text.Json;
using LineLanguageLearn.Learning.Contracts;

namespace LineLanguageLearn.Learning.Persistence;

public sealed record ReferenceDataSnapshot(
    GrammarKnowledgeBase Grammar,
    TutorProfileCatalog Tutors,
    SampleQuestionBank Questions)
{
    public static ReferenceDataSnapshot Create(
        IEnumerable<GrammarTopic> grammar,
        IEnumerable<TutorProfile> tutors,
        IEnumerable<SampleQuestion> questions,
        bool requireSampleQuestions)
    {
        var snapshot = new ReferenceDataSnapshot(
            new GrammarKnowledgeBase(grammar), new TutorProfileCatalog(tutors), new SampleQuestionBank(questions));
        if (snapshot.Grammar.Topics.Count == 0 || snapshot.Tutors.Tutors.Count == 0)
            throw new InvalidOperationException("GrammarTopics and TutorProfiles must contain reference data. Apply the project's dev-seeds to a development database, or import reference data into the deployment database. No bundled fallback is available.");
        foreach (var language in snapshot.Grammar.Topics.Select(topic => topic.Language).Distinct())
        {
            if (!snapshot.Tutors.Tutors.Any(tutor => tutor.Language == language))
                throw new InvalidOperationException($"TutorProfiles has no tutor for {language}.");
            if (requireSampleQuestions && !snapshot.Questions.Questions.Any(question => question.Language == language))
                throw new InvalidOperationException($"SampleQuestions has no reference questions for {language}.");
        }
        foreach (var question in snapshot.Questions.Questions)
        {
            var topic = snapshot.Grammar.Find(question.GrammarTopicId);
            if (topic is null || topic.Language != question.Language ||
                question.RuleIds.Any(id => topic.Rules.All(rule => rule.Id != id)))
                throw new InvalidOperationException($"Sample question {question.Id} references an unknown grammar topic, language or rule.");
        }
        return snapshot;
    }
}

/// <summary>Loads a validated snapshot on demand; unsuccessful loads can be retried after seeding.</summary>
public sealed class ReferenceDataLoader(
    IDocumentStore store,
    ILogger<ReferenceDataLoader> logger) : IDisposable
{
    private volatile ReferenceDataSnapshot? _snapshot;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    public ReferenceDataSnapshot Snapshot => _snapshot ??
        throw new InvalidOperationException("Reference data has not been loaded from the database.");

    public async Task LoadAsync(CancellationToken ct)
    {
        if (_snapshot is not null) return;
        await _loadGate.WaitAsync(ct);
        try
        {
            if (_snapshot is not null) return;
            await ReadSnapshotAsync(ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or ContractValidationException or JsonException or CosmosException or HttpRequestException)
        {
            logger.LogError(ex, "Reference data could not be loaded from {Store}; learning requests remain unavailable", store.Name);
            throw new DomainException(DomainErrorKind.Unavailable, "reference-data-unavailable",
                "Learning reference data is missing, invalid or unavailable. Initialize the database and retry.");
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private async Task ReadSnapshotAsync(CancellationToken ct)
    {
        var grammar = new List<GrammarTopic>();
        var tutors = new List<TutorProfile>();
        var questions = new List<SampleQuestion>();
        foreach (var language in Enum.GetValues<TargetLanguage>())
        {
            var partition = LearningJson.ToElement(language).GetString()!;
            grammar.AddRange(await store.ListAsync<GrammarTopic>(Containers.GrammarTopics, partition, ct));
            tutors.AddRange(await store.ListAsync<TutorProfile>(Containers.TutorProfiles, partition, ct));
            var samples = await store.ListAsync<SampleQuestion>(Containers.SampleQuestions, partition, ct);
            questions.AddRange(samples.Where(question => question.Publication is null));
        }
        _snapshot = ReferenceDataSnapshot.Create(grammar, tutors, questions, requireSampleQuestions: true);
        logger.LogInformation("Loaded reference data from {Store}: {GrammarCount} grammar topics, {TutorCount} tutor profiles, {QuestionCount} sample questions",
            store.Name, grammar.Count, tutors.Count, questions.Count);
    }

    public void Dispose() => _loadGate.Dispose();
}
