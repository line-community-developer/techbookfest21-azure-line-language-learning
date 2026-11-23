using LineLanguageLearn.Learning.Ai;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Knowledge;
using LineLanguageLearn.Learning.Observability;
using LineLanguageLearn.Learning.Practice;
using Microsoft.Extensions.Logging;

namespace LineLanguageLearn.Learning.Analysis;

public sealed record RejectedFinding(ConversationFinding Finding, string Reason);

public sealed record ConversationAnalysisResult(
    List<LearningObservation> Observations,
    List<RejectedFinding> Rejected,
    string SummaryJa,
    ProviderInfo Provider);

/// <summary>
/// Turns a tutor conversation into learning observations. The model proposes findings (Structured Outputs);
/// the application accepts only findings whose evidence is verifiably present in a learner message.
/// </summary>
public sealed class ConversationAnalysisService(
    LearningModelGateway models,
    GrammarKnowledgeBase knowledgeBase,
    ILogger<ConversationAnalysisService> logger)
{
    public const double MinimumConfidence = 0.5;

    public async Task<ConversationAnalysisResult> AnalyzeAsync(TutorSession session, LearningProfile profile, CancellationToken ct)
    {
        var candidates = knowledgeBase.ForLanguage(profile.TargetLanguage).ToList();
        var result = await models.AnalyzeConversationAsync(new ConversationAnalysisPrompt(session, candidates, profile.TargetLanguage, profile.CurrentLevel), ct);
        var accepted = ValidateFindings(session, result.Value.Findings, result.Response.Provider, out var rejected);
        if (rejected.Count > 0)
        {
            logger.RejectedAiOutput("analyze-conversation", string.Join(',', rejected.Select(r => r.Reason).Distinct()), rejected.Count);
        }
        return new ConversationAnalysisResult(accepted, rejected, result.Value.SummaryJa, result.Response.Provider);
    }

    public List<LearningObservation> ValidateFindings(TutorSession session, IEnumerable<ConversationFinding> findings, ProviderInfo provider, out List<RejectedFinding> rejected)
    {
        rejected = [];
        var observations = new Dictionary<string, LearningObservation>(StringComparer.Ordinal);
        foreach (var finding in findings)
        {
            var reason = Reject(session, finding);
            if (reason is not null)
            {
                rejected.Add(new RejectedFinding(finding, reason));
                continue;
            }
            var type = finding.Kind switch
            {
                FindingKind.GrammarError => ObservationType.GrammarError,
                FindingKind.CorrectUse => ObservationType.CorrectUse,
                FindingKind.NewExpression => ObservationType.NewExpression,
                _ => ObservationType.PossibleMisunderstanding,
            };
            var message = session.Messages.First(m => m.Id == finding.MessageId);
            var topicKey = finding.GrammarTopicId ?? "expression";
            var id = ObservationIds.Conversation(session.Id, message.Id, topicKey, type);
            observations.TryAdd(id, new LearningObservation
            {
                Id = id,
                UserId = session.UserId,
                GrammarTopicId = finding.GrammarTopicId,
                SourceType = ObservationSource.Conversation,
                SourceId = session.Id,
                SourceItemId = message.Id,
                ObservationType = type,
                Certainty = ObservationCertainty.AiInferred,
                Confidence = Math.Round(Math.Clamp(finding.Confidence, 0, 1), 3),
                Evidence = new ObservationEvidence(finding.EvidenceText, finding.Correction, finding.EvidenceText),
                DetectedBy = $"{provider.Label}:conversation-analysis",
                OccurredAt = message.CreatedAt ?? "",
            });
        }
        return observations.Values.ToList();
    }

    private string? Reject(TutorSession session, ConversationFinding finding)
    {
        var message = session.Messages.FirstOrDefault(m => m.Id == finding.MessageId);
        if (message is null) return "unknown-message";
        if (message.Role != MessageRole.Learner) return "not-learner-message";
        if (string.IsNullOrWhiteSpace(finding.EvidenceText) || !message.Text.Contains(finding.EvidenceText.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return "evidence-not-in-message";
        }
        if (finding.Kind != FindingKind.NewExpression && (finding.GrammarTopicId is null || !knowledgeBase.Exists(finding.GrammarTopicId)))
        {
            return "unknown-topic";
        }
        if (finding.GrammarTopicId is not null && !knowledgeBase.Exists(finding.GrammarTopicId)) return "unknown-topic";
        if (finding.Confidence < MinimumConfidence) return "low-confidence";
        return null;
    }
}
