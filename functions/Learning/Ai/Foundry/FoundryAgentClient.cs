using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Azure.Core;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Infrastructure;
using Microsoft.Extensions.Options;

namespace LineLanguageLearn.Learning.Ai.Foundry;

/// <summary>Invokes a saved Foundry Agent Service agent, independently of direct model inference.</summary>
/// <remarks>
/// Calls the agent's own endpoint so the caller only needs Foundry Agent Consumer
/// (endpoints/interact); the project-wide /openai/v1/responses API requires development permissions.
/// </remarks>
public sealed class FoundryAgentClient(HttpClient http, TokenCredential credential, IOptions<LearningOptions> options)
{
    public Uri ResponsesUri => new(
        $"{options.Value.Foundry.ProjectEndpoint!.TrimEnd('/')}/agents/{Uri.EscapeDataString(options.Value.Foundry.AgentName!)}" +
        "/endpoint/protocols/openai/responses?api-version=v1");

    public async Task<JsonObject> CreateResponseAsync(JsonObject body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ResponsesUri) { Content = JsonContent.Create(body) };
        var token = await credential.GetTokenAsync(new TokenRequestContext(["https://ai.azure.com/.default"]), ct);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new DomainException(DomainErrorKind.Unavailable, "agent-call-failed", $"Foundry Agent Service returned HTTP {(int)response.StatusCode}.");
        return await response.Content.ReadFromJsonAsync<JsonObject>(ct) ?? throw new InvalidOperationException("Empty agent response body.");
    }
}
