using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Identity;
using LineLanguageLearn.Learning.Infrastructure;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;

namespace LineLanguageLearn.Learning.Persistence;

/// <summary>
/// Cosmos DB adapter. Every user-scoped container uses the partition key path /userId.
/// Uses the standard SwallowKit connection settings and effective database name.
/// Containers are provisioned by SwallowKit, never by request handlers.
/// </summary>
public sealed class CosmosDocumentStore : IDocumentStore, IDisposable
{
    private readonly CosmosClient _client;
    private readonly string _databaseName;

    public CosmosDocumentStore(string databaseName, IConfiguration configuration)
    {
        _databaseName = databaseName;
        _client = CreateClient(configuration);
    }

    public string Name => "cosmos-db";

    private static CosmosClient CreateClient(IConfiguration configuration)
    {
        var endpoint = configuration["CosmosDBConnection:accountEndpoint"];
        if (string.IsNullOrWhiteSpace(endpoint)) endpoint = configuration["CosmosDBConnection__accountEndpoint"];
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            return new CosmosClient(endpoint, new DefaultAzureCredential(), Options(endpoint));
        }
        var connectionString = configuration["CosmosDBConnection"];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Learning:Storage=Cosmos requires CosmosDBConnection or CosmosDBConnection__accountEndpoint.");
        }
        return new CosmosClient(connectionString, Options(connectionString));

        static CosmosClientOptions Options(string target) =>
            target.Contains("localhost:8081", StringComparison.OrdinalIgnoreCase) || target.Contains("127.0.0.1:8081", StringComparison.OrdinalIgnoreCase)
                ? new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway }
                : new CosmosClientOptions();
    }

    private Container Container(string name) => _client.GetContainer(_databaseName, name);

    private static Stream ToStream<T>(T document) => new MemoryStream(Encoding.UTF8.GetBytes(LearningJson.Serialize(document)));

    public async Task<T?> GetAsync<T>(string container, string partitionKey, string id, CancellationToken ct) where T : class
    {
        using var response = await Container(container).ReadItemStreamAsync(id, new PartitionKey(partitionKey), cancellationToken: ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await JsonSerializer.DeserializeAsync<T>(response.Content, LearningJson.Options, ct);
    }

    public async Task<IReadOnlyList<T>> ListAsync<T>(string container, string partitionKey, CancellationToken ct) where T : class
    {
        var results = new List<T>();
        using var iterator = Container(container).GetItemQueryStreamIterator(
            new QueryDefinition("SELECT * FROM c ORDER BY c.id"),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(partitionKey) });
        while (iterator.HasMoreResults)
        {
            using var page = await iterator.ReadNextAsync(ct);
            page.EnsureSuccessStatusCode();
            var node = await JsonNode.ParseAsync(page.Content, cancellationToken: ct);
            foreach (var item in node?["Documents"]?.AsArray() ?? [])
            {
                results.Add(item!.Deserialize<T>(LearningJson.Options)!);
            }
        }
        return results;
    }

    public async Task UpsertAsync<T>(string container, string partitionKey, string id, T document, CancellationToken ct) where T : class
    {
        using var response = await Container(container).UpsertItemStreamAsync(ToStream(document), new PartitionKey(partitionKey), cancellationToken: ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<T>> FindByIdAsync<T>(string container, string id, CancellationToken ct) where T : class
    {
        var results = new List<T>();
        using var iterator = Container(container).GetItemQueryStreamIterator(
            new QueryDefinition("SELECT TOP 2 * FROM c WHERE c.id = @id").WithParameter("@id", id),
            requestOptions: new QueryRequestOptions { MaxItemCount = 2 });
        while (iterator.HasMoreResults && results.Count < 2)
        {
            using var page = await iterator.ReadNextAsync(ct);
            page.EnsureSuccessStatusCode();
            var node = await JsonNode.ParseAsync(page.Content, cancellationToken: ct);
            foreach (var item in node?["Documents"]?.AsArray() ?? [])
                results.Add(item!.Deserialize<T>(LearningJson.Options)!);
        }
        return results;
    }

    public async Task<bool> TryCreateAsync<T>(string container, string partitionKey, string id, T document, CancellationToken ct) where T : class
    {
        using var response = await Container(container).CreateItemStreamAsync(ToStream(document), new PartitionKey(partitionKey), cancellationToken: ct);
        if (response.StatusCode == HttpStatusCode.Conflict) return false;
        response.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<VersionedDocument<T>?> GetVersionedAsync<T>(string container, string partitionKey, string id, CancellationToken ct) where T : class
    {
        using var response = await Container(container).ReadItemStreamAsync(id, new PartitionKey(partitionKey), cancellationToken: ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var value = await JsonSerializer.DeserializeAsync<T>(response.Content, LearningJson.Options, ct)
            ?? throw new InvalidOperationException("Empty Cosmos document.");
        return new VersionedDocument<T>(value, response.Headers.ETag);
    }

    public async Task<bool> TryReplaceAsync<T>(string container, string partitionKey, string id, T document, string version, CancellationToken ct) where T : class
    {
        using var payload = ToStream(document);
        using var response = await Container(container).ReplaceItemStreamAsync(payload, id, new PartitionKey(partitionKey),
            new ItemRequestOptions { IfMatchEtag = version }, ct);
        if (response.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound) return false;
        response.EnsureSuccessStatusCode();
        return true;
    }

    public void Dispose() => _client.Dispose();
}
