using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Identity;
using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Messaging;
using LineLanguageLearn.Learning.Tutor;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LineLanguageLearn.Tests;

public class LineWebhookTests
{
    [Theory]
    [InlineData("https://example.com/tutors")]
    [InlineData("https://example.com/tutors/")]
    public void Tutor_icon_uses_the_lowercase_name_and_preserves_the_base_path(string baseUrl)
    {
        var json = LineMessageJson.Build([new("Ciao!", "Giulia"), new("Notice")], baseUrl);
        Assert.Equal("https://example.com/tutors/giulia.png", json[0]!["sender"]!["iconUrl"]!.GetValue<string>());
        Assert.Null(json[1]!["sender"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Unconfigured_tutor_icon_is_omitted(string? baseUrl)
    {
        var json = LineMessageJson.Build([new("Ciao!", "Giulia")], baseUrl);
        Assert.Null(json[0]!["sender"]!["iconUrl"]);
    }

    private const string Secret = "channel-secret";

    private static string Sign(byte[] body) => Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), body));

    [Fact]
    public void Signature_is_verified_over_the_raw_body_bytes()
    {
        var body = Encoding.UTF8.GetBytes("""{"events":[{"message":{"text":"caffè"}}]}""");
        var signature = Sign(body);
        Assert.True(LineSignatureVerifier.Verify(Secret, body, signature));
        Assert.True(LineSignatureVerifier.Verify(Secret, Encoding.UTF8.GetString(body), signature));
        Assert.False(LineSignatureVerifier.Verify(Secret, [.. body, (byte)' '], signature));
        Assert.False(LineSignatureVerifier.Verify(Secret, body, "not-base64!"));
        Assert.False(LineSignatureVerifier.Verify(Secret, body, null));
        Assert.False(LineSignatureVerifier.Verify("", body, signature));
    }

    [Fact]
    public void Parser_routes_text_end_and_follow_events_and_ignores_the_rest()
    {
        var body = Encoding.UTF8.GetBytes("""
        {"destination":"Ubot","events":[
          {"type":"message","webhookEventId":"E1","replyToken":"T1","source":{"type":"user","userId":"U1"},"message":{"type":"text","text":" Ciao! "}},
          {"type":"message","webhookEventId":"E2","replyToken":"T2","source":{"type":"user","userId":"U1"},"message":{"type":"text","text":"終了"}},
          {"type":"postback","webhookEventId":"E3","replyToken":"T3","source":{"type":"user","userId":"U1"},"postback":{"data":"action=end-conversation"}},
          {"type":"follow","webhookEventId":"E4","replyToken":"T4","source":{"type":"user","userId":"U2"}},
          {"type":"message","webhookEventId":"E5","replyToken":"T5","source":{"type":"group","groupId":"C1","userId":"U1"},"message":{"type":"text","text":"hi"}},
          {"type":"message","webhookEventId":"E6","replyToken":"T6","source":{"type":"user","userId":"U1"},"message":{"type":"sticker"}},
          {"type":"postback","webhookEventId":"E7","replyToken":"T7","source":{"type":"user","userId":"U1"},"postback":{"data":"other"}},
          {"type":"message","replyToken":"T8","source":{"type":"user","userId":"U1"},"message":{"type":"text","text":"no id"}},
          {"type":"message","mode":"standby","webhookEventId":"E9","source":{"type":"user","userId":"U1"},"message":{"type":"text","text":"no token"}}
        ]}
        """);
        var (handled, ignored) = LineWebhookParser.Parse(body);

        Assert.Equal(
            [
                new LineInboundEvent(LineEventKind.Text, "U1", "E1", "Ciao!", "T1"),
                new LineInboundEvent(LineEventKind.EndConversation, "U1", "E2", null, "T2"),
                new LineInboundEvent(LineEventKind.EndConversation, "U1", "E3", null, "T3"),
                new LineInboundEvent(LineEventKind.Follow, "U2", "E4", null, "T4"),
            ],
            handled);
        Assert.Equal(5, ignored.Count);
    }

    [Fact]
    public void Verification_request_with_no_events_is_accepted()
    {
        var (handled, ignored) = LineWebhookParser.Parse(Encoding.UTF8.GetBytes("""{"destination":"Ubot","events":[]}"""));
        Assert.Empty(handled);
        Assert.Empty(ignored);
    }

