using System.Text.Json;
using System.Text.Json.Nodes;
using LineLanguageLearn.Learning.Contracts;
using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Persistence;
using LineLanguageLearn.Learning.Tutor.Tools;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace LineLanguageLearn.Learning.Tutor.Mcp;

public sealed class TutorMcpFunctions(IServiceProvider services, ReferenceDataLoader references, ILogger<TutorMcpFunctions> logger)
{
    public static readonly string[] ToolNames =
    [
        "get_learning_profile", "get_recent_mistakes", "search_grammar_rules",
        "record_learning_observation", "create_practice_session", "get_review_plan",
    ];

    public static void Configure(IFunctionsWorkerApplicationBuilder builder)
    {
        foreach (var name in ToolNames)
            builder.ConfigureMcpTool(name).WithInputSchema(ContractSchemas.GetRaw(SchemaNames.ToolInput(name)));
    }

    [Function("TutorMcpGetLearningProfile")]
    public Task<string> GetLearningProfileAsync(
        [McpToolTrigger("get_learning_profile", "Get the current learner's level, goals, weak and strong grammar topics.")] ToolInvocationContext context,
        CancellationToken ct) => InvokeAsync("get_learning_profile", context, ct);

    [Function("TutorMcpGetRecentMistakes")]
    public Task<string> GetRecentMistakesAsync(
        [McpToolTrigger("get_recent_mistakes", "List the learner's most recent mistakes from practice and conversations (newest first).")] ToolInvocationContext context,
        CancellationToken ct) => InvokeAsync("get_recent_mistakes", context, ct);

    [Function("TutorMcpSearchGrammarRules")]
    public Task<string> SearchGrammarRulesAsync(
        [McpToolTrigger("search_grammar_rules", "Search the grammar knowledge base. Use it before explaining grammar; cite the returned topic.")] ToolInvocationContext context,
        CancellationToken ct) => InvokeAsync("search_grammar_rules", context, ct);

    [Function("TutorMcpRecordLearningObservation")]
    public Task<string> RecordLearningObservationAsync(
        [McpToolTrigger("record_learning_observation", "Record a grammar error, possible misunderstanding or correct use found in a learner message (quote the exact text).")] ToolInvocationContext context,
        CancellationToken ct) => InvokeAsync("record_learning_observation", context, ct);

    [Function("TutorMcpCreatePracticeSession")]
    public Task<string> CreatePracticeSessionAsync(
        [McpToolTrigger("create_practice_session", "Create a practice session (mini app exercises) for up to 3 grammar topics.")] ToolInvocationContext context,
        CancellationToken ct) => InvokeAsync("create_practice_session", context, ct);

    [Function("TutorMcpGetReviewPlan")]
    public Task<string> GetReviewPlanAsync(
        [McpToolTrigger("get_review_plan", "Get the learner's current review plan (topics to review and why).")] ToolInvocationContext context,
        CancellationToken ct) => InvokeAsync("get_review_plan", context, ct);

    private async Task<string> InvokeAsync(string name, ToolInvocationContext context, CancellationToken ct)
    {
        var reference = context.Transport is HttpTransport transport &&
            transport.Headers.TryGetValue(McpTurnService.ContextHeader, out var value) ? value : null;
        try
        {
            if (string.IsNullOrWhiteSpace(reference))
                throw new ToolException("invalid-tool-context", "A valid, active tutor turn context is required.");
            // MCP invocations can land on an instance that has never handled an HTTP learning request.
            await references.LoadAsync(ct);
            return await services.GetRequiredService<McpTurnService>()
                .InvokeAsync(reference, name, JsonSerializer.SerializeToElement(context.Arguments ?? [], LearningJson.Options), ct);
        }
        catch (ToolException ex)
        {
            logger.LogWarning("MCP tool {Tool} rejected: {Code}", name, ex.Code);
            // Return an explicit error without echoing arguments or headers.
            return new JsonObject { ["error"] = new JsonObject { ["code"] = ex.Code, ["message"] = ex.Message } }.ToJsonString();
        }
    }
}
