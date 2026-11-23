using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Knowledge;
using LineLanguageLearn.Learning.Practice;

namespace LineLanguageLearn.Learning.Api;

/// <summary>Maps persisted documents to API views. Answer keys only leave through answered results.</summary>
public sealed class ViewMapper(GrammarKnowledgeBase knowledgeBase)
{
    public TopicRef Topic(string topicId)
    {
        var topic = knowledgeBase.Find(topicId);
        return topic is null
            ? new TopicRef(topicId, topicId, topicId, CefrLevel.A1)
            : new TopicRef(topic.Id, topic.Title, topic.TitleJa, topic.Level);
    }

    public string TopicTitle(string topicId) => knowledgeBase.Find(topicId)?.TitleJa ?? topicId;

    public MasteryView Mastery(TopicMastery mastery)
    {
        var t = Topic(mastery.TopicId);
        return new MasteryView(t.TopicId, t.Title, t.TitleJa, t.Level, mastery.Score, mastery.Status, mastery.Attempts, mastery.Correct, mastery.ConversationErrors, mastery.LastObservedAt);
    }

    public GrammarReferenceView GrammarReference(string topicId, IReadOnlyCollection<string> ruleIds)
    {
        var topic = knowledgeBase.Get(topicId);
        var rules = topic.Rules.Where(r => ruleIds.Count == 0 || ruleIds.Contains(r.Id)).Select(r => new RuleView(r.Id, r.SummaryJa)).ToList();
        var example = topic.Examples.FirstOrDefault();
        return new GrammarReferenceView(topic.Id, topic.Title, topic.TitleJa, topic.Level, rules, example is null ? null : new ExampleView(example.Text, example.TranslationJa));
    }

    public AnswerResultView AnswerResult(PracticeQuestion question, PracticeAnswer answer, bool alreadyAnswered) => new(
        question.Id,
        answer.IsCorrect,
        answer.FeedbackCode,
        answer.Answer,
        question.CorrectAnswer,
        question.AcceptedAnswers,
        question.ExplanationJa,
        [GrammarReference(question.GrammarTopicId, question.RuleIds)],
        new MasteryChange(question.GrammarTopicId, answer.MasteryBefore, answer.MasteryAfter),
        alreadyAnswered);

    public PracticeSessionView PracticeSession(PracticeSession session, LearningProfile profile)
    {
        var questions = session.Questions.OrderBy(q => q.Order).Select(q =>
        {
            var answer = session.Answers.FirstOrDefault(a => a.QuestionId == q.Id);
            return new PracticeQuestionView(q.Id, q.Order, q.Type, Topic(q.GrammarTopicId), q.Difficulty, q.InstructionJa, q.Prompt, q.Choices,
                answer is null ? null : AnswerResult(q, answer, alreadyAnswered: false));
        }).ToList();

        PracticeSummaryView? summary = null;
        if (session.Summary is { } s)
        {
            var byTopic = session.Questions.GroupBy(q => q.GrammarTopicId).Select(g =>
            {
                var t = Topic(g.Key);
                var correct = g.Count(q => session.Answers.Any(a => a.QuestionId == q.Id && a.IsCorrect));
                return new TopicResultView(t.TopicId, t.Title, t.TitleJa, t.Level, g.Count(), correct, LearningProfileService.MasteryOf(profile, g.Key));
            }).ToList();
            summary = new PracticeSummaryView(s.Total, s.Answered, s.Correct, s.Accuracy, byTopic);
        }

        return new PracticeSessionView(
            session.Id, session.Mode, session.Level, session.Status,
            session.TargetTopicIds.Select(Topic).ToList(),
            session.ReviewPlanId,
            questions,
            summary,
            session.Questions.FirstOrDefault()?.GeneratedBy ?? new ProviderInfo(ProviderKind.Unavailable, "unknown"),
            session.CreatedAt ?? "");
    }

    public ReviewPlanView ReviewPlan(ReviewPlan plan, IReadOnlyList<LearningObservation> observations, IReadOnlyList<PracticeSession> sessions)
    {
        var byId = observations.ToDictionary(o => o.Id);
        var latestSession = plan.PracticeSessionIds.Select(id => sessions.FirstOrDefault(s => s.Id == id)).LastOrDefault(s => s is not null);
        return new ReviewPlanView(
            plan.Id,
            plan.SourceType,
            plan.SourceId,
            plan.SourceType == ObservationSource.Conversation ? "AI講師との会話" : "問題演習",
            plan.Status,
            plan.CreatedAt ?? "",
            plan.Items.Select(i =>
            {
                var t = Topic(i.TopicId);
                var evidence = i.EvidenceObservationIds.Where(byId.ContainsKey).Select(id => byId[id])
                    .Select(o => new EvidenceView(o.Id, o.Evidence.Text, o.Certainty)).ToList();
                return new ReviewPlanItemView(t.TopicId, t.Title, t.TitleJa, t.Level, i.Priority, i.PriorityScore, i.ReasonJa, evidence);
            }).ToList(),
            latestSession?.Id,
            latestSession?.Status);
    }

    public TutorMessageView TutorMessage(TutorMessage m) =>
        new(m.Id, m.Role, m.Text, m.CreatedAt ?? "", m.Corrections, m.RetrievedSourceIds, m.ToolCalls);

    public TutorSessionView TutorSession(TutorSession session, TutorProfile tutor) => new(
        session.Id,
        new TutorRef(tutor.Id, tutor.Name, tutor.AvatarEmoji),
        session.Channel,
        session.Status,
        session.Theme,
        session.FocusTopicIds.Select(Topic).ToList(),
        session.Messages.Select(TutorMessage).ToList(),
        session.Agent,
        session.AnalysisWorkflowRunId,
        session.CreatedAt ?? "");

    public TutorSessionSummaryView TutorSessionSummary(TutorSession session, TutorProfile tutor) => new(
        session.Id,
        new TutorRef(tutor.Id, tutor.Name, tutor.AvatarEmoji),
        session.Channel,
        session.Status,
        session.Theme,
        session.FocusTopicIds.Select(Topic).ToList(),
        session.Agent,
        session.AnalysisWorkflowRunId,
        session.CreatedAt ?? "",
        session.Messages.Count);
}
