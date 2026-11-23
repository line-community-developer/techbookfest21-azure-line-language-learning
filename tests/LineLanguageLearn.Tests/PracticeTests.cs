using System.Text.Json;
using LineLanguageLearn.Learning.Ai;
using LineLanguageLearn.Learning.Api;
using LineLanguageLearn.Learning.Contracts;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Practice;

namespace LineLanguageLearn.Tests;

public class PracticeTests
{
    private static async Task<(TestApp App, PracticeSessionView View)> StartAsync(params string[] topics)
    {
        var app = new TestApp();
        var view = await app.Get<PracticeService>().StartAsync(TestApp.Learner,
            new StartPracticeRequest(PracticeMode.Topic, topics.ToList(), CefrLevel.A2, null, 5), default);
        return (app, view);
    }

    private static PracticeQuestion StoredQuestion(TestApp app, string sessionId, string questionId) =>
        app.Store.PracticeSessions.GetAsync(TestApp.Learner.UserId, sessionId, default).Result!.Questions.Single(q => q.Id == questionId);

    [Fact]
    public async Task Generated_questions_are_grounded_validated_and_match_requested_topics()
    {
        var (app, view) = await StartAsync("it-verb-a-infinitive", "it-verb-di-infinitive");
        using var _ = app;
        Assert.Equal(5, view.Questions.Count);
        Assert.All(view.Questions, q => Assert.Contains(q.Topic.TopicId, new[] { "it-verb-a-infinitive", "it-verb-di-infinitive" }));
        var stored = await app.Store.PracticeSessions.GetAsync(TestApp.Learner.UserId, view.Id, default);
        Assert.All(stored!.Questions, q =>
        {
            Assert.Equal(ValidationStatus.Passed, q.Validation.Status);
            Assert.Contains(q.GrammarTopicId, q.SourceIds);
            Assert.NotNull(q.Validation.Semantic);
        });
        ContractSchemas.EnsureValid(SchemaNames.PracticeSession, LearningJson.ToElement(stored));
        ContractSchemas.EnsureValid(SchemaNames.PracticeSessionView, LearningJson.ToElement(view));
    }

    [Fact]
    public async Task Pre_answer_response_contains_no_answer_key()
    {
        var (app, view) = await StartAsync("it-passato-prossimo-auxiliary");
        using var _ = app;
        var json = LearningJson.Serialize(view);
        Assert.DoesNotContain("correctAnswer", json);
        Assert.DoesNotContain("acceptedAnswers", json);
        Assert.DoesNotContain("explanationJa", json);
        Assert.All(view.Questions, q => Assert.Null(q.Result));

        foreach (var cloze in view.Questions.Where(q => q.Type == QuestionType.Cloze))
        {
            var key = StoredQuestion(app, view.Id, cloze.Id).CorrectAnswer;
            var questionJson = LearningJson.Serialize(cloze);
            Assert.DoesNotContain($"\"{key}\"", questionJson);
        }
    }

    [Fact]
    public async Task Correct_answer_is_graded_recorded_and_raises_mastery()
    {
        var (app, view) = await StartAsync("it-definite-articles");
        using var _ = app;
        var question = view.Questions[0];
        var key = StoredQuestion(app, view.Id, question.Id).CorrectAnswer;

        var result = await app.Get<PracticeService>().SubmitAnswerAsync(TestApp.Learner, view.Id, new SubmitAnswerRequest(question.Id, key, null), default);

        Assert.True(result.IsCorrect);
        Assert.Equal(FeedbackCode.Correct, result.FeedbackCode);
        Assert.True(result.Mastery.After > result.Mastery.Before);
        Assert.NotEmpty(result.GrammarReferences);
        ContractSchemas.EnsureValid(SchemaNames.AnswerResult, LearningJson.ToElement(result));

        var observation = (await app.Store.Observations.ListAsync(TestApp.Learner.UserId, default)).Single();
        Assert.Equal(ObservationType.CorrectAnswer, observation.ObservationType);
        Assert.Equal(ObservationCertainty.Deterministic, observation.Certainty);
        Assert.Equal(question.Id, observation.SourceItemId);
        var profile = await app.Get<LearningProfileService>().GetOrCreateAsync(TestApp.Learner, default);
        Assert.Equal(1, profile.MasteryByTopic["it-definite-articles"].Correct);
    }

