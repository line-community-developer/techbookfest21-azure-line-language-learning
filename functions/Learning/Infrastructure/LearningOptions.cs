namespace LineLanguageLearn.Learning.Infrastructure;

public enum AiProviderMode { Unavailable, Foundry }
public enum TutorAgentMode { Unavailable, FoundryAgentService }
public enum RetrievalMode { Local, FoundryIq }
public enum StorageMode { Cosmos = 2, InMemory = 1 }
public enum IdentityMode { LocalDevelopment, Line }
public enum MessagingMode { Mock, Line }

/// <summary>
/// Bound from the "Learning" configuration section (env vars: Learning__AiProvider etc.).
/// Storage defaults to Cosmos DB; AI calls fail explicitly until a provider is configured.
/// </summary>
public sealed class LearningOptions
{
    public const string Section = "Learning";

    public AiProviderMode AiProvider { get; set; } = AiProviderMode.Unavailable;
    public TutorAgentMode TutorAgent { get; set; } = TutorAgentMode.Unavailable;
    public RetrievalMode Retrieval { get; set; } = RetrievalMode.Local;
    public StorageMode Storage { get; set; } = StorageMode.Cosmos;
    /// <summary>Unset: LINE on Azure, local development identity locally. LocalDevelopment is rejected on Azure.</summary>
    public IdentityMode? Identity { get; set; }
    /// <summary>Unset: LINE Messaging API on Azure, in-memory mock locally.</summary>
    public MessagingMode? Messaging { get; set; }

    public string LocalUserId { get; set; } = "local-demo-learner";
    public string LocalLearnerName { get; set; } = "Demo Learner";

    public AgentLimitOptions Agent { get; set; } = new();
    public QuestionGenerationOptions QuestionGeneration { get; set; } = new();
    public FoundryOptions Foundry { get; set; } = new();
    public FoundryIqOptions FoundryIq { get; set; } = new();
    public LineOptions Line { get; set; } = new();

    public IdentityMode EffectiveIdentity(bool deployed) => Identity ?? (deployed ? IdentityMode.Line : IdentityMode.LocalDevelopment);
    public MessagingMode EffectiveMessaging(bool deployed) => Messaging ?? (deployed ? MessagingMode.Line : MessagingMode.Mock);
}

/// <summary>LINE channels in the same provider, so LIFF sub and webhook userId identify the same person.</summary>
public sealed class LineOptions
{
    /// <summary>LINE Login channel that owns the LIFF app (audience of LIFF ID tokens). Not a secret.</summary>
    public string? LoginChannelId { get; set; }
    /// <summary>Messaging API channel secret for webhook signatures. Secret.</summary>
    public string? ChannelSecret { get; set; }
    /// <summary>Messaging API channel access token for push and loading. Secret.</summary>
    public string? ChannelAccessToken { get; set; }
    /// <summary>Mini app URL shown in the bot (for example https://miniapp.line.me/{liffId}).</summary>
    public string? MiniAppUrl { get; set; }
    public string? TutorIconBaseUrl { get; set; }
    public string ApiBaseUrl { get; set; } = "https://api.line.me";
}

public sealed class AgentLimitOptions
{
    public int MaxToolCallsPerTurn { get; set; } = 8;
    public int HistoryMessagesForModel { get; set; } = 12;
}

public sealed class QuestionGenerationOptions
{
    public int MaxGenerationAttempts { get; set; } = 3;
    public bool UseSemanticJudge { get; set; } = true;
}

public sealed class FoundryOptions
{
    /// <summary>Resource endpoint for direct model requests (question generation, judging and analysis).</summary>
    public string? Endpoint { get; set; }
    /// <summary>Optional API key. When empty, DefaultAzureCredential (Microsoft Entra ID) is used.</summary>
    public string? ApiKey { get; set; }
    /// <summary>Azure deployment name, not the underlying model ID. Matches the demo AI migration.</summary>
    public string Model { get; set; } = "learning";
    public string? JudgeModel { get; set; }
    /// <summary>Foundry project endpoint for saved-agent invocation, not the model inference endpoint.</summary>
    public string? ProjectEndpoint { get; set; }
    public string? AgentName { get; set; }
    /// <summary>Explicit saved-agent version. Never implicitly follows latest.</summary>
    public string? AgentVersion { get; set; }
}

public sealed class FoundryIqOptions
{
    public string? SearchEndpoint { get; set; }
    public string? KnowledgeBaseName { get; set; }
    public string? KnowledgeSourceName { get; set; }
    public string? ApiKey { get; set; }
    public string ApiVersion { get; set; } = "2026-04-01";
}
