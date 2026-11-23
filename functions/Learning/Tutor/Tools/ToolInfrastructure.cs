using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using LineLanguageLearn.Learning.Contracts;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Identity;
using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LineLanguageLearn.Learning.Tutor.Tools;

/// <summary>Server-side context for a tool call. The learner comes from authentication, never from tool arguments.</summary>
public sealed class ToolExecutionContext(UserContext user, TutorSession session, TutorMessage learnerMessage, ProviderInfo agent)
{
    public UserContext User { get; } = user;
    public TutorSession Session { get; } = session;
    public TutorMessage LearnerMessage { get; } = learnerMessage;
    public ProviderInfo Agent { get; } = agent;
    public List<string> RetrievedSourceIds { get; } = [];
}

public sealed class ToolException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public interface ITutorTool
{
    string Name { get; }
    string Description { get; }
    Task<object> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct);
}

/// <summary>Typed tool base: arguments are already schema-validated by the dispatcher before deserialization.</summary>
public abstract class TutorTool<TInput> : ITutorTool
{
    public abstract string Name { get; }
    public abstract string Description { get; }

    public Task<object> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct) =>
        ExecuteAsync(LearningJson.Deserialize<TInput>(arguments), context, ct);

    protected abstract Task<object> ExecuteAsync(TInput input, ToolExecutionContext context, CancellationToken ct);
}

public sealed record ToolDefinition(string Name, string Description, JsonObject Parameters);

public sealed class ToolRegistry
{
    private readonly Dictionary<string, ITutorTool> _tools;

    public ToolRegistry(IEnumerable<ITutorTool> tools)
    {
        _tools = tools.ToDictionary(t => t.Name, StringComparer.Ordinal);
        Definitions = _tools.Values
            .Select(t => new ToolDefinition(t.Name, t.Description, ContractSchemas.GetModelFacingSchema(SchemaNames.ToolInput(t.Name))))
            .OrderBy(d => d.Name, StringComparer.Ordinal)
            .ToList();
    }

    public IReadOnlyList<ToolDefinition> Definitions { get; }

    public ITutorTool? Find(string name) => _tools.GetValueOrDefault(name);
}

public sealed record ToolInvocationResult(string CallId, string Name, ToolCallStatus Status, string OutputJson, string? ErrorCode, double DurationMs);

/// <summary>Per-turn tool executor handed to the agent runtime (mock or Foundry).</summary>
public interface IToolInvoker
{
    Task<ToolInvocationResult> InvokeAsync(string callId, string name, string argumentsJson, CancellationToken ct);
    IReadOnlyList<ToolCallRecord> Records { get; }
    IReadOnlyList<string> RetrievedSourceIds { get; }
}

/// <summary>
/// Common tool pipeline: budget → lookup → argument JSON Schema validation (no userId accepted) → execute →
/// output schema validation → structured log. Errors are returned to the agent as data, never thrown,
/// so a failing tool cannot crash the conversation.
/// </summary>
public sealed class ToolDispatcher(ToolRegistry registry, IOptions<LearningOptions> options, ILogger<ToolDispatcher> logger)
{
    public IToolInvoker BeginTurn(ToolExecutionContext context) => new TurnInvoker(registry, context, options.Value.Agent.MaxToolCallsPerTurn, logger);

    private sealed class TurnInvoker(ToolRegistry registry, ToolExecutionContext context, int budget, ILogger logger) : IToolInvoker
    {
        private readonly List<ToolCallRecord> _records = [];
        private int _calls;

        public IReadOnlyList<ToolCallRecord> Records => _records;
        public IReadOnlyList<string> RetrievedSourceIds => context.RetrievedSourceIds.Distinct().ToList();

        public async Task<ToolInvocationResult> InvokeAsync(string callId, string name, string argumentsJson, CancellationToken ct)
        {
            var started = Stopwatch.GetTimestamp();
            using var activity = LearningTelemetry.StartActivity($"tool.{name}");
            ToolInvocationResult Finish(ToolCallStatus status, string output, string? code)
            {
                var elapsed = Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 2);
                _records.Add(new ToolCallRecord(callId, name, status, elapsed, code));
                logger.ToolCall(name, callId, context.Session.Id, status.ToString().ToLowerInvariant(), elapsed, code);
                activity?.SetTag("tool.status", status.ToString());
                return new ToolInvocationResult(callId, name, status, output, code, elapsed);
            }
            static string Error(string code, string message) => new JsonObject { ["error"] = new JsonObject { ["code"] = code, ["message"] = message } }.ToJsonString();

            if (++_calls > budget) return Finish(ToolCallStatus.Rejected, Error("tool-budget-exceeded", $"At most {budget} tool calls are allowed per turn."), "tool-budget-exceeded");
            var tool = registry.Find(name);
            if (tool is null) return Finish(ToolCallStatus.Rejected, Error("unknown-tool", $"Tool '{name}' does not exist."), "unknown-tool");

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            }
            catch (JsonException)
            {
                return Finish(ToolCallStatus.Rejected, Error("invalid-arguments", "Arguments are not valid JSON."), "invalid-arguments");
            }
            using (document)
            {
                var args = document.RootElement;
                if (args.ValueKind == JsonValueKind.Object && (args.TryGetProperty("userId", out _) || args.TryGetProperty("user_id", out _)))
                {
                    return Finish(ToolCallStatus.Rejected, Error("user-id-not-allowed", "Tools always act for the authenticated learner; userId must not be supplied."), "user-id-not-allowed");
                }
                var violations = ContractSchemas.Validate(SchemaNames.ToolInput(name), args);
                if (violations.Count > 0)
                {
                    return Finish(ToolCallStatus.Rejected, Error("invalid-arguments", string.Join("; ", violations.Take(3).Select(v => $"{v.Location} {v.Message}"))), "invalid-arguments");
                }

                try
                {
                    var output = await tool.ExecuteAsync(args, context, ct);
                    var element = LearningJson.ToElement(output);
                    if (ContractSchemas.Validate(SchemaNames.ToolOutput(name), element).Count > 0)
                    {
                        return Finish(ToolCallStatus.Failed, Error("invalid-tool-output", "The tool produced an invalid result."), "invalid-tool-output");
                    }
                    return Finish(ToolCallStatus.Succeeded, element.GetRawText(), null);
                }
                catch (ToolException ex)
                {
                    return Finish(ToolCallStatus.Failed, Error(ex.Code, ex.Message), ex.Code);
                }
                catch (DomainException ex)
                {
                    return Finish(ToolCallStatus.Failed, Error(ex.Code, ex.Message), ex.Code);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Tool {Tool} failed", name);
                    return Finish(ToolCallStatus.Failed, Error("tool-error", "The tool failed unexpectedly."), "tool-error");
                }
            }
        }
    }
}
