using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace LineLanguageLearn.Learning.Contracts;

public sealed record SchemaViolation(string Location, string Message);

public sealed class ContractValidationException(string schemaName, IReadOnlyList<SchemaViolation> violations)
    : Exception($"Payload does not match contract '{schemaName}': {string.Join("; ", violations.Take(5).Select(v => $"{v.Location} {v.Message}"))}")
{
    public string SchemaName { get; } = schemaName;
    public IReadOnlyList<SchemaViolation> Violations { get; } = violations;
}

/// <summary>
/// JSON Schemas exported from the shared Zod contracts (pnpm run contracts:export). They are the single
/// source of truth for API views, Structured Outputs and tool arguments on the C# side.
/// </summary>
public static class ContractSchemas
{
    private static readonly Assembly Assembly = typeof(ContractSchemas).Assembly;
    private static readonly ConcurrentDictionary<string, JsonSchema> Cache = new();
    private static readonly ConcurrentDictionary<string, string> RawCache = new();

    public static string ResourceName(string schemaName) => $"LearningContracts.{schemaName}.schema.json";

    public static string GetRaw(string schemaName) => RawCache.GetOrAdd(schemaName, name =>
    {
        using var stream = Assembly.GetManifestResourceStream(ResourceName(name))
            ?? throw new InvalidOperationException($"Contract schema '{name}' is not embedded. Run pnpm run contracts:export.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    public static JsonSchema Get(string schemaName) => Cache.GetOrAdd(schemaName, name => JsonSchema.FromText(GetRaw(name)));

    public static IReadOnlyList<SchemaViolation> Validate(string schemaName, JsonElement payload)
    {
        var result = Get(schemaName).Evaluate(payload, new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (result.IsValid) return [];
        var violations = new List<SchemaViolation>();
        foreach (var detail in result.Details ?? [])
        {
            if (detail.Errors is null) continue;
            foreach (var error in detail.Errors)
            {
                violations.Add(new SchemaViolation(detail.InstanceLocation.ToString() is { Length: > 0 } l ? l : "/", $"{error.Key}: {error.Value}"));
            }
        }
        if (violations.Count == 0) violations.Add(new SchemaViolation("/", "invalid"));
        return violations;
    }

    public static void EnsureValid(string schemaName, JsonElement payload)
    {
        var violations = Validate(schemaName, payload);
        if (violations.Count > 0) throw new ContractValidationException(schemaName, violations);
    }

    /// <summary>
    /// Schema for strict Structured Outputs / strict function tools: drops keywords that model providers
    /// commonly reject. The full schema is still enforced locally after the model responds.
    /// </summary>
    public static JsonObject GetModelFacingSchema(string schemaName)
    {
        var node = JsonNode.Parse(GetRaw(schemaName))!.AsObject();
        Strip(node);
        return node;

        static void Strip(JsonNode? current)
        {
            switch (current)
            {
                case JsonObject obj:
                    foreach (var key in new[] { "$schema", "minimum", "maximum", "minLength", "maxLength", "minItems", "maxItems", "pattern", "format", "exclusiveMinimum", "exclusiveMaximum" })
                    {
                        obj.Remove(key);
                    }
                    foreach (var child in obj.Select(p => p.Value).ToList()) Strip(child);
                    break;
                case JsonArray array:
                    foreach (var child in array) Strip(child);
                    break;
            }
        }
    }
}

public static class SchemaNames
{
    public const string LearningProfile = "model.LearningProfile";
    public const string GrammarTopic = "model.GrammarTopic";
    public const string PracticeSession = "model.PracticeSession";
    public const string LearningObservation = "model.LearningObservation";
    public const string TutorProfile = "model.TutorProfile";
    public const string TutorSession = "model.TutorSession";
    public const string ReviewPlan = "model.ReviewPlan";
    public const string WorkflowRun = "model.WorkflowRun";

    public const string GeneratedQuestionBatch = "ai.GeneratedQuestionBatch";
    public const string ConversationAnalysis = "ai.ConversationAnalysis";
    public const string QuestionJudgement = "ai.QuestionJudgement";

    public const string SystemStatus = "api.SystemStatus";
    public const string DashboardView = "api.DashboardView";
    public const string HistoryView = "api.HistoryView";
    public const string PracticeSessionView = "api.PracticeSessionView";
    public const string AnswerResult = "api.AnswerResult";
    public const string GrammarListView = "api.GrammarListView";
    public const string GrammarSearchView = "api.GrammarSearchView";
    public const string TutorSessionView = "api.TutorSessionView";
    public const string TutorSessionListView = "api.TutorSessionListView";
    public const string SendTutorMessageResponse = "api.SendTutorMessageResponse";
    public const string EndTutorSessionResponse = "api.EndTutorSessionResponse";
    public const string StartPracticeRequest = "api.StartPracticeRequest";
    public const string SubmitAnswerRequest = "api.SubmitAnswerRequest";
    public const string StartTutorSessionRequest = "api.StartTutorSessionRequest";
    public const string SendTutorMessageRequest = "api.SendTutorMessageRequest";

    public static string ToolInput(string toolName) => $"tool.{toolName}.input";
    public static string ToolOutput(string toolName) => $"tool.{toolName}.output";
}
