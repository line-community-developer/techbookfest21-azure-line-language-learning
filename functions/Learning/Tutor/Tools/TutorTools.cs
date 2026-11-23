using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Knowledge;
using LineLanguageLearn.Learning.Persistence;
using LineLanguageLearn.Learning.Practice;

namespace LineLanguageLearn.Learning.Tutor.Tools;

// Tool inputs (shared/contracts/tools.ts). None of them carries a user id.
public sealed record EmptyInput;
public sealed record GetRecentMistakesInput(int Limit);
public sealed record SearchGrammarRulesInput(string Query, string? TopicId, CefrLevel? Level, int Limit);
public sealed record RecordLearningObservationInput(string MessageId, string GrammarTopicId, ObservationType ObservationType, string EvidenceText, string? Correction, double Confidence);
public sealed record CreatePracticeSessionInput(List<string> TopicIds, int QuestionCount);

public sealed class GetLearningProfileTool(LearningProfileService profiles, GrammarKnowledgeBase knowledgeBase) : TutorTool<EmptyInput>
{
    public override string Name => "get_learning_profile";
    public override string Description => "Get the current learner's level, goals, weak and strong grammar topics.";

    protected override async Task<object> ExecuteAsync(EmptyInput input, ToolExecutionContext context, CancellationToken ct)
    {
        var profile = await profiles.GetOrCreateAsync(context.User, ct);
        object Score(string id) => new { topicId = id, title = knowledgeBase.Find(id)?.TitleJa ?? id, score = LearningProfileService.MasteryOf(profile, id) };
        return new
        {
            learnerName = profile.LearnerName,
            currentLevel = profile.CurrentLevel,
            nativeLanguage = profile.NativeLanguage.ToString().ToLowerInvariant(),
            targetLanguage = profile.TargetLanguage,
            learningGoals = profile.LearningGoals,
            weakTopics = profile.WeakTopics.Take(5).Select(Score).ToList(),
            strongTopics = profile.StrongTopics.Take(5).Select(Score).ToList(),
        };
    }
}

public sealed class GetRecentMistakesTool(LearningProfileService profiles, GrammarKnowledgeBase knowledgeBase) : TutorTool<GetRecentMistakesInput>
{
    public override string Name => "get_recent_mistakes";
    public override string Description => "List the learner's most recent mistakes from practice and conversations (newest first).";

    protected override async Task<object> ExecuteAsync(GetRecentMistakesInput input, ToolExecutionContext context, CancellationToken ct)
    {
        var observations = await profiles.ListObservationsAsync(context.User.UserId, ct);
        var mistakes = observations
            .Where(o => o.GrammarTopicId is not null && o.ObservationType is ObservationType.IncorrectAnswer or ObservationType.GrammarError or ObservationType.PossibleMisunderstanding)
            .Take(input.Limit)
            .Select(o => new
            {
                observationId = o.Id,
                topicId = o.GrammarTopicId!,
                title = knowledgeBase.Find(o.GrammarTopicId!)?.TitleJa ?? o.GrammarTopicId!,
                sourceType = o.SourceType,
                certainty = o.Certainty,
                evidenceText = o.Evidence.Text,
                occurredAt = o.OccurredAt,
            })
            .ToList();
        return new { mistakes };
    }
}

public sealed class SearchGrammarRulesTool(IGrammarRetriever retriever, GrammarKnowledgeBase knowledgeBase, LearningProfileService profiles) : TutorTool<SearchGrammarRulesInput>
{
    public override string Name => "search_grammar_rules";
    public override string Description => "Search the grammar knowledge base. Use it before explaining grammar; cite the returned topic.";

    protected override async Task<object> ExecuteAsync(SearchGrammarRulesInput input, ToolExecutionContext context, CancellationToken ct)
    {
        if (input.TopicId is not null && !knowledgeBase.Exists(input.TopicId)) throw new ToolException("unknown-topic", $"Unknown grammar topic '{input.TopicId}'.");
        var profile = await profiles.GetOrCreateAsync(context.User, ct);
        var level = input.Level ?? LevelPolicy.MaxRetrievalLevel(profile.CurrentLevel);
        var hits = await retriever.SearchAsync(new GrammarQuery(profile.TargetLanguage, input.Query, level, input.TopicId, input.Limit), ct);
        context.RetrievedSourceIds.AddRange(hits.SelectMany(h => h.SourceIds));
        return new
        {
            results = hits.Select(h => new
            {
                topicId = h.Topic.Id,
                title = h.Topic.TitleJa,
                level = h.Topic.Level,
                summaryJa = h.Topic.UsageJa,
                rules = h.Topic.Rules.Select(r => new { id = r.Id, summaryJa = r.SummaryJa }).ToList(),
                examples = h.Topic.Examples.Take(2).Select(e => new { text = e.Text, translationJa = e.TranslationJa }).ToList(),
                score = h.Score,
                sourceIds = h.SourceIds,
            }).ToList(),
        };
    }
}

