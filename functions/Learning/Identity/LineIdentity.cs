using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Functions.Auth.Schemes;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Infrastructure;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LineLanguageLearn.Learning.Identity;

/// <summary>
/// LIFF and the Messaging API channel belong to the same LINE provider, so the LIFF ID token's sub
/// and the webhook's source.userId are the same LINE user ID and map to the same learner.
/// </summary>
public static class LineLearner
{
    public const string Mode = "line";
    public const string DefaultName = "LINE ユーザー";

    public static string LearnerId(string lineUserId) => $"line-{lineUserId}";

    public static UserContext Context(string lineUserId, string? displayName) =>
        new(LearnerId(lineUserId), string.IsNullOrWhiteSpace(displayName) ? DefaultName : displayName.Trim(), Mode);
}

/// <summary>Resolves the learner from a LIFF ID token (Authorization: Bearer) verified by LINE.</summary>
public sealed class LineIdentityResolver(
    HttpClient http, IOptions<LearningOptions> options, TimeProvider clock, ILogger<LineIdentityResolver> logger) : IRequestIdentityResolver
{
    private static readonly TimeSpan MaxCache = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, (UserContext User, DateTimeOffset Until)> _cache = new();

    public string Mode => LineLearner.Mode;

    public async Task<UserContext?> ResolveAsync(HttpRequestData request, CancellationToken ct)
    {
        if (!request.Headers.TryGetValues("Authorization", out var values)) return null;
        var header = values.FirstOrDefault() ?? "";
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal)) return null;
        return await ResolveTokenAsync(header["Bearer ".Length..].Trim(), ct);
    }

    public async Task<UserContext?> ResolveTokenAsync(string token, CancellationToken ct)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var now = clock.GetUtcNow();
        if (_cache.TryGetValue(key, out var cached) && cached.Until > now) return cached.User;

        ExternalPrincipal_line principal;
        try
        {
            var line = options.Value.Line;
            principal = await ExternalTokenVerifier_line.VerifyAsync(token, http, line.LoginChannelId ?? "", line.ApiBaseUrl, ct);
        }
        catch (ExternalTokenVerificationException_line ex) when (!ex.IsIdentityProviderUnavailable)
        {
            logger.LogInformation("LINE ID token rejected: {Reason}", ex.Message);
            return null;
        }
        catch (ExternalTokenVerificationException_line ex)
        {
            logger.LogWarning("LINE ID token could not be verified: {Reason}", ex.Message);
            throw new DomainException(DomainErrorKind.Unavailable, "line-login-unavailable", "LINE ログインを確認できませんでした。");
        }

        var user = LineLearner.Context(principal.Subject, principal.Claims.TryGetValue("name", out var name) ? name as string : null);
        var expires = principal.Claims.TryGetValue("exp", out var exp) && exp is long seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : now;
        var until = expires < now + MaxCache ? expires : now + MaxCache;
        if (until > now)
        {
            if (_cache.Count > 10_000) _cache.Clear();
            _cache[key] = (user, until);
        }
        return user;
    }
}
