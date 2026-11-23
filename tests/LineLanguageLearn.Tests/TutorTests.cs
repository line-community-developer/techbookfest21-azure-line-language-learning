using System.Text.Json;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Identity;
using LineLanguageLearn.Learning.Messaging;
using LineLanguageLearn.Learning.Practice;
using LineLanguageLearn.Learning.Tutor;
using LineLanguageLearn.Learning.Tutor.Tools;

namespace LineLanguageLearn.Tests;

public class TutorTests
{
    private static async Task<(TutorService Tutor, TutorSession Session)> StartAsync(TestApp app, UserContext? user = null)
    {
        var tutor = app.Get<TutorService>();
        return (tutor, await tutor.StartSessionAsync(user ?? TestApp.Learner, "週末", TutorChannel.LocalSimulator, default));
    }

    [Fact]
    public async Task Turn_uses_profile_history_tools_and_rag_and_records_observations()
    {
        using var app = new TestApp();
        var (tutor, session) = await StartAsync(app);

        var reply = await tutor.SendMessageAsync(TestApp.Learner, session.Id, "e1", "Sabato ho andato al mare.", default);

        var message = Assert.Single(reply.Replies);
        var calls = message.ToolCalls.Select(c => c.Name).ToList();
        Assert.Equal(["get_learning_profile", "get_recent_mistakes", "record_learning_observation", "search_grammar_rules"], calls);
        Assert.All(message.ToolCalls, c => Assert.Equal(ToolCallStatus.Succeeded, c.Status));
        Assert.Contains("it-passato-prossimo-auxiliary", message.RetrievedSourceIds);
        var correction = Assert.Single(message.Corrections);
        Assert.Equal("ho andato", correction.Incorrect);
        Assert.Contains("sono andato", correction.Suggestion);
        Assert.Contains("essere", correction.ExplanationJa); // explains the rule that matches the error

        var observation = Assert.Single(await app.Store.Observations.ListAsync(TestApp.Learner.UserId, default));
        Assert.Equal(ObservationCertainty.AiInferred, observation.Certainty);
        Assert.Equal(ObservationType.GrammarError, observation.ObservationType);
        Assert.Equal("msg-2", observation.SourceItemId);
        Assert.Equal("ho andato", observation.Evidence.Text);
    }

    [Fact]
    public async Task Later_turns_use_conversation_history()
    {
        using var app = new TestApp();
        var (tutor, session) = await StartAsync(app);
        var first = await tutor.SendMessageAsync(TestApp.Learner, session.Id, "e1", "Ciao! Ho voglia di un gelato.", default);
        var second = await tutor.SendMessageAsync(TestApp.Learner, session.Id, "e2", "Va bene.", default);

        Assert.DoesNotContain(second.Replies[0].ToolCalls, c => c.Name == "get_learning_profile");
        Assert.NotEqual(first.Replies[0].Text, second.Replies[0].Text);
        Assert.Equal(5, second.Session.Messages.Count);
    }

    [Fact]
    public async Task Duplicate_event_is_not_processed_twice()
    {
        using var app = new TestApp();
        var (tutor, session) = await StartAsync(app);
        var first = await tutor.SendMessageAsync(TestApp.Learner, session.Id, "e1", "Comincio di studiare.", default);
        var again = await tutor.SendMessageAsync(TestApp.Learner, session.Id, "e1", "Comincio di studiare.", default);

        Assert.True(again.Duplicate);
        Assert.Equal(first.Replies[0].Id, again.Replies[0].Id);
        Assert.Equal(3, again.Session.Messages.Count);
        Assert.Single(await app.Store.Observations.ListAsync(TestApp.Learner.UserId, default));
    }

    [Fact]
    public async Task Practice_request_creates_session_through_tool_idempotently()
    {
        using var app = new TestApp();
        var (tutor, session) = await StartAsync(app);
        var reply = await tutor.SendMessageAsync(TestApp.Learner, session.Id, "e1", "Ho finito a lavorare. Vorrei fare esercizi!", default);

        var call = reply.Replies[0].ToolCalls.Single(c => c.Name == "create_practice_session");
        Assert.Equal(ToolCallStatus.Succeeded, call.Status);
        var practice = Assert.Single(await app.Store.PracticeSessions.ListAsync(TestApp.Learner.UserId, default));
        Assert.Equal(PracticeOriginKind.TutorTool, practice.Origin.Kind);
        Assert.Contains("it-verb-di-infinitive", practice.TargetTopicIds);
    }

    private static async Task<(IToolInvoker Invoker, TestApp App)> InvokerAsync(string learnerText = "Ho finito a lavorare.")
    {
        var app = new TestApp();
        var (tutor, session) = await StartAsync(app);
        session = (await tutor.SendMessageAsync(TestApp.Learner, session.Id, "e1", learnerText, default)).Session;
        var learnerMessage = session.Messages.First(m => m.Role == MessageRole.Learner);
        var invoker = app.Get<ToolDispatcher>().BeginTurn(new ToolExecutionContext(TestApp.Learner, session, learnerMessage, app.Get<ITutorAgentRuntime>().Info));
        return (invoker, app);
    }

