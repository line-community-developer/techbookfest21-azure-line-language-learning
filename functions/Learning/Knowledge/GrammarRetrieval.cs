using System.Diagnostics;
using System.Globalization;
using System.Text;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Observability;
using Microsoft.Extensions.Logging;

namespace LineLanguageLearn.Learning.Knowledge;

public sealed record GrammarQuery(TargetLanguage Language, string Query, CefrLevel? MaxLevel = null, string? TopicId = null, int Limit = 3);

/// <summary>A retrieved grammar entry plus the evidence identifiers used for citation/grounding.</summary>
public sealed record GrammarSearchHit(GrammarTopic Topic, double Score, IReadOnlyList<string> MatchedTerms, IReadOnlyList<string> SourceIds);

/// <summary>Retrieval boundary for grammar knowledge (local keyword search now, Foundry IQ later).</summary>
public interface IGrammarRetriever
{
    string Name { get; }
    bool IsMock { get; }
    Task<IReadOnlyList<GrammarSearchHit>> SearchAsync(GrammarQuery query, CancellationToken ct);
}

/// <summary>
/// Deterministic keyword retriever over the bundled knowledge base. It selects only the relevant entries
/// so callers never paste the whole knowledge base into a prompt.
/// </summary>
public sealed class LocalGrammarRetriever(GrammarKnowledgeBase knowledgeBase, ILogger<LocalGrammarRetriever> logger) : IGrammarRetriever
{
    public string Name => "local-keyword";
    public bool IsMock => false;

    public Task<IReadOnlyList<GrammarSearchHit>> SearchAsync(GrammarQuery query, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var limit = Math.Clamp(query.Limit, 1, 10);
        var text = Normalize(query.Query);
        var tokens = Tokenize(text);

        var hits = new List<GrammarSearchHit>();
        foreach (var topic in knowledgeBase.ForLanguage(query.Language))
        {
            if (query.MaxLevel is { } max && topic.Level.Rank() > max.Rank()) continue;

            var matched = new SortedSet<string>(StringComparer.Ordinal);
            double score = 0;
            if (query.TopicId is not null && topic.Id == query.TopicId)
            {
                score += 10;
                matched.Add(topic.Id);
            }
            if (text.Length > 0 && Normalize(topic.Id) == text)
            {
                score += 10;
                matched.Add(topic.Id);
            }
            foreach (var keyword in topic.Keywords.Select(Normalize).Where(k => k.Length > 0))
            {
                // Multi-character phrases match as substrings (works for Japanese without word breaks);
                // single short words must match a whole token.
                var phraseMatch = keyword.Length >= 3 ? text.Contains(keyword, StringComparison.Ordinal) : tokens.Contains(keyword);
                if (phraseMatch)
                {
                    score += keyword.Contains(' ') || keyword.Length >= 6 ? 3 : 2;
                    matched.Add(keyword);
                }
            }
            var titleText = Normalize($"{topic.Title} {topic.TitleJa}");
            foreach (var token in tokens.Where(t => t.Length >= 3 && titleText.Contains(t, StringComparison.Ordinal)))
            {
                score += 1.5;
                matched.Add(token);
            }
            var bodyText = Normalize(string.Join(' ',
                topic.Rules.Select(r => r.SummaryJa + " " + r.Pattern)
                    .Concat(topic.Examples.Select(e => e.Text))
                    .Concat(topic.CommonMistakes.Select(m => m.Incorrect + " " + m.Correct))));
            foreach (var token in tokens.Where(t => t.Length >= 4 && bodyText.Contains(t, StringComparison.Ordinal)))
            {
                score += 0.5;
                matched.Add(token);
            }
            if (score > 0)
            {
                var sourceIds = new List<string> { topic.Id };
                sourceIds.AddRange(topic.SourceReferences.Select(s => s.Id));
                hits.Add(new GrammarSearchHit(topic, Math.Round(score, 2), matched.ToList(), sourceIds));
            }
        }

        IReadOnlyList<GrammarSearchHit> result = hits
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.Topic.Level.Rank())
            .ThenBy(h => h.Topic.Id, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
        logger.Retrieval(Name, result.Count, string.Join(',', result.Select(h => h.Topic.Id)), Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return Task.FromResult(result);
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var lowered = value.Trim().ToLowerInvariant().Replace('’', '\'').Replace('‘', '\'').Replace('/', ' ');
        var builder = new StringBuilder(lowered.Length);
        var previousSpace = false;
        foreach (var c in lowered.Normalize(NormalizationForm.FormC))
        {
            var isSpace = char.IsWhiteSpace(c) || c is '.' or ',' or '!' or '?' or '(' or ')' or '「' or '」' or '、' or '。';
            if (isSpace)
            {
                if (!previousSpace) builder.Append(' ');
                previousSpace = true;
            }
            else
            {
                builder.Append(c);
                previousSpace = false;
            }
        }
        return builder.ToString().Trim();
    }

    private static HashSet<string> Tokenize(string normalized) =>
        normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
}