public sealed class RecordLearningObservationTool(LearningProfileService profiles, GrammarKnowledgeBase knowledgeBase) : TutorTool<RecordLearningObservationInput>
{
    public override string Name => "record_learning_observation";
    public override string Description => "Record a grammar error, possible misunderstanding or correct use found in a learner message (quote the exact text).";

    protected override async Task<object> ExecuteAsync(RecordLearningObservationInput input, ToolExecutionContext context, CancellationToken ct)
    {
        var message = context.Session.Messages.FirstOrDefault(m => m.Id == input.MessageId)
            ?? throw new ToolException("unknown-message", "messageId does not belong to this conversation.");
        if (message.Role != MessageRole.Learner) throw new ToolException("not-learner-message", "Only learner messages can be used as evidence.");
        if (!message.Text.Contains(input.EvidenceText.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ToolException("evidence-not-in-message", "evidenceText must be copied verbatim from the learner message.");
        }
        if (!knowledgeBase.Exists(input.GrammarTopicId)) throw new ToolException("unknown-topic", $"Unknown grammar topic '{input.GrammarTopicId}'.");

        var observation = new LearningObservation
        {
            Id = ObservationIds.Conversation(context.Session.Id, message.Id, input.GrammarTopicId, input.ObservationType),
            UserId = context.User.UserId,
            GrammarTopicId = input.GrammarTopicId,
            SourceType = ObservationSource.Conversation,
            SourceId = context.Session.Id,
            SourceItemId = message.Id,
            ObservationType = input.ObservationType,
            Certainty = ObservationCertainty.AiInferred,
            Confidence = Math.Round(input.Confidence, 3),
            Evidence = new ObservationEvidence(input.EvidenceText.Trim(), input.Correction, input.EvidenceText.Trim()),
            DetectedBy = $"{context.Agent.Label}:tutor-tool",
            OccurredAt = message.CreatedAt ?? "",
        };
        var result = await profiles.RecordObservationsAsync(context.User.UserId, [observation], ct);
        return new { observationId = observation.Id, created = result.CreatedIds.Count == 1 };
    }
}

public sealed class CreatePracticeSessionTool(PracticeService practice) : TutorTool<CreatePracticeSessionInput>
{
    public override string Name => "create_practice_session";
    public override string Description => "Create a practice session (mini app exercises) for up to 3 grammar topics.";

    protected override async Task<object> ExecuteAsync(CreatePracticeSessionInput input, ToolExecutionContext context, CancellationToken ct)
    {
        // Deterministic id: replaying the same learner message never creates a second session.
        var sessionId = $"ps-tutor-{context.Session.Id}-{context.LearnerMessage.Id}";
        var creation = await practice.CreateSessionAsync(context.User, new PracticeSessionSpec(
            sessionId, PracticeMode.Topic, input.TopicIds, null, input.QuestionCount,
            new PracticeOrigin(PracticeOriginKind.TutorTool, context.Session.Id), null), ct);
        return new
        {
            practiceSessionId = creation.Session.Id,
            questionCount = creation.Session.Questions.Count,
            topicIds = creation.Session.TargetTopicIds,
            created = creation.Created,
        };
    }
}

public sealed class GetReviewPlanTool(LearningStore store, GrammarKnowledgeBase knowledgeBase) : TutorTool<EmptyInput>
{
    public override string Name => "get_review_plan";
    public override string Description => "Get the learner's current review plan (topics to review and why).";

    protected override async Task<object> ExecuteAsync(EmptyInput input, ToolExecutionContext context, CancellationToken ct)
    {
        var plan = (await store.ReviewPlans.ListAsync(context.User.UserId, ct))
            .Where(p => p.Status == ReviewPlanStatus.Active)
            .OrderByDescending(p => p.CreatedAt, StringComparer.Ordinal)
            .FirstOrDefault();
        return new
        {
            reviewPlan = plan is null ? null : new
            {
                id = plan.Id,
                items = plan.Items.Select(i => new { topicId = i.TopicId, title = knowledgeBase.Find(i.TopicId)?.TitleJa ?? i.TopicId, priority = i.Priority, reasonJa = i.ReasonJa }).ToList(),
                practiceSessionIds = plan.PracticeSessionIds,
            },
        };
    }
}
