using System.Net;
using LineLanguageLearn.Learning.Identity;
using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Persistence;
using LineLanguageLearn.Learning.Tutor;
using LineLanguageLearn.Learning.Workflows;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LineLanguageLearn.Learning.Messaging;

public sealed record ConversationInput(LineEventKind Kind, string LineUserId, string WebhookEventId, string? Text, string? ReplyToken = null);

public sealed record SaveMessageInput(UserContext User, string WebhookEventId, string Text);

public sealed record AgentTurnInput(UserContext User, AcceptedMessage Accepted);

public sealed record ReplyInput(UserContext User, string WebhookEventId, string ReplyToken, List<OutboundMessage> Messages);

public sealed record PushInput(UserContext User, string LineUserId, string WebhookEventId, List<OutboundMessage> Messages, Guid RetryKey);

/// <summary>
/// LINE Bot: the webhook verifies the signature and starts one orchestration per event; the orchestration
/// shows the loading animation, stores the message, calls the Foundry agent and relays reply tokens during the turn.
/// </summary>
public sealed class LineBotFunctions(
    LineConversationService conversations,
    IServiceProvider services,
    ReferenceDataLoader references,
    IOptions<LearningOptions> options,
    ILogger<LineBotFunctions> logger)
{
    [Function(nameof(LineWebhook))]
    public async Task<HttpResponseData> LineWebhook(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "line/webhook")] HttpRequestData req,
        [DurableClient] DurableTaskClient durable)
    {
        var secret = options.Value.Line.ChannelSecret;
        if (string.IsNullOrWhiteSpace(secret))
        {
            logger.LogError("LINE webhook received but Learning__Line__ChannelSecret is not configured.");
            return req.CreateResponse(HttpStatusCode.ServiceUnavailable);
        }

        // The signature covers the raw bytes, so verify before parsing (never re-serialize).
        using var buffer = new MemoryStream();
        await req.Body.CopyToAsync(buffer);
        var body = buffer.ToArray();
        var signature = req.Headers.TryGetValues("x-line-signature", out var values) ? values.FirstOrDefault() : null;
        if (!LineSignatureVerifier.Verify(secret, body, signature)) return req.CreateResponse(HttpStatusCode.Unauthorized);

        List<LineInboundEvent> events;
        try
        {
            (events, var ignored) = LineWebhookParser.Parse(body);
            if (ignored.Count > 0) logger.LogInformation("LINE webhook ignored events: {Types}", string.Join(',', ignored));
        }
        catch (System.Text.Json.JsonException)
        {
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }

        foreach (var ev in events)
        {
            // Redelivered events have the same webhookEventId, so they map to the same instance.
            var instanceId = $"chat-{ev.LineUserId}-{ev.WebhookEventId}";
            if (await durable.GetInstanceAsync(instanceId) is not null) continue;
            await durable.ScheduleNewOrchestrationInstanceAsync(
                ev.Kind == LineEventKind.Follow ? nameof(ConversationOrchestrator) : nameof(RelayConversationOrchestrator),
                new ConversationInput(ev.Kind, ev.LineUserId, ev.WebhookEventId, ev.Text, ev.ReplyToken),
                new StartOrchestrationOptions(instanceId));
        }
        return req.CreateResponse(HttpStatusCode.OK);
    }

    [Function(nameof(ConversationOrchestrator))]
    public static async Task ConversationOrchestrator([OrchestrationTrigger] TaskOrchestrationContext context, ConversationInput input)
    {
        var learner = await context.CallActivityAsync<UserContext>(nameof(ResolveLearner), input.LineUserId);
        var messages = await BuildConversationReplyAsync(context, input, learner);

        if (input.ReplyToken is { } replyToken)
        {
            await context.CallActivityAsync(nameof(SendReply),
                new ReplyInput(learner, input.WebhookEventId, replyToken, messages),
                TaskOptions.FromRetryPolicy(new RetryPolicy(maxNumberOfAttempts: 3, firstRetryInterval: TimeSpan.FromSeconds(5))));
            return;
        }

        // Keep the original activity sequence for orchestrations already queued before reply tokens were captured.
        // Created by the orchestrator so replays and retries reuse the same X-Line-Retry-Key.
        var retryKey = context.NewGuid();
        await context.CallActivityAsync(nameof(PushReply),
            new PushInput(learner, input.LineUserId, input.WebhookEventId, messages, retryKey),
            TaskOptions.FromRetryPolicy(new RetryPolicy(maxNumberOfAttempts: 3, firstRetryInterval: TimeSpan.FromSeconds(5))));
    }

    [Function(nameof(RelayConversationOrchestrator))]
    public static async Task RelayConversationOrchestrator([OrchestrationTrigger] TaskOrchestrationContext context, ConversationInput input)
    {
        var relay = new EntityInstanceId(nameof(ReplyRelay), input.LineUserId);
        var replyUser = LineLearner.Context(input.LineUserId, null);
        await using (await context.Entities.LockEntitiesAsync(relay))
        {
            var target = new ReplyTarget(input.WebhookEventId, input.ReplyToken!);
            var registration = new ReplyRelayRegistration(context.InstanceId, target, context.CurrentUtcDateTime);
            var admission = await context.Entities.CallEntityAsync<ReplyRelayAdmission>(relay, nameof(ReplyRelayEntity.Register), registration);
            if (!admission.StartTurn)
            {
                if (admission.WaitingTarget is { } previous)
                {
                    // Keep the notice and final reply serialized with the token swap.
                    await SendRelayReplyAsync(context, replyUser, previous,
                        [new OutboundMessage("いま返答を準備しています。")]);
                }
                return;
            }
        }

        List<OutboundMessage>? messages = null;
        try
        {
            var learner = await context.CallActivityAsync<UserContext>(nameof(ResolveLearner), input.LineUserId);
            messages = await BuildConversationReplyAsync(context, input, learner);
        }
        catch (TaskFailedException)
        {
            messages = LineConversationService.TutorUnavailable();
        }
        finally
        {
            await using (await context.Entities.LockEntitiesAsync(relay))
            {
                var target = await context.Entities.CallEntityAsync<ReplyTarget?>(relay, nameof(ReplyRelayEntity.Complete), context.InstanceId);
                if (target is not null && messages is not null)
                    await SendRelayReplyAsync(context, replyUser, target, messages);
            }
        }
    }

    private static Task SendRelayReplyAsync(TaskOrchestrationContext context, UserContext user, ReplyTarget target, List<OutboundMessage> messages) =>
        context.CallActivityAsync(nameof(SendReply), new ReplyInput(user, target.WebhookEventId, target.Token, messages),
            TaskOptions.FromRetryPolicy(new RetryPolicy(maxNumberOfAttempts: 3, firstRetryInterval: TimeSpan.FromSeconds(5))));

    private static async Task<List<OutboundMessage>> BuildConversationReplyAsync(TaskOrchestrationContext context, ConversationInput input, UserContext learner)
    {
        List<OutboundMessage> messages;

        if (input.Kind == LineEventKind.Follow)
        {
            messages = await context.CallActivityAsync<List<OutboundMessage>>(nameof(Welcome), input.LineUserId);
        }
        else if (input.Kind == LineEventKind.Text && (input.Text?.Length ?? 0) is 0 or > LineConversationService.MaxTextLength)
        {
            messages = LineConversationService.TooLong();
        }
        else
        {
            // One conversation turn at a time per learner, even when messages arrive in quick succession.
            await using (await context.Entities.LockEntitiesAsync(new EntityInstanceId(nameof(ChatLock), learner.UserId)))
            {
                try
                {
                    if (input.Kind == LineEventKind.EndConversation)
                    {
                        var ended = await context.CallActivityAsync<EndConversationResult>(nameof(EndConversation), learner);
                        if (ended.StartWorkflow && ended.WorkflowRunId is { } runId)
                        {
                            // Analysis runs as its own orchestration; the conversation does not wait for it.
                            await context.CallActivityAsync(nameof(StartAnalysis), new WorkflowInstanceInput(learner, runId));
                        }
                        messages = ended.Messages;
                    }
                    else
                    {
                        await context.CallActivityAsync(nameof(ShowLoading), input.LineUserId);
                        var accepted = await context.CallActivityAsync<AcceptedMessage>(
                            nameof(SaveLearnerMessage), new SaveMessageInput(learner, input.WebhookEventId, input.Text ?? ""));
                        messages = await context.CallActivityAsync<List<OutboundMessage>>(
                            nameof(CallTutorAgent), new AgentTurnInput(learner, accepted));
                    }
                }
                catch (TaskFailedException)
                {
                    messages = LineConversationService.TutorUnavailable();
                }
            }
        }

        return messages;
    }

    [Function(nameof(ResolveLearner))]
    public Task<UserContext> ResolveLearner([ActivityTrigger] string lineUserId, FunctionContext ctx) =>
        conversations.ResolveLearnerAsync(lineUserId, ctx.CancellationToken);

    [Function(nameof(Welcome))]
    public List<OutboundMessage> Welcome([ActivityTrigger] string lineUserId) => conversations.Welcome();

    [Function(nameof(ShowLoading))]
    public Task ShowLoading([ActivityTrigger] string lineUserId, FunctionContext ctx) =>
        conversations.ShowLoadingAsync(lineUserId, ctx.CancellationToken);

    [Function(nameof(SaveLearnerMessage))]
    public async Task<AcceptedMessage> SaveLearnerMessage([ActivityTrigger] SaveMessageInput input, FunctionContext ctx)
    {
        await references.LoadAsync(ctx.CancellationToken);
        return await conversations.SaveLearnerMessageAsync(input.User, input.WebhookEventId, input.Text, ctx.CancellationToken);
    }

    [Function(nameof(CallTutorAgent))]
    public async Task<List<OutboundMessage>> CallTutorAgent([ActivityTrigger] AgentTurnInput input, FunctionContext ctx)
    {
        await references.LoadAsync(ctx.CancellationToken);
        return await conversations.CallTutorAgentAsync(input.User, input.Accepted, ctx.CancellationToken);
    }

    [Function(nameof(EndConversation))]
    public async Task<EndConversationResult> EndConversation([ActivityTrigger] UserContext learner, FunctionContext ctx)
    {
        await references.LoadAsync(ctx.CancellationToken);
        return await conversations.EndConversationAsync(learner, ctx.CancellationToken);
    }

    [Function(nameof(StartAnalysis))]
    public async Task StartAnalysis([ActivityTrigger] WorkflowInstanceInput input, [DurableClient] DurableTaskClient durable, FunctionContext ctx)
    {
        await references.LoadAsync(ctx.CancellationToken);
        var workflows = services.GetRequiredService<WorkflowRunner>();
        await new DurableWorkflowScheduler(durable, workflows).ScheduleAsync(input.User, input.RunId, ctx.CancellationToken);
    }

    [Function(nameof(SendReply))]
    public Task SendReply([ActivityTrigger] ReplyInput input, FunctionContext ctx) =>
        conversations.ReplyAsync(input.User, input.WebhookEventId, input.ReplyToken, input.Messages, ctx.CancellationToken);

    [Function(nameof(PushReply))]
    public Task PushReply([ActivityTrigger] PushInput input, FunctionContext ctx) =>
        conversations.PushAsync(input.User, input.LineUserId, input.WebhookEventId, input.Messages, input.RetryKey, ctx.CancellationToken);

    [Function(nameof(ReplyRelay))]
    public static Task ReplyRelay([EntityTrigger] TaskEntityDispatcher dispatcher) => dispatcher.DispatchAsync<ReplyRelayEntity>();

    /// <summary>Entity used only as a per-learner lock for conversation turns.</summary>
    [Function(nameof(ChatLock))]
    public static Task ChatLock([EntityTrigger] TaskEntityDispatcher dispatcher) => dispatcher.DispatchAsync<ChatLockEntity>();
}

public sealed class ChatLockEntity : TaskEntity<int>
{
}