    [Fact]
    public void Message_json_puts_sender_and_the_end_quick_reply_on_the_last_bubble_only()
    {
        var messages = Enumerable.Range(1, 7).Select(i => new OutboundMessage($"m{i}", "Giulia", OfferEnd: i == 7)).ToList();
        messages[4] = messages[4] with { OfferEnd = true };
        var json = LineMessageJson.Build(messages);

        Assert.Equal(5, json.Count);
        Assert.All(json, m => Assert.Equal("Giulia", m!["sender"]!["name"]!.GetValue<string>()));
        Assert.Null(json[0]!["quickReply"]);
        var action = json[4]!["quickReply"]!["items"]![0]!["action"]!;
        Assert.Equal("postback", action["type"]!.GetValue<string>());
        Assert.Equal(LineMessageJson.EndConversationData, action["data"]!.GetValue<string>());
    }
}

public class ReplyRelayTests
{
    private static readonly DateTime Now = new(2026, 11, 7, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Durable_dispatch_can_initialize_and_reuse_an_empty_relay()
    {
        var state = new PersistedRelayState();
        var first = new ReplyTarget("E1", "T1");
        var next = new ReplyTarget("E2", "T2");

        async Task<object?> Dispatch(string operation, object input) =>
            await new ReplyRelayEntity().RunAsync(new RelayOperation(operation, state, input));

        Assert.Equal(new ReplyRelayAdmission(true, null),
            await Dispatch(nameof(ReplyRelayEntity.Register), new ReplyRelayRegistration("A", first, Now)));
        Assert.Equal(new ReplyRelayAdmission(false, first),
            await Dispatch(nameof(ReplyRelayEntity.Register), new ReplyRelayRegistration("B", next, Now.AddSeconds(5))));
        Assert.Equal(next, await Dispatch(nameof(ReplyRelayEntity.Complete), "A"));
        Assert.False(state.HasState);
        Assert.Equal(new ReplyRelayAdmission(true, null),
            await Dispatch(nameof(ReplyRelayEntity.Register), new ReplyRelayRegistration("C", first, Now.AddSeconds(10))));
    }

    private sealed class RelayOperation(string name, TaskEntityState state, object input) : TaskEntityOperation
    {
        private readonly JsonElement _input = JsonSerializer.SerializeToElement(input);
        public override string Name => name;
        public override TaskEntityContext Context => null!;
        public override TaskEntityState State => state;
        public override bool HasInput => true;
        public override T? GetInput<T>() where T : default => _input.Deserialize<T>();
        public override object? GetInput(Type inputType) => _input.Deserialize(inputType);
    }

    private sealed class PersistedRelayState : TaskEntityState
    {
        private JsonElement? _value;
        public override bool HasState => _value.HasValue;
        public override T? GetState<T>(T? defaultValue = default) where T : default =>
            _value is { } value ? value.Deserialize<T>() : defaultValue;
        public override object? GetState(Type type) => _value?.Deserialize(type);
        public override void SetState(object? state) => _value = state is null ? null : JsonSerializer.SerializeToElement(state);
    }

    [Fact]
    public void Follow_up_events_replace_the_token_without_starting_another_turn()
    {
        var relay = new ReplyRelayEntity();
        var first = new ReplyTarget("E1", "T1");
        var second = new ReplyTarget("E2", "T2");
        var third = new ReplyTarget("E3", "T3");

        Assert.Equal(new ReplyRelayAdmission(true, null), relay.Register(new("A", first, Now)));
        Assert.Equal(new ReplyRelayAdmission(false, first), relay.Register(new("B", second, Now.AddSeconds(5))));
        Assert.Equal(new ReplyRelayAdmission(false, null), relay.Register(new("B", second, Now.AddSeconds(6))));
        Assert.Equal(new ReplyRelayAdmission(false, second), relay.Register(new("C", third, Now.AddSeconds(10))));
        Assert.Null(relay.Complete("B"));
        Assert.Equal(third, relay.Complete("A"));
        Assert.Null(relay.Complete("A"));
        Assert.True(relay.Register(new("D", new ReplyTarget("E4", "T4"), Now.AddSeconds(20))).StartTurn);
    }

    [Fact]
    public void Abandoned_turn_can_be_reclaimed_without_letting_the_old_owner_reply()
    {
        var relay = new ReplyRelayEntity();
        var first = new ReplyTarget("E1", "T1");
        var next = new ReplyTarget("E2", "T2");

        Assert.True(relay.Register(new("A", first, Now)).StartTurn);
        Assert.True(relay.Register(new("B", next, Now.AddMinutes(5))).StartTurn);
        Assert.Null(relay.Complete("A"));
        Assert.Equal(next, relay.Complete("B"));
    }
}

public class LineConversationTests
{
    private static (TestApp App, LineConversationService Service, UserContext User) Create()
    {
        var app = new TestApp();
        return (app, app.Get<LineConversationService>(), TestApp.Learner);
    }

