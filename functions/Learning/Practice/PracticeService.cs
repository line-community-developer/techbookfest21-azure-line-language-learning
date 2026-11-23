using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Api;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Identity;
using LineLanguageLearn.Learning.Knowledge;
using LineLanguageLearn.Learning.Persistence;

namespace LineLanguageLearn.Learning.Practice;

public sealed record PracticeSessionSpec(
    string? SessionId,
    PracticeMode Mode,
    IReadOnlyList<string> TopicIds,
    CefrLevel? Level,
    int QuestionCount,
    PracticeOrigin Origin,
    string? ReviewPlanId);

public sealed record PracticeSessionCreation(PracticeSession Session, bool Created);

/// <summary>Practice use cases: start a session, answer (deterministic grading), complete.</summary>
public sealed class PracticeService(
    LearningStore store,
    LearningProfileService profiles,
    QuestionGenerationService generator,
    PublishedQuestionService publishedQuestions,
    GrammarKnowledgeBase knowledgeBase,
    ViewMapper views,
    TimeProvider clock)
{
    public const int DefaultQuestionCount = 5;

    public async Task<PracticeSessionView> StartAsync(UserContext user, StartPracticeRequest request, CancellationToken ct)
    {
        var profile = await profiles.GetOrCreateAsync(user, ct);
        var count = request.QuestionCount ?? DefaultQuestionCount;
        PracticeSession session;
        switch (request.Mode)
        {
            case PracticeMode.Review:
                if (string.IsNullOrEmpty(request.ReviewPlanId)) throw DomainException.Invalid("review-plan-required", "reviewPlanId is required for review mode.");
                session = await StartReviewAsync(user, profile, request.ReviewPlanId, count, ct);
                break;
            case PracticeMode.Topic:
                if (request.TopicIds is not { Count: > 0 }) throw DomainException.Invalid("topics-required", "topicIds are required for topic mode.");
                session = (await CreateSessionAsync(user, new PracticeSessionSpec(null, PracticeMode.Topic, request.TopicIds, request.Level, count,
                    new PracticeOrigin(PracticeOriginKind.Learner, null), null), ct)).Session;
                break;
            default:
                var topics = request.TopicIds is { Count: > 0 } ? request.TopicIds : RecommendTopics(profile);
                session = (await CreateSessionAsync(user, new PracticeSessionSpec(null, PracticeMode.Recommended, topics, request.Level, count,
                    new PracticeOrigin(PracticeOriginKind.Learner, null), null), ct)).Session;
                break;
        }
        return views.PracticeSession(session, await profiles.GetOrCreateAsync(user, ct));
    }

    /// <summary>Weakest topics first, then developing, then unseen topics at the learner's level.</summary>
    public List<string> RecommendTopics(LearningProfile profile, int max = 2)
    {
        var candidates = profile.WeakTopics
            .Concat(profile.MasteryByTopic.Values.Where(m => m.Status == MasteryStatus.Developing).OrderBy(m => m.Score).Select(m => m.TopicId))
            .Concat(knowledgeBase.ForLanguage(profile.TargetLanguage)
                .Where(t => t.Level.Rank() <= profile.CurrentLevel.Rank() && !profile.MasteryByTopic.ContainsKey(t.Id))
                .OrderBy(t => t.Level.Rank()).Select(t => t.Id))
            .Concat(knowledgeBase.ForLanguage(profile.TargetLanguage).Select(t => t.Id))
            .Distinct()
            .Where(id => knowledgeBase.Find(id) is { } t && LevelPolicy.TopicAllowed(profile.CurrentLevel, t.Level))
            .Take(max)
            .ToList();
        return candidates;
    }

    private async Task<PracticeSession> StartReviewAsync(UserContext user, LearningProfile profile, string reviewPlanId, int count, CancellationToken ct)
    {
        var plan = await store.ReviewPlans.GetRequiredAsync(user.UserId, reviewPlanId, "Review plan", ct);
        foreach (var existingId in plan.PracticeSessionIds.AsEnumerable().Reverse())
        {
            var existing = await store.PracticeSessions.GetAsync(user.UserId, existingId, ct);
            if (existing is { Status: PracticeStatus.InProgress }) return existing;
        }
        var topics = plan.Items.OrderBy(i => i.Priority).ThenByDescending(i => i.PriorityScore).Select(i => i.TopicId).Take(3).ToList();
        var creation = await CreateSessionAsync(user, new PracticeSessionSpec(
            $"ps-review-{plan.Id}-{plan.PracticeSessionIds.Count + 1}", PracticeMode.Review, topics, profile.CurrentLevel, count,
            new PracticeOrigin(PracticeOriginKind.Learner, plan.Id), plan.Id), ct);
        await LinkToReviewPlanAsync(user.UserId, plan.Id, creation.Session.Id, ct);
        return creation.Session;
    }

    public async Task LinkToReviewPlanAsync(string userId, string reviewPlanId, string sessionId, CancellationToken ct)
    {
        var plan = await store.ReviewPlans.GetRequiredAsync(userId, reviewPlanId, "Review plan", ct);
        if (plan.PracticeSessionIds.Contains(sessionId)) return;
        plan.PracticeSessionIds.Add(sessionId);
        plan.Status = ReviewPlanStatus.Active;
        await store.ReviewPlans.UpsertAsync(plan, ct);
    }

    /// <summary>
    /// Creates (or returns, when SessionId is deterministic and already exists) a validated practice session.
    /// Shared by the learner UI, the tutor tool and the review workflow.
    /// </summary>
    public async Task<PracticeSessionCreation> CreateSessionAsync(UserContext user, PracticeSessionSpec spec, CancellationToken ct)
    {
        if (spec.SessionId is not null && await store.PracticeSessions.GetAsync(user.UserId, spec.SessionId, ct) is { } existing)
        {
            return new PracticeSessionCreation(existing, false);
        }
        var profile = await profiles.GetOrCreateAsync(user, ct);
        var topics = ValidateTopics(profile, spec.TopicIds);
        var sessionId = spec.SessionId ?? $"ps-{Guid.NewGuid():N}";
        var level = spec.Level ?? profile.CurrentLevel;
        var count = Math.Clamp(spec.QuestionCount, 1, 10);
        var questions = spec.Origin.Kind == PracticeOriginKind.Learner && spec.Mode != PracticeMode.Review
            ? await publishedQuestions.SelectAsync(profile.TargetLanguage, level, topics, count, sessionId, ct)
            : [];
        if (questions.Count == 0)
        {
            questions = await generator.GenerateValidatedAsync(profile,
                new QuestionGenerationSpec(profile.TargetLanguage, level, topics, count, spec.Mode == PracticeMode.Review ? "review" : "practice", sessionId), ct);
        }
        return await SaveNewSessionAsync(user.UserId, sessionId, spec with { TopicIds = topics, Level = level }, QuestionGenerationService.ToPracticeQuestions(sessionId, questions), ct);
    }

    public async Task<PracticeSessionCreation> SaveNewSessionAsync(string userId, string sessionId, PracticeSessionSpec spec, List<PracticeQuestion> questions, CancellationToken ct)
    {
        if (questions.Count == 0) throw DomainException.Invalid("no-questions", "A practice session needs at least one validated question.");
        if (questions.Any(q => q.Validation.Status != ValidationStatus.Passed)) throw new InvalidOperationException("Only validated questions can be saved.");
        var session = new PracticeSession
        {
            Id = sessionId,
            UserId = userId,
            Mode = spec.Mode,
            Level = spec.Level ?? CefrLevel.A1,
            TargetTopicIds = spec.TopicIds.ToList(),
            ReviewPlanId = spec.ReviewPlanId,
            Origin = spec.Origin,
            Status = PracticeStatus.InProgress,
            Questions = questions,
        };
        var created = await store.PracticeSessions.TryCreateAsync(session, ct);
        return new PracticeSessionCreation(created ? session : (await store.PracticeSessions.GetAsync(userId, sessionId, ct))!, created);
    }

    private List<string> ValidateTopics(LearningProfile profile, IReadOnlyList<string> topicIds)
    {
        var topics = topicIds.Distinct().ToList();
        if (topics.Count is 0 or > 3) throw DomainException.Invalid("invalid-topic-count", "Choose 1 to 3 grammar topics.");
        foreach (var id in topics)
        {
            var topic = knowledgeBase.Find(id) ?? throw DomainException.Invalid("unknown-topic", $"Unknown grammar topic '{id}'.");
            if (topic.Language != profile.TargetLanguage) throw DomainException.Invalid("topic-language-mismatch", $"Topic '{id}' is not for the learner's target language.");
        }
        return topics;
    }

    public async Task<PracticeSessionView> GetAsync(UserContext user, string sessionId, CancellationToken ct)
    {
        var session = await store.PracticeSessions.GetRequiredAsync(user.UserId, sessionId, "Practice session", ct);
        return views.PracticeSession(session, await profiles.GetOrCreateAsync(user, ct));
    }

    public async Task<AnswerResultView> SubmitAnswerAsync(UserContext user, string sessionId, SubmitAnswerRequest request, CancellationToken ct)
    {
        var session = await store.PracticeSessions.GetRequiredAsync(user.UserId, sessionId, "Practice session", ct);
        var question = session.Questions.FirstOrDefault(q => q.Id == request.QuestionId)
            ?? throw DomainException.NotFound("Question", request.QuestionId);

        // Idempotency: the first answer per question is final; replays return the stored result.
        if (session.Answers.FirstOrDefault(a => a.QuestionId == question.Id) is { } previous)
        {
            return views.AnswerResult(question, previous, alreadyAnswered: true);
        }
        if (session.Status == PracticeStatus.Completed)
        {
            throw DomainException.Conflict("session-completed", "This practice session is already completed.");
        }

        var grade = AnswerGrader.Grade(question, request.Answer);
        var profile = await profiles.GetOrCreateAsync(user, ct);
        var before = LearningProfileService.MasteryOf(profile, question.GrammarTopicId);
        var now = clock.GetUtcNow().ToString("O");
        var observation = new LearningObservation
        {
            Id = ObservationIds.Practice(session.Id, question.Id),
            UserId = user.UserId,
            GrammarTopicId = question.GrammarTopicId,
            SourceType = ObservationSource.Practice,
            SourceId = session.Id,
            SourceItemId = question.Id,
            ObservationType = grade.IsCorrect ? ObservationType.CorrectAnswer : ObservationType.IncorrectAnswer,
            Certainty = ObservationCertainty.Deterministic,
            Confidence = 1,
            Evidence = new ObservationEvidence(question.Prompt.Replace(QuestionQualityEvaluator.Blank, $"[{request.Answer.Trim()}]"), question.CorrectAnswer, request.Answer.Trim()),
            DetectedBy = "answer-grader",
            OccurredAt = now,
        };
        var write = await profiles.RecordObservationsAsync(user.UserId, [observation], ct);
        var after = LearningProfileService.MasteryOf(write.Profile, question.GrammarTopicId);

        var answer = new PracticeAnswer(question.Id, request.AttemptId ?? $"attempt-{Guid.NewGuid():N}", request.Answer, grade.NormalizedAnswer, grade.IsCorrect, grade.FeedbackCode, before, after, now);
        session.Answers.Add(answer);
        await store.PracticeSessions.UpsertAsync(session, ct);
        return views.AnswerResult(question, answer, alreadyAnswered: false);
    }

    public async Task<PracticeSessionView> CompleteAsync(UserContext user, string sessionId, CancellationToken ct)
    {
        var session = await store.PracticeSessions.GetRequiredAsync(user.UserId, sessionId, "Practice session", ct);
        if (session.Status != PracticeStatus.Completed)
        {
            var correct = session.Answers.Count(a => a.IsCorrect);
            session.Summary = new PracticeSummary(session.Questions.Count, session.Answers.Count, correct,
                session.Answers.Count == 0 ? 0 : Math.Round((double)correct / session.Answers.Count, 4));
            session.Status = PracticeStatus.Completed;
            session.CompletedAt = clock.GetUtcNow().ToString("O");
            await store.PracticeSessions.UpsertAsync(session, ct);

            if (session.ReviewPlanId is not null && await store.ReviewPlans.GetAsync(user.UserId, session.ReviewPlanId, ct) is { } plan)
            {
                plan.Status = ReviewPlanStatus.Completed;
                await store.ReviewPlans.UpsertAsync(plan, ct);
            }
        }
        return views.PracticeSession(session, await profiles.GetOrCreateAsync(user, ct));
    }

    public static int SeedFor(string key) => (int)(StableHash.Of(key) % int.MaxValue);
}
