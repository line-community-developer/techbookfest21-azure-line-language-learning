using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Azure.Core;
using LineLanguageLearn.Learning.Ai;
using LineLanguageLearn.Learning.Ai.Foundry;
using LineLanguageLearn.Learning.Contracts;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Knowledge;
using LineLanguageLearn.Learning.Tutor;
using LineLanguageLearn.Learning.Tutor.Mcp;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LineLanguageLearn.Tests;

/// <summary>Records requests and replays canned Responses API payloads (no network).</summary>
public sealed class FakeResponsesHandler(params string[] responses) : HttpMessageHandler
{
    private int _index;
    public List<(Uri Uri, JsonObject Body, HttpRequestMessage Request)> Requests { get; } = [];
    public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;
    public Func<JsonObject, CancellationToken, Task>? BeforeRespond { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
        Requests.Add((request.RequestUri!, body, request));
        if (BeforeRespond is not null) await BeforeRespond(body, cancellationToken);
        var payload = responses[Math.Min(_index++, responses.Length - 1)];
        return new HttpResponseMessage(StatusCode) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
    }
}

public sealed class NoCredential : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => throw new InvalidOperationException("API key auth expected in tests.");
    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) => throw new InvalidOperationException("API key auth expected in tests.");
}

public class FoundryAdapterTests
{
    [Fact]
    public async Task Demo_azure_openai_endpoint_uses_entra_identity_without_an_api_key()
    {
        var handler = new FakeResponsesHandler("""{"id":"resp_demo","output":[]}""");
        var credential = new RecordingCredential();
        using var http = new HttpClient(handler);
        var settings = new LearningOptions
        {
            AiProvider = AiProviderMode.Foundry,
            TutorAgent = TutorAgentMode.Unavailable,
            Foundry = new FoundryOptions
            {
                Endpoint = "https://demo.openai.azure.com/",
            },
        };
        var client = new FoundryResponsesClient(http, credential, Options.Create(settings));

        await client.CreateResponseAsync(new JsonObject { ["model"] = settings.Foundry.Model, ["input"] = "Ciao" }, default);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://demo.openai.azure.com/openai/v1/responses", request.Uri.ToString());
        Assert.Equal("learning", request.Body["model"]!.GetValue<string>());
        Assert.Equal(["https://ai.azure.com/.default"], credential.Scopes);
        Assert.Equal("Bearer", request.Request.Headers.Authorization!.Scheme);
        Assert.Equal("test-identity-token", request.Request.Headers.Authorization.Parameter);
        Assert.False(request.Request.Headers.Contains("api-key"));
    }

    private sealed class RecordingCredential : TokenCredential
    {
        public string[] Scopes { get; private set; } = [];
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Scopes = requestContext.Scopes;
            return new AccessToken("test-identity-token", DateTimeOffset.UtcNow.AddMinutes(5));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    internal static TestApp FoundryApp(FakeResponsesHandler handler) => new(
        services =>
        {
            services.AddSingleton(new HttpClient(handler));
            services.AddSingleton<TokenCredential>(new RecordingCredential());
        },
        o =>
        {
            o.AiProvider = AiProviderMode.Foundry;
            o.TutorAgent = TutorAgentMode.FoundryAgentService;
            o.Foundry.Endpoint = "https://example.openai.azure.com";
            o.Foundry.ProjectEndpoint = "https://example.services.ai.azure.com/api/projects/demo";
            o.Foundry.AgentName = "learning-tutor";
            o.Foundry.AgentVersion = "7";
            o.Foundry.ApiKey = "test-key";
            o.Foundry.Model = "gpt-test";
        });

    [Fact]
    public void Cloud_modes_without_settings_fail_fast_instead_of_falling_back_to_mocks()
    {
        Assert.Throws<OptionsValidationException>(() => LearningServiceCollectionExtensions.Validate(new LearningOptions { Storage = StorageMode.InMemory, AiProvider = AiProviderMode.Foundry }));
        Assert.Throws<OptionsValidationException>(() => LearningServiceCollectionExtensions.Validate(new LearningOptions { Storage = StorageMode.InMemory, Retrieval = RetrievalMode.FoundryIq }));
        LearningServiceCollectionExtensions.Validate(new LearningOptions { Storage = StorageMode.InMemory });
    }

