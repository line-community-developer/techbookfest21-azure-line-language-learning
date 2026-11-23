using System.Security.Cryptography;
using System.Text;
using LineLanguageLearn.Learning.Ai;
using LineLanguageLearn.Learning.Contracts;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Knowledge;
using LineLanguageLearn.Learning.Persistence;

namespace LineLanguageLearn.Learning.Practice;

public sealed class PublishedQuestionService(
    IDocumentStore store,
    GrammarKnowledgeBase knowledgeBase,
    QuestionGenerationService generator,
    TimeProvider clock)
{
    public async Task<string> PublishAsync(string topicId, GeneratedQuestion question, ProviderInfo provider, CancellationToken ct)
    {
        var topic = knowledgeBase.Get(topicId);
        if (question.GrammarTopicId != topic.Id)
            throw DomainException.Invalid("topic-mismatch", "Question does not match the selected grammar topic.");
        // Derive the id from the content so a retry or concurrent save returns the same document.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(LearningJson.Serialize(question)));
        var id = $"published-{Convert.ToHexStringLower(hash)}";
        var partition = Partition(topic.Language);
        if (await store.GetAsync<SampleQuestion>(Containers.SampleQuestions, partition, id, ct) is { Publication: not null })
            return id;

        // The authenticated administrator's save is the approval. Validate the content on the server
        // before publishing, regardless of how the administrator reviewed it.
        var validation = await generator.ValidatePreviewAsync(topicId, question, id, ct, useSemanticJudge: false);
        if (validation.Status != ValidationStatus.Passed)
            throw DomainException.Invalid("question-not-publishable", "Question failed required publication checks.");

        var now = clock.GetUtcNow().ToString("O");
        var document = new SampleQuestion
        {
            Id = id,
            Language = topic.Language,
            GrammarTopicId = topic.Id,
            Type = question.Type,
            Difficulty = question.Difficulty,
            Prompt = question.Prompt,
            Choices = question.Choices,
            CorrectAnswer = question.CorrectAnswer,
            AcceptedAnswers = question.AcceptedAnswers.Where(answer => !string.IsNullOrWhiteSpace(answer)).ToList(),
            ExplanationJa = question.ExplanationJa,
            RuleIds = question.RuleIds,
            Publication = new QuestionPublication(question.InstructionJa, question.SourceIds, provider, validation),
            CreatedAt = now,
            UpdatedAt = now,
        };
        ContractSchemas.EnsureValid("model.SampleQuestion", LearningJson.ToElement(document));
        await store.TryCreateAsync(Containers.SampleQuestions, partition, id, document, ct);
        return id;
    }

    public async Task<List<EvaluatedQuestion>> SelectAsync(TargetLanguage language, CefrLevel level,
        IReadOnlyList<string> topicIds, int count, string sessionId, CancellationToken ct)
    {
        var documents = await store.ListAsync<SampleQuestion>(Containers.SampleQuestions, Partition(language), ct);
        var (minDifficulty, maxDifficulty) = LevelPolicy.DifficultyRange(level);
        return documents.Where(document =>
                document.Publication?.Validation.Status == ValidationStatus.Passed &&
                topicIds.Contains(document.GrammarTopicId) &&
                document.Difficulty >= minDifficulty && document.Difficulty <= maxDifficulty &&
                knowledgeBase.Find(document.GrammarTopicId) is { } topic &&
                LevelPolicy.TopicAllowed(level, topic.Level) &&
                document.RuleIds.All(id => topic.Rules.Any(rule => rule.Id == id)))
            .OrderBy(document => StableHash.Of($"{sessionId}:{document.Id}"))
            .DistinctBy(document => document.Prompt, StringComparer.Ordinal)
            .Take(count)
            .Select(document =>
            {
                var publication = document.Publication!;
                var question = new GeneratedQuestion(document.Type, document.GrammarTopicId, document.Difficulty,
                    publication.InstructionJa, document.Prompt, document.Choices, document.CorrectAnswer,
                    document.AcceptedAnswers, document.ExplanationJa, document.RuleIds, publication.SourceIds);
                return new EvaluatedQuestion(question, publication.Validation, document.Id, publication.Provider);
            }).ToList();
    }

    private static string Partition(TargetLanguage language) => LearningJson.ToElement(language).GetString()!;
}
