using System.Text.Json;
using LineLanguageLearn.Learning.Ai;
using LineLanguageLearn.Learning.Api;
using LineLanguageLearn.Learning.Contracts;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Identity;
using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Knowledge;
using LineLanguageLearn.Learning.Persistence;
using LineLanguageLearn.Learning.Practice;
using LineLanguageLearn.Learning.Tutor.Tools;
using LineLanguageLearn.Learning.Workflows;

namespace LineLanguageLearn.Learning.Tutor;

public sealed class TutorProfileCatalog
{
    public TutorProfileCatalog(IEnumerable<TutorProfile> tutors)
    {
        Tutors = tutors.ToList();
        foreach (var tutor in Tutors)
            ContractSchemas.EnsureValid(SchemaNames.TutorProfile, LearningJson.ToElement(tutor));
        if (Tutors.Select(tutor => tutor.Id).Distinct().Count() != Tutors.Count)
            throw new InvalidOperationException("Duplicate tutor profile IDs.");
    }

    public IReadOnlyList<TutorProfile> Tutors { get; }

    public TutorProfile Get(string id) => Tutors.FirstOrDefault(t => t.Id == id) ?? throw DomainException.NotFound("Tutor", id);

    public TutorProfile DefaultFor(TargetLanguage language) =>
        Tutors.FirstOrDefault(t => t.Language == language) ?? throw DomainException.NotFound("Tutor for language", language.ToString());
}

public sealed record TutorReply(TutorSession Session, List<TutorMessage> Replies, bool Duplicate);

public sealed record AcceptedMessage(string SessionId, string LearnerMessageId);

