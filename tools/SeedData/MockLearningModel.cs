using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Knowledge;

namespace LineLanguageLearn.Learning.Ai.Mock;

/// <summary>
/// Deterministic stand-in for Foundry Models. Output depends on topics, level and seed, and goes through
/// the same JSON + schema validation path as a real model response. It does not call any service.
/// </summary>
public sealed class MockLearningModel(SampleQuestionBank bank) : ILearningModel
{
    public ProviderInfo Info { get; } = new(ProviderKind.Mock, "mock-learning-model-v1");

    public Task<ModelResponse> GenerateQuestionsAsync(QuestionGenerationPrompt prompt, CancellationToken ct)
    {
        var (_, maxDifficulty) = LevelPolicy.DifficultyRange(prompt.Level);
        var grounded = prompt.Grammar.ToDictionary(h => h.Topic.Id);
        var avoid = new HashSet<string>(prompt.AvoidPrompts, StringComparer.Ordinal);

        var queues = prompt.TopicIds
            .Where(grounded.ContainsKey)
            .Select(topicId => new Queue<SampleQuestion>(bank.Questions
                .Where(q => q.GrammarTopicId == topicId && q.Difficulty <= maxDifficulty && !avoid.Contains(q.Prompt))
                .OrderBy(q => StableHash.Of($"{prompt.Seed}:{q.Id}"))))
            .ToList();

        var selected = new List<GeneratedQuestion>();
        while (selected.Count < prompt.QuestionCount && queues.Any(q => q.Count > 0))
        {
            foreach (var queue in queues.Where(q => q.Count > 0))
            {
                if (selected.Count >= prompt.QuestionCount) break;
                var item = queue.Dequeue();
                var hit = grounded[item.GrammarTopicId];
                selected.Add(new GeneratedQuestion(
                    item.Type,
                    item.GrammarTopicId,
                    item.Difficulty,
                    item.Type == QuestionType.MultipleChoice ? "空欄に入る正しい語を選んでください。" : "空欄に入る語を入力してください。",
                    item.Prompt,
                    item.Choices,
                    item.CorrectAnswer,
                    item.AcceptedAnswers,
                    item.ExplanationJa,
                    item.RuleIds,
                    hit.SourceIds.ToList()));
            }
        }

        return Task.FromResult(Respond(new GeneratedQuestionBatch(selected)));
    }

    public Task<ModelResponse> AnalyzeConversationAsync(ConversationAnalysisPrompt prompt, CancellationToken ct)
    {
        var findings = new List<ConversationFinding>();
        foreach (var message in prompt.Session.Messages.Where(m => m.Role == MessageRole.Learner))
        {
            foreach (var match in MockGrammarHeuristics.Detect(message.Text))
            {
                findings.Add(new ConversationFinding(
                    match.Kind == HeuristicKind.Error ? FindingKind.GrammarError : FindingKind.CorrectUse,
                    match.TopicId,
                    message.Id,
                    match.Evidence,
                    match.Suggestion,
                    match.ExplanationJa,
                    match.Kind == HeuristicKind.Error ? 0.8 : 0.7));
            }
        }
        var errors = findings.Count(f => f.Kind == FindingKind.GrammarError);
        var summary = errors == 0
            ? "（Mock分析）大きな文法上の誤りは検出されませんでした。"
            : $"（Mock分析）{errors} 件の文法上の誤りを検出しました。";
        return Task.FromResult(Respond(new ConversationAnalysisOutput(findings, summary)));
    }

    public Task<ModelResponse> JudgeQuestionAsync(QuestionJudgePrompt prompt, CancellationToken ct)
    {
        var issues = new List<JudgementIssue>();
        var q = prompt.Question;
        if (q.Type == QuestionType.MultipleChoice && q.Choices.Count(c => string.Equals(c.Trim(), q.CorrectAnswer.Trim(), StringComparison.OrdinalIgnoreCase)) != 1)
        {
            issues.Add(new JudgementIssue("ambiguous-answer", "正答と同じ選択肢が1つではありません。"));
        }
        var verdict = issues.Count == 0 ? JudgementVerdict.Pass : JudgementVerdict.Fail;
        var judgement = new QuestionJudgementOutput(
            verdict,
            verdict == JudgementVerdict.Pass ? 0.75 : 0.2,
            issues,
            "Mock評価：意味的な妥当性は実際には評価していません。Foundry 接続時にモデルによる評価へ置き換わります。");
        return Task.FromResult(Respond(judgement));
    }

    private ModelResponse Respond<T>(T value) => new(LearningJson.Serialize(value), Info, null);
}
