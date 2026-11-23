using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker.Http;
using Functions.Auth.Schemes;
namespace Functions.Auth;
public sealed class ExternalTokenExpiredException : Exception { }
public sealed class ExternalTokenConfigurationException : InvalidOperationException { }
public record AuthPrincipal(string Subject, string Scheme, string Issuer, string[] Roles, IReadOnlyDictionary<string, object?> Claims)
{
    [Obsolete("Use Subject")] public string UserId => Subject;
    public string IdentityKey => $"{Scheme}:{Subject}";
}
public static class AuthRouter
{
    private static readonly Dictionary<string, (string[] Schemes, string[] Roles)> Policies = new()
    {
        ["learner"] = (new[] { "line" }, Array.Empty<string>()),
        ["adminOnly"] = (new[] { "admin" }, new[] { "administrator" }),
    };
    private static readonly Dictionary<string,string> Providers = new() { ["line"]="external-token", ["admin"]="swa" };
    private static readonly Dictionary<string,string[]> AllowedProviders = new() { ["line"]=Array.Empty<string>(), ["admin"]=new[] { "github" } };
    public static async Task<(AuthPrincipal? Principal, HttpResponseData? ErrorResponse)> Authorize(HttpRequestData request, string policyName)
    {
        if (!Policies.TryGetValue(policyName, out var policy)) return (null, await Error(request, HttpStatusCode.Unauthorized, "Undefined authorization policy"));
        var bearerHeader = request.Headers.TryGetValues("Authorization", out var authValues) ? authValues.FirstOrDefault() : null;
        var bearer = bearerHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true ? bearerHeader[7..] : null;
        var swa = request.Headers.TryGetValues("x-ms-client-principal", out var swaValues) ? swaValues.FirstOrDefault() : null;
        var candidates = policy.Schemes.Where(s => Providers[s] == "swa" ? swa != null : Providers[s] is "external-token" or "custom-jwt" ? bearer != null : false).ToArray();
        if (candidates.Length == 0 && bearerHeader != null && policy.Schemes.Any(s => Providers[s] == "external-token"))
            return (null, await Error(request, HttpStatusCode.Unauthorized, "Invalid token", "invalid_token"));
        if (candidates.Length != 1) return (null, await Error(request, HttpStatusCode.Unauthorized, candidates.Length == 0 ? "Authentication required" : "Ambiguous credentials", candidates.Length == 0 ? "unauthenticated" : "invalid_token"));
        var scheme = candidates[0]; AuthPrincipal principal;
        if (Providers[scheme] == "external-token" && (string.IsNullOrWhiteSpace(bearer) || bearer.Any(char.IsWhiteSpace) || bearer.Contains(',')))
            return (null, await Error(request, HttpStatusCode.Unauthorized, "Invalid token", "invalid_token"));
        switch (scheme)
        {
            case "line":
                try
                {
                    var context = request.FunctionContext;
                    var verify = context.InstanceServices.GetService(typeof(Func<string, CancellationToken, Task<ExternalPrincipal_line>>))
                        as Func<string, CancellationToken, Task<ExternalPrincipal_line>>;
                    context.CancellationToken.ThrowIfCancellationRequested();
                    var external = verify != null
                        ? await verify(bearer!, context.CancellationToken)
                        : await ExternalTokenVerifier_line.VerifyAsync(bearer!);
                    context.CancellationToken.ThrowIfCancellationRequested();
                    if (external == null || string.IsNullOrWhiteSpace(external.Subject) ||
                        string.IsNullOrWhiteSpace(external.Issuer) || external.Roles == null || external.Claims == null)
                        return (null, await Error(request, HttpStatusCode.Unauthorized, "Invalid token", "invalid_token"));
                    principal = new AuthPrincipal(external.Subject, "line", external.Issuer, external.Roles, external.Claims);
                }
                catch (OperationCanceledException) when (request.FunctionContext.CancellationToken.IsCancellationRequested) { throw; }
                catch (ExternalTokenExpiredException) { return (null, await Error(request, HttpStatusCode.Unauthorized, "Token expired", "expired_token")); }
                catch (ExternalTokenVerificationException_line ex) when (ex.IsIdentityProviderUnavailable) { return (null, await Error(request, HttpStatusCode.ServiceUnavailable, "Identity provider unavailable", "identity_provider_unavailable")); }
                catch (ExternalTokenVerificationException_line) { return (null, await Error(request, HttpStatusCode.Unauthorized, "Invalid token", "invalid_token")); }
                catch (HttpRequestException) { return (null, await Error(request, HttpStatusCode.ServiceUnavailable, "Identity provider unavailable", "identity_provider_unavailable")); }
                catch (TimeoutException) { return (null, await Error(request, HttpStatusCode.ServiceUnavailable, "Identity provider unavailable", "identity_provider_unavailable")); }
                catch (OperationCanceledException) { return (null, await Error(request, HttpStatusCode.ServiceUnavailable, "Identity provider unavailable", "identity_provider_unavailable")); }
                catch (InvalidOperationException ex) when (ex is ExternalTokenConfigurationException || ex.Message == "External token verifier for line is not configured") { return (null, await Error(request, HttpStatusCode.InternalServerError, "External token verifier is not configured", "verifier_not_configured")); }
                catch { return (null, await Error(request, HttpStatusCode.ServiceUnavailable, "External token verifier unavailable", "verifier_unavailable")); }
                break;
            default:
                if (Providers[scheme] != "swa") return (null, await Error(request, HttpStatusCode.Unauthorized, "Authentication adapter is not configured"));
                try
                {
                    using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(swa!)));
                    var root = doc.RootElement; var subject = root.GetProperty("userId").GetString();
                    var roles = root.TryGetProperty("userRoles", out var rv) ? rv.EnumerateArray().Where(v=>v.ValueKind==JsonValueKind.String).Select(v=>v.GetString()!).ToArray() : Array.Empty<string>();
                    if (string.IsNullOrEmpty(subject) || !roles.Contains("authenticated")) throw new InvalidOperationException();
                    var identityProvider = root.TryGetProperty("identityProvider", out var iv) ? iv.GetString() : null;
                    if (AllowedProviders[scheme].Length > 0 && (identityProvider == null || !AllowedProviders[scheme].Contains(identityProvider))) throw new InvalidOperationException();
                    var issuer = identityProvider == null ? "swa" : "swa:" + identityProvider;
                    principal = new AuthPrincipal(subject!, scheme, issuer, roles, new Dictionary<string,object?> { ["clientPrincipal"] = root.Clone() });
                }
                catch { return (null, await Error(request, HttpStatusCode.Unauthorized, "Invalid SWA client principal")); }
                break;
        }
        if (policy.Roles.Length > 0 && !policy.Roles.Any(principal.Roles.Contains)) return (null, await Error(request, HttpStatusCode.Forbidden, "Insufficient permissions"));
        return (principal, null);
    }
    private static async Task<HttpResponseData> Error(HttpRequestData request, HttpStatusCode status, string message, string? code = null) { var response=request.CreateResponse(status); await response.WriteAsJsonAsync(new { error=message, code }); return response; }
}
