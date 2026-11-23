using LineLanguageLearn.Learning.Infrastructure;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Options;

namespace LineLanguageLearn.Learning.Identity;

/// <summary>Learner identity established by the server. Never built from request bodies or tool arguments.</summary>
public sealed record UserContext(string UserId, string DisplayName, string IdentityMode);

public interface IRequestIdentityResolver
{
    string Mode { get; }
    /// <summary>Returns the authenticated learner, or null when the request is not authenticated.</summary>
    Task<UserContext?> ResolveAsync(HttpRequestData request, CancellationToken ct);
}

/// <summary>
/// Local development identity: every request is the configured demo learner. Client-supplied headers,
/// query strings and bodies are ignored on purpose. Replace with a LIFF ID token verifier for LINE.
/// </summary>
public sealed class LocalDevelopmentIdentityResolver(IOptions<LearningOptions> options) : IRequestIdentityResolver
{
    public string Mode => "local-development";

    public UserContext Current => new(options.Value.LocalUserId, options.Value.LocalLearnerName, Mode);

    public Task<UserContext?> ResolveAsync(HttpRequestData request, CancellationToken ct) => Task.FromResult<UserContext?>(Current);
}
