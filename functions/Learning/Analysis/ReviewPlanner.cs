using System.Globalization;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Knowledge;
using LineLanguageLearn.Learning.Practice;

namespace LineLanguageLearn.Learning.Analysis;

/// <summary>
/// Deterministic review planning from validated observations and the mastery projection
/// (the AI only contributes the observations; prioritisation is application logic).
/// </summary>
public sealed class ReviewPlanner(GrammarKnowledgeBase knowledgeBase)
{
    public const int MaxItems = 3;

    public static string PlanIdForConversation(string tutorSessionId) => $"rp-conv-{tutorSessionId}";

    public List<ReviewPlanItem> PlanItems(LearningProfile profile, IReadOnlyList<LearningObservation> sourceObservations, IReadOnlyList<LearningObservation> allObservations)
    {
        var items = new List<ReviewPlanItem>();
        var problems = sourceObservations
            .Where(o => o.GrammarTopicId is not null && o.ObservationType is ObservationType.GrammarError or ObservationType.PossibleMisunderstanding or ObservationType.IncorrectAnswer)
            .GroupBy(o => o.GrammarTopicId!);

        foreach (var group in problems)
        {
            var mastery = LearningProfileService.MasteryOf(profile, group.Key);
            var count = group.Count();
            var score = Math.Round(Math.Clamp((1 - mastery) * 0.6 + Math.Min(count, 3) / 3.0 * 0.4, 0, 1), 3);
            var example = group.First().Evidence;
            var title = knowledgeBase.Find(group.Key)?.TitleJa ?? group.Key;
            var reason = example.Expected is { Length: > 0 } expected
                ? $"会話で「{example.Text}」と表現していました（正しくは「{expected}」）。{title} を復習しましょう。"
                : $"会話で「{example.Text}」の部分に {title} の誤りが見られました。";
            var evidenceIds = group.Select(o => o.Id)
                .Concat(allObservations.Where(o => o.GrammarTopicId == group.Key && o.ObservationType == ObservationType.IncorrectAnswer).Take(2).Select(o => o.Id))
                .Distinct().ToList();
            items.Add(new ReviewPlanItem(group.Key, PriorityFor(score), score, reason, evidenceIds));
        }

        // Fill with existing weak topics so a plan also reflects long-term weaknesses.
        foreach (var topicId in profile.WeakTopics.Where(t => items.All(i => i.TopicId != t)))
        {
            if (items.Count >= MaxItems) break;
            var mastery = LearningProfileService.MasteryOf(profile, topicId);
            var score = Math.Round(Math.Clamp((1 - mastery) * 0.6, 0, 1), 3);
            var evidence = allObservations.Where(o => o.GrammarTopicId == topicId && o.ObservationType is ObservationType.IncorrectAnswer or ObservationType.GrammarError)
                .Take(2).Select(o => o.Id).ToList();
            items.Add(new ReviewPlanItem(topicId, PriorityFor(score), score,
                $"習熟度が {(mastery * 100).ToString("0", CultureInfo.InvariantCulture)}% と低めの苦手項目です。", evidence));
        }

        return items.OrderByDescending(i => i.PriorityScore).ThenBy(i => i.TopicId, StringComparer.Ordinal).Take(MaxItems).ToList();
    }

    public static ReviewPriority PriorityFor(double score) => score >= 0.6 ? ReviewPriority.High : score >= 0.4 ? ReviewPriority.Medium : ReviewPriority.Low;
}
