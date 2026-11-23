using System.Net;
using System.Text.Json;
using Functions.Auth;
using LineLanguageLearn.Learning.Contracts;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Identity;
using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Persistence;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace LineLanguageLearn.Learning.Api;

/// <summary>
/// Shared HTTP pipeline: server-side identity, request contract validation, response contract validation
/// and DomainException → HTTP status mapping.
/// </summary>
public sealed class ApiPipeline(
    IRequestIdentityResolver identity,
    ReferenceDataLoader references,
    ILogger<ApiPipeline> logger)
{
    public Task<HttpResponseData> HandleAsync(HttpRequestData request, string responseSchema, Func<UserContext, CancellationToken, Task<object>> action, HttpStatusCode success = HttpStatusCode.OK) =>
        HandleAsync<UserContext>(request, responseSchema, async ct => (await identity.ResolveAsync(request, ct), null), action, success);

    public Task<HttpResponseData> HandleAdminAsync(HttpRequestData request, string responseSchema, Func<CancellationToken, Task<object>> action) =>
        HandleAsync<JwtPayload>(request, responseSchema, _ => JwtHelper.Authorize(request, "administrator"),
            (_, ct) => action(ct), HttpStatusCode.OK);

    private async Task<HttpResponseData> HandleAsync<TPrincipal>(
        HttpRequestData request, string responseSchema,
        Func<CancellationToken, Task<(TPrincipal?, HttpResponseData?)>> authorize,
        Func<TPrincipal, CancellationToken, Task<object>> action, HttpStatusCode success) where TPrincipal : class
    {
        var ct = request.FunctionContext.CancellationToken;
        try
        {
            var (user, denial) = await authorize(ct);
            if (denial is not null) return denial;
            if (user is null) return await ErrorAsync(request, HttpStatusCode.Unauthorized, "unauthenticated", "Authentication is required.");

            await references.LoadAsync(ct);
            var body = await action(user, ct);
            var element = LearningJson.ToElement(body);
            var violations = ContractSchemas.Validate(responseSchema, element);
            if (violations.Count > 0)
            {
                logger.LogError("Response violates contract {Schema}: {Violations}", responseSchema, string.Join("; ", violations.Take(5).Select(v => $"{v.Location} {v.Message}")));
                return await ErrorAsync(request, HttpStatusCode.InternalServerError, "contract-violation", "The server produced an invalid response.");
            }
            var response = request.CreateResponse(success);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            response.Headers.Add("Cache-Control", "no-store");
            await response.WriteStringAsync(element.GetRawText());
            return response;
        }
        catch (RequestValidationException ex)
        {
            return await ErrorAsync(request, HttpStatusCode.BadRequest, "invalid-request", ex.Message, ex.Details);
        }
        catch (DomainException ex)
        {
            var status = ex.Kind switch
            {
                DomainErrorKind.NotFound => HttpStatusCode.NotFound,
                DomainErrorKind.Conflict => HttpStatusCode.Conflict,
                DomainErrorKind.Forbidden => HttpStatusCode.Forbidden,
                DomainErrorKind.Unavailable => HttpStatusCode.ServiceUnavailable,
                _ => HttpStatusCode.BadRequest,
            };
            return await ErrorAsync(request, status, ex.Code, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Unhandled error in {Function}", request.FunctionContext.FunctionDefinition.Name);
            return await ErrorAsync(request, HttpStatusCode.InternalServerError, "internal-error", "Unexpected server error.");
        }
    }

    public static async Task<T> ReadBodyAsync<T>(HttpRequestData request, string schemaName)
    {
        var raw = await request.ReadAsStringAsync() ?? "";
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
        }
        catch (JsonException)
        {
            throw new RequestValidationException("Request body must be valid JSON.", null);
        }
        using (document)
        {
            var violations = ContractSchemas.Validate(schemaName, document.RootElement);
            if (violations.Count > 0) throw new RequestValidationException("Request does not match the API contract.", violations);
            return LearningJson.Deserialize<T>(document.RootElement);
        }
    }

    private static async Task<HttpResponseData> ErrorAsync(HttpRequestData request, HttpStatusCode status, string code, string message, object? details = null)
    {
        var response = request.CreateResponse(status);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(LearningJson.Serialize(new { error = message, code, details }));
        return response;
    }
}

public sealed class RequestValidationException(string message, IReadOnlyList<SchemaViolation>? details) : Exception(message)
{
    public IReadOnlyList<SchemaViolation>? Details { get; } = details;
}
