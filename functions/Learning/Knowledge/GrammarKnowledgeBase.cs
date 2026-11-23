using LineLanguageLearn.Learning.Contracts;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Infrastructure;

namespace LineLanguageLearn.Learning.Knowledge;

/// <summary>
/// Read-only grammar snapshot loaded from the database before learning requests execute.
/// </summary>
public sealed class GrammarKnowledgeBase
{
    private readonly Dictionary<string, GrammarTopic> _byId;

    public GrammarKnowledgeBase(IEnumerable<GrammarTopic> topics)
    {
        Topics = topics.ToList();
        foreach (var topic in Topics)
            ContractSchemas.EnsureValid(SchemaNames.GrammarTopic, LearningJson.ToElement(topic));
        foreach (var topic in Topics)
        {
            var missing = topic.RelatedTopics.Where(id => Topics.All(t => t.Id != id)).ToList();
            if (missing.Count > 0) throw new InvalidOperationException($"Grammar topic {topic.Id} references unknown topics: {string.Join(", ", missing)}");
        }
        _byId = Topics.ToDictionary(t => t.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<GrammarTopic> Topics { get; }

    public GrammarTopic? Find(string topicId) => _byId.GetValueOrDefault(topicId);

    public GrammarTopic Get(string topicId) => Find(topicId) ?? throw DomainException.NotFound("Grammar topic", topicId);

    public bool Exists(string topicId) => _byId.ContainsKey(topicId);

    public IEnumerable<GrammarTopic> ForLanguage(TargetLanguage language) => Topics.Where(t => t.Language == language);
}
