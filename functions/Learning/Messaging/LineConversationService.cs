using System.Net;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Identity;
using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Persistence;
using LineLanguageLearn.Learning.Tutor;
using LineLanguageLearn.Learning.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LineLanguageLearn.Learning.Messaging;

public sealed record EndConversationResult(string? WorkflowRunId, bool StartWorkflow, List<OutboundMessage> Messages);

/// <summary>
/// LINE conversation use cases. Each method is the body of one Durable activity, so every step is
/// idempotent per webhook event: redelivery or activity retries never duplicate replies.
/// TutorService is resolved lazily: it needs reference data, which activities load first.
/// </summary>
public sealed class LineConversationService(
    IServiceProvider services,
    IExternalIdentityMapper identities,
    IOutboundMessenger messenger,
    LearningStore store,
    IOptions<LearningOptions> options,
    ILogger<LineConversationService> logger)
{
    public const int LoadingSeconds = 30;

    private TutorService tutor => services.GetRequiredService<TutorService>();

    public async Task<UserContext> ResolveLearnerAsync(string lineUserId, CancellationToken ct) =>
        await identities.ResolveAsync("line", lineUserId, ct)
        ?? throw DomainException.Invalid("unknown-line-user", "The LINE user could not be mapped to a learner.");

    /// <summary>The loading animation is best-effort: a failure is logged and never delays the reply.</summary>
    public async Task ShowLoadingAsync(string lineUserId, CancellationToken ct)
    {
        try
        {
            await messenger.ShowLoadingAsync(lineUserId, LoadingSeconds, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("LINE loading animation failed: {Error}", ex.GetType().Name);
        }
    }

    public async Task<AcceptedMessage> SaveLearnerMessageAsync(UserContext user, string webhookEventId, string text, CancellationToken ct)
    {
        var session = await tutor.GetOrStartActiveAsync(user, TutorChannel.Line, ct);
        return await tutor.AcceptLearnerMessageAsync(user, session.Id, $"line:{webhookEventId}", text, ct);
    }

    public async Task<List<OutboundMessage>> CallTutorAgentAsync(UserContext user, AcceptedMessage accepted, CancellationToken ct)
    {
        var reply = await tutor.RespondAsync(user, accepted.SessionId, accepted.LearnerMessageId, ct);
        var name = tutor.TutorFor(reply.Session).Name;
        var messages = new List<OutboundMessage>();
        foreach (var message in reply.Replies)
        {
            messages.Add(new OutboundMessage(message.Text, name));
            // Corrections go in a separate bubble so the conversation itself stays short.
            if (message.Corrections.Count > 0)
            {
                var lines = message.Corrections.Select(c => $"✏️ {c.Incorrect} → {c.Suggestion}\n{c.ExplanationJa}");
                messages.Add(new OutboundMessage(string.Join("\n\n", lines), name));
            }
        }
        if (messages.Count == 0) messages.Add(new OutboundMessage("……", name));
        messages = messages.Take(5).ToList();
        messages[^1] = messages[^1] with { OfferEnd = true };
        return messages;
    }

    /// <summary>Ends the active LINE conversation. The orchestrator then starts the analysis orchestration.</summary>
    public async Task<EndConversationResult> EndConversationAsync(UserContext user, CancellationToken ct)
    {
        var session = await tutor.FindActiveAsync(user, TutorChannel.Line, ct);
        if (session is null)
            return new EndConversationResult(null, false, [new OutboundMessage("いま続いている会話はありません。メッセージを送ると、新しい会話を始めます。")]);

        var (_, run) = await tutor.EndSessionAsync(user, session.Id, scheduler: null, ct);
        var miniApp = options.Value.Line.MiniAppUrl;
        var text = "おつかれさまでした！ 今日の会話から苦手な文法を分析して、復習問題を用意します。" +
            (string.IsNullOrWhiteSpace(miniApp) ? "\nしばらくしたら、ミニアプリの履歴で確認してください。" : $"\nしばらくしたら、ミニアプリで確認してください。\n{miniApp}");
        return new EndConversationResult(run.Id, run.Status == WorkflowStatus.Pending, [new OutboundMessage(text, tutor.TutorFor(session).Name)]);
    }

    public List<OutboundMessage> Welcome()
    {
        var miniApp = options.Value.Line.MiniAppUrl;
        return
        [
            new OutboundMessage("友だち追加ありがとうございます！ AI講師とイタリア語で会話しながら練習できます。" +
                "\n話しかけると会話が始まり、「終了」と送ると会話を終えて、苦手な文法を分析します。" +
                (string.IsNullOrWhiteSpace(miniApp) ? "" : $"\n問題演習と学習履歴はミニアプリで。\n{miniApp}")),
        ];
    }

    public static List<OutboundMessage> TutorUnavailable() =>
        [new OutboundMessage("ごめんなさい、いま講師が返事をできません。少し時間をおいて、もう一度送ってください。")];

    /// <summary>Same limit as the tutor session (1-500 characters per learner message).</summary>
    public const int MaxTextLength = 500;

    public static List<OutboundMessage> TooLong() =>
        [new OutboundMessage($"メッセージは{MaxTextLength}文字までにしてください。短く分けて送ってもらえると、講師が答えやすくなります。")];

    public async Task ReplyAsync(UserContext user, string webhookEventId, string replyToken, IReadOnlyList<OutboundMessage> messages, CancellationToken ct)
    {
        var markerId = $"evt-line-{webhookEventId}";
        if (await store.ProcessedEvents.GetAsync(user.UserId, markerId, ct) is not null) return;

        try
        {
            await messenger.ReplyAsync(replyToken, messages, ct);
        }
        catch (LineApiException ex) when (ex.Status == HttpStatusCode.BadRequest)
        {
            // LINE uses 400 for expired/used tokens as well as invalid messages. A used token
            // can mean an earlier attempt succeeded, so switching to Push here can duplicate it.
            logger.LogWarning("LINE Reply API rejected webhook event {WebhookEventId} with HTTP 400.", webhookEventId);
            return;
        }

        await RecordSentAsync(user, webhookEventId, markerId, "reply", ct);
    }

    /// <summary>Pushes at most once per webhook event: a stored marker plus X-Line-Retry-Key guard against duplicates.</summary>
    public async Task PushAsync(UserContext user, string lineUserId, string webhookEventId, IReadOnlyList<OutboundMessage> messages, Guid retryKey, CancellationToken ct)
    {
        var markerId = $"evt-line-{webhookEventId}";
        if (await store.ProcessedEvents.GetAsync(user.UserId, markerId, ct) is not null) return;
        await messenger.PushAsync(lineUserId, messages, retryKey, ct);
        await RecordSentAsync(user, webhookEventId, markerId, retryKey.ToString(), ct);
    }

    private Task<bool> RecordSentAsync(UserContext user, string webhookEventId, string markerId, string resultRef, CancellationToken ct) =>
        store.ProcessedEvents.TryCreateAsync(new ProcessedEvent
        {
            Id = markerId,
            UserId = user.UserId,
            Channel = "line",
            ExternalEventId = webhookEventId,
            ResultRef = resultRef,
        }, ct);
}
