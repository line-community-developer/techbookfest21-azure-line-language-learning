using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Azure.Core;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Infrastructure;
using Microsoft.Extensions.Options;

namespace LineLanguageLearn.Learning.Ai.Foundry;

/// <summary>
/// Thin REST client for the Foundry / Azure OpenAI v1 Responses API (POST {endpoint}/openai/v1/responses).
/// Uses Microsoft Entra ID (scope https://ai.azure.com/.default) unless an API key is configured.
/// Written against the documented request/response shapes; NOT exercised against a live deployment here.
/// </summary>
public sealed class FoundryResponsesClient(HttpClient http, TokenCredential credential, IOptions<LearningOptions> options)
{
    private static readonly string[] Scopes = ["https://ai.azure.com/.default"];

    private FoundryOptions Settings => options.Value.Foundry;

    public Uri ResponsesUri => new($"{Settings.Endpoint!.TrimEnd('/')}/openai/v1/responses");

    public Task<JsonObject> CreateResponseAsync(JsonObject body, CancellationToken ct) =>
        SendAsync(ResponsesUri, body, ct);

    private async Task<JsonObject> SendAsync(Uri uri, JsonObject body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(body) };
        if (!string.IsNullOrEmpty(Settings.ApiKey))
        {
            request.Headers.Add("api-key", Settings.ApiKey);
        }
        else
        {
            var token = await credential.GetTokenAsync(new TokenRequestContext(Scopes), ct);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        }
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new DomainException(DomainErrorKind.Unavailable, "model-call-failed", $"Foundry Responses API returned HTTP {(int)response.StatusCode}.");
        }
        return await response.Content.ReadFromJsonAsync<JsonObject>(ct) ?? throw new InvalidOperationException("Empty response body.");
    }

    /// <summary>Builds a strict Structured Outputs request (text.format = json_schema) from an exported contract schema.</summary>
    public static JsonObject StructuredRequest(string model, string systemPrompt, string userContent, string schemaName, string formatName) => new()
    {
        ["model"] = model,
        ["input"] = new JsonArray(
            new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
            new JsonObject { ["role"] = "user", ["content"] = userContent }),
        ["text"] = new JsonObject
        {
            ["format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["name"] = formatName,
                ["schema"] = Contracts.ContractSchemas.GetModelFacingSchema(schemaName),
                ["strict"] = true,
            },
        },
    };

    public static string ExtractOutputText(JsonObject response)
    {
        var texts = new List<string>();
        foreach (var item in response["output"]?.AsArray() ?? [])
        {
            if (item?["type"]?.GetValue<string>() != "message") continue;
            foreach (var content in item["content"]?.AsArray() ?? [])
            {
                var type = content?["type"]?.GetValue<string>();
                if (type == "output_text") texts.Add(content!["text"]!.GetValue<string>());
                if (type == "refusal") throw new DomainException(DomainErrorKind.Unavailable, "model-refused", "The model refused to answer.");
            }
        }
        return string.Concat(texts);
    }

}
