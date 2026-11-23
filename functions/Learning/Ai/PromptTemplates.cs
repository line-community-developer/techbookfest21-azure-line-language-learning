using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace LineLanguageLearn.Learning.Ai;

/// <summary>Versioned prompt templates embedded from Ai/Prompts/*.md with {{placeholder}} substitution.</summary>
public static partial class PromptTemplates
{
    private static readonly ConcurrentDictionary<string, string> Cache = new();

    public static string Load(string templateId) => Cache.GetOrAdd(templateId, id =>
    {
        using var stream = typeof(PromptTemplates).Assembly.GetManifestResourceStream($"LearningPrompts.{id}.md")
            ?? throw new InvalidOperationException($"Prompt template '{id}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    public static string Render(string templateId, IReadOnlyDictionary<string, string> values)
    {
        var rendered = Placeholder().Replace(Load(templateId), m =>
            values.TryGetValue(m.Groups[1].Value, out var value) ? value : throw new InvalidOperationException($"Missing prompt value '{m.Groups[1].Value}' for {templateId}."));
        return rendered;
    }

    public static string LanguageName(Domain.TargetLanguage language) => language switch
    {
        Domain.TargetLanguage.It => "Italian",
        Domain.TargetLanguage.En => "English",
        _ => language.ToString(),
    };

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex Placeholder();
}
