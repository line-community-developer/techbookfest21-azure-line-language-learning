using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Identity;
using LineLanguageLearn.Learning.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LineLanguageLearn.Learning.Messaging;

/// <summary>One LINE text bubble. The tutor's name is shown as the sender; the last bubble can offer "end".</summary>
public sealed record OutboundMessage(string Text, string? SenderName = null, bool OfferEnd = false);

/// <summary>Maps a channel user (LINE userId from a verified webhook) to the app learner.</summary>
public interface IExternalIdentityMapper
{
    Task<UserContext?> ResolveAsync(string channel, string externalUserId, CancellationToken ct);
}

/// <summary>Sends messages to LINE (Messaging API) or records them (tests and local development).</summary>
public interface IOutboundMessenger
{
    string Mode { get; }
    Task ShowLoadingAsync(string lineUserId, int seconds, CancellationToken ct);
    Task ReplyAsync(string replyToken, IReadOnlyList<OutboundMessage> messages, CancellationToken ct);
    /// <summary>Push with X-Line-Retry-Key: retrying with the same key does not send twice.</summary>
    Task PushAsync(string lineUserId, IReadOnlyList<OutboundMessage> messages, Guid retryKey, CancellationToken ct);
    Task<string?> GetDisplayNameAsync(string lineUserId, CancellationToken ct);
}

/// <summary>Local development: every external user is the configured demo learner.</summary>
public sealed class LocalDevelopmentIdentityMapper(LocalDevelopmentIdentityResolver identity) : IExternalIdentityMapper
{
    public Task<UserContext?> ResolveAsync(string channel, string externalUserId, CancellationToken ct) => Task.FromResult<UserContext?>(identity.Current);
}

/// <summary>LINE: the webhook userId is the learner (same provider as the LIFF app).</summary>
public sealed class LineIdentityMapper(IOutboundMessenger messenger) : IExternalIdentityMapper
{
    public async Task<UserContext?> ResolveAsync(string channel, string externalUserId, CancellationToken ct) =>
        channel == "line" && externalUserId.StartsWith('U')
            ? LineLearner.Context(externalUserId, await messenger.GetDisplayNameAsync(externalUserId, ct))
            : null;
}

public sealed record SentPush(string LineUserId, IReadOnlyList<OutboundMessage> Messages, Guid RetryKey);

/// <summary>Records messages in memory instead of calling the LINE Messaging API.</summary>
public sealed class MockMessagingAdapter : IOutboundMessenger
{
    private readonly ConcurrentQueue<SentPush> _sent = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<OutboundMessage>> _replies = new();
    private readonly ConcurrentQueue<string> _loading = new();

    public string Mode => "mock-outbox";
    public IReadOnlyCollection<SentPush> Sent => _sent.ToArray();
    public int ReplyCount => _replies.Count;
    public IReadOnlyCollection<string> Loading => _loading.ToArray();

    public Task ShowLoadingAsync(string lineUserId, int seconds, CancellationToken ct)
    {
        _loading.Enqueue(lineUserId);
        return Task.CompletedTask;
    }

    public Task ReplyAsync(string replyToken, IReadOnlyList<OutboundMessage> messages, CancellationToken ct)
    {
        _replies.TryAdd(replyToken, messages);
        return Task.CompletedTask;
    }

    public Task PushAsync(string lineUserId, IReadOnlyList<OutboundMessage> messages, Guid retryKey, CancellationToken ct)
    {
        if (_sent.Any(s => s.RetryKey == retryKey)) return Task.CompletedTask;
        _sent.Enqueue(new SentPush(lineUserId, messages, retryKey));
        return Task.CompletedTask;
    }

    public Task<string?> GetDisplayNameAsync(string lineUserId, CancellationToken ct) => Task.FromResult<string?>(null);
}

public sealed class LineApiException(string message, HttpStatusCode status) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}

/// <summary>LINE Messaging API client (reply, push, loading animation, profile).</summary>
public sealed class LineMessagingClient(HttpClient http, IOptions<LearningOptions> options, ILogger<LineMessagingClient> logger) : IOutboundMessenger
{
    public string Mode => "line-messaging-api";

    private LineOptions Line => options.Value.Line;

    public async Task ShowLoadingAsync(string lineUserId, int seconds, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Post, "/v2/bot/chat/loading/start",
            new JsonObject { ["chatId"] = lineUserId, ["loadingSeconds"] = Math.Clamp(seconds / 5 * 5, 5, 60) }, null, ct);
        if (!response.IsSuccessStatusCode) throw new LineApiException($"Loading animation failed with HTTP {(int)response.StatusCode}.", response.StatusCode);
    }

    public async Task ReplyAsync(string replyToken, IReadOnlyList<OutboundMessage> messages, CancellationToken ct)
    {
        var body = new JsonObject { ["replyToken"] = replyToken, ["messages"] = LineMessageJson.Build(messages, Line.TutorIconBaseUrl) };
        using var response = await SendAsync(HttpMethod.Post, "/v2/bot/message/reply", body, null, ct);
        if (!response.IsSuccessStatusCode) throw new LineApiException($"Reply failed with HTTP {(int)response.StatusCode}.", response.StatusCode);
    }

    public async Task PushAsync(string lineUserId, IReadOnlyList<OutboundMessage> messages, Guid retryKey, CancellationToken ct)
    {
        var body = new JsonObject { ["to"] = lineUserId, ["messages"] = LineMessageJson.Build(messages, Line.TutorIconBaseUrl) };
        using var response = await SendAsync(HttpMethod.Post, "/v2/bot/message/push", body, retryKey, ct);
        // 409 with x-line-accepted-request-id: this retry key was already accepted, so the message was sent once.
        if (response.StatusCode == HttpStatusCode.Conflict && response.Headers.Contains("x-line-accepted-request-id")) return;
        if (!response.IsSuccessStatusCode) throw new LineApiException($"Push failed with HTTP {(int)response.StatusCode}.", response.StatusCode);
    }

    public async Task<string?> GetDisplayNameAsync(string lineUserId, CancellationToken ct)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Get, $"/v2/bot/profile/{Uri.EscapeDataString(lineUserId)}", null, null, ct);
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            return json.TryGetProperty("displayName", out var name) ? name.GetString() : null;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("LINE profile lookup failed: {Error}", ex.GetType().Name);
            return null;
        }
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, JsonObject? body, Guid? retryKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Line.ChannelAccessToken))
            throw new DomainException(DomainErrorKind.Unavailable, "line-not-configured", "LINE Messaging API channel access token is not configured.");
        var request = new HttpRequestMessage(method, $"{Line.ApiBaseUrl.TrimEnd('/')}{path}");
        request.Headers.Authorization = new("Bearer", Line.ChannelAccessToken);
        if (retryKey is { } key) request.Headers.Add("X-Line-Retry-Key", key.ToString());
        if (body is not null) request.Content = JsonContent.Create(body);
        return http.SendAsync(request, ct);
    }
}

