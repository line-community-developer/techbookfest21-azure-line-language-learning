using LineLanguageLearn.Learning.Ai;
using LineLanguageLearn.Learning.Identity;
using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Knowledge;
using LineLanguageLearn.Learning.Messaging;
using LineLanguageLearn.Learning.Persistence;
using LineLanguageLearn.Learning.Tutor;
using LineLanguageLearn.Learning.Workflows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace LineLanguageLearn.Learning.Api;

/// <summary>Reports which adapter backs each component, so the UI never implies Foundry is active when it is a mock.</summary>
public sealed class SystemStatusService(
    IConfiguration configuration,
    IOptions<LearningOptions> options,
    LearningModelGateway models,
    ITutorAgentRuntime agent,
    IGrammarRetriever retriever,
    LearningStore store,
    IRequestIdentityResolver identity,
    IOutboundMessenger messenger)
{
    public SystemStatusView Get(UserContext user)
    {
        var o = options.Value;
        var deployed = !string.IsNullOrWhiteSpace(configuration["WEBSITE_SITE_NAME"]) ||
            !string.IsNullOrWhiteSpace(configuration["WEBSITE_INSTANCE_ID"]);
        return new SystemStatusView(
            deployed ? "cloud" : "local",
            [
                new("ai-model", models.Info.Label, models.Info.Kind == Domain.ProviderKind.Mock, models.Info.Kind != Domain.ProviderKind.Foundry
                    ? "AI接続なし。この環境では問題生成・会話分析・意味的評価は利用できません。"
                    : $"Foundry Models（Responses API / Structured Outputs）: {o.Foundry.Endpoint}"),
                new("tutor-agent", agent.Info.Label, agent.Info.Kind == Domain.ProviderKind.Mock, agent.Info.Kind != Domain.ProviderKind.Foundry
                    ? "AI講師の接続なし。この環境では新しい応答を生成できません。"
                    : $"Foundry Agent Service（保存済み agent / MCP）: {o.Foundry.AgentName}, version {o.Foundry.AgentVersion}"),
                new("retrieval", retriever.Name, false, retriever.Name == "local-keyword"
                    ? "Local Knowledge Retriever：DBから読み込んだ文法ナレッジをキーワード検索"
                    : $"Foundry IQ knowledge base: {o.FoundryIq.KnowledgeBaseName}"),
                new("storage", store.StoreName, store.StoreName != "cosmos-db", store.StoreName switch
                {
                    "in-memory" => "インメモリ（再起動で消えます）",
                    _ => "Azure Cosmos DB",
                }),
                new("identity", identity.Mode, identity.Mode != LineLearner.Mode, identity.Mode == LineLearner.Mode
                    ? "LINE ログイン（LIFF の ID トークンをサーバーで検証）"
                    : "Local Development Identity：サーバー側で固定の開発ユーザーを付与（ローカル専用）"),
                new("messaging", messenger.Mode, messenger is MockMessagingAdapter, messenger is MockMessagingAdapter
                    ? "Mock Messaging Adapter：LINE には送信しません"
                    : "LINE Messaging API（Push・ローディング表示）"),
                new("workflow", DurableWorkflowScheduler.ModeName, false, "Durable Functions（会話後の学習分析をオーケストレーションで実行）"),
            ],
            new LearnerIdentityView(user.UserId, user.DisplayName, user.IdentityMode));
    }
}