    [Theory]
    [InlineData(null, "7")]
    [InlineData("learning-tutor", null)]
    [InlineData("learning-tutor", "latest")]
    public void Saved_agent_name_and_pinned_version_are_required(string? name, string? version)
    {
        Assert.Throws<OptionsValidationException>(() => LearningServiceCollectionExtensions.Validate(new LearningOptions
        {
            Storage = StorageMode.InMemory,
            TutorAgent = TutorAgentMode.FoundryAgentService,
            Foundry = new FoundryOptions
            {
                ProjectEndpoint = "https://example.services.ai.azure.com/api/projects/demo",
                AgentName = name,
                AgentVersion = version,
            },
        }));
    }

    [Fact]
    public async Task Saved_definition_and_runtime_use_the_same_structured_input_contract()
    {
        using var app = new TestApp();
        var session = await app.Get<TutorService>().StartSessionAsync(TestApp.Learner, null, TutorChannel.LocalSimulator, default);
        var profile = await app.Get<LineLanguageLearn.Learning.Practice.LearningProfileService>().GetOrCreateAsync(TestApp.Learner, default);
        var tutor = app.Get<TutorProfileCatalog>().Get(session.TutorProfileId);
        var context = new TutorTurnContext(TestApp.Learner, tutor, session, session.Messages[0], profile, "Not sent");
        var definition = JsonNode.Parse(File.ReadAllText(TestApp.SourcePath("docs", "foundry-tutor-agent.json")))!["definition"]!;
        var instructions = PromptTemplates.Load(tutor.PromptTemplateId);
        var request = FoundryAgentTutorRuntime.BuildRequest(context, "reference", new LearningOptions());
        Assert.Equal(definition["structured_inputs"]!.AsObject().Select(p => p.Key).Order(),
            request["structured_inputs"]!.AsObject().Select(p => p.Key).Order());
        var templateNames = System.Text.RegularExpressions.Regex.Matches(instructions, @"\{\{(\w+)\}\}")
            .Select(m => m.Groups[1].Value).Append("turnReference").Distinct().Order();
        Assert.Equal(templateNames, definition["structured_inputs"]!.AsObject().Select(p => p.Key).Order());
        Assert.DoesNotContain("turnReference", instructions);
        Assert.Null(definition["instructions"]); // The setup procedure loads the canonical prompt, not a second copy.
        var tool = Assert.Single(definition["tools"]!.AsArray())!;
        Assert.Equal(TutorMcpFunctions.ToolNames.Order(), tool["allowed_tools"]!.AsArray().Select(n => n!.GetValue<string>()).Order());
        Assert.Equal("{{turnReference}}", tool["headers"]![McpTurnService.ContextHeader]!.GetValue<string>());
        Assert.Equal("never", tool["require_approval"]!.GetValue<string>());
        Assert.False(tool["headers"]!.AsObject().ContainsKey("x-functions-key"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Agent_service_errors_do_not_retry_direct_model_inference(HttpStatusCode status)
    {
        var handler = new FakeResponsesHandler("{}") { StatusCode = status };
        var credential = new RecordingCredential();
        using var http = new HttpClient(handler);
        var client = new FoundryAgentClient(http, credential, Options.Create(new LearningOptions
        {
            Foundry = new FoundryOptions { ProjectEndpoint = "https://example.services.ai.azure.com/api/projects/demo", AgentName = "learning-tutor", ApiKey = "must-not-be-used" },
        }));
        var error = await Assert.ThrowsAsync<DomainException>(() => client.CreateResponseAsync(new JsonObject(), default));
        Assert.Equal("agent-call-failed", error.Code);
        Assert.Contains(((int)status).ToString(), error.Message);
        Assert.False(Assert.Single(handler.Requests).Request.Headers.Contains("api-key"));
        Assert.Equal(["https://ai.azure.com/.default"], credential.Scopes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://demo.openai.azure.com")]
    [InlineData("http://tools.example.com/runtime/webhooks/mcp")]
    [InlineData("https://example.services.ai.azure.com/api/projects/demo?code=secret")]
    [InlineData("https://user:password@example.services.ai.azure.com/api/projects/demo")]
    public void Saved_agent_requires_an_https_project_endpoint(string? endpoint)
    {
        Assert.Throws<OptionsValidationException>(() => LearningServiceCollectionExtensions.Validate(new LearningOptions
        {
            Storage = StorageMode.InMemory,
            TutorAgent = TutorAgentMode.FoundryAgentService,
            Foundry = new FoundryOptions
            {
                ProjectEndpoint = endpoint,
                AgentName = "learning-tutor",
                AgentVersion = "7",
            },
        }));
    }

    [Fact]
    public async Task Saved_agent_uses_a_pinned_reference_and_structured_inputs_and_keeps_remote_audit_records()
    {
        var handler = new FakeResponsesHandler(
            """{"id":"resp_mcp","status":"completed","output":[{"type":"mcp_call","name":"get_recent_mistakes","error":null},{"type":"message","content":[{"type":"output_text","text":"Ciao!"}]}]}""");
        using var app = FoundryApp(handler);
        string? token = null;
        handler.BeforeRespond = async (body, ct) =>
        {
            token = body["structured_inputs"]!["turnReference"]!.GetValue<string>();
            var result = await app.Get<McpTurnService>().InvokeAsync(token, "get_recent_mistakes", JsonSerializer.SerializeToElement(new { limit = 3 }), ct);
            Assert.Contains("mistakes", result);
        };
        var tutor = app.Get<TutorService>();
        var session = await tutor.StartSessionAsync(TestApp.Learner, null, TutorChannel.LocalSimulator, default);
        var reply = await tutor.SendMessageAsync(TestApp.Learner, session.Id, "mcp-event", "Ciao!", default);

        Assert.Equal("Ciao!", Assert.Single(reply.Replies).Text);
        Assert.Equal("agent:learning-tutor:7", session.Agent.Model);
        Assert.Equal(ToolCallStatus.Succeeded, Assert.Single(reply.Replies[0].ToolCalls).Status);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://example.services.ai.azure.com/api/projects/demo/agents/learning-tutor/endpoint/protocols/openai/responses?api-version=v1", request.Uri.ToString());
        Assert.Null(request.Body["model"]);
        Assert.Null(request.Body["instructions"]);
        Assert.Null(request.Body["tools"]);
        Assert.Equal("learning-tutor", request.Body["agent_reference"]!["name"]!.GetValue<string>());
        Assert.Equal("7", request.Body["agent_reference"]!["version"]!.GetValue<string>());
        Assert.Equal("agent_reference", request.Body["agent_reference"]!["type"]!.GetValue<string>());
        Assert.Contains("Giulia", request.Body["structured_inputs"]!["tutorName"]!.GetValue<string>());
        Assert.DoesNotContain(token!, request.Body["input"]!.ToJsonString());
        Assert.Contains("[messageId=msg-2]", request.Body["input"]!.ToJsonString());
        Assert.False(request.Body["store"]!.GetValue<bool>());
        Assert.False(request.Body["parallel_tool_calls"]!.GetValue<bool>());
        Assert.Equal("Bearer", request.Request.Headers.Authorization!.Scheme);
        Assert.False(request.Request.Headers.Contains("api-key"));
        Assert.DoesNotContain("test-key", request.Body.ToJsonString());
        var error = await Assert.ThrowsAsync<LineLanguageLearn.Learning.Tutor.Tools.ToolException>(() =>
            app.Get<McpTurnService>().InvokeAsync(token, "get_review_plan", JsonSerializer.SerializeToElement(new { }), default));
        Assert.Equal("invalid-tool-context", error.Code);
    }

    [Fact]
    public async Task Failed_model_request_revokes_the_mcp_context_without_fallback()
    {
        var handler = new FakeResponsesHandler("{}") { StatusCode = HttpStatusCode.NotFound };
        using var app = FoundryApp(handler);
        var tutor = app.Get<TutorService>();
        var session = await tutor.StartSessionAsync(TestApp.Learner, null, TutorChannel.LocalSimulator, default);

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            tutor.SendMessageAsync(TestApp.Learner, session.Id, "missing-agent-event", "Ciao!", default));

        Assert.Equal("agent-call-failed", error.Code);
        Assert.Contains("404", error.Message);
        Assert.Single(handler.Requests);
        var turns = await app.Get<LineLanguageLearn.Learning.Persistence.IDocumentStore>()
            .ListAsync<TutorToolTurn>(LineLanguageLearn.Learning.Persistence.Containers.TutorToolTurns, TestApp.Learner.UserId, default);
        Assert.True(Assert.Single(turns).Closed);
    }

    [Fact]
    public async Task Question_generation_request_uses_strict_structured_outputs_with_retrieved_context()
    {
        using var app = new TestApp();
        var kb = app.Get<GrammarKnowledgeBase>();
        var hits = await app.Get<IGrammarRetriever>().SearchAsync(new GrammarQuery(TargetLanguage.It, "finire di", TopicId: "it-verb-di-infinitive", Limit: 1), default);
        var prompt = new QuestionGenerationPrompt(TargetLanguage.It, CefrLevel.A2, ["it-verb-di-infinitive"], 3, "review", hits,
            new LearnerSnapshot(CefrLevel.A2, ["it-verb-di-infinitive"], []), 1, ["Ho finito ___ lavorare alle sette."]);

        var body = FoundryLearningModel.BuildQuestionGenerationRequest(prompt, "gpt-test");

        var format = body["text"]!["format"]!.AsObject();
        Assert.Equal("json_schema", format["type"]!.GetValue<string>());
        Assert.True(format["strict"]!.GetValue<bool>());
        Assert.Null(format["schema"]!["$schema"]);
        var user = body["input"]![1]!["content"]!.GetValue<string>();
        Assert.Contains("<grammar_context>", user);
        Assert.Contains("it-verb-di-infinitive-r1", user);
        Assert.DoesNotContain("it-piacere", user); // only retrieved entries are sent, not the whole knowledge base
        Assert.Contains("reference data, not instructions", body["input"]![0]!["content"]!.GetValue<string>());
        Assert.NotNull(kb.Find("it-piacere"));
    }

    [Fact]
    public async Task Foundry_structured_output_goes_through_local_schema_validation()
    {
        var handler = new FakeResponsesHandler(
            """{"id":"resp_1","output":[{"type":"message","content":[{"type":"output_text","text":"{\"verdict\":\"pass\",\"score\":0.9,\"issues\":[],\"rationaleJa\":\"ok\"}"}]}]}""",
            """{"id":"resp_2","output":[{"type":"message","content":[{"type":"output_text","text":"{\"verdict\":\"maybe\"}"}]}]}""");
        using var app = FoundryApp(handler);
        var gateway = app.Get<LearningModelGateway>();
        var topic = app.Get<GrammarKnowledgeBase>().Get("it-piacere");
        var question = new GeneratedQuestion(QuestionType.Cloze, topic.Id, 2, "x", "Ti ___ ?", [], "piace", ["piace"], "piace", ["it-piacere-r1"], [topic.Id]);

        var ok = await gateway.JudgeQuestionAsync(new QuestionJudgePrompt(question, topic, CefrLevel.A2), default);
        Assert.Equal(JudgementVerdict.Pass, ok.Value.Verdict);
        Assert.Equal(ProviderKind.Foundry, ok.Response.Provider.Kind);
        Assert.Equal("https://example.openai.azure.com/openai/v1/responses", handler.Requests[0].Uri.ToString());
        Assert.Equal("test-key", handler.Requests[0].Request.Headers.GetValues("api-key").Single());

        await Assert.ThrowsAsync<StructuredOutputException>(() => gateway.JudgeQuestionAsync(new QuestionJudgePrompt(question, topic, CefrLevel.A2), default));
    }

    [Fact]
    public async Task Mcp_protocol_errors_are_not_reported_as_successful_replies()
    {
        var handler = new FakeResponsesHandler(
            """{"id":"resp_1","output":[{"type":"mcp_call","error":{"code":"connection_error"}},{"type":"message","content":[{"type":"output_text","text":"Invented success"}]}]}""");
        using var app = FoundryApp(handler);
        var tutor = app.Get<TutorService>();
        var session = await tutor.StartSessionAsync(TestApp.Learner, null, TutorChannel.LocalSimulator, default);

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            tutor.SendMessageAsync(TestApp.Learner, session.Id, "e1", "Ciao Giulia!", default));
        Assert.Equal("tutor-mcp-failed", error.Code);
    }

    [Fact]
    public async Task Mcp_runtime_rejects_unexpected_client_function_calls()
    {
        var loop = """{"id":"resp_x","output":[{"type":"function_call","call_id":"c","name":"get_review_plan","arguments":"{}"}]}""";
        using var app = FoundryApp(new FakeResponsesHandler(loop));
        var tutor = app.Get<TutorService>();
        var session = await tutor.StartSessionAsync(TestApp.Learner, null, TutorChannel.LocalSimulator, default);

        var ex = await Assert.ThrowsAsync<DomainException>(() => tutor.SendMessageAsync(TestApp.Learner, session.Id, "e1", "Ciao", default));
        Assert.Equal("tutor-mcp-failed", ex.Code);
    }

    [Fact]
    public void Refusals_are_surfaced_as_errors()
    {
        var response = JsonNode.Parse("""{"output":[{"type":"message","content":[{"type":"refusal","refusal":"no"}]}]}""")!.AsObject();
        Assert.Throws<DomainException>(() => FoundryResponsesClient.ExtractOutputText(response));
    }

    [Theory]
    [InlineData("""{"status":"incomplete","output":[]}""", "tutor-mcp-failed")]
    [InlineData("""{"status":"in_progress","output":[]}""", "tutor-mcp-failed")]
    [InlineData("""{"output":[{"type":"mcp_approval_request"}]}""", "tutor-mcp-failed")]
    [InlineData("""{"output":[{"type":"mcp_list_tools","error":"not reachable"}]}""", "tutor-mcp-failed")]
    [InlineData("""{"output":[{"type":"mcp_call","name":"get_review_plan","error":null},{"type":"message","content":[{"type":"output_text","text":"Invented success"}]}]}""", "tutor-mcp-audit-missing")]
    public async Task Incomplete_approval_or_unaudited_remote_calls_fail_explicitly(string response, string code)
    {
        var handler = new FakeResponsesHandler(response);
        using var app = FoundryApp(handler);
        var tutor = app.Get<TutorService>();
        var session = await tutor.StartSessionAsync(TestApp.Learner, null, TutorChannel.LocalSimulator, default);
        var error = await Assert.ThrowsAsync<DomainException>(() =>
            tutor.SendMessageAsync(TestApp.Learner, session.Id, "event", "Ciao", default));
        Assert.Equal(code, error.Code);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Cancellation_revokes_the_remote_context()
    {
        var handler = new FakeResponsesHandler("{}") { BeforeRespond = (_, _) => throw new OperationCanceledException() };
        using var app = FoundryApp(handler);
        var tutor = app.Get<TutorService>();
        var session = await tutor.StartSessionAsync(TestApp.Learner, null, TutorChannel.LocalSimulator, default);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            tutor.SendMessageAsync(TestApp.Learner, session.Id, "event", "Ciao", default));
        var turns = await app.Get<LineLanguageLearn.Learning.Persistence.IDocumentStore>()
            .ListAsync<TutorToolTurn>(LineLanguageLearn.Learning.Persistence.Containers.TutorToolTurns, TestApp.Learner.UserId, default);
        Assert.True(Assert.Single(turns).Closed);
    }

    [Fact]
    public void Contract_schemas_exist_for_every_tool_and_ai_output()
    {
        foreach (var name in new[] { SchemaNames.GeneratedQuestionBatch, SchemaNames.ConversationAnalysis, SchemaNames.QuestionJudgement })
        {
            Assert.NotNull(ContractSchemas.Get(name));
        }
        foreach (var tool in new[] { "get_learning_profile", "get_recent_mistakes", "search_grammar_rules", "record_learning_observation", "create_practice_session", "get_review_plan" })
        {
            Assert.NotNull(ContractSchemas.Get(SchemaNames.ToolInput(tool)));
            Assert.NotNull(ContractSchemas.Get(SchemaNames.ToolOutput(tool)));
        }
    }
}