    [Fact]
    public async Task A_message_is_stored_and_answered_once_even_when_redelivered()
    {
        var (app, service, user) = Create();
        using var _ = app;

        var accepted = await service.SaveLearnerMessageAsync(user, "E1", "Ho voglia a mangiare.", default);
        var messages = await service.CallTutorAgentAsync(user, accepted, default);
        var again = await service.SaveLearnerMessageAsync(user, "E1", "Ho voglia a mangiare.", default);
        var repeated = await service.CallTutorAgentAsync(user, again, default);

        Assert.Equal(accepted, again);
        Assert.Equal(messages, repeated);
        Assert.All(messages, m => Assert.Equal("Giulia", m.SenderName));
        Assert.True(messages[^1].OfferEnd);
        Assert.DoesNotContain(messages.SkipLast(1), m => m.OfferEnd);
        var session = Assert.Single(await app.Store.TutorSessions.ListAsync(user.UserId, default));
        Assert.Equal(TutorChannel.Line, session.Channel);
        Assert.Equal(3, session.Messages.Count);
        Assert.Equal("line:E1", session.Messages[1].ExternalEventId);
    }

    [Fact]
    public async Task Consecutive_messages_continue_the_same_line_conversation()
    {
        var (app, service, user) = Create();
        using var _ = app;

        await service.CallTutorAgentAsync(user, await service.SaveLearnerMessageAsync(user, "E1", "Ciao!", default), default);
        await service.CallTutorAgentAsync(user, await service.SaveLearnerMessageAsync(user, "E2", "Come stai?", default), default);

        var session = Assert.Single(await app.Store.TutorSessions.ListAsync(user.UserId, default));
        Assert.Equal(5, session.Messages.Count);
    }

    [Fact]
    public async Task Reply_uses_each_events_token_and_records_delivery_without_the_token()
    {
        var (app, service, user) = Create();
        using var _ = app;
        var messages = new List<OutboundMessage> { new("Ciao!") };
        await service.ReplyAsync(user, "E1", "T1", messages, default);
        await service.ReplyAsync(user, "E1", "redelivered-token", messages, default);
        await service.ReplyAsync(user, "E2", "T2", messages, default);

        Assert.Equal(2, app.Outbox.ReplyCount);
        Assert.Empty(app.Outbox.Sent);
        Assert.All(await app.Store.ProcessedEvents.ListAsync(user.UserId, default),
            marker => Assert.Equal("reply", marker.ResultRef));
    }

    [Fact]
    public async Task Push_is_sent_once_per_webhook_event()
    {
        var (app, service, user) = Create();
        using var _ = app;
        var messages = new List<OutboundMessage> { new("Ciao!", "Giulia", true) };

        await service.PushAsync(user, "U1", "E1", messages, Guid.NewGuid(), default);
        await service.PushAsync(user, "U1", "E1", messages, Guid.NewGuid(), default);
        await service.PushAsync(user, "U1", "E2", messages, Guid.NewGuid(), default);

        Assert.Equal(2, app.Outbox.Sent.Count);
    }

    [Fact]
    public async Task Ending_creates_a_pending_analysis_run_for_the_orchestrator_to_start()
    {
        var (app, service, user) = Create();
        using var _ = app;
        await service.CallTutorAgentAsync(user, await service.SaveLearnerMessageAsync(user, "E1", "Ho voglia a mangiare.", default), default);

        var ended = await service.EndConversationAsync(user, default);
        var again = await service.EndConversationAsync(user, default);

        Assert.True(ended.StartWorkflow);
        Assert.NotNull(ended.WorkflowRunId);
        var session = Assert.Single(await app.Store.TutorSessions.ListAsync(user.UserId, default));
        Assert.Equal(TutorSessionStatus.Ended, session.Status);
        Assert.Equal(ended.WorkflowRunId, session.AnalysisWorkflowRunId);
        Assert.Equal(WorkflowStatus.Pending, (await app.Store.WorkflowRuns.GetAsync(user.UserId, ended.WorkflowRunId!, default))!.Status);
        Assert.False(again.StartWorkflow);
        Assert.Null(again.WorkflowRunId);
    }

