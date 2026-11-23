using System.Text.Json.Nodes;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Knowledge;
using LineLanguageLearn.SeedData;

namespace LineLanguageLearn.Tests;

public class RetrievalTests
{
    private static IGrammarRetriever Retriever(TestApp app) => app.Get<IGrammarRetriever>();

    [Fact]
    public async Task Topic_id_search_returns_that_topic_with_evidence_ids()
    {
        using var app = new TestApp();
        var hits = await Retriever(app).SearchAsync(new GrammarQuery(TargetLanguage.It, "avere voglia di", TopicId: "it-avere-voglia-di", Limit: 3), default);
        var top = hits[0];
        Assert.Equal("it-avere-voglia-di", top.Topic.Id);
        Assert.Contains("it-avere-voglia-di", top.SourceIds);
        Assert.Contains("src-original-it-avere-voglia-di", top.SourceIds);
        Assert.NotEmpty(top.MatchedTerms);
    }

    [Theory]
    [InlineData("a と di の使い分け", "it-verb-a-infinitive", "it-verb-di-infinitive")]
    [InlineData("cominciare a studiare", "it-verb-a-infinitive")]
    [InlineData("Ho andato al mare: avere o essere?", "it-passato-prossimo-auxiliary")]
    [InlineData("近過去の助動詞", "it-passato-prossimo-auxiliary")]
    public async Task Keyword_search_finds_relevant_topics(string query, params string[] expected)
    {
        using var app = new TestApp();
        var hits = await Retriever(app).SearchAsync(new GrammarQuery(TargetLanguage.It, query, Limit: 3), default);
        foreach (var id in expected) Assert.Contains(hits, h => h.Topic.Id == id);
    }

    [Fact]
    public async Task Unknown_subject_returns_no_results()
    {
        using var app = new TestApp();
        Assert.Empty(await Retriever(app).SearchAsync(new GrammarQuery(TargetLanguage.It, "quantum chromodynamics"), default));
    }

    [Fact]
    public async Task Level_and_language_filters_apply()
    {
        using var app = new TestApp();
        var a1 = await Retriever(app).SearchAsync(new GrammarQuery(TargetLanguage.It, "a と di の使い分け 冠詞", CefrLevel.A1, Limit: 10), default);
        Assert.All(a1, h => Assert.Equal(CefrLevel.A1, h.Topic.Level));
        Assert.DoesNotContain(a1, h => h.Topic.Id == "it-verb-a-infinitive");
        Assert.Empty(await Retriever(app).SearchAsync(new GrammarQuery(TargetLanguage.En, "cominciare a"), default));
    }

    [Fact]
    public void Knowledge_base_is_valid_and_marked_unreviewed()
    {
        var kb = ReferenceDataFixtures.Load().Grammar;
        Assert.InRange(kb.Topics.Count, 10, 20);
        Assert.All(kb.Topics, t =>
        {
            Assert.Equal(ReviewStatus.Unreviewed, t.ReviewStatus);
            Assert.NotEmpty(t.CommonMistakes.Concat<object>(t.Examples));
            Assert.All(t.Rules, r => Assert.StartsWith(t.Id, r.Id));
        });
    }

    [Fact]
    public void Foundry_iq_references_map_to_domain_hits_and_unknown_keys_are_dropped()
    {
        var kb = ReferenceDataFixtures.Load().Grammar;
        var body = JsonNode.Parse("""
            {"references":[
              {"type":"searchIndex","id":"0","docKey":"it-verb-di-infinitive","rerankerScore":2.5},
              {"type":"searchIndex","id":"1","docKey":"made-up-topic"},
              {"type":"searchIndex","id":"2","docKey":"it-verb-a-infinitive"}]}
            """)!.AsObject();
        var hits = FoundryIqGrammarRetriever.MapReferences(body, new GrammarQuery(TargetLanguage.It, "finire di"), kb);
        Assert.Equal(["it-verb-di-infinitive", "it-verb-a-infinitive"], hits.Select(h => h.Topic.Id));
        Assert.Contains("foundry-iq:ref:0", hits[0].SourceIds);

        var request = FoundryIqGrammarRetriever.BuildRequestBody(new GrammarQuery(TargetLanguage.It, "finire di"), new FoundryIqOptions { KnowledgeSourceName = "grammar-ks" });
        Assert.Equal("semantic", request["intents"]![0]!["type"]!.GetValue<string>());
        Assert.Equal("grammar-ks", request["knowledgeSourceParams"]![0]!["knowledgeSourceName"]!.GetValue<string>());
    }
}
