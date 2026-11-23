using LineLanguageLearn.Learning.Domain;

namespace LineLanguageLearn.Learning.Persistence;

/// <summary>Typed access to the learner partition of each container.</summary>
public sealed class DocumentCollection<T>(IDocumentStore store, string container, TimeProvider clock) where T : UserDocument
{
    public string Container { get; } = container;

    public Task<T?> GetAsync(string userId, string id, CancellationToken ct) => store.GetAsync<T>(Container, userId, id, ct);

    public Task<IReadOnlyList<T>> ListAsync(string userId, CancellationToken ct) => store.ListAsync<T>(Container, userId, ct);

    public async Task<T> GetRequiredAsync(string userId, string id, string what, CancellationToken ct) =>
        await GetAsync(userId, id, ct) ?? throw DomainException.NotFound(what, id);

    public Task UpsertAsync(T document, CancellationToken ct)
    {
        Stamp(document, isNew: document.CreatedAt is null);
        return store.UpsertAsync(Container, document.UserId, document.Id, document, ct);
    }

    public Task<bool> TryCreateAsync(T document, CancellationToken ct)
    {
        Stamp(document, isNew: true);
        return store.TryCreateAsync(Container, document.UserId, document.Id, document, ct);
    }

    private void Stamp(T document, bool isNew)
    {
        if (string.IsNullOrWhiteSpace(document.UserId)) throw new InvalidOperationException($"{typeof(T).Name} must have a userId.");
        var now = clock.GetUtcNow().ToString("O");
        if (isNew) document.CreatedAt = now;
        document.UpdatedAt = now;
    }
}

/// <summary>Repository facade over the configured document store.</summary>
public sealed class LearningStore(IDocumentStore store, TimeProvider clock)
{
    public string StoreName => store.Name;
    public DocumentCollection<LearningProfile> Profiles { get; } = new(store, Containers.LearningProfiles, clock);
    public DocumentCollection<LearningObservation> Observations { get; } = new(store, Containers.LearningObservations, clock);
    public DocumentCollection<PracticeSession> PracticeSessions { get; } = new(store, Containers.PracticeSessions, clock);
    public DocumentCollection<TutorSession> TutorSessions { get; } = new(store, Containers.TutorSessions, clock);
    public DocumentCollection<ReviewPlan> ReviewPlans { get; } = new(store, Containers.ReviewPlans, clock);
    public DocumentCollection<WorkflowRun> WorkflowRuns { get; } = new(store, Containers.WorkflowRuns, clock);
    public DocumentCollection<ProcessedEvent> ProcessedEvents { get; } = new(store, Containers.ProcessedEvents, clock);
}