    [Fact]
    public async Task Wrong_answer_is_graded_and_lowers_mastery()
    {
        var (app, view) = await StartAsync("it-verb-di-infinitive");
        using var _ = app;
        var question = view.Questions[0];

        var result = await app.Get<PracticeService>().SubmitAnswerAsync(TestApp.Learner, view.Id, new SubmitAnswerRequest(question.Id, "zzz", null), default);

        Assert.False(result.IsCorrect);
        Assert.Equal(FeedbackCode.Incorrect, result.FeedbackCode);
        Assert.True(result.Mastery.After < result.Mastery.Before);
        Assert.Equal(StoredQuestion(app, view.Id, question.Id).CorrectAnswer, result.CorrectAnswer);
        var observation = (await app.Store.Observations.ListAsync(TestApp.Learner.UserId, default)).Single();
        Assert.Equal(ObservationType.IncorrectAnswer, observation.ObservationType);
    }

    [Fact]
    public async Task Duplicate_answers_do_not_update_mastery_twice()
    {
        var (app, view) = await StartAsync("it-verb-a-infinitive");
        using var _ = app;
        var practice = app.Get<PracticeService>();
        var question = view.Questions[0];

        var first = await practice.SubmitAnswerAsync(TestApp.Learner, view.Id, new SubmitAnswerRequest(question.Id, "di", "attempt-1"), default);
        var second = await practice.SubmitAnswerAsync(TestApp.Learner, view.Id, new SubmitAnswerRequest(question.Id, "a", "attempt-2"), default);

        Assert.False(first.AlreadyAnswered);
        Assert.True(second.AlreadyAnswered);
        Assert.Equal(first.IsCorrect, second.IsCorrect);
        Assert.Single(await app.Store.Observations.ListAsync(TestApp.Learner.UserId, default));
        var profile = await app.Get<LearningProfileService>().GetOrCreateAsync(TestApp.Learner, default);
        Assert.Equal(first.Mastery.After, profile.MasteryByTopic["it-verb-a-infinitive"].Score);
        Assert.Equal(1, profile.MasteryByTopic["it-verb-a-infinitive"].Attempts);
    }

    [Fact]
    public async Task Completing_a_session_produces_summary_and_is_idempotent()
    {
        var (app, view) = await StartAsync("it-definite-articles");
        using var _ = app;
        var practice = app.Get<PracticeService>();
        foreach (var q in view.Questions.Take(3))
        {
            await practice.SubmitAnswerAsync(TestApp.Learner, view.Id, new SubmitAnswerRequest(q.Id, StoredQuestion(app, view.Id, q.Id).CorrectAnswer, null), default);
        }
        var done = await practice.CompleteAsync(TestApp.Learner, view.Id, default);
        var again = await practice.CompleteAsync(TestApp.Learner, view.Id, default);

        Assert.Equal(PracticeStatus.Completed, done.Status);
        Assert.Equal(3, done.Summary!.Correct);
        Assert.Equal(3, done.Summary.Answered);
        Assert.Equal(done.Summary.Correct, again.Summary!.Correct);
        await Assert.ThrowsAsync<DomainException>(() => practice.SubmitAnswerAsync(TestApp.Learner, view.Id, new SubmitAnswerRequest(view.Questions[4].Id, "x", null), default));
    }

    [Fact]
    public async Task Sessions_belong_to_their_learner()
    {
        var (app, view) = await StartAsync("it-definite-articles");
        using var _ = app;
        var ex = await Assert.ThrowsAsync<DomainException>(() => app.Get<PracticeService>().GetAsync(TestApp.OtherLearner, view.Id, default));
        Assert.Equal(DomainErrorKind.NotFound, ex.Kind);
    }

