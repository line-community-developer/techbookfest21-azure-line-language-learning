using System.Text.RegularExpressions;

namespace LineLanguageLearn.Learning.Ai.Mock;

public enum HeuristicKind { Error, CorrectUse }

public sealed record HeuristicMatch(string TopicId, HeuristicKind Kind, string Evidence, string? Suggestion, string ExplanationJa, string? RuleId = null);

/// <summary>
/// MOCK ONLY. Regex rules that stand in for an LLM so the tutor and the conversation analysis are
/// deterministic and testable offline. Covers a handful of frequent A2 errors; it is not a grammar checker.
/// </summary>
public static partial class MockGrammarHeuristics
{
    private const string EssereParticiples = @"(?:andat|venut|partit|arrivat|uscit|tornat|restat|rimast|stat)[oaie]";
    private const string AVerbs = @"(?:cominci\p{L}*|inizi(?:o|a|amo|ate|ano|ato|are|i)|impar\p{L}*|riesc\p{L}*|riusc\p{L}*|continu\p{L}*)";
    private const string DiVerbs = @"(?:fin(?:isco|isci|isce|iamo|ite|iscono|ito|ita|iti|ire)|smett\p{L}*|decis[oaie]|decid\p{L}*|cerc(?:o|hi|a|hiamo|ate|ano|ato|are)|sper(?:o|i|a|iamo|ate|ano|ato|are)|dimentic\p{L}*)";
    private const string Infinitive = @"\p{L}+(?:are|ere|ire)";

