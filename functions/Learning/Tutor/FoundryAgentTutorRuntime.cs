using System.Text.Json.Nodes;
using LineLanguageLearn.Learning.Ai.Foundry;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Tutor.Mcp;
using LineLanguageLearn.Learning.Tutor.Tools;
using Microsoft.Extensions.Options;

namespace LineLanguageLearn.Learning.Tutor;

/// <summary>The saved Foundry agent owns instructions and tools; the application owns history and turn scope.</summary>
public sealed class FoundryAgentTutorRuntime(
    FoundryAgentClient client, McpTurnService turns, IOptions<LearningOptions> options) : ITutorAgentRuntime
{
    public ProviderInfo Info => new(ProviderKind.Foundry, $"agent:{options.Value.Foundry.AgentName}:{options.Value.Foundry.AgentVersion}");

    public async Task<TutorTurnResult> RunTurnAsync(TutorTurnContext context, IToolInvoker tools, CancellationToken ct)
    {
        var lease = await turns.BeginAsync(context, ct);
        JsonObject response;
        TutorToolTurn turn;
        try
        {
            response = await client.CreateResponseAsync(BuildRequest(context, lease.Reference, options.Value), ct);
        }
        finally
        {
            using var finish = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            turn = await turns.CloseAsync(lease, finish.Token);
        }
        if (turn.Calls.Any(c => !c.Completed))
            throw new DomainException(DomainErrorKind.Unavailable, "tool-call-incomplete", "An MCP tool did not finish before the model response.");
        if (response["status"]?.GetValue<string>() is { } status && status != "completed" ||
            response["error"] is not null ||
            (response["output"]?.AsArray() ?? []).Any(item =>
                item?["type"]?.GetValue<string>() is "mcp_approval_request" or "function_call" ||
                item?["type"]?.GetValue<string>() is "mcp_call" or "mcp_list_tools" &&
                    (item["error"] is not null || item["status"]?.GetValue<string>() is { } toolStatus && toolStatus != "completed")))
            throw new DomainException(DomainErrorKind.Unavailable, "tutor-mcp-failed", "Foundry Agent Service could not complete the MCP tutor turn.");

        foreach (var group in (response["output"]?.AsArray() ?? [])
            .Where(item => item?["type"]?.GetValue<string>() == "mcp_call")
            .GroupBy(item => item!["name"]?.GetValue<string>()))
        {
            if (group.Key is null || turn.Calls.Count(call => call.Name == group.Key) < group.Count())
                throw new DomainException(DomainErrorKind.Unavailable, "tutor-mcp-audit-missing", "MCP execution could not be verified against this turn's audit records.");
        }

        return new TutorTurnResult(FoundryResponsesClient.ExtractOutputText(response), [],
            turn.Calls.Where(c => c.Record is not null).Select(c => c.Record!).ToList(),
            turn.Calls.SelectMany(c => c.SourceIds).Distinct().ToList());
    }

    public static JsonObject BuildRequest(TutorTurnContext context, string reference, LearningOptions settings)
    {
        var input = new JsonArray();
        foreach (var message in context.Session.Messages.TakeLast(settings.Agent.HistoryMessagesForModel))
            input.Add(new JsonObject
            {
                ["role"] = message.Role == MessageRole.Learner ? "user" : "assistant",
                ["content"] = message.Role == MessageRole.Learner ? $"[messageId={message.Id}] {message.Text}" : message.Text,
            });
        return new JsonObject
        {
            ["agent_reference"] = new JsonObject
            {
                ["type"] = "agent_reference",
                ["name"] = settings.Foundry.AgentName,
                ["version"] = settings.Foundry.AgentVersion,
            },
            ["input"] = input,
            ["store"] = false,
            ["parallel_tool_calls"] = false,
            ["max_tool_calls"] = settings.Agent.MaxToolCallsPerTurn,
            ["structured_inputs"] = new JsonObject
            {
                ["turnReference"] = reference,
                ["tutorName"] = context.Tutor.Name,
                ["persona"] = context.Tutor.PersonaJa,
                ["speakingStyle"] = context.Tutor.SpeakingStyle,
                ["level"] = context.Profile.CurrentLevel.ToString(),
                ["focusTopics"] = string.Join(", ", context.Session.FocusTopicIds),
                ["maxCorrections"] = context.Tutor.CorrectionPolicy.MaxCorrectionsPerTurn.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["correctionStyle"] = context.Tutor.CorrectionPolicy.Style.ToString().ToLowerInvariant(),
            },
        };
    }
}