    [Fact]
    public async Task Mock_generation_varies_by_topic_and_is_reproducible()
    {
        using var app = new TestApp();
        var generator = app.Get<QuestionGenerationService>();
        var profile = await app.Get<LearningProfileService>().GetOrCreateAsync(TestApp.Learner, default);
        profile.CurrentLevel = CefrLevel.A2;
        var spec = new QuestionGenerationSpec(TargetLanguage.It, CefrLevel.A2, ["it-piacere"], 3, "practice", "seed-1");

        var a = await generator.GenerateCandidatesAsync(profile, spec, 3, [], 0, default);
        var b = await generator.GenerateCandidatesAsync(profile, spec, 3, [], 0, default);
        var c = await generator.GenerateCandidatesAsync(profile, spec with { TopicIds = ["it-andare-in"] }, 3, [], 0, default);

        Assert.Equal(a.Questions.Select(q => q.Prompt), b.Questions.Select(q => q.Prompt));
        Assert.All(c.Questions, q => Assert.Equal("it-andare-in", q.GrammarTopicId));
        Assert.Empty(a.Questions.Select(q => q.Prompt).Intersect(c.Questions.Select(q => q.Prompt)));
    }

    [Fact]
    public async Task Recommended_mode_targets_weak_topics()
    {
        using var app = new TestApp();
        var profiles = app.Get<LearningProfileService>();
        await profiles.GetOrCreateAsync(TestApp.Learner, default);
        var weakObservation = new LearningObservation
        {
            Id = "obs-test-1", UserId = TestApp.Learner.UserId, GrammarTopicId = "it-andare-in", SourceType = ObservationSource.Practice,
            SourceId = "s", SourceItemId = "q", ObservationType = ObservationType.IncorrectAnswer, Certainty = ObservationCertainty.Deterministic,
            Confidence = 1, Evidence = new ObservationEvidence("x", "in", "a"), DetectedBy = "test", OccurredAt = "2026-10-01T00:00:00Z",
        };
        await profiles.RecordObservationsAsync(TestApp.Learner.UserId, [weakObservation, Clone(weakObservation, "obs-test-2")], default);

        var view = await app.Get<PracticeService>().StartAsync(TestApp.Learner, new StartPracticeRequest(PracticeMode.Recommended, null, null, null, 3), default);

        Assert.Contains(view.Topics, t => t.TopicId == "it-andare-in");
    }

    private static LearningObservation Clone(LearningObservation o, string id)
    {
        var copy = LearningJson.Clone(o);
        copy.Id = id;
        return copy;
    }
}

public class GradingAndMasteryTests
{
    private static PracticeQuestion Question(QuestionType type, string answer, params string[] choices) => new()
    {
        Id = "q1", Type = type, CorrectAnswer = answer, AcceptedAnswers = [answer], Choices = choices.ToList(),
    };

    [Theory]
    [InlineData("  Dall’ ", "dall'")]
    [InlineData("LO", "lo")]
    [InlineData("sono   andato.", "sono andato")]
    [InlineData("è!", "è")]
    public void Normalizer_applies_explicit_rules(string input, string expected) => Assert.Equal(expected, AnswerNormalizer.Normalize(input));

    [Fact]
    public void Cloze_accepts_listed_answers_and_flags_missing_accents()
    {
        var q = Question(QuestionType.Cloze, "è");
        Assert.True(AnswerGrader.Grade(q, " È ").IsCorrect);
        var accent = AnswerGrader.Grade(q, "e");
        Assert.False(accent.IsCorrect);
        Assert.Equal(FeedbackCode.AccentMismatch, accent.FeedbackCode);
        Assert.Equal(FeedbackCode.Incorrect, AnswerGrader.Grade(q, "ha").FeedbackCode);
    }

    [Fact]
    public void Multiple_choice_requires_an_offered_choice()
    {
        var q = Question(QuestionType.MultipleChoice, "dall'", "dall'", "dal", "dallo", "da l'");
        Assert.True(AnswerGrader.Grade(q, "dall’").IsCorrect);
        Assert.False(AnswerGrader.Grade(q, "dal").IsCorrect);
        Assert.False(AnswerGrader.Grade(q, "").IsCorrect);
    }

    private static LearningObservation Obs(string id, ObservationType type, ObservationCertainty certainty = ObservationCertainty.Deterministic, double confidence = 1) => new()
    {
        Id = id, UserId = "u", GrammarTopicId = "t", ObservationType = type, Certainty = certainty, Confidence = confidence,
        OccurredAt = $"2026-10-01T00:00:{id.PadLeft(2, '0')}Z",
    };

