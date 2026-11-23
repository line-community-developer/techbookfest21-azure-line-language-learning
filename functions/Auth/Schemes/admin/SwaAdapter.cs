using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker.Http;

namespace Functions.Auth;

public class JwtPayload
{
    public string Sub { get; set; } = "";
    public string LoginId { get; set; } = "";
    public string[] Roles { get; set; } = Array.Empty<string>();
}

public static class JwtHelper
{
    public static async Task<(JwtPayload?, HttpResponseData?)> Authorize(HttpRequestData request, params string[] requiredRoles)
    {
        try
        {
            var encoded = request.Headers.TryGetValues("x-ms-client-principal", out var values) ? values.FirstOrDefault() : null;
            if (string.IsNullOrWhiteSpace(encoded)) throw new InvalidOperationException();
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
            var root = document.RootElement;
            if (root.GetProperty("identityProvider").GetString() != "github")
                throw new InvalidOperationException();
            var userId = root.GetProperty("userId").GetString() ?? "";
            var details = root.TryGetProperty("userDetails", out var detailValue) ? detailValue.GetString() ?? "" : "";
            var roles = root.TryGetProperty("userRoles", out var roleValue) && roleValue.ValueKind == JsonValueKind.Array
                ? roleValue.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToArray()
                : Array.Empty<string>();
            if (string.IsNullOrEmpty(userId) || !roles.Contains("authenticated")) throw new InvalidOperationException();
            if (requiredRoles.Length > 0 && !requiredRoles.Any(roles.Contains))
            {
                var forbidden = request.CreateResponse(System.Net.HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Forbidden" });
                return (null, forbidden);
            }
            return (new JwtPayload { Sub = userId, LoginId = details, Roles = roles }, null);
        }
        catch
        {
            var unauthorized = request.CreateResponse(System.Net.HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new { error = "Unauthorized" });
            return (null, unauthorized);
        }
    }
}