    private static readonly Dictionary<string, string> AvereToEssere = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ho"] = "sono", ["hai"] = "sei", ["ha"] = "è", ["abbiamo"] = "siamo", ["avete"] = "siete", ["hanno"] = "sono",
    };

    [GeneratedRegex(@"\b(mi|ti|si|ci|vi)\s+(ho|hai|ha|abbiamo|avete|hanno)\s+(\p{L}+)", RegexOptions.IgnoreCase)]
    private static partial Regex ReflexiveWithAvere();

    [GeneratedRegex(@"\b(ho|hai|ha|abbiamo|avete|hanno)\s+(" + EssereParticiples + @")\b", RegexOptions.IgnoreCase)]
    private static partial Regex AvereWithMotionVerb();

    [GeneratedRegex(@"\b(" + AVerbs + @")\s+di\s+(" + Infinitive + @")\b", RegexOptions.IgnoreCase)]
    private static partial Regex AVerbWithDi();

    [GeneratedRegex(@"\b(" + DiVerbs + @")\s+a\s+(" + Infinitive + @")\b", RegexOptions.IgnoreCase)]
    private static partial Regex DiVerbWithA();

    [GeneratedRegex(@"\b(ho|hai|ha|abbiamo|avete|hanno)\s+voglia\s+(a|da|per)\b", RegexOptions.IgnoreCase)]
    private static partial Regex VogliaWrongPreposition();

    [GeneratedRegex(@"\b(sono|sei|siamo|siete)\s+voglia\b", RegexOptions.IgnoreCase)]
    private static partial Regex VogliaWithEssere();

    [GeneratedRegex(@"\b(vado|vai|va|andiamo|andate|vanno|andare)\s+a\s+(Italia|Giappone|Francia|Spagna|Germania|America|Inghilterra|Cina|Corea)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CountryWithA();

    [GeneratedRegex(@"\b(vado|vai|va|andiamo|andate|vanno|andare)\s+in\s+(Roma|Milano|Firenze|Venezia|Napoli|Torino|Bologna|Tokyo|Osaka|Kyoto)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CityWithIn();

    [GeneratedRegex(@"\bmi\s+piace\s+(gli|i|le)\s+\p{L}+", RegexOptions.IgnoreCase)]
    private static partial Regex PiaceWithPlural();

    [GeneratedRegex(@"\b(?:sono|sei|è|siamo|siete)\s+" + EssereParticiples + @"\b", RegexOptions.IgnoreCase)]
    private static partial Regex EssereWithMotionVerb();

    [GeneratedRegex(@"\b(?:ho|hai|ha|abbiamo|avete|hanno)\s+(?:mangiat|fatt|vist|lavorat|dormit|studiat|comprat|lett|bevut|pres)[oaie]\b", RegexOptions.IgnoreCase)]
    private static partial Regex AvereWithTransitive();

    [GeneratedRegex(@"\b(?:ho|hai|ha|abbiamo|avete|hanno)\s+voglia\s+di\b", RegexOptions.IgnoreCase)]
    private static partial Regex VogliaDi();

    [GeneratedRegex(@"\b" + AVerbs + @"\s+a\s+" + Infinitive + @"\b", RegexOptions.IgnoreCase)]
    private static partial Regex AVerbCorrect();

    [GeneratedRegex(@"\b" + DiVerbs + @"\s+di\s+" + Infinitive + @"\b", RegexOptions.IgnoreCase)]
    private static partial Regex DiVerbCorrect();

    public static IReadOnlyList<HeuristicMatch> Detect(string text)
    {
        var matches = new List<HeuristicMatch>();

        foreach (Match m in ReflexiveWithAvere().Matches(text))
        {
            var essere = AvereToEssere[m.Groups[2].Value];
            matches.Add(new(KnownTopics.ReflexivePassato, HeuristicKind.Error, m.Value, $"{m.Groups[1].Value} {essere} {Participle(m.Groups[3].Value, m.Groups[2].Value)}",
                "再帰動詞の近過去は avere ではなく essere を使います。", "it-reflexive-passato-r1"));
        }
        foreach (Match m in AvereWithMotionVerb().Matches(text))
        {
            var essere = AvereToEssere[m.Groups[1].Value];
            matches.Add(new(KnownTopics.PassatoAuxiliary, HeuristicKind.Error, m.Value, $"{MatchCase(m.Groups[1].Value, essere)} {Participle(m.Groups[2].Value, m.Groups[1].Value)}",
                "移動や状態の変化を表す動詞（andare、tornare など）の近過去は essere を使います。", "it-passato-prossimo-auxiliary-r2"));
        }
        foreach (Match m in AVerbWithDi().Matches(text))
        {
            matches.Add(new(KnownTopics.VerbA, HeuristicKind.Error, m.Value, $"{m.Groups[1].Value} a {m.Groups[2].Value}",
                $"{m.Groups[1].Value} の後の不定詞は di ではなく a でつなぎます。",
                Regex.IsMatch(m.Groups[1].Value, "^(impar|riesc|riusc)", RegexOptions.IgnoreCase) ? "it-verb-a-infinitive-r2" : "it-verb-a-infinitive-r1"));
        }
        foreach (Match m in DiVerbWithA().Matches(text))
        {
            matches.Add(new(KnownTopics.VerbDi, HeuristicKind.Error, m.Value, $"{m.Groups[1].Value} di {m.Groups[2].Value}",
                $"{m.Groups[1].Value} の後の不定詞は a ではなく di でつなぎます。",
                Regex.IsMatch(m.Groups[1].Value, "^(fin|smett)", RegexOptions.IgnoreCase) ? "it-verb-di-infinitive-r1" : "it-verb-di-infinitive-r2"));
        }
        foreach (Match m in VogliaWrongPreposition().Matches(text))
        {
            matches.Add(new(KnownTopics.AvereVogliaDi, HeuristicKind.Error, m.Value, $"{m.Groups[1].Value} voglia di", "avere voglia の後は常に di です。", "it-avere-voglia-di-r1"));
        }
        foreach (Match m in VogliaWithEssere().Matches(text))
        {
            var avere = m.Groups[1].Value.ToLowerInvariant() switch { "sono" => "ho", "sei" => "hai", "siamo" => "abbiamo", _ => "avete" };
            matches.Add(new(KnownTopics.AvereVogliaDi, HeuristicKind.Error, m.Value, $"{avere} voglia", "voglia は essere ではなく avere と組み合わせます。", "it-avere-voglia-di-r2"));
        }
        foreach (Match m in CountryWithA().Matches(text))
        {
            matches.Add(new(KnownTopics.AndareIn, HeuristicKind.Error, m.Value, $"{m.Groups[1].Value} in {m.Groups[2].Value}", "国名には a ではなく in を使います。", "it-andare-in-r1"));
        }
        foreach (Match m in CityWithIn().Matches(text))
        {
            matches.Add(new(KnownTopics.AndareA, HeuristicKind.Error, m.Value, $"{m.Groups[1].Value} a {m.Groups[2].Value}", "都市名には in ではなく a を使います。", "it-andare-a-r1"));
        }
        foreach (Match m in PiaceWithPlural().Matches(text))
        {
            matches.Add(new(KnownTopics.Piacere, HeuristicKind.Error, m.Value, m.Value.Replace("piace", "piacciono", StringComparison.OrdinalIgnoreCase),
                "好きなものが複数のときは piacciono です。", "it-piacere-r1"));
        }

        AddCorrectUses(matches, EssereWithMotionVerb(), KnownTopics.PassatoAuxiliary, text, "移動を表す動詞を essere で正しく使えています。");
        AddCorrectUses(matches, AvereWithTransitive(), KnownTopics.PassatoAuxiliary, text, "他動詞を avere で正しく使えています。");
        AddCorrectUses(matches, VogliaDi(), KnownTopics.AvereVogliaDi, text, "avere voglia di を正しく使えています。");
        AddCorrectUses(matches, AVerbCorrect(), KnownTopics.VerbA, text, "a + 不定詞をとる動詞を正しく使えています。");
        AddCorrectUses(matches, DiVerbCorrect(), KnownTopics.VerbDi, text, "di + 不定詞をとる動詞を正しく使えています。");
        return matches;
    }

    private static void AddCorrectUses(List<HeuristicMatch> matches, Regex regex, string topicId, string text, string explanation)
    {
        foreach (Match m in regex.Matches(text))
        {
            if (matches.Any(x => x.TopicId == topicId && x.Kind == HeuristicKind.Error)) continue;
            matches.Add(new(topicId, HeuristicKind.CorrectUse, m.Value, null, explanation));
        }
    }

    // First/second person singular could be either gender, so offer both endings.
    private static string Participle(string participle, string avereForm) =>
        participle.EndsWith('o') && avereForm.ToLowerInvariant() is "ho" or "hai" ? participle + "/a" : participle;

    private static string MatchCase(string original, string replacement) =>
        original.Length > 0 && char.IsUpper(original[0]) ? char.ToUpperInvariant(replacement[0]) + replacement[1..] : replacement;
}

public static class KnownTopics
{
    public const string DefiniteArticles = "it-definite-articles";
    public const string PresentRegular = "it-present-regular";
    public const string VerbA = "it-verb-a-infinitive";
    public const string VerbDi = "it-verb-di-infinitive";
    public const string AvereVogliaDi = "it-avere-voglia-di";
    public const string AndareA = "it-andare-a";
    public const string AndareIn = "it-andare-in";
    public const string PassatoAuxiliary = "it-passato-prossimo-auxiliary";
    public const string ReflexivePassato = "it-reflexive-passato";
    public const string Piacere = "it-piacere";
}