/// <summary>
/// Conversation use cases, independent of the channel (LINE, local simulator, demo seed).
/// Learner messages are idempotent per external event id.
/// </summary>
public sealed class TutorService(
    LearningStore store,
    LearningProfileService profiles,
    TutorProfileCatalog tutors,
    ITutorAgentRuntime agent,
    ToolDispatcher dispatcher,
    WorkflowRunner workflows,
    PracticeService practice,
    ViewMapper views,
    TimeProvider clock)
{
    public async Task<TutorSession> StartSessionAsync(UserContext user, string? theme, TutorChannel channel, CancellationToken ct, string? sessionId = null)
    {
        EnsureAgentAvailable();
        var profile = await profiles.GetOrCreateAsync(user, ct);
        var tutor = tutors.DefaultFor(profile.TargetLanguage);
        var id = sessionId ?? $"ts-{Guid.NewGuid():N}";
        var now = clock.GetUtcNow().ToString("O");
        var session = new TutorSession
        {
            Id = id,
            UserId = user.UserId,
            TutorProfileId = tutor.Id,
            Channel = channel,
            Theme = string.IsNullOrWhiteSpace(theme) ? "自由会話" : theme.Trim(),
            FocusTopicIds = profile.WeakTopics.Take(2).DefaultIfEmpty(practice.RecommendTopics(profile, 1).FirstOrDefault() ?? "").Where(t => t.Length > 0).ToList(),
            Status = TutorSessionStatus.Active,
            Agent = agent.Info,
            Messages =
            [
                new TutorMessage { Id = "msg-1", SessionId = id, Role = MessageRole.Tutor, Text = tutor.Greeting, CreatedAt = now, UpdatedAt = now },
            ],
        };
        await store.TutorSessions.TryCreateAsync(session, ct);
        return (await store.TutorSessions.GetAsync(user.UserId, id, ct))!;
    }

    public async Task<TutorReply> SendMessageAsync(UserContext user, string sessionId, string eventId, string text, CancellationToken ct)
    {
        var accepted = await AcceptLearnerMessageAsync(user, sessionId, eventId, text, ct);
        return await RespondAsync(user, sessionId, accepted.LearnerMessageId, ct);
    }

    /// <summary>Stores the learner's message once per external event id (redelivery returns the stored message).</summary>
    public async Task<AcceptedMessage> AcceptLearnerMessageAsync(UserContext user, string sessionId, string eventId, string text, CancellationToken ct)
    {
        var session = await store.TutorSessions.GetRequiredAsync(user.UserId, sessionId, "Tutor session", ct);
        var existing = session.Messages.FirstOrDefault(m => m.Role == MessageRole.Learner && m.ExternalEventId == eventId);
        if (existing is not null) return new AcceptedMessage(session.Id, existing.Id);
        if (session.Status != TutorSessionStatus.Active) throw DomainException.Conflict("session-ended", "This tutor session has ended.");
        if (string.IsNullOrWhiteSpace(text) || text.Length > 500) throw DomainException.Invalid("invalid-message", "Message must be 1-500 characters.");
        EnsureAgentAvailable();

        var now = clock.GetUtcNow().ToString("O");
        var learnerMessage = new TutorMessage
        {
            Id = $"msg-{session.Messages.Count + 1}",
            SessionId = session.Id,
            Role = MessageRole.Learner,
            Text = text.Trim(),
            ExternalEventId = eventId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        session.Messages.Add(learnerMessage);
        await store.TutorSessions.UpsertAsync(session, ct);
        return new AcceptedMessage(session.Id, learnerMessage.Id);
    }

    /// <summary>Runs the tutor turn for a stored learner message, or returns the replies if it already ran.</summary>
    public async Task<TutorReply> RespondAsync(UserContext user, string sessionId, string learnerMessageId, CancellationToken ct)
    {
        var session = await store.TutorSessions.GetRequiredAsync(user.UserId, sessionId, "Tutor session", ct);
        var learnerMessage = session.Messages.FirstOrDefault(m => m.Id == learnerMessageId && m.Role == MessageRole.Learner)
            ?? throw DomainException.NotFound("Learner message", learnerMessageId);
        var replies = RepliesAfter(session, learnerMessage);
        if (replies.Count > 0) return new TutorReply(session, replies, Duplicate: true);
        // The learner message was stored but the turn did not finish (e.g. model outage): run or resume the turn.
        EnsureAgentAvailable();
        session = await RunTurnAsync(user, session, learnerMessage, ct);
        return new TutorReply(session, RepliesAfter(session, learnerMessage), Duplicate: false);
    }

    /// <summary>Returns the active session for the channel, starting one when none is active.</summary>
    public async Task<TutorSession> GetOrStartActiveAsync(UserContext user, TutorChannel channel, CancellationToken ct) =>
        await FindActiveAsync(user, channel, ct) ?? await StartSessionAsync(user, null, channel, ct);

    public TutorProfile TutorFor(TutorSession session) => tutors.Get(session.TutorProfileId);

    private async Task<TutorSession> RunTurnAsync(UserContext user, TutorSession session, TutorMessage learnerMessage, CancellationToken ct)
    {
        var profile = await profiles.GetOrCreateAsync(user, ct);
        var tutor = tutors.Get(session.TutorProfileId);
        var systemPrompt = PromptTemplates.Render(tutor.PromptTemplateId, new Dictionary<string, string>
        {
            ["tutorName"] = tutor.Name,
            ["persona"] = tutor.PersonaJa,
            ["speakingStyle"] = tutor.SpeakingStyle,
            ["level"] = profile.CurrentLevel.ToString(),
            ["focusTopics"] = string.Join(", ", session.FocusTopicIds),
            ["maxCorrections"] = tutor.CorrectionPolicy.MaxCorrectionsPerTurn.ToString(),
            ["correctionStyle"] = tutor.CorrectionPolicy.Style.ToString().ToLowerInvariant(),
        });
        var toolContext = new ToolExecutionContext(user, session, learnerMessage, agent.Info);
        var invoker = dispatcher.BeginTurn(toolContext);
        var result = await agent.RunTurnAsync(new TutorTurnContext(user, tutor, session, learnerMessage, profile, systemPrompt), invoker, ct);
        if (string.IsNullOrWhiteSpace(result.ReplyText))
            throw new DomainException(DomainErrorKind.Unavailable, "empty-ai-response", "AIサービスから応答を取得できませんでした。");

        var now = clock.GetUtcNow().ToString("O");
        session.Messages.Add(new TutorMessage
        {
            Id = $"msg-{session.Messages.Count + 1}",
            SessionId = session.Id,
            Role = MessageRole.Tutor,
            Text = result.ReplyText,
            Corrections = result.Corrections,
            RetrievedSourceIds = (result.RetrievedSourceIds ?? invoker.RetrievedSourceIds).ToList(),
            ToolCalls = (result.ToolCalls ?? invoker.Records).ToList(),
            CreatedAt = now,
            UpdatedAt = now,
        });
        await store.TutorSessions.UpsertAsync(session, ct);
        return session;
    }

    private static List<TutorMessage> RepliesAfter(TutorSession session, TutorMessage learnerMessage) =>
        session.Messages.SkipWhile(m => m.Id != learnerMessage.Id).Skip(1).TakeWhile(m => m.Role == MessageRole.Tutor).ToList();

    private void EnsureAgentAvailable()
    {
        if (agent.Info.Kind == ProviderKind.Unavailable)
            throw UnavailableLearningModel.Error();
    }

    /// <summary>
    /// Ends the session and creates the post-conversation workflow run. Safe to call repeatedly.
    /// The run is started on the given host; pass null when the caller starts it (the LINE orchestrator).
    /// </summary>
    public async Task<(TutorSession Session, WorkflowRun Run)> EndSessionAsync(UserContext user, string sessionId, IWorkflowScheduler? scheduler, CancellationToken ct)
    {
        var session = await store.TutorSessions.GetRequiredAsync(user.UserId, sessionId, "Tutor session", ct);
        if (session.Status != TutorSessionStatus.Ended)
        {
            session.Status = TutorSessionStatus.Ended;
            session.EndedAt = clock.GetUtcNow().ToString("O");
        }
        var run = await workflows.GetOrCreateAsync(user, PostConversationWorkflow.WorkflowName, session.Id, ct);
        if (session.AnalysisWorkflowRunId != run.Id)
        {
            session.AnalysisWorkflowRunId = run.Id;
        }
        await store.TutorSessions.UpsertAsync(session, ct);
        if (run.Status == WorkflowStatus.Pending && scheduler is not null) run = await scheduler.ScheduleAsync(user, run.Id, ct);
        return (session, run);
    }

    public async Task<TutorSessionView> GetViewAsync(UserContext user, string sessionId, CancellationToken ct)
    {
        var session = await store.TutorSessions.GetRequiredAsync(user.UserId, sessionId, "Tutor session", ct);
        return views.TutorSession(session, tutors.Get(session.TutorProfileId));
    }

    public TutorSessionView View(TutorSession session) => views.TutorSession(session, tutors.Get(session.TutorProfileId));

    public async Task<TutorSessionListView> ListAsync(UserContext user, CancellationToken ct)
    {
        var sessions = (await store.TutorSessions.ListAsync(user.UserId, ct)).OrderByDescending(s => s.CreatedAt, StringComparer.Ordinal);
        return new TutorSessionListView(sessions.Select(s => views.TutorSessionSummary(s, tutors.Get(s.TutorProfileId))).ToList());
    }

    public async Task<TutorSession?> FindActiveAsync(UserContext user, TutorChannel channel, CancellationToken ct) =>
        (await store.TutorSessions.ListAsync(user.UserId, ct))
            .Where(s => s.Channel == channel && s.Status == TutorSessionStatus.Active)
            .OrderByDescending(s => s.CreatedAt, StringComparer.Ordinal)
            .FirstOrDefault();
}
