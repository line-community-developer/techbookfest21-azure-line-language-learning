using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LineLanguageLearn.Learning.Contracts;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Identity;
using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Persistence;
using LineLanguageLearn.Learning.Practice;
using LineLanguageLearn.Learning.Tutor.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LineLanguageLearn.Learning.Tutor.Mcp;

public sealed class TutorToolTurn : UserDocument
{
    public string SessionId { get; set; } = "";
    public string LearnerMessageId { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public bool Closed { get; set; }
    public int MaxCalls { get; set; }
    public List<McpCall> Calls { get; set; } = [];
}

public sealed class McpCall
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Completed { get; set; }
    public ToolCallRecord? Record { get; set; }
    public List<string> SourceIds { get; set; } = [];
}

public sealed record McpTurnLease(string Reference, string UserId, string Id);

/// <summary>
/// Per-turn routing reference, used only behind the MCP host's independent service authentication.
/// References contain no learner identity and are never included in model input.
/// Cosmos conditional writes serialize reservations across instances and enforce the turn budget.
/// </summary>
public sealed class McpTurnService(
    IDocumentStore documents, LearningStore store, ToolDispatcher dispatcher,
    IOptions<LearningOptions> options, TimeProvider clock, ILogger<McpTurnService> logger)
{
    public const string ContextHeader = "X-Learning-Context";
    private const int UpdateAttempts = 8;

    public async Task<McpTurnLease> BeginAsync(TutorTurnContext context, CancellationToken ct)
    {
        var reference = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var lease = new McpTurnLease(reference, context.User.UserId, Digest(reference));
        var turn = new TutorToolTurn
        {
            Id = lease.Id, UserId = lease.UserId, SessionId = context.Session.Id,
            LearnerMessageId = context.LearnerMessage.Id,
            ExpiresAt = clock.GetUtcNow().AddMinutes(5),
            MaxCalls = options.Value.Agent.MaxToolCallsPerTurn,
            CreatedAt = clock.GetUtcNow().ToString("O"),
        };
        ContractSchemas.EnsureValid("model.TutorToolTurn", LearningJson.ToElement(turn));
        if (!await documents.TryCreateAsync(Containers.TutorToolTurns, lease.UserId, lease.Id, turn, ct))
            throw new InvalidOperationException("Unable to create MCP turn.");
        return lease;
    }

    public async Task<string> InvokeAsync(string? reference, string name, JsonElement arguments, CancellationToken ct)
    {
        if (reference is null || reference.Length != 64 || !reference.All(Uri.IsHexDigit)) throw Denied();
        var id = Digest(reference);
        var matches = await documents.FindByIdAsync<TutorToolTurn>(Containers.TutorToolTurns, id, ct);
        if (matches.Count != 1 || matches[0].Id != id || string.IsNullOrWhiteSpace(matches[0].UserId)) throw Denied();
        var lease = new McpTurnLease(reference, matches[0].UserId, id);
        var callId = $"mcp-{Guid.NewGuid():N}";
        var turn = await UpdateAsync(lease, current =>
        {
            if (current.Closed || current.ExpiresAt <= clock.GetUtcNow()) throw Denied();
            if (current.Calls.Count >= current.MaxCalls)
                throw new ToolException("tool-budget-exceeded", "The per-turn tool budget has been exhausted.");
            if (current.Calls.Any(c => !c.Completed))
                throw new ToolException("tool-call-in-progress", "Another tool is still running for this turn.");
            current.Calls.Add(new McpCall { Id = callId, Name = name });
        }, ct);

        ToolInvocationResult? result = null;
        IReadOnlyList<string> sources = [];
        try
        {
            var session = await store.TutorSessions.GetRequiredAsync(turn.UserId, turn.SessionId, "Tutor session", ct);
            if (session.Status != TutorSessionStatus.Active) throw Denied();
            var message = session.Messages.FirstOrDefault(m => m.Id == turn.LearnerMessageId && m.Role == MessageRole.Learner)
                ?? throw Denied();
            var profile = await store.Profiles.GetRequiredAsync(turn.UserId, LearningProfileService.ProfileId, "Learning profile", ct);
            var user = new UserContext(turn.UserId, profile.LearnerName, "mcp");
            var invoker = dispatcher.BeginTurn(new ToolExecutionContext(user, session, message, session.Agent));
            result = await invoker.InvokeAsync(callId, name, arguments.GetRawText(), ct);
            sources = invoker.RetrievedSourceIds;
            return result.OutputJson;
        }
        finally
        {
            // Persist the reservation outcome even if the request was cancelled. A failed write is
            // surfaced, not silently reported as a successful tool call.
            using var finish = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await UpdateAsync(lease, current =>
            {
                var call = current.Calls.Single(c => c.Id == callId);
                call.Completed = true;
                call.Record = result is null
                    ? new ToolCallRecord(callId, name, ToolCallStatus.Failed, 0, "tool-interrupted")
                    : new ToolCallRecord(callId, name, result.Status, result.DurationMs, result.ErrorCode);
                call.SourceIds = sources.ToList();
            }, finish.Token);
        }
    }

    public Task<TutorToolTurn> CloseAsync(McpTurnLease lease, CancellationToken ct) =>
        UpdateAsync(lease, current => current.Closed = true, ct);

    private async Task<TutorToolTurn> UpdateAsync(McpTurnLease lease, Action<TutorToolTurn> change, CancellationToken ct)
    {
        for (var attempt = 0; attempt < UpdateAttempts; attempt++)
        {
            var stored = await documents.GetVersionedAsync<TutorToolTurn>(Containers.TutorToolTurns, lease.UserId, lease.Id, ct)
                ?? throw Denied();
            var current = stored.Value;
            if (current.UserId != lease.UserId || current.Id != lease.Id) throw Denied();
            change(current);
            current.UpdatedAt = clock.GetUtcNow().ToString("O");
            if (await documents.TryReplaceAsync(Containers.TutorToolTurns, lease.UserId, lease.Id, current, stored.Version, ct))
                return current;
        }
        logger.LogWarning("MCP turn update conflicted after {Attempts} attempts", UpdateAttempts);
        throw new ToolException("tool-state-conflict", "Concurrent tool state update; retry the turn.");
    }

    private static string Digest(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static ToolException Denied() => new("invalid-tool-context", "A valid, active tutor turn context is required.");
}
