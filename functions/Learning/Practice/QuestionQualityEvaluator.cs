using LineLanguageLearn.Learning.Ai;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Knowledge;
using LineLanguageLearn.Learning.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LineLanguageLearn.Learning.Practice;

public sealed record QuestionEvaluationContext(CefrLevel Level, IReadOnlyCollection<string> RequestedTopicIds, IReadOnlyList<GrammarSearchHit> Grammar);

/// <summary>
/// Content-quality checks for generated questions. Structured Outputs schema validation already happened
/// (StructuredOutput.Parse); this class checks meaning-level constraints. Deterministic checks gate the
/// question; the optional semantic judge (an LLM) can only add a failure, never override a deterministic one.
/// </summary>
public sealed class QuestionQualityEvaluator(
    LearningModelGateway models,
    TimeProvider clock,
    IOptions<LearningOptions> options,
    ILogger<QuestionQualityEvaluator> logger)
{
    public const string Blank = "___";

    public static List<QuestionValidationCheck> DeterministicChecks(GeneratedQuestion q, QuestionEvaluationContext context)
    {
        var checks = new List<QuestionValidationCheck>();
        void Check(string id, bool passed, string message, ValidationSeverity severity = ValidationSeverity.Error) =>
            checks.Add(new QuestionValidationCheck(id, ValidationCheckKind.Deterministic, passed, severity, message));

        var hit = context.Grammar.FirstOrDefault(h => h.Topic.Id == q.GrammarTopicId);
        var normalizedAnswer = AnswerNormalizer.Normalize(q.CorrectAnswer);
        var accepted = q.AcceptedAnswers.Select(AnswerNormalizer.Normalize).Where(a => a.Length > 0).ToList();
        var (minDifficulty, maxDifficulty) = LevelPolicy.DifficultyRange(context.Level);

        Check("instruction-present", !string.IsNullOrWhiteSpace(q.InstructionJa), "設問の指示文があること");
        Check("prompt-has-single-blank", CountOccurrences(q.Prompt, Blank) == 1, "問題文に空欄（___）がちょうど1つあること");
        Check("topic-requested", context.RequestedTopicIds.Contains(q.GrammarTopicId), "指定された文法項目を対象にしていること");
        Check("topic-grounded", hit is not null && q.SourceIds.Contains(q.GrammarTopicId) && q.SourceIds.All(s => hit.SourceIds.Contains(s)),
            "検索で取得した文法知識に根拠（sourceIds）を持つこと");
        Check("rules-exist", hit is not null && q.RuleIds.Count > 0 && q.RuleIds.All(r => hit.Topic.Rules.Any(rule => rule.Id == r)),
            "参照しているルールIDが文法知識に存在すること");
        Check("difficulty-in-range", q.Difficulty >= minDifficulty && q.Difficulty <= maxDifficulty, $"難易度が {context.Level} の範囲（{minDifficulty}〜{maxDifficulty}）であること");
        Check("topic-level-allowed", hit is not null && LevelPolicy.TopicAllowed(context.Level, hit.Topic.Level), "文法項目のレベルが学習者に適していること");
        Check("answer-present", normalizedAnswer.Length > 0 && accepted.Contains(normalizedAnswer), "正答が存在し、許容解答に含まれること");
        Check("explanation-mentions-answer", normalizedAnswer.Length > 0 && AnswerNormalizer.Normalize(q.ExplanationJa).Contains(normalizedAnswer, StringComparison.Ordinal),
            "解説が正答に言及し、問題と整合していること");

        if (q.Type == QuestionType.MultipleChoice)
        {
            var choices = q.Choices.Select(AnswerNormalizer.Normalize).ToList();
            Check("choice-count", choices.Count is >= 3 and <= 5, "選択肢が3〜5個であること");
            Check("choices-distinct", choices.Distinct(StringComparer.Ordinal).Count() == choices.Count && choices.All(c => c.Length > 0), "重複・空の選択肢がないこと");
            Check("answer-in-choices", choices.Count(c => c == normalizedAnswer) == 1, "正答が選択肢にちょうど1つ含まれること");
            Check("single-correct-choice", choices.Count(c => accepted.Contains(c)) == 1, "正答になりうる選択肢が複数ないこと");
        }
        else
        {
            Check("cloze-has-no-choices", q.Choices.Count == 0, "穴埋め問題に選択肢がないこと");
            Check("accepted-distinct", accepted.Distinct(StringComparer.Ordinal).Count() == accepted.Count, "許容解答に重複がないこと");
            Check("answer-not-in-prompt", !AnswerNormalizer.Normalize(q.Prompt.Replace(Blank, " ")).Split(' ').Contains(normalizedAnswer) || normalizedAnswer.Length <= 2,
                "問題文に正答がそのまま書かれていないこと", ValidationSeverity.Warning);
        }
        return checks;
    }

    public async Task<QuestionValidation> EvaluateAsync(GeneratedQuestion question, QuestionEvaluationContext context, string generationId, CancellationToken ct, bool? useSemanticJudge = null)
    {
        var checks = DeterministicChecks(question, context);
        SemanticJudgement? semantic = null;
        var deterministicPassed = checks.All(c => c.Passed || c.Severity == ValidationSeverity.Warning);

        if (deterministicPassed && (useSemanticJudge ?? options.Value.QuestionGeneration.UseSemanticJudge))
        {
            var topic = context.Grammar.First(h => h.Topic.Id == question.GrammarTopicId).Topic;
            var judged = await models.JudgeQuestionAsync(new QuestionJudgePrompt(question, topic, context.Level), ct);
            semantic = new SemanticJudgement(judged.Value.Verdict, judged.Value.Score, judged.Value.RationaleJa, judged.Response.Provider);
            checks.Add(new QuestionValidationCheck("semantic-judge", ValidationCheckKind.Semantic,
                judged.Value.Verdict != JudgementVerdict.Fail,
                judged.Value.Verdict == JudgementVerdict.Uncertain ? ValidationSeverity.Warning : ValidationSeverity.Error,
                judged.Value.Issues.Count == 0 ? "意味的評価で問題が見つからないこと" : string.Join(" / ", judged.Value.Issues.Select(i => i.MessageJa))));
        }

        var failed = checks.Where(c => !c.Passed && c.Severity == ValidationSeverity.Error).Select(c => c.Id).ToList();
        var validation = new QuestionValidation(failed.Count == 0 ? ValidationStatus.Passed : ValidationStatus.Failed, checks, semantic, clock.GetUtcNow().ToString("O"));
        logger.QuestionEvaluated(generationId, question.GrammarTopicId, validation.Status.ToString(), string.Join(',', failed), semantic?.Verdict.ToString() ?? "skipped");
        return validation;
    }

    private static int CountOccurrences(string text, string token)
    {
        var count = 0;
        for (var index = text.IndexOf(token, StringComparison.Ordinal); index >= 0; index = text.IndexOf(token, index + token.Length, StringComparison.Ordinal)) count++;
        return count;
    }
}
