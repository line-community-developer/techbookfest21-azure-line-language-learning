using LineLanguageLearn.Learning.Domain;

namespace LineLanguageLearn.Learning.Practice;

/// <summary>
/// Rule-based mastery model (initial version). Mastery is a projection over observations, replayed in order,
/// so recomputation is deterministic and duplicate events (same observation id) cannot double count.
///
///   deterministic correct answer  : s += 0.20 * (1 - s)
///   deterministic incorrect answer: s -= 0.25 * s
///   AI-inferred findings are weighted by w = 0.6 * confidence:
///     grammar-error               : s -= 0.25 * s * w
///     possible-misunderstanding   : s -= 0.10 * s * w
///     correct-use                 : s += 0.20 * (1 - s) * w
///   new-expression                : no change
/// Status: no evidence = unknown, s &lt; 0.45 = weak, s &gt;= 0.75 = strong, otherwise developing.
/// </summary>
public static class MasteryCalculator
{
    public const double InitialScore = 0.5;
    public const double WeakThreshold = 0.45;
    public const double StrongThreshold = 0.75;
    public const double AiWeightFactor = 0.6;

    public static double Apply(double score, LearningObservation observation)
    {
        var weight = observation.Certainty == ObservationCertainty.Deterministic ? 1.0 : AiWeightFactor * Math.Clamp(observation.Confidence, 0, 1);
        var next = observation.ObservationType switch
        {
            ObservationType.CorrectAnswer => score + 0.20 * (1 - score) * weight,
            ObservationType.IncorrectAnswer => score - 0.25 * score * weight,
            ObservationType.GrammarError => score - 0.25 * score * weight,
            ObservationType.PossibleMisunderstanding => score - 0.10 * score * weight,
            ObservationType.CorrectUse => score + 0.20 * (1 - score) * weight,
            _ => score,
        };
        return Math.Round(Math.Clamp(next, 0, 1), 4);
    }

    public static MasteryStatus StatusFor(double score, int evidenceCount) =>
        evidenceCount == 0 ? MasteryStatus.Unknown
        : score < WeakThreshold ? MasteryStatus.Weak
        : score >= StrongThreshold ? MasteryStatus.Strong
        : MasteryStatus.Developing;

    public static Dictionary<string, TopicMastery> Compute(IEnumerable<LearningObservation> observations)
    {
        var result = new Dictionary<string, TopicMastery>(StringComparer.Ordinal);
        var evidence = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var observation in observations
                     .Where(o => o.GrammarTopicId is not null)
                     .OrderBy(o => o.OccurredAt, StringComparer.Ordinal)
                     .ThenBy(o => o.Id, StringComparer.Ordinal))
        {
            var topicId = observation.GrammarTopicId!;
            if (!result.TryGetValue(topicId, out var mastery))
            {
                mastery = new TopicMastery { TopicId = topicId, Score = InitialScore };
                result[topicId] = mastery;
            }
            if (observation.ObservationType == ObservationType.NewExpression) continue;

            mastery.Score = Apply(mastery.Score, observation);
            evidence[topicId] = evidence.GetValueOrDefault(topicId) + 1;
            mastery.LastObservedAt = observation.OccurredAt;
            switch (observation.ObservationType)
            {
                case ObservationType.CorrectAnswer:
                    mastery.Attempts++;
                    mastery.Correct++;
                    break;
                case ObservationType.IncorrectAnswer:
                    mastery.Attempts++;
                    break;
                case ObservationType.GrammarError:
                case ObservationType.PossibleMisunderstanding:
                    mastery.ConversationErrors++;
                    break;
                case ObservationType.CorrectUse:
                    mastery.ConversationCorrectUses++;
                    break;
            }
        }
        foreach (var (topicId, mastery) in result)
        {
            mastery.Status = StatusFor(mastery.Score, evidence.GetValueOrDefault(topicId));
        }
        return result;
    }
}
