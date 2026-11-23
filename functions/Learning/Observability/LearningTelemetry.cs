using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace LineLanguageLearn.Learning.Observability;

/// <summary>
/// Structured telemetry for AI calls, tool calls, retrieval, question evaluation and workflows.
/// Emits ILogger events (Application Insights picks them up in Azure) and Activities on an ActivitySource
/// so OpenTelemetry / Foundry tracing exporters can be attached later. Learner text and prompts are
/// never logged; only ids, counts, durations and outcome codes.
/// </summary>
public static partial class LearningTelemetry
{
    public const string SourceName = "LineLanguageLearn.Learning";
    public static readonly ActivitySource Source = new(SourceName);

    public static Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal) => Source.StartActivity(name, kind);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "AI call {Operation} provider={Provider} model={Model} durationMs={DurationMs} outcome={Outcome}")]
    public static partial void AiCall(this ILogger logger, string operation, string provider, string model, double durationMs, string outcome);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information,
        Message = "Tool call {ToolName} callId={CallId} session={SessionId} status={Status} durationMs={DurationMs} error={ErrorCode}")]
    public static partial void ToolCall(this ILogger logger, string toolName, string callId, string sessionId, string status, double durationMs, string? errorCode);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information,
        Message = "Grammar retrieval retriever={Retriever} hits={HitCount} sources={SourceIds} durationMs={DurationMs}")]
    public static partial void Retrieval(this ILogger logger, string retriever, int hitCount, string sourceIds, double durationMs);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information,
        Message = "Question evaluation generation={GenerationId} topic={TopicId} status={Status} failedChecks={FailedChecks} semantic={SemanticVerdict}")]
    public static partial void QuestionEvaluated(this ILogger logger, string generationId, string topicId, string status, string failedChecks, string semanticVerdict);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Information,
        Message = "Workflow {WorkflowName} run={RunId} step={Step} status={Status} attempt={Attempt} durationMs={DurationMs} error={ErrorCode}")]
    public static partial void WorkflowStep(this ILogger logger, string workflowName, string runId, string step, string status, int attempt, double durationMs, string? errorCode);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Warning,
        Message = "Rejected AI output {Operation}: {Reason} count={Count}")]
    public static partial void RejectedAiOutput(this ILogger logger, string operation, string reason, int count);
}
