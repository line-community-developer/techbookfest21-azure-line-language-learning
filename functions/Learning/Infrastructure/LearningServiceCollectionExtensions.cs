using Azure.Core;
using Azure.Identity;
using LineLanguageLearn.Learning.Ai;
using LineLanguageLearn.Learning.Ai.Foundry;
using LineLanguageLearn.Learning.Analysis;
using LineLanguageLearn.Learning.Api;
using LineLanguageLearn.Learning.Identity;
using LineLanguageLearn.Learning.Knowledge;
using LineLanguageLearn.Learning.Messaging;
using LineLanguageLearn.Learning.Persistence;
using LineLanguageLearn.Learning.Practice;
using LineLanguageLearn.Learning.Tutor;
using LineLanguageLearn.Learning.Tutor.Tools;
using LineLanguageLearn.Learning.Tutor.Mcp;
using LineLanguageLearn.Learning.Workflows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace LineLanguageLearn.Learning.Infrastructure;

/// <summary>Composition root shared by the Functions host and the tests.</summary>
public static class LearningServiceCollectionExtensions
{
    public static IServiceCollection AddLearningServices(this IServiceCollection services, IConfiguration configuration, Action<LearningOptions>? configure = null)
    {
        services.AddOptions<LearningOptions>().Bind(configuration.GetSection(LearningOptions.Section)).Configure(o => configure?.Invoke(o));
        var options = new LearningOptions();
        configuration.GetSection(LearningOptions.Section).Bind(options);
        configure?.Invoke(options);
        Validate(options, configuration);

        services.TryAddSingleton(configuration);
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ReferenceDataLoader>();
        services.AddSingleton(sp => sp.GetRequiredService<ReferenceDataLoader>().Snapshot.Grammar);
        services.AddSingleton(sp => sp.GetRequiredService<ReferenceDataLoader>().Snapshot.Questions);
        services.AddSingleton(sp => sp.GetRequiredService<ReferenceDataLoader>().Snapshot.Tutors);
        var deployed = !string.IsNullOrWhiteSpace(configuration["WEBSITE_SITE_NAME"]) ||
            !string.IsNullOrWhiteSpace(configuration["WEBSITE_INSTANCE_ID"]);
        services.AddSingleton<TokenCredential>(_ => deployed ? new ManagedIdentityCredential() : new DefaultAzureCredential());
        services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(60) });

        // Persistence
        services.AddSingleton<IDocumentStore>(sp => options.Storage switch
        {
            StorageMode.InMemory => new InMemoryDocumentStore(),
            StorageMode.Cosmos => new CosmosDocumentStore(configuration["COSMOS_DB_DATABASE_NAME"]!, configuration),
            _ => throw new OptionsValidationException(LearningOptions.Section, typeof(LearningOptions), ["Unsupported Learning:Storage mode."]),
        });
        services.AddSingleton<LearningStore>();

        // Identity & messaging: LINE on Azure, fixed development learner and in-memory outbox locally.
        services.AddSingleton<LocalDevelopmentIdentityResolver>();
        services.AddSingleton<LineIdentityResolver>();
        services.AddSingleton<MockMessagingAdapter>();
        services.AddSingleton<LineMessagingClient>();
        if (options.EffectiveIdentity(deployed) == IdentityMode.Line)
        {
            services.AddSingleton<IRequestIdentityResolver>(sp => sp.GetRequiredService<LineIdentityResolver>());
            services.AddSingleton<IExternalIdentityMapper, LineIdentityMapper>();
        }
        else
        {
            services.AddSingleton<IRequestIdentityResolver>(sp => sp.GetRequiredService<LocalDevelopmentIdentityResolver>());
            services.AddSingleton<IExternalIdentityMapper, LocalDevelopmentIdentityMapper>();
        }
        services.AddSingleton<IOutboundMessenger>(sp => options.EffectiveMessaging(deployed) == MessagingMode.Line
            ? sp.GetRequiredService<LineMessagingClient>()
            : sp.GetRequiredService<MockMessagingAdapter>());

        // AI providers
        services.AddSingleton<FoundryResponsesClient>();
        services.AddSingleton<FoundryAgentClient>();
        services.AddSingleton<ILearningModel>(sp => options.AiProvider == AiProviderMode.Foundry
            ? ActivatorUtilities.CreateInstance<FoundryLearningModel>(sp)
            : new UnavailableLearningModel());
        services.AddSingleton<LearningModelGateway>();
        services.AddSingleton<ITutorAgentRuntime>(sp => options.TutorAgent == TutorAgentMode.FoundryAgentService
            ? ActivatorUtilities.CreateInstance<FoundryAgentTutorRuntime>(sp)
            : new UnavailableTutorAgent());

        // Retrieval
        services.AddSingleton<IGrammarRetriever>(sp => options.Retrieval == RetrievalMode.FoundryIq
            ? ActivatorUtilities.CreateInstance<FoundryIqGrammarRetriever>(sp)
            : ActivatorUtilities.CreateInstance<LocalGrammarRetriever>(sp));

        // Application services
        services.AddSingleton<ViewMapper>();
        services.AddSingleton<LearningProfileService>();
        services.AddSingleton<QuestionQualityEvaluator>();
        services.AddSingleton<QuestionGenerationService>();
        services.AddSingleton<PublishedQuestionService>();
        services.AddSingleton<PracticeService>();
        services.AddSingleton<ConversationAnalysisService>();
        services.AddSingleton<ReviewPlanner>();
        services.AddSingleton<IWorkflowDefinition, PostConversationWorkflow>();
        services.AddSingleton<WorkflowRunner>();
        services.AddSingleton<ITutorTool, GetLearningProfileTool>();
        services.AddSingleton<ITutorTool, GetRecentMistakesTool>();
        services.AddSingleton<ITutorTool, SearchGrammarRulesTool>();
        services.AddSingleton<ITutorTool, RecordLearningObservationTool>();
        services.AddSingleton<ITutorTool, CreatePracticeSessionTool>();
        services.AddSingleton<ITutorTool, GetReviewPlanTool>();
        services.AddSingleton<ToolRegistry>();
        services.AddSingleton<ToolDispatcher>();
        services.AddSingleton<McpTurnService>();
        services.AddSingleton<TutorService>();
        services.AddSingleton<LineConversationService>();
        services.AddSingleton<LearnerInsightsService>();
        services.AddSingleton<SystemStatusService>();
        return services;
    }

    /// <summary>Fail fast instead of silently falling back to mocks when a cloud mode is selected without settings.</summary>
    public static void Validate(LearningOptions options, IConfiguration? configuration = null)
    {
        var errors = new List<string>();
        var deployed = !string.IsNullOrWhiteSpace(configuration?["WEBSITE_SITE_NAME"]) ||
            !string.IsNullOrWhiteSpace(configuration?["WEBSITE_INSTANCE_ID"]);
        if (deployed)
        {
            if (options.Storage != StorageMode.Cosmos)
                errors.Add("Deployed Functions require Cosmos DB storage. InMemory storage is for tests only.");
            if (options.EffectiveIdentity(deployed) != IdentityMode.Line)
                errors.Add("Deployed Functions require Learning:Identity=Line. The local development identity is for local use only.");
        }
        if (options.Identity is { } identityMode && !Enum.IsDefined(identityMode) ||
            options.Messaging is { } messagingMode && !Enum.IsDefined(messagingMode))
            errors.Add("Unsupported Learning:Identity or Learning:Messaging mode.");
        if (!Uri.TryCreate(options.Line.ApiBaseUrl, UriKind.Absolute, out var lineApi) || lineApi.Scheme != Uri.UriSchemeHttps)
            errors.Add("Learning:Line:ApiBaseUrl must be an HTTPS URL.");
        if (!string.IsNullOrWhiteSpace(options.Line.TutorIconBaseUrl) &&
            (!Uri.TryCreate(options.Line.TutorIconBaseUrl, UriKind.Absolute, out var iconBase) ||
             iconBase.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(iconBase.UserInfo) ||
             !string.IsNullOrEmpty(iconBase.Query) || !string.IsNullOrEmpty(iconBase.Fragment)))
            errors.Add("Learning:Line:TutorIconBaseUrl must be an HTTPS base URL without credentials, query or fragment.");
        if (!Enum.IsDefined(options.Storage))
            errors.Add("Unsupported Learning:Storage mode.");
        if (!Enum.IsDefined(options.AiProvider) || !Enum.IsDefined(options.TutorAgent))
            errors.Add("Unsupported AI provider mode.");
        if (options.Storage == StorageMode.Cosmos)
        {
            if (string.IsNullOrWhiteSpace(configuration?["COSMOS_DB_DATABASE_NAME"]))
                errors.Add("COSMOS_DB_DATABASE_NAME must match the provisioned database.");
            if (string.IsNullOrWhiteSpace(configuration?["CosmosDBConnection__accountEndpoint"]) &&
                string.IsNullOrWhiteSpace(configuration?["CosmosDBConnection:accountEndpoint"]) &&
                string.IsNullOrWhiteSpace(configuration?["CosmosDBConnection"]))
                errors.Add("Learning:Storage=Cosmos requires CosmosDBConnection__accountEndpoint or CosmosDBConnection.");
        }
        if (options.AiProvider == AiProviderMode.Foundry && string.IsNullOrWhiteSpace(options.Foundry.Endpoint))
        {
            errors.Add("Learning:Foundry:Endpoint is required when a Foundry provider is enabled.");
        }
        if (options.TutorAgent == TutorAgentMode.FoundryAgentService)
        {
            if (!Uri.TryCreate(options.Foundry.ProjectEndpoint, UriKind.Absolute, out var endpoint) ||
                endpoint.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(endpoint.UserInfo) ||
                !System.Text.RegularExpressions.Regex.IsMatch(endpoint.AbsolutePath, @"^/api/projects/[^/]+/?$") ||
                !string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment))
                errors.Add("Learning:Foundry:ProjectEndpoint must be an HTTPS /api/projects/<project> endpoint without credentials, query or fragment.");
            if (string.IsNullOrWhiteSpace(options.Foundry.AgentName) || string.IsNullOrWhiteSpace(options.Foundry.AgentVersion))
                errors.Add("Learning:Foundry:AgentName and AgentVersion are required for saved-agent invocation.");
            if (string.Equals(options.Foundry.AgentVersion, "latest", StringComparison.OrdinalIgnoreCase))
                errors.Add("Learning:Foundry:AgentVersion must pin a saved version, not latest.");
        }
        if (options.Agent.MaxToolCallsPerTurn is < 1 or > 32)
            errors.Add("Learning:Agent:MaxToolCallsPerTurn must be between 1 and 32.");
        if (options.Retrieval == RetrievalMode.FoundryIq && (string.IsNullOrWhiteSpace(options.FoundryIq.SearchEndpoint) || string.IsNullOrWhiteSpace(options.FoundryIq.KnowledgeBaseName)))
        {
            errors.Add("Learning:FoundryIq:SearchEndpoint and KnowledgeBaseName are required when Learning:Retrieval=FoundryIq.");
        }
        if (errors.Count > 0) throw new OptionsValidationException(LearningOptions.Section, typeof(LearningOptions), errors);
    }
}