    [Theory]
    [InlineData("get_recent_mistakes", """{"limit":0}""", "invalid-arguments")]
    [InlineData("get_recent_mistakes", """{"limit":5,"extra":true}""", "invalid-arguments")]
    [InlineData("get_recent_mistakes", """{"limit":5,"userId":"other-learner"}""", "user-id-not-allowed")]
    [InlineData("get_learning_profile", """{"userId":"other-learner"}""", "user-id-not-allowed")]
    [InlineData("delete_everything", "{}", "unknown-tool")]
    [InlineData("search_grammar_rules", "{not json", "invalid-arguments")]
    public async Task Dispatcher_rejects_invalid_calls(string tool, string args, string code)
    {
        var (invoker, app) = await InvokerAsync();
        using var _ = app;
        var result = await invoker.InvokeAsync("c1", tool, args, default);
        Assert.Equal(ToolCallStatus.Rejected, result.Status);
        Assert.Equal(code, result.ErrorCode);
        Assert.Contains("\"error\"", result.OutputJson);
    }

    [Fact]
    public async Task Tool_failures_are_returned_as_data()
    {
        var (invoker, app) = await InvokerAsync();
        using var _ = app;
        var fabricated = await invoker.InvokeAsync("c1", "record_learning_observation",
            """{"messageId":"msg-2","grammarTopicId":"it-verb-di-infinitive","observationType":"grammar-error","evidenceText":"something the learner never wrote","correction":null,"confidence":0.9}""", default);
        Assert.Equal(ToolCallStatus.Failed, fabricated.Status);
        Assert.Equal("evidence-not-in-message", fabricated.ErrorCode);

        var tutorMessage = await invoker.InvokeAsync("c2", "record_learning_observation",
            """{"messageId":"msg-1","grammarTopicId":"it-verb-di-infinitive","observationType":"correct-use","evidenceText":"Ciao","correction":null,"confidence":0.9}""", default);
        Assert.Equal("not-learner-message", tutorMessage.ErrorCode);

        var unknownTopic = await invoker.InvokeAsync("c3", "search_grammar_rules", """{"query":"x","topicId":"nope","level":null,"limit":1}""", default);
        Assert.Equal(ToolCallStatus.Failed, unknownTopic.Status);
        Assert.Equal("unknown-topic", unknownTopic.ErrorCode);
    }

    [Fact]
    public async Task Tool_budget_limits_calls_per_turn()
    {
        var (invoker, app) = await InvokerAsync();
        using var _ = app;
        var budget = new LineLanguageLearn.Learning.Infrastructure.AgentLimitOptions().MaxToolCallsPerTurn;
        ToolInvocationResult? last = null;
        for (var i = 0; i <= budget; i++) last = await invoker.InvokeAsync($"c{i}", "get_learning_profile", "{}", default);
        Assert.Equal("tool-budget-exceeded", last!.ErrorCode);
        Assert.Equal(budget + 1, invoker.Records.Count);
    }

    [Fact]
    public async Task Recording_is_idempotent_and_scoped_to_the_authenticated_learner()
    {
        var (invoker, app) = await InvokerAsync();
        using var _ = app;
        var args = """{"messageId":"msg-2","grammarTopicId":"it-verb-di-infinitive","observationType":"possible-misunderstanding","evidenceText":"finito a lavorare","correction":"finito di lavorare","confidence":0.8}""";
        var first = await invoker.InvokeAsync("c1", "record_learning_observation", args, default);
        var second = await invoker.InvokeAsync("c2", "record_learning_observation", args, default);
        Assert.Contains("\"created\":true", first.OutputJson);
        Assert.Contains("\"created\":false", second.OutputJson);

        var mistakes = await invoker.InvokeAsync("c3", "get_recent_mistakes", """{"limit":10}""", default);
        using var doc = JsonDocument.Parse(mistakes.OutputJson);
        var stored = await app.Store.Observations.ListAsync(TestApp.Learner.UserId, default);
        Assert.Equal(2, stored.Count); // the agent's grammar-error + this possible-misunderstanding
        Assert.Equal(stored.Count, doc.RootElement.GetProperty("mistakes").GetArrayLength());
        Assert.Empty(await app.Store.Observations.ListAsync(TestApp.OtherLearner.UserId, default));
    }

    [Fact]
    public async Task Tool_definitions_are_generated_from_shared_contracts()
    {
        using var app = new TestApp();
        var registry = app.Get<ToolRegistry>();
        Assert.Equal(
            ["create_practice_session", "get_learning_profile", "get_recent_mistakes", "get_review_plan", "record_learning_observation", "search_grammar_rules"],
            registry.Definitions.Select(d => d.Name));
        Assert.All(registry.Definitions, d =>
        {
            Assert.Equal("object", d.Parameters["type"]!.GetValue<string>());
            Assert.False(d.Parameters["properties"]!.AsObject().ContainsKey("userId"));
        });
    }
}
