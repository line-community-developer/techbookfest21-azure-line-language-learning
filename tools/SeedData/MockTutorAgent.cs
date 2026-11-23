using System.Text.Json;
using System.Text.RegularExpressions;
using LineLanguageLearn.Learning.Ai.Mock;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Tutor.Tools;

namespace LineLanguageLearn.Learning.Tutor;

/// <summary>
/// MOCK tutor agent with a deterministic scenario. It does not pretend to be an LLM: decisions come from
/// fixed rules (MockGrammarHeuristics + keywords). Its value is that it really exercises the tool pipeline
/// (real tool calls → real services → real data), so tool calling can be tested without Foundry.
/// </summary>
public sealed partial class MockTutorAgent : ITutorAgentRuntime
{
    private static readonly Dictionary<string, string> FollowUps = new()
    {
        [KnownTopics.PassatoAuxiliary] = "E dopo, a che ora sei tornato/a a casa?",
        [KnownTopics.VerbDi] = "A che ora hai finito di lavorare ieri?",
        [KnownTopics.VerbA] = "Quando hai cominciato a studiare l'italiano?",
        [KnownTopics.AvereVogliaDi] = "E adesso, di che cosa hai voglia?",
        [KnownTopics.ReflexivePassato] = "A che ora ti sei alzato/a stamattina?",
        [KnownTopics.Piacere] = "Che cosa ti piace mangiare in Italia?",
    };

    private static readonly string[] Reactions = ["Che bello!", "Interessante!", "Capisco!", "Ottimo!"];

    public ProviderInfo Info { get; } = new(ProviderKind.Mock, "mock-tutor-agent-v1");

    public async Task<TutorTurnResult> RunTurnAsync(TutorTurnContext context, IToolInvoker tools, CancellationToken ct)
    {
        var message = context.LearnerMessage;
        var isFirstLearnerTurn = context.Session.Messages.Count(m => m.Role == MessageRole.Learner) == 1;
        var call = 0;
        string NextId() => $"{message.Id}-call{++call}";

        var weakTopics = context.Profile.WeakTopics;
        if (isFirstLearnerTurn)
        {
            var profileResult = await tools.InvokeAsync(NextId(), "get_learning_profile", "{}", ct);
            if (profileResult.Status == ToolCallStatus.Succeeded)
            {
                using var doc = JsonDocument.Parse(profileResult.OutputJson);
                weakTopics = doc.RootElement.GetProperty("weakTopics").EnumerateArray().Select(t => t.GetProperty("topicId").GetString()!).ToList();
            }
            await tools.InvokeAsync(NextId(), "get_recent_mistakes", """{"limit":5}""", ct);
        }

        var matches = MockGrammarHeuristics.Detect(message.Text);
        var corrections = new List<TutorCorrection>();
        var notes = new List<string>();
        var maxCorrections = context.Tutor.CorrectionPolicy.MaxCorrectionsPerTurn;

        foreach (var error in matches.Where(m => m.Kind == HeuristicKind.Error))
        {
            await RecordAsync(tools, NextId(), message.Id, error, "grammar-error", 0.8, ct);
            if (corrections.Count >= maxCorrections) continue;

            var search = await tools.InvokeAsync(NextId(), "search_grammar_rules",
                LearningJson.Serialize(new { query = error.Evidence, topicId = error.TopicId, level = (string?)null, limit = 1 }), ct);
            var rule = search.Status == ToolCallStatus.Succeeded ? RuleSummary(search.OutputJson, error.RuleId) : null;
            corrections.Add(new TutorCorrection(error.TopicId, error.Evidence, error.Suggestion ?? error.Evidence, rule ?? error.ExplanationJa));
            notes.Add($"💡「{error.Evidence}」→「{error.Suggestion}」：{rule ?? error.ExplanationJa}");
        }
        foreach (var good in matches.Where(m => m.Kind == HeuristicKind.CorrectUse && weakTopics.Contains(m.TopicId)))
        {
            await RecordAsync(tools, NextId(), message.Id, good, "correct-use", 0.7, ct);
        }

        string? extra = null;
        if (AsksForPractice().IsMatch(message.Text))
        {
            var topics = (corrections.Select(c => c.GrammarTopicId).Concat(weakTopics)).Distinct().Take(2).ToList();
            if (topics.Count == 0) topics = context.Session.FocusTopicIds.Take(2).ToList();
            var created = await tools.InvokeAsync(NextId(), "create_practice_session",
                LearningJson.Serialize(new { topicIds = topics, questionCount = 5 }), ct);
            extra = created.Status == ToolCallStatus.Succeeded
                ? "📝 苦手な項目の練習問題を用意しました。ミニアプリの「演習」から解いてみてください。"
                : "📝 ごめんなさい、練習問題を今は用意できませんでした。";
        }
        else if (AsksForReview().IsMatch(message.Text))
        {
            var plan = await tools.InvokeAsync(NextId(), "get_review_plan", "{}", ct);
            extra = plan.Status == ToolCallStatus.Succeeded && !plan.OutputJson.Contains("\"reviewPlan\":null", StringComparison.Ordinal)
                ? "📚 復習プランがあります。ホーム画面の「会話から見つかった復習」を確認してね。"
                : "📚 今はまだ復習プランがありません。もう少しおしゃべりしましょう！";
        }

        var reaction = Reactions[context.Session.Messages.Count % Reactions.Length];
        var recast = corrections.FirstOrDefault() is { } first ? $" Si dice: «{first.Suggestion}»." : "";
        var focus = context.Session.FocusTopicIds.Concat(weakTopics).Distinct()
            .FirstOrDefault(t => FollowUps.ContainsKey(t) && !context.Session.Messages.Any(m => m.Role == MessageRole.Tutor && m.Text.Contains(FollowUps[t], StringComparison.Ordinal)));
        var question = focus is not null ? FollowUps[focus] : "Raccontami ancora: che cosa fai questo fine settimana?";

        var parts = new List<string> { $"{reaction}{recast} {question}" };
        parts.AddRange(notes);
        if (extra is not null) parts.Add(extra);
        return new TutorTurnResult(string.Join("\n\n", parts), corrections);
    }

    private static Task<ToolInvocationResult> RecordAsync(IToolInvoker tools, string callId, string messageId, HeuristicMatch match, string type, double confidence, CancellationToken ct) =>
        tools.InvokeAsync(callId, "record_learning_observation", LearningJson.Serialize(new
        {
            messageId,
            grammarTopicId = match.TopicId,
            observationType = type,
            evidenceText = match.Evidence,
            correction = match.Suggestion,
            confidence,
        }), ct);

    // Explain with the rule that matches the detected error, falling back to the topic's first rule.
    private static string? RuleSummary(string json, string? ruleId)
    {
        using var doc = JsonDocument.Parse(json);
        var results = doc.RootElement.GetProperty("results");
        if (results.GetArrayLength() == 0) return null;
        var rules = results[0].GetProperty("rules").EnumerateArray().ToList();
        if (rules.Count == 0) return null;
        var match = rules.FirstOrDefault(r => r.GetProperty("id").GetString() == ruleId);
        return (match.ValueKind == JsonValueKind.Object ? match : rules[0]).GetProperty("summaryJa").GetString();
    }

    [GeneratedRegex(@"esercizi|練習|問題|quiz", RegexOptions.IgnoreCase)]
    private static partial Regex AsksForPractice();

    [GeneratedRegex(@"ripass|復習|review", RegexOptions.IgnoreCase)]
    private static partial Regex AsksForReview();
}
