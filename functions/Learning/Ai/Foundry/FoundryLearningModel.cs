using System.Text.Json.Nodes;
using LineLanguageLearn.Learning.Contracts;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Infrastructure;
using Microsoft.Extensions.Options;

namespace LineLanguageLearn.Learning.Ai.Foundry;

/// <summary>
/// Foundry Models adapter for the structured tasks (question generation, conversation analysis, judging)
/// using Responses API Structured Outputs. Only builds prompts and parses responses; validation stays in
/// the application layer. Compiles and is unit-tested for request shape only (no live call in this repo).
/// </summary>
public sealed class FoundryLearningModel(FoundryResponsesClient client, IOptions<LearningOptions> options) : ILearningModel
{
    private FoundryOptions Settings => options.Value.Foundry;

    public ProviderInfo Info => new(ProviderKind.Foundry, Settings.Model);

    public async Task<ModelResponse> GenerateQuestionsAsync(QuestionGenerationPrompt prompt, CancellationToken ct)
    {
        var body = BuildQuestionGenerationRequest(prompt, Settings.Model);
        return await SendAsync(body, Settings.Model, ct);
    }

    public async Task<ModelResponse> AnalyzeConversationAsync(ConversationAnalysisPrompt prompt, CancellationToken ct)
    {
        var body = BuildConversationAnalysisRequest(prompt, Settings.Model);
        return await SendAsync(body, Settings.Model, ct);
    }

    public async Task<ModelResponse> JudgeQuestionAsync(QuestionJudgePrompt prompt, CancellationToken ct)
    {
        var model = Settings.JudgeModel ?? Settings.Model;
        var system = PromptTemplates.Render("question-judge.system", new Dictionary<string, string>
        {
            ["languageName"] = PromptTemplates.LanguageName(prompt.Topic.Language),
            ["level"] = prompt.Level.ToString(),
        });
        var user = $"<question>{LearningJson.Serialize(prompt.Question)}</question>\n<grammar>{LearningJson.Serialize(GrammarContext(prompt.Topic))}</grammar>";
        var body = FoundryResponsesClient.StructuredRequest(model, system, user, SchemaNames.QuestionJudgement, "question_judgement");
        return await SendAsync(body, model, ct);
    }

    public static JsonObject BuildQuestionGenerationRequest(QuestionGenerationPrompt prompt, string model)
    {
        var (min, max) = LevelPolicy.DifficultyRange(prompt.Level);
        var system = PromptTemplates.Render("question-generation.system", new Dictionary<string, string>
        {
            ["languageName"] = PromptTemplates.LanguageName(prompt.Language),
            ["level"] = prompt.Level.ToString(),
            ["questionCount"] = prompt.QuestionCount.ToString(),
            ["purpose"] = prompt.Purpose,
            ["minDifficulty"] = min.ToString(),
            ["maxDifficulty"] = max.ToString(),
        });
        var user = string.Join('\n',
            $"<request>{LearningJson.Serialize(new { topicIds = prompt.TopicIds, prompt.QuestionCount, prompt.Purpose })}</request>",
            $"<grammar_context>{LearningJson.Serialize(prompt.Grammar.Select(h => new { context = GrammarContext(h.Topic), sourceIds = h.SourceIds }))}</grammar_context>",
            $"<learner>{LearningJson.Serialize(prompt.Learner)}</learner>",
            $"<avoid>{LearningJson.Serialize(prompt.AvoidPrompts)}</avoid>");
        return FoundryResponsesClient.StructuredRequest(model, system, user, SchemaNames.GeneratedQuestionBatch, "generated_question_batch");
    }

    public static JsonObject BuildConversationAnalysisRequest(ConversationAnalysisPrompt prompt, string model)
    {
        var system = PromptTemplates.Render("conversation-analysis.system", new Dictionary<string, string>
        {
            ["languageName"] = PromptTemplates.LanguageName(prompt.Language),
            ["level"] = prompt.Level.ToString(),
        });
        var transcript = prompt.Session.Messages.Select(m => new { id = m.Id, role = m.Role, text = m.Text });
        var topics = prompt.CandidateTopics.Select(t => new { id = t.Id, title = t.Title, titleJa = t.TitleJa });
        var user = $"<topics>{LearningJson.Serialize(topics)}</topics>\n<conversation>{LearningJson.Serialize(transcript)}</conversation>";
        return FoundryResponsesClient.StructuredRequest(model, system, user, SchemaNames.ConversationAnalysis, "conversation_analysis");
    }

    private static object GrammarContext(GrammarTopic topic) => new
    {
        topicId = topic.Id,
        topic.Title,
        topic.TitleJa,
        level = topic.Level.ToString(),
        topic.UsageJa,
        rules = topic.Rules,
        examples = topic.Examples.Select(e => new { e.Text, e.TranslationJa }),
        commonMistakes = topic.CommonMistakes,
    };

    private async Task<ModelResponse> SendAsync(JsonObject body, string model, CancellationToken ct)
    {
        var response = await client.CreateResponseAsync(body, ct);
        return new ModelResponse(FoundryResponsesClient.ExtractOutputText(response), new ProviderInfo(ProviderKind.Foundry, model), response["id"]?.GetValue<string>());
    }
}