    [Fact]
    public void Mastery_rules_are_deterministic_and_weight_ai_findings_less()
    {
        Assert.Equal(0.6, MasteryCalculator.Apply(0.5, Obs("1", ObservationType.CorrectAnswer)), 4);
        Assert.Equal(0.375, MasteryCalculator.Apply(0.5, Obs("1", ObservationType.IncorrectAnswer)), 4);
        var aiError = MasteryCalculator.Apply(0.5, Obs("1", ObservationType.GrammarError, ObservationCertainty.AiInferred, 0.8));
        Assert.InRange(aiError, 0.375 + 0.0001, 0.5 - 0.0001);
        Assert.Equal(0.5, MasteryCalculator.Apply(0.5, Obs("1", ObservationType.NewExpression)));
    }

    [Fact]
    public void Mastery_status_thresholds()
    {
        var weak = MasteryCalculator.Compute([Obs("1", ObservationType.IncorrectAnswer), Obs("2", ObservationType.IncorrectAnswer)]);
        Assert.Equal(MasteryStatus.Weak, weak["t"].Status);
        var strong = MasteryCalculator.Compute(Enumerable.Range(1, 4).Select(i => Obs(i.ToString(), ObservationType.CorrectAnswer)));
        Assert.Equal(MasteryStatus.Strong, strong["t"].Status);
        Assert.Equal(4, strong["t"].Correct);
        var developing = MasteryCalculator.Compute([Obs("1", ObservationType.CorrectAnswer)]);
        Assert.Equal(MasteryStatus.Developing, developing["t"].Status);
        Assert.Equal(MasteryStatus.Unknown, MasteryCalculator.StatusFor(0.5, 0));
    }

    [Fact]
    public void Mastery_is_order_stable_regardless_of_input_order()
    {
        var items = new[] { Obs("1", ObservationType.CorrectAnswer), Obs("2", ObservationType.IncorrectAnswer), Obs("3", ObservationType.CorrectAnswer) };
        Assert.Equal(MasteryCalculator.Compute(items)["t"].Score, MasteryCalculator.Compute(items.Reverse())["t"].Score);
    }
}

public class StructuredOutputTests
{
    [Fact]
    public void Valid_batch_parses()
    {
        var json = """{"questions":[{"type":"cloze","grammarTopicId":"it-piacere","difficulty":2,"instructionJa":"x","prompt":"Ti ___ ?","choices":[],"correctAnswer":"piace","acceptedAnswers":["piace"],"explanationJa":"piace","ruleIds":["it-piacere-r1"],"sourceIds":["it-piacere"]}]}""";
        var batch = StructuredOutput.Parse<GeneratedQuestionBatch>(SchemaNames.GeneratedQuestionBatch, json);
        Assert.Equal(QuestionType.Cloze, batch.Questions[0].Type);
    }

    [Theory]
    [InlineData("""{"questions":[{"type":"essay"}]}""", "schema-mismatch")]
    [InlineData("""{"questions":[],"extra":1}""", "schema-mismatch")]
    [InlineData("not json", "invalid-json")]
    public void Invalid_model_output_is_rejected_before_domain_logic(string json, string code)
    {
        var ex = Assert.Throws<StructuredOutputException>(() => StructuredOutput.Parse<GeneratedQuestionBatch>(SchemaNames.GeneratedQuestionBatch, json));
        Assert.Equal(code, ex.Code);
    }

    [Fact]
    public void Model_facing_schema_is_strict_compatible()
    {
        var schema = ContractSchemas.GetModelFacingSchema(SchemaNames.GeneratedQuestionBatch).ToJsonString();
        Assert.DoesNotContain("$schema", schema);
        Assert.DoesNotContain("minimum", schema);
        Assert.Contains("\"additionalProperties\":false", schema);
        using var doc = JsonDocument.Parse(schema);
        var item = doc.RootElement.GetProperty("properties").GetProperty("questions").GetProperty("items");
        Assert.Equal(item.GetProperty("properties").EnumerateObject().Count(), item.GetProperty("required").GetArrayLength());
    }
}
