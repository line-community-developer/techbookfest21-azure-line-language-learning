using LineLanguageLearn.Learning.Ai;
using LineLanguageLearn.Learning.Api;
using LineLanguageLearn.Learning.Contracts;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Knowledge;
using LineLanguageLearn.Learning.Practice;
using LineLanguageLearn.Learning.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace LineLanguageLearn.Tests;

public sealed class AdminQuestionTests
{
    private const string TopicId = "it-definite-articles";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Generation_and_explicit_validation_are_separate(bool includePassingQuestion)
    {
        using var app = new TestApp(TestApp.WithScriptedModel, options => options.QuestionGeneration.UseSemanticJudge = false);
        var model = app.Get<ScriptedModel>();
        var topic = app.Get<GrammarKnowledgeBase>().Get(TopicId);
        var sample = app.Get<SampleQuestionBank>().Questions.First(question => question.GrammarTopicId == TopicId);
        var passing = new GeneratedQuestion(sample.Type, TopicId, sample.Difficulty, "空欄を埋めてください。",
            sample.Prompt, sample.Choices.ToList(), sample.CorrectAnswer, sample.AcceptedAnswers.ToList(),
            sample.ExplanationJa, sample.RuleIds.ToList(), [TopicId]);
        var failing = passing with { Prompt = "問題文に空欄がない" };
        var calls = 0;
        model.QuestionOverride = prompt =>
        {
            calls++;
            Assert.Equal(topic.Language, prompt.Language);
            Assert.Equal(topic.Level, prompt.Level);
            Assert.Equal([TopicId], prompt.TopicIds);
            Assert.Equal(2, prompt.QuestionCount);
            Assert.Empty(prompt.Learner.WeakTopicIds);
            Assert.Empty(prompt.Learner.RecentMistakeExamples);
            return LearningJson.Serialize(new GeneratedQuestionBatch(includePassingQuestion ? [passing, failing] : [failing]));
        };

        var generator = app.Get<QuestionGenerationService>();
        var batch = await generator.GeneratePreviewAsync(TopicId, 2, default);

        Assert.Equal(1, calls);
        Assert.Equal(0, model.JudgeCalls);
        Assert.Equal(includePassingQuestion ? 2 : 1, batch.Questions.Count);
        var response = new { topicId = TopicId, questions = batch.Questions.Select(question => new { question, batch.GenerationId, batch.Provider }) };
        ContractSchemas.EnsureValid("api.AdminQuestionGenerationResponse", LearningJson.ToElement(response));

        var rejectedQuestion = Assert.Single(batch.Questions, question => question.Prompt == failing.Prompt);
        var validation = await generator.ValidatePreviewAsync(TopicId, rejectedQuestion, batch.GenerationId, default);
        Assert.Equal(ValidationStatus.Failed, validation.Status);
        Assert.Contains(validation.Checks, check => check.Id == "prompt-has-single-blank" && !check.Passed);
        Assert.Equal(0, model.JudgeCalls);
        ContractSchemas.EnsureValid("api.QuestionValidation", LearningJson.ToElement(validation));

        if (includePassingQuestion)
        {
            var request = new AdminQuestionValidationRequest(TopicId, passing, batch.GenerationId);
            ContractSchemas.EnsureValid("api.AdminQuestionValidationRequest", LearningJson.ToElement(request));
            validation = await generator.ValidatePreviewAsync(request.TopicId, request.Question, request.GenerationId, default);
            Assert.Equal(ValidationStatus.Passed, validation.Status);
            Assert.NotNull(validation.Semantic);
            Assert.Equal(1, model.JudgeCalls);
            Assert.Equal(1, calls);
        }
    }

    [Fact]
    public async Task Published_question_is_idempotent_and_available_to_new_learner_practice_without_ai()
    {
        using var app = new TestApp(services => services.AddSingleton<ILearningModel, UnavailableLearningModel>());
        var sample = app.Get<SampleQuestionBank>().Questions.First(question => question.GrammarTopicId == TopicId);
        var candidate = new GeneratedQuestion(sample.Type, TopicId, sample.Difficulty, "空欄を埋めてください。",
            sample.Prompt, sample.Choices, sample.CorrectAnswer, sample.AcceptedAnswers,
            sample.ExplanationJa, sample.RuleIds, [TopicId]);
        var provider = new ProviderInfo(ProviderKind.Foundry, "learning");
        var published = app.Get<PublishedQuestionService>();
        var store = app.Get<IDocumentStore>();

        var id = await published.PublishAsync(TopicId, candidate, provider, default);
        Assert.Equal(id, await published.PublishAsync(TopicId, candidate, provider, default));
        var stored = Assert.Single(await store.ListAsync<SampleQuestion>(Containers.SampleQuestions, "it", default),
            question => question.Publication is not null);
        Assert.Equal(id, stored.Id);
        Assert.NotNull(stored.CreatedAt);
        ContractSchemas.EnsureValid("model.SampleQuestion", LearningJson.ToElement(stored));

        var practice = app.Get<PracticeService>();
        var session = await practice.StartAsync(TestApp.Learner, new StartPracticeRequest(PracticeMode.Topic, [TopicId], CefrLevel.A1, null, 5), default);
        var question = Assert.Single(session.Questions);
        Assert.Equal(candidate.Prompt, question.Prompt);
        Assert.Equal(candidate.InstructionJa, question.InstructionJa);
        Assert.DoesNotContain("correctAnswer", LearningJson.Serialize(session));
        var answer = await practice.SubmitAnswerAsync(TestApp.Learner, session.Id,
            new SubmitAnswerRequest(question.Id, candidate.CorrectAnswer, null), default);
        Assert.True(answer.IsCorrect);
        var persistedSession = await app.Store.PracticeSessions.GetRequiredAsync(TestApp.Learner.UserId, session.Id, "session", default);
        ContractSchemas.EnsureValid(SchemaNames.PracticeSession, LearningJson.ToElement(persistedSession));

        var invalid = candidate with { Prompt = "問題文に空欄がない" };
        var error = await Assert.ThrowsAsync<DomainException>(() => published.PublishAsync(TopicId, invalid, provider, default));
        Assert.Equal("question-not-publishable", error.Code);
        Assert.Single(await store.ListAsync<SampleQuestion>(Containers.SampleQuestions, "it", default),
            question => question.Publication is not null);
    }

    [Fact]
    public async Task Preview_rejects_unknown_topic()
    {
        using var app = new TestApp();
        var error = await Assert.ThrowsAsync<DomainException>(
            () => app.Get<QuestionGenerationService>().GeneratePreviewAsync("unknown", 5, default));
        Assert.Equal(DomainErrorKind.NotFound, error.Kind);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Request_contract_rejects_out_of_range_count(int count)
    {
        var request = LearningJson.ToElement(new AdminQuestionGenerationRequest(TopicId, count));
        Assert.NotEmpty(ContractSchemas.Validate("api.AdminQuestionGenerationRequest", request));
    }

    [Fact]
    public async Task Preview_reports_unavailable_ai()
    {
        using var app = new TestApp(services => services.AddSingleton<ILearningModel, UnavailableLearningModel>());
        var error = await Assert.ThrowsAsync<DomainException>(
            () => app.Get<QuestionGenerationService>().GeneratePreviewAsync(TopicId, 5, default));
        Assert.Equal(DomainErrorKind.Unavailable, error.Kind);
        Assert.Equal("ai-not-configured", error.Code);
    }
}
