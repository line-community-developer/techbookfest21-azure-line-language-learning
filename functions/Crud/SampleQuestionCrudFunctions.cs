using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Functions.Auth;

namespace SwallowKit.Functions;

public sealed class SampleQuestionCrudFunctions
{
    private readonly ILogger<SampleQuestionCrudFunctions> _logger;
    private static readonly string ContainerName = "SampleQuestions";

    public SampleQuestionCrudFunctions(ILogger<SampleQuestionCrudFunctions> logger)
    {
        _logger = logger;
    }

    private static string DatabaseName => Environment.GetEnvironmentVariable("COSMOS_DB_DATABASE_NAME") ?? "AppDatabase";

    private static bool IsLocalCosmosEndpoint(string endpoint) =>
        endpoint.Contains("localhost:8081", StringComparison.OrdinalIgnoreCase) ||
        endpoint.Contains("127.0.0.1:8081", StringComparison.OrdinalIgnoreCase);

    private static CosmosClient CreateCosmosClient()
    {
        var endpoint = Environment.GetEnvironmentVariable("CosmosDBConnection__accountEndpoint");
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            var options = IsLocalCosmosEndpoint(endpoint)
                ? new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway }
                : new CosmosClientOptions();
            return new CosmosClient(endpoint, new DefaultAzureCredential(), options);
        }

        var connectionString = Environment.GetEnvironmentVariable("CosmosDBConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Cosmos DB connection is not configured.");
        }

        var connectionOptions = IsLocalCosmosEndpoint(connectionString)
            ? new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway }
            : new CosmosClientOptions();

        return new CosmosClient(connectionString, connectionOptions);
    }

    private static Container GetContainer(CosmosClient client) => client.GetContainer(DatabaseName, ContainerName);

    private static async Task<JsonObject> ReadRequestBodyAsync(HttpRequestData request)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var raw = await reader.ReadToEndAsync();
        var node = JsonNode.Parse(raw) as JsonObject;

        if (node is null)
        {
            throw new JsonException("Request body must be a JSON object.");
        }

        return node;
    }

    private static JsonObject BuildManagedDocument(JsonObject source, string id, string createdAt, string updatedAt)
    {
        var payload = new JsonObject();

        foreach (var entry in source)
        {
            if (entry.Key is "id" or "createdAt" or "updatedAt")
            {
                continue;
            }

            payload[entry.Key] = entry.Value?.DeepClone();
        }

        payload["id"] = id;
        payload["createdAt"] = createdAt;
        payload["updatedAt"] = updatedAt;

        return payload;
    }

    private static Stream CreateJsonStream(JsonObject payload) =>
        new MemoryStream(Encoding.UTF8.GetBytes(payload.ToJsonString()));

    private static async Task<JsonObject> ReadCosmosItemAsync(Container container, string id)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @id")
            .WithParameter("@id", id);
        using var iterator = container.GetItemQueryStreamIterator(query);

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync();
            if (!page.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Cosmos query failed with status {(int)page.StatusCode}.");
            }

            using var document = await JsonDocument.ParseAsync(page.Content);
            if (document.RootElement.TryGetProperty("Documents", out var documents))
            {
                foreach (var item in documents.EnumerateArray())
                {
                    return JsonNode.Parse(item.GetRawText())?.AsObject()
                        ?? throw new JsonException("Cosmos item payload must be a JSON object.");
                }
            }
        }

        throw new CosmosException("Item not found", HttpStatusCode.NotFound, 0, "", 0);
    }

    private static async Task<HttpResponseData> WriteJsonAsync(HttpRequestData request, HttpStatusCode status, object payload)
    {
        var response = request.CreateResponse(status);
        await response.WriteAsJsonAsync(payload);
        return response;
    }

    [Function("sampleQuestionGetAll")]
    public async Task<HttpResponseData> GetAll(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "sampleQuestion")] HttpRequestData request)
    {
        try
        {
            var (principal, errorResponse) = await AuthRouter.Authorize(request, "adminOnly");
            if (errorResponse != null) return errorResponse;

            using var client = CreateCosmosClient();
            var container = GetContainer(client);
            using var iterator = container.GetItemQueryStreamIterator("SELECT * FROM c");
            var items = new List<JsonElement>();

            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync();
                if (!page.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException($"Cosmos query failed with status {(int)page.StatusCode}.");
                }

                using var document = await JsonDocument.ParseAsync(page.Content);
                if (!document.RootElement.TryGetProperty("Documents", out var documents))
                {
                    continue;
                }

                foreach (var item in documents.EnumerateArray())
                {
                    items.Add(item.Clone());
                }
            }

            _logger.LogInformation("Fetched {Count} SampleQuestion item(s) from {Container}.", items.Count, ContainerName);
            return await WriteJsonAsync(request, HttpStatusCode.OK, items);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch SampleQuestion items from Cosmos DB.");
            return await WriteJsonAsync(request, HttpStatusCode.InternalServerError, new { error = "Failed to fetch items" });
        }
    }

    [Function("sampleQuestionGetById")]
    public async Task<HttpResponseData> GetById(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "sampleQuestion/{id}")] HttpRequestData request,
        string id)
    {
        try
        {
            var (principal, errorResponse) = await AuthRouter.Authorize(request, "adminOnly");
            if (errorResponse != null) return errorResponse;

            using var client = CreateCosmosClient();
            var container = GetContainer(client);
            var item = await ReadCosmosItemAsync(container, id);
            return await WriteJsonAsync(request, HttpStatusCode.OK, item);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return await WriteJsonAsync(request, HttpStatusCode.NotFound, new { error = "SampleQuestion not found", id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch SampleQuestion item {Id} from Cosmos DB.", id);
            return await WriteJsonAsync(request, HttpStatusCode.InternalServerError, new { error = "Failed to fetch item", id });
        }
    }

    [Function("sampleQuestionCreate")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "sampleQuestion")] HttpRequestData request)
    {
        try
        {
            var (principal, errorResponse) = await AuthRouter.Authorize(request, "adminOnly");
            if (errorResponse != null) return errorResponse;

            var body = await ReadRequestBodyAsync(request);
            var now = DateTimeOffset.UtcNow.ToString("O");
            var id = body["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString();
            var payload = BuildManagedDocument(body, id, now, now);

            using var client = CreateCosmosClient();
            var container = GetContainer(client);
            using var stream = CreateJsonStream(payload);
            var pkValue = payload["language"]?.GetValue<string>() ?? throw new InvalidOperationException("Partition key field 'language' is required.");
            var response = await container.CreateItemStreamAsync(stream, new PartitionKey(pkValue));
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Cosmos create failed with status {(int)response.StatusCode}.");
            }

            return await WriteJsonAsync(request, HttpStatusCode.Created, payload);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid SampleQuestion create payload.");
            return await WriteJsonAsync(request, HttpStatusCode.BadRequest, new { error = "Request body must be a JSON object." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create SampleQuestion item in Cosmos DB.");
            return await WriteJsonAsync(request, HttpStatusCode.InternalServerError, new { error = "Failed to create item" });
        }
    }

    [Function("sampleQuestionUpdate")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Function, "put", Route = "sampleQuestion/{id}")] HttpRequestData request,
        string id)
    {
        try
        {
            var (principal, errorResponse) = await AuthRouter.Authorize(request, "adminOnly");
            if (errorResponse != null) return errorResponse;

            using var client = CreateCosmosClient();
            var container = GetContainer(client);

            JsonObject existing;
            try
            {
                existing = await ReadCosmosItemAsync(container, id);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return await WriteJsonAsync(request, HttpStatusCode.NotFound, new { error = "SampleQuestion not found", id });
            }

            var body = await ReadRequestBodyAsync(request);
            var createdAt = existing["createdAt"]?.GetValue<string>() ?? DateTimeOffset.UtcNow.ToString("O");
            var payload = BuildManagedDocument(body, id, createdAt, DateTimeOffset.UtcNow.ToString("O"));

            using var stream = CreateJsonStream(payload);
            var response = await container.ReplaceItemStreamAsync(stream, id, new PartitionKey(payload["language"]?.GetValue<string>()));
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Cosmos replace failed with status {(int)response.StatusCode}.");
            }

            return await WriteJsonAsync(request, HttpStatusCode.OK, payload);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid SampleQuestion update payload for {Id}.", id);
            return await WriteJsonAsync(request, HttpStatusCode.BadRequest, new { error = "Request body must be a JSON object.", id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update SampleQuestion item {Id} in Cosmos DB.", id);
            return await WriteJsonAsync(request, HttpStatusCode.InternalServerError, new { error = "Failed to update item", id });
        }
    }

    [Function("sampleQuestionDelete")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "sampleQuestion/{id}")] HttpRequestData request,
        string id)
    {
        try
        {
            var (principal, errorResponse) = await AuthRouter.Authorize(request, "adminOnly");
            if (errorResponse != null) return errorResponse;

            using var client = CreateCosmosClient();
            var container = GetContainer(client);
            var existing = await ReadCosmosItemAsync(container, id);
            var pkValue = existing["language"]?.GetValue<string>();
            await container.DeleteItemAsync<JsonObject>(id, new PartitionKey(pkValue));

            return request.CreateResponse(HttpStatusCode.NoContent);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return await WriteJsonAsync(request, HttpStatusCode.NotFound, new { error = "SampleQuestion not found", id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete SampleQuestion item {Id} from Cosmos DB.", id);
            return await WriteJsonAsync(request, HttpStatusCode.InternalServerError, new { error = "Failed to delete item", id });
        }
    }
}
