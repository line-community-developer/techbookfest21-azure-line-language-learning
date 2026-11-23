using LineLanguageLearn.Learning.Ai;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Knowledge;
using Microsoft.Extensions.Options;

namespace LineLanguageLearn.Learning.Practice;

public sealed record QuestionGenerationSpec(TargetLanguage Language, CefrLevel Level, IReadOnlyList<string> TopicIds, int QuestionCount, string Purpose, string SeedKey);

public sealed record CandidateBatch(string GenerationId, ProviderInfo Provider, List<GeneratedQuestion> Questions, QuestionEvaluationContext Context);

public sealed record EvaluatedQuestion(GeneratedQuestion Question, QuestionValidation Validation, string GenerationId, ProviderInfo Provider);

/// <summary>
/// The single question-generation pipeline shared by learner practice, the tutor tool and the review workflow:
/// retrieve grammar (RAG) → model (Structured Outputs) → schema validation → quality evaluation → retry.
/// </summary>
public sealed class QuestionGenerationService(
    IGrammarRetriever retriever,
    GrammarKnowledgeBase knowledgeBase,
    LearningModelGateway models,
    QuestionQualityEvaluator evaluator,
    LearningProfileService profiles,
    IOptions<LearningOptions> options)
{
    public ProviderInfo Provider => models.Info;

    public async Task<QuestionEvaluationContext> RetrieveContextAsync(QuestionGenerationSpec spec, CancellationToken ct)
    {
        foreach (var topicId in spec.TopicIds)
        {
            var topic = knowledgeBase.Find(topicId) ?? throw DomainException.Invalid("unknown-topic", $"Unknown grammar topic '{topicId}'.");
            if (topic.Language != spec.Language) throw DomainException.Invalid("topic-language-mismatch", $"Topic '{topicId}' is not for {spec.Language}.");
        }

        // RAG: retrieve only the requested grammar entries instead of sending the whole knowledge base.
        var grammar = new List<GrammarSearchHit>();
        foreach (var topicId in spec.TopicIds)
        {
            var topic = knowledgeBase.Get(topicId);
            var hits = await retriever.SearchAsync(new GrammarQuery(spec.Language, topic.Title, LevelPolicy.MaxRetrievalLevel(spec.Level), topicId, 3), ct);
            var exact = hits.FirstOrDefault(h => h.Topic.Id == topicId);
            if (exact is not null) grammar.Add(exact);
        }
        if (grammar.Count == 0) throw new DomainException(DomainErrorKind.Unavailable, "grammar-not-found", "No grammar knowledge could be retrieved for the requested topics.");
        return new QuestionEvaluationContext(spec.Level, spec.TopicIds.ToHashSet(StringComparer.Ordinal), grammar);
    }

    public async Task<CandidateBatch> GenerateCandidatesAsync(LearningProfile profile, QuestionGenerationSpec spec, int count, IReadOnlyCollection<string> avoidPrompts, int attempt, CancellationToken ct)
    {
        var context = await RetrieveContextAsync(spec, ct);
        var observations = await profiles.ListObservationsAsync(profile.UserId, ct);
        var learner = new LearnerSnapshot(
            profile.CurrentLevel,
            profile.WeakTopics.Take(5).ToList(),
            observations.Where(o => o.ObservationType is ObservationType.IncorrectAnswer or ObservationType.GrammarError)
                .Take(3).Select(o => o.Evidence.Text).ToList());

        return await GenerateCandidatesAsync(spec, count, avoidPrompts, attempt, context, learner, ct);
    }

    public async Task<CandidateBatch> GeneratePreviewAsync(string topicId, int count, CancellationToken ct)
    {
        var topic = knowledgeBase.Get(topicId);
        var spec = new QuestionGenerationSpec(topic.Language, topic.Level, [topic.Id], count, "practice", Guid.NewGuid().ToString("N"));
        var context = await RetrieveContextAsync(spec, ct);
        var learner = new LearnerSnapshot(topic.Level, [], []);
        var batch = await GenerateCandidatesAsync(spec, count, [], 0, context, learner, ct);
        if (batch.Questions.Count == 0)
            throw new DomainException(DomainErrorKind.Unavailable, "question-generation-failed", "No question candidates were generated.");
        return batch with { Questions = batch.Questions.Take(count).ToList() };
    }

    public async Task<QuestionValidation> ValidatePreviewAsync(string topicId, GeneratedQuestion question, string generationId, CancellationToken ct, bool useSemanticJudge = true)
    {
        var topic = knowledgeBase.Get(topicId);
        var spec = new QuestionGenerationSpec(topic.Language, topic.Level, [topic.Id], 1, "practice", generationId);
        var context = await RetrieveContextAsync(spec, ct);
        return await evaluator.EvaluateAsync(question, context, generationId, ct, useSemanticJudge);
    }

    private async Task<CandidateBatch> GenerateCandidatesAsync(
        QuestionGenerationSpec spec, int count, IReadOnlyCollection<string> avoidPrompts, int attempt,
        QuestionEvaluationContext context, LearnerSnapshot learner, CancellationToken ct)
    {
        var seed = (int)(StableHash.Of($"{spec.SeedKey}:{attempt}") % int.MaxValue);
        var prompt = new QuestionGenerationPrompt(spec.Language, spec.Level, spec.TopicIds, count, spec.Purpose, context.Grammar, learner, seed, avoidPrompts);
        var result = await models.GenerateQuestionsAsync(prompt, ct);
        var generationId = $"gen-{spec.SeedKey}-{attempt}";
        return new CandidateBatch(generationId, result.Response.Provider, result.Value.Questions, context);
    }

    public async Task<List<EvaluatedQuestion>> EvaluateAsync(CandidateBatch batch, CancellationToken ct)
    {
        var evaluated = new List<EvaluatedQuestion>();
        foreach (var question in batch.Questions)
        {
            var validation = await evaluator.EvaluateAsync(question, batch.Context, batch.GenerationId, ct);
            evaluated.Add(new EvaluatedQuestion(question, validation, batch.GenerationId, batch.Provider));
        }
        return evaluated;
    }

    /// <summary>Generates until enough questions pass validation or the attempt budget is used up.</summary>
    public async Task<List<EvaluatedQuestion>> GenerateValidatedAsync(LearningProfile profile, QuestionGenerationSpec spec, CancellationToken ct)
    {
        var accepted = new List<EvaluatedQuestion>();
        var seenPrompts = new HashSet<string>(StringComparer.Ordinal);
        var maxAttempts = Math.Max(1, options.Value.QuestionGeneration.MaxGenerationAttempts);
        for (var attempt = 0; attempt < maxAttempts && accepted.Count < spec.QuestionCount; attempt++)
        {
            var batch = await GenerateCandidatesAsync(profile, spec, spec.QuestionCount - accepted.Count, seenPrompts, attempt, ct);
            foreach (var evaluated in await EvaluateAsync(batch, ct))
            {
                seenPrompts.Add(evaluated.Question.Prompt);
                if (evaluated.Validation.Status == ValidationStatus.Passed && accepted.Count < spec.QuestionCount
                    && accepted.All(a => a.Question.Prompt != evaluated.Question.Prompt))
                {
                    accepted.Add(evaluated);
                }
            }
        }
        if (accepted.Count == 0)
        {
            throw new DomainException(DomainErrorKind.Unavailable, "question-generation-failed", "No generated question passed validation.");
        }
        return accepted;
    }

    public static List<PracticeQuestion> ToPracticeQuestions(string sessionId, IEnumerable<EvaluatedQuestion> questions) =>
        questions.Select((e, index) => new PracticeQuestion
        {
            Id = $"{sessionId}-q{index + 1}",
            SessionId = sessionId,
            Order = index,
            Type = e.Question.Type,
            GrammarTopicId = e.Question.GrammarTopicId,
            Difficulty = e.Question.Difficulty,
            InstructionJa = e.Question.InstructionJa,
            Prompt = e.Question.Prompt,
            Choices = e.Question.Choices,
            CorrectAnswer = e.Question.CorrectAnswer,
            AcceptedAnswers = e.Question.AcceptedAnswers,
            ExplanationJa = e.Question.ExplanationJa,
            RuleIds = e.Question.RuleIds,
            SourceIds = e.Question.SourceIds,
            GeneratedBy = e.Provider,
            GenerationId = e.GenerationId,
            Validation = e.Validation,
        }).ToList();
}