    [Fact]
    public async Task Loading_failures_never_block_the_reply()
    {
        var service = new LineConversationService(null!, null!, new ThrowingMessenger(), null!,
            Options.Create(new LearningOptions()), NullLogger<LineConversationService>.Instance);
        await service.ShowLoadingAsync("U1", default);
    }

    [Fact]
    public async Task Line_users_map_to_their_own_learner()
    {
        var mapper = new LineIdentityMapper(new MockMessagingAdapter());
        var user = await mapper.ResolveAsync("line", "U1234", default);
        Assert.Equal("line-U1234", user!.UserId);
        Assert.Equal(LineLearner.Mode, user.IdentityMode);
        Assert.Null(await mapper.ResolveAsync("line", "not-a-line-id", default));
    }

    private sealed class ThrowingMessenger : IOutboundMessenger
    {
        public string Mode => "throwing";
        public Task ShowLoadingAsync(string lineUserId, int seconds, CancellationToken ct) => throw new HttpRequestException("down");
        public Task ReplyAsync(string replyToken, IReadOnlyList<OutboundMessage> messages, CancellationToken ct) => throw new HttpRequestException("down");
        public Task PushAsync(string lineUserId, IReadOnlyList<OutboundMessage> messages, Guid retryKey, CancellationToken ct) => throw new HttpRequestException("down");
        public Task<string?> GetDisplayNameAsync(string lineUserId, CancellationToken ct) => throw new HttpRequestException("down");
    }
}

public class LineApiTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
            return respond(request);
        }
    }

    private static LearningOptions Options(string channelId = "1234567890") => new()
    {
        Line = new LineOptions { LoginChannelId = channelId, ChannelAccessToken = "test-access-token" },
    };

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string Token => "header.payload.signature";

    private static string Verified(string aud = "1234567890", string iss = "https://access.line.me", long? exp = null) =>
        new JsonObject
        {
            ["iss"] = iss, ["sub"] = "U0123", ["aud"] = aud, ["name"] = "Taro",
            ["exp"] = exp ?? DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds(),
        }.ToJsonString();

    private static LineIdentityResolver Resolver(Handler handler, LearningOptions? options = null) =>
        new(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(options ?? Options()), TimeProvider.System, NullLogger<LineIdentityResolver>.Instance);

    [Fact]
    public async Task Verified_id_token_maps_to_the_line_learner_and_is_cached()
    {
        var handler = new Handler(_ => Json(HttpStatusCode.OK, Verified()));
        var resolver = Resolver(handler);

        var user = await resolver.ResolveTokenAsync(Token, default);
        var cached = await resolver.ResolveTokenAsync(Token, default);

        Assert.Equal(LineLearner.Context("U0123", "Taro"), user);
        Assert.Equal(user, cached);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.line.me/oauth2/v2.1/verify", request.Request.RequestUri!.ToString());
        Assert.Contains("client_id=1234567890", request.Body);
    }

    [Theory]
    [InlineData("other-channel", "https://access.line.me", 30)]
    [InlineData("1234567890", "https://evil.example", 30)]
    [InlineData("1234567890", "https://access.line.me", -5)]
    public async Task Tokens_for_another_channel_issuer_or_expired_are_rejected(string aud, string iss, int minutes)
    {
        var handler = new Handler(_ => Json(HttpStatusCode.OK, Verified(aud, iss, DateTimeOffset.UtcNow.AddMinutes(minutes).ToUnixTimeSeconds())));
        Assert.Null(await Resolver(handler).ResolveTokenAsync(Token, default));
    }

    [Fact]
    public async Task Rejected_and_malformed_tokens_are_unauthenticated()
    {
        var handler = new Handler(_ => Json(HttpStatusCode.BadRequest, """{"error":"invalid_request"}"""));
        Assert.Null(await Resolver(handler).ResolveTokenAsync(Token, default));
        Assert.Null(await Resolver(handler).ResolveTokenAsync("not-a-jwt", default));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Line_outage_or_missing_channel_is_unavailable_not_anonymous()
    {
        var outage = new Handler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var error = await Assert.ThrowsAsync<DomainException>(() => Resolver(outage).ResolveTokenAsync(Token, default));
        Assert.Equal("line-login-unavailable", error.Code);

        var unused = new Handler(_ => Json(HttpStatusCode.OK, Verified()));
        await Assert.ThrowsAsync<DomainException>(() => Resolver(unused, Options(channelId: "")).ResolveTokenAsync(Token, default));
        Assert.Empty(unused.Requests);
    }

    [Fact]
    public async Task Reply_sends_the_event_token_without_a_push_retry_key()
    {
        var handler = new Handler(_ => Json(HttpStatusCode.OK, "{}"));
        var client = new LineMessagingClient(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(Options()), NullLogger<LineMessagingClient>.Instance);
        await client.ReplyAsync("T1", [new OutboundMessage("Ciao!", "Giulia", true)], default);

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal("https://api.line.me/v2/bot/message/reply", request.RequestUri!.ToString());
        Assert.False(request.Headers.Contains("X-Line-Retry-Key"));
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        var json = JsonNode.Parse(body)!;
        Assert.Equal("T1", json["replyToken"]!.GetValue<string>());
        Assert.Null(json["to"]);
        Assert.Equal("Ciao!", json["messages"]![0]!["text"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Rejected_reply_has_no_delivery_marker_or_push_fallback(HttpStatusCode status)
    {
        using var app = new TestApp();
        var handler = new Handler(_ => new HttpResponseMessage(status));
        var options = Microsoft.Extensions.Options.Options.Create(Options());
        var client = new LineMessagingClient(new HttpClient(handler), options, NullLogger<LineMessagingClient>.Instance);
        var service = new LineConversationService(null!, null!, client, app.Store, options, NullLogger<LineConversationService>.Instance);

        Task Reply() => service.ReplyAsync(TestApp.Learner, "E1", "T1", [new OutboundMessage("Ciao!")], default);
        if (status == HttpStatusCode.BadRequest)
            await Reply();
        else
            Assert.Equal(status, (await Assert.ThrowsAsync<LineApiException>(Reply)).Status);

        Assert.Empty(await app.Store.ProcessedEvents.ListAsync(TestApp.Learner.UserId, default));
        Assert.EndsWith("/reply", Assert.Single(handler.Requests).Request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Push_sends_the_retry_key_and_treats_an_accepted_retry_as_success()
    {
        var handler = new Handler(_ =>
        {
            var conflict = new HttpResponseMessage(HttpStatusCode.Conflict);
            conflict.Headers.Add("x-line-accepted-request-id", "req-1");
            return conflict;
        });
        var client = new LineMessagingClient(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(Options()), NullLogger<LineMessagingClient>.Instance);
        var key = Guid.NewGuid();

        await client.PushAsync("U1", [new OutboundMessage("Ciao!", "Giulia", true)], key, default);

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal("https://api.line.me/v2/bot/message/push", request.RequestUri!.ToString());
        Assert.Equal(key.ToString(), request.Headers.GetValues("X-Line-Retry-Key").Single());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        var json = JsonNode.Parse(body)!;
        Assert.Equal("U1", json["to"]!.GetValue<string>());
        Assert.Equal("Ciao!", json["messages"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Push_failures_are_reported_for_the_orchestrator_to_retry()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var client = new LineMessagingClient(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(Options()), NullLogger<LineMessagingClient>.Instance);
        await Assert.ThrowsAsync<LineApiException>(() => client.PushAsync("U1", [new OutboundMessage("x")], Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Loading_animation_uses_a_supported_duration()
    {
        var handler = new Handler(_ => Json(HttpStatusCode.Accepted, "{}"));
        var client = new LineMessagingClient(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(Options()), NullLogger<LineMessagingClient>.Instance);
        await client.ShowLoadingAsync("U1", 33, default);
        var json = JsonNode.Parse(Assert.Single(handler.Requests).Body)!;
        Assert.Equal("U1", json["chatId"]!.GetValue<string>());
        Assert.Equal(30, json["loadingSeconds"]!.GetValue<int>());
    }

    [Fact]
    public void Azure_requires_line_identity_and_rejects_the_local_development_identity()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WEBSITE_SITE_NAME"] = "func-test",
            ["COSMOS_DB_DATABASE_NAME"] = "db",
            ["CosmosDBConnection__accountEndpoint"] = "https://example.documents.azure.com/",
        }).Build();

        Assert.Equal(IdentityMode.Line, new LearningOptions().EffectiveIdentity(deployed: true));
        Assert.Equal(MessagingMode.Line, new LearningOptions().EffectiveMessaging(deployed: true));
        LearningServiceCollectionExtensions.Validate(new LearningOptions(), configuration);
        var error = Assert.Throws<OptionsValidationException>(() =>
            LearningServiceCollectionExtensions.Validate(new LearningOptions { Identity = IdentityMode.LocalDevelopment }, configuration));
        Assert.Contains(error.Failures, f => f.Contains("Learning:Identity=Line"));
    }
}
