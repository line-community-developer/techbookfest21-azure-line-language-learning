using System.Collections.Concurrent;
using LineLanguageLearn.Learning.Infrastructure;

namespace LineLanguageLearn.Learning.Persistence;

/// <summary>Process-local store for tests. Documents are stored serialized so callers never share references.</summary>
public sealed class InMemoryDocumentStore : IDocumentStore
{
    private readonly ConcurrentDictionary<(string Container, string Pk), ConcurrentDictionary<string, string>> _data = new();

    public string Name => "in-memory";

    private ConcurrentDictionary<string, string> Partition(string container, string pk) =>
        _data.GetOrAdd((container, pk), _ => new ConcurrentDictionary<string, string>());

    public Task<T?> GetAsync<T>(string container, string partitionKey, string id, CancellationToken ct) where T : class =>
        Task.FromResult(Partition(container, partitionKey).TryGetValue(id, out var json) ? LearningJson.Deserialize<T>(json) : null);

    public Task<IReadOnlyList<T>> ListAsync<T>(string container, string partitionKey, CancellationToken ct) where T : class =>
        Task.FromResult<IReadOnlyList<T>>(Partition(container, partitionKey).OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => LearningJson.Deserialize<T>(p.Value)).ToList());

    public Task<IReadOnlyList<T>> FindByIdAsync<T>(string container, string id, CancellationToken ct) where T : class =>
        Task.FromResult<IReadOnlyList<T>>(_data.Where(p => p.Key.Container == container)
            .SelectMany(p => p.Value.Where(d => d.Key == id)).Take(2)
            .Select(d => LearningJson.Deserialize<T>(d.Value)).ToList());

    public Task UpsertAsync<T>(string container, string partitionKey, string id, T document, CancellationToken ct) where T : class
    {
        Partition(container, partitionKey)[id] = LearningJson.Serialize(document);
        return Task.CompletedTask;
    }

    public Task<bool> TryCreateAsync<T>(string container, string partitionKey, string id, T document, CancellationToken ct) where T : class =>
        Task.FromResult(Partition(container, partitionKey).TryAdd(id, LearningJson.Serialize(document)));

    public Task<VersionedDocument<T>?> GetVersionedAsync<T>(string container, string partitionKey, string id, CancellationToken ct) where T : class =>
        Task.FromResult(Partition(container, partitionKey).TryGetValue(id, out var json)
            ? new VersionedDocument<T>(LearningJson.Deserialize<T>(json), json) : null);

    public Task<bool> TryReplaceAsync<T>(string container, string partitionKey, string id, T document, string version, CancellationToken ct) where T : class =>
        Task.FromResult(Partition(container, partitionKey).TryUpdate(id, LearningJson.Serialize(document), version));
}
