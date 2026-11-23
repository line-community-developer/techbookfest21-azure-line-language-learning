using System.Net.Http.Json;
using System.Text.Json;

namespace Functions.Auth.Schemes;

public record ExternalPrincipal_line(string Subject, string Issuer, string[] Roles, IReadOnlyDictionary<string, object?> Claims);

public sealed class ExternalTokenVerificationException_line : Exception
{
    public bool IsIdentityProviderUnavailable { get; }
    public ExternalTokenVerificationException_line(string message, bool unavailable = false) : base(message) => IsIdentityProviderUnavailable = unavailable;
}

/// <summary>
/// Verifies a LIFF ID token with LINE's verify endpoint (issuer, audience = LINE Login channel ID, expiry).
/// The decoded profile sent by the browser is never trusted.
/// </summary>
public static class ExternalTokenVerifier_line
{
    public const string Issuer = "https://access.line.me";
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static Task<ExternalPrincipal_line> VerifyAsync(string token) =>
        VerifyAsync(token, SharedHttp, Environment.GetEnvironmentVariable("Learning__Line__LoginChannelId") ?? "", "https://api.line.me", CancellationToken.None);

    public static async Task<ExternalPrincipal_line> VerifyAsync(string token, HttpClient http, string channelId, string apiBaseUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(channelId))
            throw new ExternalTokenVerificationException_line("LINE Login channel ID is not configured.", unavailable: true);
        if (string.IsNullOrWhiteSpace(token) || token.Length > 8192 || token.Count(c => c == '.') != 2)
            throw new ExternalTokenVerificationException_line("Malformed ID token.");

        using var content = new FormUrlEncodedContent([new("id_token", token), new("client_id", channelId)]);
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync($"{apiBaseUrl.TrimEnd('/')}/oauth2/v2.1/verify", content, ct);
        }
        catch (HttpRequestException)
        {
            throw new ExternalTokenVerificationException_line("LINE verify endpoint is unreachable.", unavailable: true);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ExternalTokenVerificationException_line("LINE verify endpoint timed out.", unavailable: true);
        }

        using (response)
        {
            if ((int)response.StatusCode >= 500 || (int)response.StatusCode == 429)
                throw new ExternalTokenVerificationException_line($"LINE verify endpoint returned HTTP {(int)response.StatusCode}.", unavailable: true);
            if (!response.IsSuccessStatusCode)
                throw new ExternalTokenVerificationException_line("LINE rejected the ID token.");

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            var issuer = Text(json, "iss");
            var subject = Text(json, "sub");
            var audience = Text(json, "aud");
            var expires = json.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds) ? seconds : 0;
            if (issuer != Issuer || audience != channelId || string.IsNullOrEmpty(subject) || !subject.StartsWith('U') ||
                DateTimeOffset.FromUnixTimeSeconds(expires) <= DateTimeOffset.UtcNow)
                throw new ExternalTokenVerificationException_line("ID token claims are not valid for this channel.");

            var claims = new Dictionary<string, object?> { ["exp"] = expires, ["name"] = Text(json, "name") };
            return new ExternalPrincipal_line(subject, issuer, [], claims);
        }
    }

    private static string? Text(JsonElement json, string name) =>
        json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
