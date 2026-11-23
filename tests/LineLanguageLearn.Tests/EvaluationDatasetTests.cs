using System.Text.Json;
using System.Text.Json.Nodes;
using LineLanguageLearn.Learning.Ai;
using LineLanguageLearn.Learning.Analysis;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Knowledge;
using LineLanguageLearn.Learning.Practice;
using LineLanguageLearn.Learning.Tutor;
using Xunit.Abstractions;

namespace LineLanguageLearn.Tests;

/// <summary>
/// Runs the curated evaluation datasets (Evaluation/Datasets/*.json) and writes JSONL records
/// {datasetId, caseId, input, expected, actual, passed} to TestResults/evaluation so the same data can be
/// uploaded to Foundry Evaluation later. These are regression gates for deterministic components and the mock.
/// </summary>
public class EvaluationDatasetTests(ITestOutputHelper output)
{
    private static JsonObject Load(string file) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Evaluation", "Datasets", file)))!.AsObject();

    private void Write(string datasetId, List<JsonObject> records)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "TestResults", "evaluation");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{datasetId}.jsonl");
        File.WriteAllLines(path, records.Select(r => r.ToJsonString()));
        output.WriteLine($"{datasetId}: {records.Count(r => r["passed"]!.GetValue<bool>())}/{records.Count} passed → {path}");
    }

    private static JsonObject Record(string datasetId, JsonNode testCase, JsonNode? input, JsonNode? expected, JsonNode? actual, bool passed) => new()
    {
        ["datasetId"] = datasetId,
        ["caseId"] = testCase["id"]!.GetValue<string>(),
        ["input"] = input?.DeepClone(),
        ["expected"] = expected?.DeepClone(),
        ["actual"] = actual,
        ["passed"] = passed,
    };

    [Fact]
    public async Task Question_quality_dataset()
    {
        using var app = new TestApp();
        var generator = app.Get<QuestionGenerationService>();
        var dataset = Load("question-quality.json");
        var datasetId = dataset["datasetId"]!.GetValue<string>();
        var records = new List<JsonObject>();
        foreach (var c in dataset["cases"]!.AsArray())
        {
            var level = Enum.Parse<CefrLevel>(c!["level"]!.GetValue<string>());
            var topics = c["topicIds"]!.AsArray().Select(t => t!.GetValue<string>()).ToList();
            var question = LearningJson.Deserialize<GeneratedQuestion>(c["question"]!.ToJsonString());
            var context = await generator.RetrieveContextAsync(new QuestionGenerationSpec(TargetLanguage.It, level, topics, 1, "eval", "eval"), default);
            var checks = QuestionQualityEvaluator.DeterministicChecks(question, context);
            var failed = checks.Where(x => !x.Passed && x.Severity == ValidationSeverity.Error).Select(x => x.Id).Order().ToList();
            var status = failed.Count == 0 ? "passed" : "failed";
            var expectedFailed = c["expectedFailedChecks"]!.AsArray().Select(x => x!.GetValue<string>()).Order().ToList();
            var passed = status == c["expectedStatus"]!.GetValue<string>() && failed.SequenceEqual(expectedFailed);
            records.Add(Record(datasetId, c, c["question"], new JsonObject { ["status"] = c["expectedStatus"]!.DeepClone(), ["failedChecks"] = c["expectedFailedChecks"]!.DeepClone() },
                new JsonObject { ["status"] = status, ["failedChecks"] = new JsonArray(failed.Select(f => (JsonNode)f).ToArray()) }, passed));
        }
        Write(datasetId, records);
        Assert.All(records, r => Assert.True(r["passed"]!.GetValue<bool>(), r.ToJsonString()));
    }

    [Fact]
    public void Grading_dataset()
    {
        var dataset = Load("grading.json");
        var datasetId = dataset["datasetId"]!.GetValue<string>();
        var records = new List<JsonObject>();
        foreach (var c in dataset["cases"]!.AsArray())
        {
            var q = c!["question"]!;
            var question = new PracticeQuestion
            {
                Type = q["type"]!.GetValue<string>() == "cloze" ? QuestionType.Cloze : QuestionType.MultipleChoice,
                CorrectAnswer = q["correctAnswer"]!.GetValue<string>(),
                AcceptedAnswers = q["acceptedAnswers"]!.AsArray().Select(x => x!.GetValue<string>()).ToList(),
                Choices = q["choices"]!.AsArray().Select(x => x!.GetValue<string>()).ToList(),
            };
            var grade = AnswerGrader.Grade(question, c["answer"]!.GetValue<string>());
            var feedback = JsonNamingPolicy.KebabCaseLower.ConvertName(grade.FeedbackCode.ToString());
            var passed = grade.IsCorrect == c["expectedCorrect"]!.GetValue<bool>() && feedback == c["expectedFeedback"]!.GetValue<string>();
            records.Add(Record(datasetId, c, new JsonObject { ["question"] = q.DeepClone(), ["answer"] = c["answer"]!.DeepClone() },
                new JsonObject { ["isCorrect"] = c["expectedCorrect"]!.DeepClone(), ["feedback"] = c["expectedFeedback"]!.DeepClone() },
                new JsonObject { ["isCorrect"] = grade.IsCorrect, ["feedback"] = feedback }, passed));
        }
        Write(datasetId, records);
        Assert.All(records, r => Assert.True(r["passed"]!.GetValue<bool>(), r.ToJsonString()));
    }

    [Fact]
    public async Task Tutor_conversation_dataset()
    {
        var dataset = Load("tutor-conversation.json");
        var datasetId = dataset["datasetId"]!.GetValue<string>();
        var records = new List<JsonObject>();
        foreach (var c in dataset["cases"]!.AsArray())
        {
            using var app = new TestApp();
            var tutor = app.Get<TutorService>();
            var session = await tutor.StartSessionAsync(TestApp.Learner, null, TutorChannel.LocalSimulator, default);
            var reply = await tutor.SendMessageAsync(TestApp.Learner, session.Id, "eval", c!["utterance"]!.GetValue<string>(), default);
            var toolCalls = reply.Replies.SelectMany(r => r.ToolCalls).Where(t => t.Status == ToolCallStatus.Succeeded).Select(t => t.Name).ToList();

            var model = app.Get<LearningModelGateway>();
            var analysis = await model.AnalyzeConversationAsync(new ConversationAnalysisPrompt(reply.Session, app.Get<GrammarKnowledgeBase>().Topics, TargetLanguage.It, CefrLevel.A2), default);
            var accepted = app.Get<ConversationAnalysisService>().ValidateFindings(reply.Session, analysis.Value.Findings, analysis.Response.Provider, out _);
            var findings = accepted.Select(o => $"{JsonNamingPolicy.KebabCaseLower.ConvertName(o.ObservationType.ToString())}|{o.GrammarTopicId}|{o.Evidence.Text.ToLowerInvariant()}").Order().ToList();
            var expectedFindings = c["expectedFindings"]!.AsArray()
                .Select(f => $"{f!["kind"]!.GetValue<string>()}|{f["topicId"]!.GetValue<string>()}|{f["evidence"]!.GetValue<string>().ToLowerInvariant()}").Order().ToList();
            var expectedTools = c["expectedToolCalls"]!.AsArray().Select(t => t!.GetValue<string>()).ToList();

            var passed = findings.SequenceEqual(expectedFindings) && toolCalls.SequenceEqual(expectedTools);
            records.Add(Record(datasetId, c, c["utterance"], new JsonObject { ["findings"] = c["expectedFindings"]!.DeepClone(), ["toolCalls"] = c["expectedToolCalls"]!.DeepClone() },
                new JsonObject
                {
                    ["findings"] = new JsonArray(findings.Select(f => (JsonNode)f).ToArray()),
                    ["toolCalls"] = new JsonArray(toolCalls.Select(t => (JsonNode)t).ToArray()),
                    ["provider"] = analysis.Response.Provider.Label,
                }, passed));
        }
        Write(datasetId, records);
        Assert.All(records, r => Assert.True(r["passed"]!.GetValue<bool>(), r.ToJsonString()));
    }

    [Fact]
    public async Task Grammar_retrieval_dataset()
    {
        using var app = new TestApp();
        var retriever = app.Get<IGrammarRetriever>();
        var dataset = Load("grammar-retrieval.json");
        var datasetId = dataset["datasetId"]!.GetValue<string>();
        var records = new List<JsonObject>();
        foreach (var c in dataset["cases"]!.AsArray())
        {
            var hits = await retriever.SearchAsync(new GrammarQuery(TargetLanguage.It, c!["query"]!.GetValue<string>(), Limit: c["limit"]!.GetValue<int>()), default);
            var ids = hits.Select(h => h.Topic.Id).ToList();
            var expected = c["expectedTopicIds"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
            var passed = expected.Count == 0 ? ids.Count == 0 : expected.All(ids.Contains);
            records.Add(Record(datasetId, c, c["query"], c["expectedTopicIds"], new JsonArray(ids.Select(i => (JsonNode)i).ToArray()), passed));
        }
        Write(datasetId, records);
        Assert.All(records, r => Assert.True(r["passed"]!.GetValue<bool>(), r.ToJsonString()));
    }
}
