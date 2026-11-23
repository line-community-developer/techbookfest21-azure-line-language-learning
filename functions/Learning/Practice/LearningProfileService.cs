using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Identity;
using LineLanguageLearn.Learning.Persistence;

namespace LineLanguageLearn.Learning.Practice;

public static class ObservationIds
{
    public static string Practice(string sessionId, string questionId) => $"obs-p-{sessionId}-{questionId}";

    public static string Conversation(string sessionId, string messageId, string topicId, ObservationType type) =>
        $"obs-c-{sessionId}-{messageId}-{topicId}-{type.ToString().ToLowerInvariant()}";
}

public sealed record ObservationWriteResult(IReadOnlyList<string> CreatedIds, IReadOnlyList<string> DuplicateIds, LearningProfile Profile);

/// <summary>
/// Owns the learner profile and its observations. All mastery changes go through RecordObservationsAsync,
/// which is idempotent (deterministic observation ids + create-if-absent) and recomputes the projection.
/// </summary>
public sealed class LearningProfileService(LearningStore store, TimeProvider clock)
{
    public const string ProfileId = "profile";

    public async Task<LearningProfile> GetOrCreateAsync(UserContext user, CancellationToken ct)
    {
        var profile = await store.Profiles.GetAsync(user.UserId, ProfileId, ct);
        if (profile is not null) return profile;
        profile = new LearningProfile
        {
            Id = ProfileId,
            UserId = user.UserId,
            LearnerName = user.DisplayName,
            NativeLanguage = LanguageCode.Ja,
            TargetLanguage = TargetLanguage.It,
            CurrentLevel = CefrLevel.A1,
        };
        await store.Profiles.TryCreateAsync(profile, ct);
        return (await store.Profiles.GetAsync(user.UserId, ProfileId, ct))!;
    }

    public Task<LearningProfile?> FindAsync(string userId, CancellationToken ct) => store.Profiles.GetAsync(userId, ProfileId, ct);

    public Task SaveAsync(LearningProfile profile, CancellationToken ct) => store.Profiles.UpsertAsync(profile, ct);

    public async Task<IReadOnlyList<LearningObservation>> ListObservationsAsync(string userId, CancellationToken ct) =>
        (await store.Observations.ListAsync(userId, ct))
            .OrderByDescending(o => o.OccurredAt, StringComparer.Ordinal)
            .ThenByDescending(o => o.Id, StringComparer.Ordinal)
            .ToList();

    public async Task<ObservationWriteResult> RecordObservationsAsync(string userId, IEnumerable<LearningObservation> observations, CancellationToken ct)
    {
        var created = new List<string>();
        var duplicates = new List<string>();
        foreach (var observation in observations)
        {
            if (observation.UserId != userId) throw new InvalidOperationException("Observation belongs to another learner.");
            if (string.IsNullOrEmpty(observation.OccurredAt)) observation.OccurredAt = clock.GetUtcNow().ToString("O");
            if (await store.Observations.TryCreateAsync(observation, ct)) created.Add(observation.Id);
            else duplicates.Add(observation.Id);
        }
        var profile = await RecomputeAsync(userId, ct);
        return new ObservationWriteResult(created, duplicates, profile);
    }

    public async Task<LearningProfile> RecomputeAsync(string userId, CancellationToken ct)
    {
        var profile = await store.Profiles.GetAsync(userId, ProfileId, ct) ?? throw DomainException.NotFound("Learning profile", userId);
        var observations = await store.Observations.ListAsync(userId, ct);
        profile.MasteryByTopic = MasteryCalculator.Compute(observations);
        profile.WeakTopics = profile.MasteryByTopic.Values
            .Where(m => m.Status == MasteryStatus.Weak)
            .OrderBy(m => m.Score).ThenBy(m => m.TopicId, StringComparer.Ordinal)
            .Select(m => m.TopicId).ToList();
        profile.StrongTopics = profile.MasteryByTopic.Values
            .Where(m => m.Status == MasteryStatus.Strong)
            .OrderByDescending(m => m.Score).ThenBy(m => m.TopicId, StringComparer.Ordinal)
            .Select(m => m.TopicId).ToList();
        profile.ObservationCount = observations.Count;
        await store.Profiles.UpsertAsync(profile, ct);
        return profile;
    }

    public static double MasteryOf(LearningProfile profile, string topicId) =>
        profile.MasteryByTopic.TryGetValue(topicId, out var mastery) ? mastery.Score : MasteryCalculator.InitialScore;
}
