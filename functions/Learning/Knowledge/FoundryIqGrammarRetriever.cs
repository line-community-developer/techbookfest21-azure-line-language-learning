using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Azure.Core;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LineLanguageLearn.Learning.Knowledge;

/// <summary>
/// Foundry IQ (Azure AI Search knowledge base) retriever using the GA "retrieve" REST action
/// (POST {search}/knowledgebases/{kb}/retrieve?api-version=2026-04-01, semantic intents, extractive output).
///
/// Assumes the knowledge source indexes one document per grammar topic with the topic id as document key,
/// so references[].docKey maps back to the bundled GrammarTopic master data (same domain result shape as the
/// local retriever). NOT verified against a live service in this repository: no index or knowledge base exists yet.
/// </summary>
public sealed class FoundryIqGrammarRetriever(
    HttpClient http,
    TokenCredential credential,
    GrammarKnowledgeBase knowledgeBase,
    IOptions<LearningOptions> options,
    ILogger<FoundryIqGrammarRetriever> logger) : IGrammarRetriever
{
    private static readonly string[] Scopes = ["https://search.azure.com/.default"];

    public string Name => "foundry-iq";
    public bool IsMock => false;

    public async Task<IReadOnlyList<GrammarSearchHit>> SearchAsync(GrammarQuery query, CancellationToken ct)
    {
        var settings = options.Value.FoundryIq;
        var started = Stopwatch.GetTimestamp();
        var url = $"{settings.SearchEndpoint!.TrimEnd('/')}/knowledgebases/{Uri.EscapeDataString(settings.KnowledgeBaseName!)}/retrieve?api-version={settings.ApiVersion}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(BuildRequestBody(query, settings)) };
        if (!string.IsNullOrEmpty(settings.ApiKey))
        {
            request.Headers.Add("api-key", settings.ApiKey);
        }
        else
        {
            var token = await credential.GetTokenAsync(new TokenRequestContext(Scopes), ct);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        }

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new DomainException(DomainErrorKind.Unavailable, "retrieval-failed", $"Foundry IQ retrieve failed with HTTP {(int)response.StatusCode}.");
        }
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(ct) ?? [];
        var hits = MapReferences(body, query, knowledgeBase);
        logger.Retrieval(Name, hits.Count, string.Join(',', hits.Select(h => h.Topic.Id)), Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return hits;
    }

    public static JsonObject BuildRequestBody(GrammarQuery query, FoundryIqOptions settings)
    {
        var search = query.TopicId is null ? query.Query : $"{query.Query} ({query.TopicId})";
        var body = new JsonObject
        {
            ["intents"] = new JsonArray(new JsonObject { ["type"] = "semantic", ["search"] = search }),
        };
        if (!string.IsNullOrEmpty(settings.KnowledgeSourceName))
        {
            body["knowledgeSourceParams"] = new JsonArray(new JsonObject
            {
                ["knowledgeSourceName"] = settings.KnowledgeSourceName,
                ["kind"] = "searchIndex",
                ["includeReferences"] = true,
            });
        }
        return body;
    }

    /// <summary>Maps retrieve references to grammar topics. Unknown document keys are dropped (never invented).</summary>
    public static IReadOnlyList<GrammarSearchHit> MapReferences(JsonObject body, GrammarQuery query, GrammarKnowledgeBase knowledgeBase)
    {
        var hits = new List<GrammarSearchHit>();
        var references = body["references"]?.AsArray() ?? [];
        var rank = 0;
        foreach (var reference in references)
        {
            var docKey = reference?["docKey"]?.GetValue<string>() ?? reference?["sourceData"]?["id"]?.GetValue<string>();
            if (docKey is null || knowledgeBase.Find(docKey) is not { } topic) continue;
            if (topic.Language != query.Language) continue;
            if (query.MaxLevel is { } max && topic.Level.Rank() > max.Rank()) continue;
            if (hits.Any(h => h.Topic.Id == topic.Id)) continue;
            var score = reference?["rerankerScore"]?.GetValue<double>() ?? Math.Max(0.1, 1.0 - rank * 0.1);
            var sourceIds = new List<string> { topic.Id, $"foundry-iq:ref:{reference?["id"]?.GetValue<string>() ?? rank.ToString()}" };
            sourceIds.AddRange(topic.SourceReferences.Select(s => s.Id));
            hits.Add(new GrammarSearchHit(topic, score, [], sourceIds));
            rank++;
        }
        return hits.Take(Math.Clamp(query.Limit, 1, 10)).ToList();
    }
}
