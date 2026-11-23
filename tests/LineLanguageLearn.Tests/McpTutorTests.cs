using System.Text.Json;
using System.Text.Json.Nodes;
using LineLanguageLearn.Learning.Contracts;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Identity;
using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Persistence;
using LineLanguageLearn.Learning.Practice;
using LineLanguageLearn.Learning.Tutor;
using LineLanguageLearn.Learning.Tutor.Mcp;
using LineLanguageLearn.Learning.Tutor.Tools;
using LineLanguageLearn.SeedData;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LineLanguageLearn.Tests;

public class McpTutorTests
{
    private sealed class BlockingTool(ITutorTool inner) : ITutorTool
    {
        public string Name => inner.Name;
        public string Description => inner.Description;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<object> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return await inner.ExecuteAsync(arguments, context, ct);
        }
    }

    private static JsonElement Args(string json = "{}") => JsonSerializer.Deserialize<JsonElement>(json);

    private static async Task<TutorTurnContext> ContextAsync(TestApp app, UserContext? user = null)
    {
        user ??= TestApp.Learner;
        var session = await app.Get<TutorService>().StartSessionAsync(user, "MCP", TutorChannel.LocalSimulator, default);
        var message = new TutorMessage
        {
            Id = "msg-2", SessionId = session.Id, Role = MessageRole.Learner, Text = "Ho finito a lavorare.",
        };
        session.Messages.Add(message);
        await app.Store.TutorSessions.UpsertAsync(session, default);
        var profile = await app.Get<LearningProfileService>().GetOrCreateAsync(user, default);
        return new TutorTurnContext(user, app.Get<TutorProfileCatalog>().Get(session.TutorProfileId),
            session, message, profile, "Tutor instructions");
    }

    private static McpTurnService AnotherInstance(TestApp app) => new(
        app.Get<IDocumentStore>(), app.Store, app.Get<ToolDispatcher>(), app.Get<IOptions<LearningOptions>>(),
        app.Get<TimeProvider>(), NullLogger<McpTurnService>.Instance);

    [Fact]
    public void Mcp_host_requires_independent_system_key_authentication()
    {
        var host = JsonNode.Parse(File.ReadAllText(TestApp.SourcePath("functions", "host.json")))!;
        Assert.Equal("System", host["extensions"]!["mcp"]!["system"]!["webhookAuthorizationLevel"]!.GetValue<string>());
    }

    [Fact]
    public async Task Parallel_turns_resolve_their_own_learner_and_session_on_separate_instances()
    {
        using var app = new TestApp();
        var first = await app.Get<McpTurnService>().BeginAsync(await ContextAsync(app), default);
        var second = await app.Get<McpTurnService>().BeginAsync(await ContextAsync(app, TestApp.OtherLearner), default);
        var third = await app.Get<McpTurnService>().BeginAsync(await ContextAsync(app), default);
        Assert.Equal(3, new[] { first.Reference, second.Reference, third.Reference }.Distinct().Count());
        var results = await Task.WhenAll(new[] { first, second, third }.Select(lease =>
            AnotherInstance(app).InvokeAsync(lease.Reference, "get_learning_profile", Args(), default)));
        Assert.Contains("Test Learner", results[0]);
        Assert.DoesNotContain("Other Learner", results[0]);
        Assert.Contains("Other Learner", results[1]);
        Assert.DoesNotContain("Test Learner", results[1]);
        Assert.Contains("Test Learner", results[2]);
        var turns = await Task.WhenAll(new[] { first, second, third }.Select(lease => AnotherInstance(app).CloseAsync(lease, default)));
        Assert.Equal(3, turns.Select(turn => turn.SessionId).Distinct().Count());
        Assert.All(turns, turn => Assert.Single(turn.Calls));
    }

    [Fact]
    public async Task Ambiguous_reference_across_partitions_is_denied_without_executing_a_tool()
    {
        using var app = new TestApp();
        var service = app.Get<McpTurnService>();
        var lease = await service.BeginAsync(await ContextAsync(app), default);
        var documents = app.Get<IDocumentStore>();
        var copy = (await documents.GetAsync<TutorToolTurn>(Containers.TutorToolTurns, lease.UserId, lease.Id, default))!;
        copy.UserId = TestApp.OtherLearner.UserId;
        await documents.UpsertAsync(Containers.TutorToolTurns, copy.UserId, copy.Id, copy, default);
        var error = await Assert.ThrowsAsync<ToolException>(() =>
            AnotherInstance(app).InvokeAsync(lease.Reference, "get_learning_profile", Args(), default));
        Assert.Equal("invalid-tool-context", error.Code);
        Assert.Empty((await service.CloseAsync(lease, default)).Calls);
    }

    [Fact]
    public void Official_mcp_metadata_uses_exact_shared_input_schemas_and_descriptions()
    {
        using var host = new HostBuilder().ConfigureFunctionsWorkerDefaults(TutorMcpFunctions.Configure).Build();
        using var app = new TestApp();
        var definitions = app.Get<ToolRegistry>().Definitions;
        var triggers = typeof(TutorMcpFunctions).GetMethods()
            .SelectMany(m => m.GetParameters())
            .SelectMany(p => p.GetCustomAttributes(typeof(McpToolTriggerAttribute), false).Cast<McpToolTriggerAttribute>())
            .OrderBy(t => t.ToolName).ToList();
        Assert.Equal(definitions.Select(d => d.Name), triggers.Select(t => t.ToolName));
        foreach (var trigger in triggers)
        {
            Assert.Equal(definitions.Single(d => d.Name == trigger.ToolName).Description, trigger.Description);
            var schema = host.Services.GetRequiredService<IOptionsMonitor<ToolOptions>>().Get(trigger.ToolName).InputSchema!;
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(ContractSchemas.GetRaw(SchemaNames.ToolInput(trigger.ToolName))), JsonNode.Parse(schema.Json)));
        }
    }

    [Fact]
    public async Task Reference_works_across_instances_is_scoped_and_contains_no_learner_identity()
    {
        using var app = new TestApp();
        var context = await ContextAsync(app);
        var lease = await app.Get<McpTurnService>().BeginAsync(context, default);
        var result = await AnotherInstance(app).InvokeAsync(lease.Reference, "get_learning_profile", Args(), default);
        Assert.Contains("Test Learner", result);
        var stored = await app.Get<IDocumentStore>().GetAsync<TutorToolTurn>(Containers.TutorToolTurns, lease.UserId, lease.Id, default);
        Assert.NotNull(stored);
        Assert.Matches("^[0-9A-F]{64}$", lease.Reference);
        Assert.DoesNotContain(context.User.UserId, lease.Reference);
        Assert.DoesNotContain(lease.Reference, LearningJson.Serialize(stored));
        Assert.Equal(context.Session.Id, stored.SessionId);
        Assert.Equal("msg-2", stored.LearnerMessageId);
        Assert.Equal(ToolCallStatus.Succeeded, Assert.Single(stored.Calls).Record!.Status);
        ContractSchemas.EnsureValid("model.TutorToolTurn", LearningJson.ToElement(stored));
        Assert.Null(await app.Get<IDocumentStore>().GetAsync<TutorToolTurn>(Containers.TutorToolTurns, TestApp.OtherLearner.UserId, lease.Id, default));
    }

    [Fact]
    public async Task Unknown_and_tampered_references_cannot_select_another_learner()
    {
        using var app = new TestApp();
        var lease = await app.Get<McpTurnService>().BeginAsync(await ContextAsync(app), default);
        foreach (var token in new[] { new string('A', 64), lease.Reference[..^1] + (lease.Reference[^1] == '0' ? "1" : "0") })
        {
            var ex = await Assert.ThrowsAsync<ToolException>(() => AnotherInstance(app).InvokeAsync(token, "get_review_plan", Args(), default));
            Assert.Equal("invalid-tool-context", ex.Code);
        }
        Assert.Empty((await app.Get<McpTurnService>().CloseAsync(lease, default)).Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("plain-learner-id")]
    [InlineData("ZZ.0000000000000000000000000000000000000000000000000000000000000000")]
    public async Task Missing_or_malformed_context_is_denied(string? token)
    {
        using var app = new TestApp();
        var ex = await Assert.ThrowsAsync<ToolException>(() => app.Get<McpTurnService>().InvokeAsync(token, "get_review_plan", Args(), default));
        Assert.Equal("invalid-tool-context", ex.Code);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Expired_and_closed_contexts_cannot_be_reused(bool expire)
    {
        using var app = new TestApp();
        var service = app.Get<McpTurnService>();
        var lease = await service.BeginAsync(await ContextAsync(app), default);
        if (expire)
        {
            var documents = app.Get<IDocumentStore>();
            var stored = (await documents.GetAsync<TutorToolTurn>(Containers.TutorToolTurns, lease.UserId, lease.Id, default))!;
            stored.ExpiresAt = DateTimeOffset.MinValue;
            await documents.UpsertAsync(Containers.TutorToolTurns, lease.UserId, lease.Id, stored, default);
        }
        else await service.CloseAsync(lease, default);
        var ex = await Assert.ThrowsAsync<ToolException>(() => AnotherInstance(app).InvokeAsync(lease.Reference, "get_review_plan", Args(), default));
        Assert.Equal("invalid-tool-context", ex.Code);
    }

    [Fact]
    public async Task All_instances_share_the_same_call_budget_including_rejected_arguments()
    {
        using var app = new TestApp(configure: o => o.Agent.MaxToolCallsPerTurn = 2);
        var service = app.Get<McpTurnService>();
        var lease = await service.BeginAsync(await ContextAsync(app), default);
        await service.InvokeAsync(lease.Reference, "get_review_plan", Args(), default);
        var rejected = await AnotherInstance(app).InvokeAsync(lease.Reference, "get_learning_profile", Args("""{"userId":"other"}"""), default);
        Assert.Contains("user-id-not-allowed", rejected);
        var ex = await Assert.ThrowsAsync<ToolException>(() => AnotherInstance(app).InvokeAsync(lease.Reference, "get_review_plan", Args(), default));
        Assert.Equal("tool-budget-exceeded", ex.Code);
        var closed = await service.CloseAsync(lease, default);
        Assert.Equal(2, closed.Calls.Count);
        Assert.Equal(ToolCallStatus.Rejected, closed.Calls[1].Record!.Status);
    }

    [Fact]
    public async Task Conditional_writes_reject_a_stale_version()
    {
        using var app = new TestApp();
        var lease = await app.Get<McpTurnService>().BeginAsync(await ContextAsync(app), default);
        var documents = app.Get<IDocumentStore>();
        var first = (await documents.GetVersionedAsync<TutorToolTurn>(Containers.TutorToolTurns, lease.UserId, lease.Id, default))!;
        var stale = (await documents.GetVersionedAsync<TutorToolTurn>(Containers.TutorToolTurns, lease.UserId, lease.Id, default))!;
        first.Value.Closed = true;
        Assert.True(await documents.TryReplaceAsync(Containers.TutorToolTurns, lease.UserId, lease.Id, first.Value, first.Version, default));
        Assert.False(await documents.TryReplaceAsync(Containers.TutorToolTurns, lease.UserId, lease.Id, stale.Value, stale.Version, default));
    }

    [Fact]
    public async Task Simultaneous_calls_cannot_exceed_the_global_budget()
    {
        using var app = new TestApp(configure: o => o.Agent.MaxToolCallsPerTurn = 1);
        var service = app.Get<McpTurnService>();
        var lease = await service.BeginAsync(await ContextAsync(app), default);
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(async () =>
        {
            try { await AnotherInstance(app).InvokeAsync(lease.Reference, "get_review_plan", Args(), default); return "success"; }
            catch (ToolException ex) { return ex.Code; }
        })));
        Assert.Equal(1, outcomes.Count(result => result == "success"));
        Assert.All(outcomes.Where(result => result != "success"), result => Assert.Equal("tool-budget-exceeded", result));
        Assert.Single((await service.CloseAsync(lease, default)).Calls);
    }

    [Fact]
    public async Task Evidence_and_source_ids_survive_the_remote_boundary()
    {
        using var app = new TestApp();
        var service = app.Get<McpTurnService>();
        var lease = await service.BeginAsync(await ContextAsync(app), default);
        var invalid = await service.InvokeAsync(lease.Reference, "record_learning_observation",
            Args("""{"messageId":"msg-2","grammarTopicId":"it-verb-di-infinitive","observationType":"grammar-error","evidenceText":"invented quotation","correction":null,"confidence":0.9}"""), default);
        Assert.Contains("evidence-not-in-message", invalid);
        await service.InvokeAsync(lease.Reference, "search_grammar_rules",
            Args("""{"query":"finire di","topicId":"it-verb-di-infinitive","level":null,"limit":1}"""), default);
        var closed = await service.CloseAsync(lease, default);
        Assert.Equal("evidence-not-in-message", closed.Calls[0].Record!.ErrorCode);
        Assert.Contains("it-verb-di-infinitive", closed.Calls[1].SourceIds);
        Assert.Empty(await app.Store.Observations.ListAsync(TestApp.Learner.UserId, default));
    }

    [Fact]
    public async Task Inflight_call_blocks_parallel_execution_and_cancellation_preserves_a_failed_audit_record()
    {
        BlockingTool? blocker = null;
        using var app = new TestApp(services => services.AddSingleton<ToolRegistry>(sp =>
        {
            var tools = sp.GetServices<ITutorTool>().ToList();
            blocker = new BlockingTool(tools.Single(tool => tool.Name == "get_review_plan"));
            return new ToolRegistry(tools.Where(tool => tool.Name != blocker.Name).Append(blocker));
        }));
        var service = app.Get<McpTurnService>();
        var lease = await service.BeginAsync(await ContextAsync(app), default);
        using var cancel = new CancellationTokenSource();
        var first = service.InvokeAsync(lease.Reference, "get_review_plan", Args(), cancel.Token);
        await blocker!.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var error = await Assert.ThrowsAsync<ToolException>(() =>
            AnotherInstance(app).InvokeAsync(lease.Reference, "get_review_plan", Args(), default));
        Assert.Equal("tool-call-in-progress", error.Code);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        var call = Assert.Single((await service.CloseAsync(lease, default)).Calls);
        Assert.True(call.Completed);
        Assert.Equal(ToolCallStatus.Failed, call.Record!.Status);
        Assert.Equal("tool-interrupted", call.Record.ErrorCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Ended_session_or_nonlearner_message_invalidates_tool_execution(bool endSession)
    {
        using var app = new TestApp();
        var context = await ContextAsync(app);
        var lease = await app.Get<McpTurnService>().BeginAsync(context, default);
        if (endSession) context.Session.Status = TutorSessionStatus.Ended;
        else context.LearnerMessage.Role = MessageRole.Tutor;
        await app.Store.TutorSessions.UpsertAsync(context.Session, default);
        await Assert.ThrowsAsync<ToolException>(() => app.Get<McpTurnService>().InvokeAsync(lease.Reference, "get_review_plan", Args(), default));
        Assert.Equal(ToolCallStatus.Failed, Assert.Single((await app.Get<McpTurnService>().CloseAsync(lease, default)).Calls).Record!.Status);
    }

    [Fact]
    public async Task Mcp_trigger_loads_reference_data_on_a_cold_instance_before_resolving_tools()
    {
        using var app = new TestApp();
        var context = await ContextAsync(app);
        var lease = await app.Get<McpTurnService>().BeginAsync(context, default);
        var documents = app.Get<IDocumentStore>();
        var data = ReferenceDataFixtures.Load();
        foreach (var topic in data.Grammar.Topics)
            await documents.UpsertAsync(Containers.GrammarTopics, LearningJson.ToElement(topic.Language).GetString()!, topic.Id, topic, default);
        foreach (var tutor in data.Tutors.Tutors)
            await documents.UpsertAsync(Containers.TutorProfiles, LearningJson.ToElement(tutor.Language).GetString()!, tutor.Id, tutor, default);
        foreach (var question in data.Questions.Questions)
            await documents.UpsertAsync(Containers.SampleQuestions, LearningJson.ToElement(question.Language).GetString()!, question.Id, question, default);

        var services = new ServiceCollection();
        services.AddLearningServices(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Learning:Storage"] = "InMemory" }).Build());
        services.AddSingleton(documents);
        services.AddSingleton(app.Get<TimeProvider>());
        using var instance = services.BuildServiceProvider();
        var trigger = ActivatorUtilities.CreateInstance<TutorMcpFunctions>(instance);
        var invocation = new ToolInvocationContext
        {
            Name = "get_learning_profile",
            Arguments = [],
            Transport = new HttpTransport("http-streamable") { Headers = { [McpTurnService.ContextHeader] = lease.Reference } },
        };
        var result = await trigger.GetLearningProfileAsync(invocation, default);
        Assert.Contains("Test Learner", result);
    }
}
