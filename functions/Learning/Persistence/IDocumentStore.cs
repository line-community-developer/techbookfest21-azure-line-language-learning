namespace LineLanguageLearn.Learning.Persistence;

public sealed record VersionedDocument<T>(T Value, string Version);

/// <summary>
/// Minimal partitioned document store. Partition keys follow the Cosmos DB design (/userId for learner data),
/// so the Cosmos adapter and the test store use the same document contract.
/// </summary>
public interface IDocumentStore
{
    string Name { get; }
    Task<T?> GetAsync<T>(string container, string partitionKey, string id, CancellationToken ct) where T : class;
    Task<IReadOnlyList<T>> ListAsync<T>(string container, string partitionKey, CancellationToken ct) where T : class;
    /// <summary>Finds at most two documents by exact id across partitions, so callers can reject ambiguous references.</summary>
    Task<IReadOnlyList<T>> FindByIdAsync<T>(string container, string id, CancellationToken ct) where T : class;
    Task UpsertAsync<T>(string container, string partitionKey, string id, T document, CancellationToken ct) where T : class;
    /// <summary>Creates the document only if the id does not exist yet. Returns false when it already existed.</summary>
    Task<bool> TryCreateAsync<T>(string container, string partitionKey, string id, T document, CancellationToken ct) where T : class;
    Task<VersionedDocument<T>?> GetVersionedAsync<T>(string container, string partitionKey, string id, CancellationToken ct) where T : class;
    Task<bool> TryReplaceAsync<T>(string container, string partitionKey, string id, T document, string version, CancellationToken ct) where T : class;
}

public static class Containers
{
    public const string LearningProfiles = "LearningProfiles";
    public const string LearningObservations = "LearningObservations";
    public const string PracticeSessions = "PracticeSessions";
    public const string TutorSessions = "TutorSessions";
    public const string ReviewPlans = "ReviewPlans";
    public const string WorkflowRuns = "WorkflowRuns";
    public const string ProcessedEvents = "ProcessedEvents";
    public const string GrammarTopics = "GrammarTopics";
    public const string TutorProfiles = "TutorProfiles";
    public const string SampleQuestions = "SampleQuestions";
    public const string TutorToolTurns = "TutorToolTurns";
}