/// <summary>Builds Messaging API message objects (max 5 per request).</summary>
public static class LineMessageJson
{
    public const string EndConversationData = "action=end-conversation";

    public static JsonArray Build(IReadOnlyList<OutboundMessage> messages, string? tutorIconBaseUrl = null)
    {
        var array = new JsonArray();
        var list = messages.Take(5).ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var message = list[i];
            var json = new JsonObject { ["type"] = "text", ["text"] = Truncate(message.Text, 5000) };
            if (!string.IsNullOrWhiteSpace(message.SenderName))
            {
                var sender = new JsonObject { ["name"] = Truncate(message.SenderName, 20) };
                if (!string.IsNullOrWhiteSpace(tutorIconBaseUrl))
                    sender["iconUrl"] = $"{tutorIconBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(message.SenderName.ToLowerInvariant())}.png";
                json["sender"] = sender;
            }
            if (message.OfferEnd && i == list.Count - 1)
            {
                json["quickReply"] = new JsonObject
                {
                    ["items"] = new JsonArray(new JsonObject
                    {
                        ["type"] = "action",
                        ["action"] = new JsonObject
                        {
                            ["type"] = "postback",
                            ["label"] = "会話を終わる",
                            ["data"] = EndConversationData,
                            ["displayText"] = "会話を終わる",
                        },
                    }),
                };
            }
            array.Add(json);
        }
        return array;
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];
}

/// <summary>LINE webhook signature check: Base64(HMAC-SHA256(channelSecret, raw body bytes)) == X-Line-Signature.</summary>
public static class LineSignatureVerifier
{
    public static bool Verify(string channelSecret, string rawBody, string? signatureHeader) =>
        Verify(channelSecret, Encoding.UTF8.GetBytes(rawBody), signatureHeader);

    public static bool Verify(string channelSecret, byte[] rawBody, string? signatureHeader)
    {
        if (string.IsNullOrEmpty(signatureHeader) || string.IsNullOrEmpty(channelSecret)) return false;
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(channelSecret), rawBody);
        byte[] provided;
        try { provided = Convert.FromBase64String(signatureHeader); }
        catch (FormatException) { return false; }
        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }
}

public enum LineEventKind { Text, EndConversation, Follow }

/// <summary>A webhook event this app handles. Other event types are ignored.</summary>
public sealed record LineInboundEvent(LineEventKind Kind, string LineUserId, string WebhookEventId, string? Text, string ReplyToken);

public static class LineWebhookParser
{
    private static readonly HashSet<string> EndWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "終了", "終わり", "おわり", "会話を終わる", "会話を終了", "end",
    };

    /// <summary>Parses a verified webhook body. Only 1:1 chats with a user are handled.</summary>
    public static (List<LineInboundEvent> Handled, List<string> IgnoredTypes) Parse(byte[] body)
    {
        var handled = new List<LineInboundEvent>();
        var ignored = new List<string>();
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array) return (handled, ignored);
        foreach (var ev in events.EnumerateArray())
        {
            var type = Text(ev, "type") ?? "unknown";
            var eventId = Text(ev, "webhookEventId");
            var replyToken = Text(ev, "replyToken");
            var source = ev.TryGetProperty("source", out var s) ? s : default;
            var userId = source.ValueKind == JsonValueKind.Object && Text(source, "type") == "user" ? Text(source, "userId") : null;
            if (eventId is null || userId is null || string.IsNullOrWhiteSpace(replyToken))
            {
                ignored.Add(type);
                continue;
            }

            switch (type)
            {
                case "message" when ev.TryGetProperty("message", out var message) && Text(message, "type") == "text":
                    var text = (Text(message, "text") ?? "").Trim();
                    handled.Add(EndWords.Contains(text)
                        ? new LineInboundEvent(LineEventKind.EndConversation, userId, eventId, null, replyToken)
                        : new LineInboundEvent(LineEventKind.Text, userId, eventId, text, replyToken));
                    break;
                case "postback" when ev.TryGetProperty("postback", out var postback) && Text(postback, "data") == LineMessageJson.EndConversationData:
                    handled.Add(new LineInboundEvent(LineEventKind.EndConversation, userId, eventId, null, replyToken));
                    break;
                case "follow":
                    handled.Add(new LineInboundEvent(LineEventKind.Follow, userId, eventId, null, replyToken));
                    break;
                default:
                    ignored.Add(type);
                    break;
            }
        }
        return (handled, ignored);
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
