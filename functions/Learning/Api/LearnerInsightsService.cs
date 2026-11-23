using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Identity;
using LineLanguageLearn.Learning.Knowledge;
using LineLanguageLearn.Learning.Persistence;
using LineLanguageLearn.Learning.Practice;
using LineLanguageLearn.Learning.Tutor;

namespace LineLanguageLearn.Learning.Api;

/// <summary>Read models for the home and history screens, computed from stored data (no UI-side logic).</summary>
public sealed class LearnerInsightsService(
    LearningStore store,
    LearningProfileService profiles,
    PracticeService practice,
    TutorProfileCatalog tutors,
    ViewMapper views)
{
    public async Task<DashboardView> GetDashboardAsync(UserContext user, CancellationToken ct)
    {
        var profile = await profiles.GetOrCreateAsync(user, ct);
        var observations = await profiles.ListObservationsAsync(user.UserId, ct);
        var sessions = await store.PracticeSessions.ListAsync(user.UserId, ct);
        var tutorSessions = await store.TutorSessions.ListAsync(user.UserId, ct);
        var plans = (await store.ReviewPlans.ListAsync(user.UserId, ct)).OrderByDescending(p => p.CreatedAt, StringComparer.Ordinal).ToList();
        var planViews = plans.Select(p => views.ReviewPlan(p, observations, sessions)).ToList();

        var practiceObservations = observations.Where(o => o.SourceType == ObservationSource.Practice).ToList();
        var correct = practiceObservations.Count(o => o.ObservationType == ObservationType.CorrectAnswer);
        var stats = new DashboardStats(practiceObservations.Count, correct, practiceObservations.Count == 0 ? 0 : Math.Round((double)correct / practiceObservations.Count, 4));

        var activeReview = planViews.FirstOrDefault(p => p.Status == ReviewPlanStatus.Active);
        var recommendation = activeReview is not null
            ? new RecommendationView("review", activeReview.Items.Select(i => views.Topic(i.TopicId)).ToList(),
                $"{activeReview.SourceLabel}で見つかった弱点を中心に復習しましょう。", activeReview.Id,
                activeReview.PracticeSessionStatus == PracticeStatus.InProgress ? activeReview.PracticeSessionId : null)
            : new RecommendationView("recommended", practice.RecommendTopics(profile).Select(views.Topic).ToList(),
                profile.WeakTopics.Count > 0 ? "習熟度の低い文法項目を優先した演習です。" : "今のレベルに合った文法項目から始めましょう。", null, null);

        var activity = sessions.Select(s => new ActivityItem("practice", s.Id,
                s.Mode == PracticeMode.Review ? "復習演習" : "問題演習",
                s.Summary is { } sum ? $"{sum.Correct}/{sum.Total} 問正解 · {string.Join("・", s.TargetTopicIds.Select(views.TopicTitle))}" : $"進行中 · {string.Join("・", s.TargetTopicIds.Select(views.TopicTitle))}",
                s.CompletedAt ?? s.CreatedAt ?? ""))
            .Concat(tutorSessions.Select(t => new ActivityItem("conversation", t.Id,
                $"{tutors.Get(t.TutorProfileId).Name}との会話", $"{t.Theme} · {t.Messages.Count(m => m.Role == MessageRole.Learner)} 発話",
                t.EndedAt ?? t.CreatedAt ?? "")))
            .OrderByDescending(a => a.OccurredAt, StringComparer.Ordinal)
            .Take(6)
            .ToList();

        return new DashboardView(
            new DashboardLearner(profile.LearnerName, profile.CurrentLevel, LanguageName(profile.NativeLanguage.ToString()), LanguageName(profile.TargetLanguage.ToString()), profile.LearningGoals),
            stats,
            recommendation,
            profile.WeakTopics.Select(id => views.Mastery(profile.MasteryByTopic[id])).ToList(),
            profile.StrongTopics.Select(id => views.Mastery(profile.MasteryByTopic[id])).ToList(),
            activity,
            planViews.Take(3).ToList());
    }

    public async Task<HistoryView> GetHistoryAsync(UserContext user, CancellationToken ct)
    {
        var profile = await profiles.GetOrCreateAsync(user, ct);
        var observations = await profiles.ListObservationsAsync(user.UserId, ct);
        var sessions = (await store.PracticeSessions.ListAsync(user.UserId, ct)).OrderByDescending(s => s.CreatedAt, StringComparer.Ordinal).ToList();
        var plans = (await store.ReviewPlans.ListAsync(user.UserId, ct)).OrderByDescending(p => p.CreatedAt, StringComparer.Ordinal).ToList();

        var mastery = profile.MasteryByTopic.Values.OrderBy(m => m.Score).ThenBy(m => m.TopicId, StringComparer.Ordinal).Select(views.Mastery).ToList();
        var attempts = sessions
            .SelectMany(s => s.Answers.Select(a => (Session: s, Answer: a, Question: s.Questions.First(q => q.Id == a.QuestionId))))
            .OrderByDescending(x => x.Answer.AnsweredAt, StringComparer.Ordinal)
            .Take(50)
            .Select(x => new AttemptView(x.Session.Id, x.Question.Id, x.Question.GrammarTopicId, views.TopicTitle(x.Question.GrammarTopicId),
                x.Question.Prompt, x.Answer.Answer, x.Question.CorrectAnswer, x.Answer.IsCorrect, x.Answer.AnsweredAt))
            .ToList();
        var observationViews = observations.Take(50).Select(o => new ObservationView(
            o.Id, o.GrammarTopicId, o.GrammarTopicId is null ? null : views.TopicTitle(o.GrammarTopicId), o.SourceType, o.ObservationType,
            o.Certainty, o.Confidence, o.Evidence.Text, o.OccurredAt)).ToList();
        var sessionViews = sessions.Select(s => new SessionHistoryView(s.Id, s.Mode, s.Status, s.TargetTopicIds.Select(views.Topic).ToList(),
            s.CreatedAt ?? "", s.CompletedAt, s.Summary)).ToList();

        var priorities = new List<ReviewPriorityView>();
        foreach (var item in plans.Where(p => p.Status == ReviewPlanStatus.Active).SelectMany(p => p.Items))
        {
            if (priorities.Any(p => p.TopicId == item.TopicId)) continue;
            var t = views.Topic(item.TopicId);
            priorities.Add(new ReviewPriorityView(t.TopicId, t.Title, t.TitleJa, t.Level, item.Priority, item.PriorityScore, item.ReasonJa));
        }
        foreach (var topicId in profile.WeakTopics.Where(id => priorities.All(p => p.TopicId != id)))
        {
            var score = Math.Round((1 - LearningProfileService.MasteryOf(profile, topicId)) * 0.6, 3);
            var t = views.Topic(topicId);
            priorities.Add(new ReviewPriorityView(t.TopicId, t.Title, t.TitleJa, t.Level, Analysis.ReviewPlanner.PriorityFor(score), score, "習熟度が低い苦手項目です。"));
        }

        return new HistoryView(mastery, attempts, observationViews, sessionViews, priorities.OrderByDescending(p => p.PriorityScore).ToList());
    }

    private static string LanguageName(string code) => code.ToLowerInvariant() switch
    {
        "ja" => "日本語",
        "it" => "イタリア語",
        "en" => "英語",
        _ => code,
    };
}

