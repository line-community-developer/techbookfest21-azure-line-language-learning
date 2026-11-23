using System.Text.Json;
using System.Text.Json.Nodes;
using LineLanguageLearn.Learning.Api;
using LineLanguageLearn.Learning.Contracts;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using LineLanguageLearn.SeedData;

namespace LineLanguageLearn.Tests;

public class PersistenceContractTests
{
    [Fact]
    public void Mastery_uses_typed_entries_and_reads_legacy_documents()
    {
        var profile = new LearningProfile { Id = "profile", UserId = "u", LearnerName = "Learner" };
        profile.MasteryByTopic["topic"] = new TopicMastery { TopicId = "topic", Score = 0.7 };
        var json = LearningJson.ToElement(profile);
        ContractSchemas.EnsureValid(SchemaNames.LearningProfile, json);
        Assert.Equal(JsonValueKind.Array, json.GetProperty("masteryByTopic").ValueKind);
        Assert.Equal("topic", json.GetProperty("masteryByTopic")[0].GetProperty("key").GetString());
        Assert.Equal(0.7, LearningJson.Deserialize<LearningProfile>(json).MasteryByTopic["topic"].Score);

        var legacy = JsonNode.Parse(json.GetRawText())!.AsObject();
        legacy["masteryByTopic"] = new JsonObject
        {
            ["topic"] = legacy["masteryByTopic"]![0]!["value"]!.DeepClone(),
        };
        var restored = LearningJson.Deserialize<LearningProfile>(legacy.ToJsonString());
        Assert.Equal(0.7, restored.MasteryByTopic["topic"].Score);
        ContractSchemas.EnsureValid(SchemaNames.LearningProfile, LearningJson.ToElement(restored));
    }

    [Fact]
    public void Workflow_outputs_use_typed_entries_and_read_legacy_documents()
    {
        var run = new WorkflowRun
        {
            Id = "workflow", UserId = "u", WorkflowName = "review", SubjectId = "session",
            Steps = [new WorkflowStepState { Name = "plan-review", Output = new() { ["reviewPlanId"] = "rp-1" } }],
        };
        var json = LearningJson.ToElement(run);
        ContractSchemas.EnsureValid(SchemaNames.WorkflowRun, json);
        Assert.Equal(JsonValueKind.Array, json.GetProperty("steps")[0].GetProperty("output").ValueKind);
        Assert.Equal("rp-1", LearningJson.Deserialize<WorkflowRun>(json).Steps[0].Output["reviewPlanId"]);

        var legacy = JsonNode.Parse(json.GetRawText())!.AsObject();
        legacy["steps"]![0]!["output"] = new JsonObject { ["reviewPlanId"] = "rp-1" };
        var restored = LearningJson.Deserialize<WorkflowRun>(legacy.ToJsonString());
        Assert.Equal("rp-1", restored.Steps[0].Output["reviewPlanId"]);
        ContractSchemas.EnsureValid(SchemaNames.WorkflowRun, LearningJson.ToElement(restored));
    }

    [Theory]
    [InlineData("""[{"key":"x","value":"1"},{"key":"x","value":"2"}]""")]
    [InlineData("""[{"key":1,"value":"1"}]""")]
    [InlineData("""[{"key":"x"}]""")]
    [InlineData("""[{"key":"x","value":null}]""")]
    [InlineData("""{"x":"1","x":"2"}""")]
    [InlineData("null")]
    public void Invalid_or_duplicate_entries_are_rejected(string output)
    {
        Assert.Throws<JsonException>(() => LearningJson.Deserialize<WorkflowStepState>($$"""{"name":"step","output":{{output}}}"""));
    }

    [Fact]
    public void Internal_idempotency_marker_matches_its_source_model()
    {
        ContractSchemas.EnsureValid("model.ProcessedEvent", LearningJson.ToElement(new ProcessedEvent
        {
            Id = "event", UserId = "u", Channel = "line", ExternalEventId = "external", ResultRef = "session",
        }));
    }

    [Theory]
    [InlineData("WEBSITE_SITE_NAME")]
    [InlineData("WEBSITE_INSTANCE_ID")]
    [InlineData(null)]
    public void Standard_cosmos_settings_work_without_any_learning_settings(string? deploymentMarker)
    {
        var settings = new Dictionary<string, string?>
        {
            ["COSMOS_DB_DATABASE_NAME"] = "testDatabase",
            ["CosmosDBConnection:accountEndpoint"] = "https://example.documents.azure.com",
        };
        if (deploymentMarker is not null) settings[deploymentMarker] = "test-functions";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLearningServices(configuration);
        services.AddReferenceDataFixtures();
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<LearningOptions>>().Value;
        Assert.Equal(StorageMode.Cosmos, options.Storage);
        Assert.IsType<CosmosDocumentStore>(provider.GetRequiredService<IDocumentStore>());
        Assert.Equal(deploymentMarker is null ? "local" : "cloud",
            provider.GetRequiredService<SystemStatusService>().Get(TestApp.Learner).Environment);
    }

    [Fact]
    public void Azure_rejects_in_memory_storage()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WEBSITE_SITE_NAME"] = "test-functions",
        }).Build();
        var options = new LearningOptions { Storage = StorageMode.InMemory };
        var error = Assert.Throws<OptionsValidationException>(() => LearningServiceCollectionExtensions.Validate(options, configuration));
        Assert.Contains(error.Failures, message => message.Contains("Cosmos DB storage"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Cosmos_requires_standard_database_setting_even_if_legacy_setting_exists(string? database)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["COSMOS_DB_DATABASE_NAME"] = database,
            ["Learning:Cosmos:DatabaseName"] = "legacy-database",
            ["CosmosDBConnection:accountEndpoint"] = "https://example.documents.azure.com",
        }).Build();
        var error = Assert.Throws<OptionsValidationException>(() =>
            new ServiceCollection().AddLearningServices(configuration));
        Assert.Contains(error.Failures, message => message.Contains("COSMOS_DB_DATABASE_NAME"));
    }

    [Fact]
    public void Missing_cosmos_connection_fails_instead_of_falling_back()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["COSMOS_DB_DATABASE_NAME"] = "testDatabase",
        }).Build();
        var error = Assert.Throws<OptionsValidationException>(() =>
            new ServiceCollection().AddLearningServices(configuration));
        Assert.Contains(error.Failures, message => message.Contains("CosmosDBConnection"));
    }

    [Theory]
    [InlineData("CosmosDBConnection__accountEndpoint")]
    [InlineData("CosmosDBConnection:accountEndpoint")]
    [InlineData("CosmosDBConnection")]
    public void Standard_cosmos_connection_forms_are_accepted(string connectionSetting)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["COSMOS_DB_DATABASE_NAME"] = "testDatabase",
            [connectionSetting] = "configured",
        }).Build();
        LearningServiceCollectionExtensions.Validate(new LearningOptions(), configuration);
    }

    [Fact]
    public void File_mode_is_no_longer_supported()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Learning:Storage"] = "File",
        }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddLearningServices(configuration));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public void Invalid_numeric_storage_is_rejected(int storage)
    {
        Assert.Throws<OptionsValidationException>(() =>
            LearningServiceCollectionExtensions.Validate(new LearningOptions { Storage = (StorageMode)storage }));
    }
}
