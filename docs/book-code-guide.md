# 書籍の設計とコードの対応表

[README](../README.md)

書籍の節を読みながら、次の順で実装をたどれます。Azure が担当する実行基盤と、アプリが担当する認証・学習ロジックを確認してください。

| 書籍の話題・メリット | 見るコード | 注目点 |
| --- | --- | --- |
| サーバーレスで基盤の運用を減らす | [main.bicep](../infra/main.bicep)、[Flex Consumption](../infra/modules/functions-flex.bicep) | 画面・API・データ・AI・監視をまとめて再現。OS の保守より学習体験に時間を使う |
| SWA と独立 Functions の役割分担 | [BFF 中継](../bff/src/lib/call-function.ts)、[学習 API](../functions/Learning/Api/LearningFunctions.cs) | 同一オリジンの /api を入口にし、DB と業務処理は独立 Functions に置く |
| 秘密情報をブラウザーへ渡さない | [SWA 設定](../infra/modules/staticwebapp-config.bicep) | URL と関数キーをサーバー側に保持 |
| LINE と GitHub 認証の使い分け | [SWA ルート](../staticwebapp.config.json)、[認証ポリシー](../swallowkit.config.js)、[AuthRouter](../functions/Auth/AuthRouter.cs) | 学習者の LINE トークンと管理者ロールを分ける |
| ミニアプリと Bot の学習者を統合 | [LINE identity](../functions/Learning/Identity/LineIdentity.cs)、[トークン検証](../functions/Auth/Schemes/line/ExternalTokenVerifier.cs)、[Webhook](../functions/Learning/Messaging/LineBotFunctions.cs) | 自己申告 ID を信用せず検証済み情報から学習者を確定 |
| Cosmos DB を両入口で共有 | [CosmosDocumentStore](../functions/Learning/Persistence/CosmosDocumentStore.cs)、[コンテナー](../infra/containers/) | 教材と学習記録のパーティション、競合・更新処理 |
| 接続キーを減らす Managed Identity | [Cosmos RBAC](../infra/modules/cosmosdb-role-assignment.bicep)、[Foundry](../infra/modules/foundry.bicep) | 推論・Agent Consumer・DB・Durable ストレージを用途別に付与 |
| データのネットワーク保護 | [VNet](../infra/modules/vnet.bicep)、[Private Endpoint](../infra/modules/private-endpoint-cosmos.bicep) | Functions は VNet 統合、Cosmos は公開アクセスを無効化 |
| 生成と意味的な品質検証 | [QuestionGenerationService](../functions/Learning/Practice/QuestionGenerationService.cs)、[品質評価](../functions/Learning/Practice/QuestionQualityEvaluator.cs)、[Responses](../functions/Learning/Ai/Foundry/FoundryResponsesClient.cs) | 共有 JSON Schema、回数制限、失敗を成功に見せない |
| AI 利用量を抑える問題バンク | [PublishedQuestionService](../functions/Learning/Practice/PublishedQuestionService.cs) | 公開問題がある場合は出題時の生成を省く |
| 保存済み講師と固定バージョン | [FoundryAgentClient](../functions/Learning/Ai/Foundry/FoundryAgentClient.cs)、[講師 runtime](../functions/Learning/Tutor/FoundryAgentTutorRuntime.cs)、[指示](../functions/Learning/Ai/Prompts/tutor.it-giulia.system.md) | モデル直接呼び出しと会話エージェントの使い分け |
| Functions を MCP ツールとして公開 | [TutorMcpFunctions](../functions/Learning/Tutor/Mcp/TutorMcpFunctions.cs)、[ツール](../functions/Learning/Tutor/Tools/TutorTools.cs)、[契約](../shared/contracts/tools.ts) | 六つの明示的なツールと入力検証 |
| AI に学習者を選ばせない | [McpTurnService](../functions/Learning/Tutor/Mcp/McpTurnService.cs) | 短命参照、対象学習者の復元、失効、予算、監査 |
| Webhook の受付と長い処理の分離 | [LineBotFunctions](../functions/Learning/Messaging/LineBotFunctions.cs)、[DurableWorkflows](../functions/Learning/Workflows/DurableWorkflows.cs)、[ReplyRelayEntity](../functions/Learning/Messaging/ReplyRelayEntity.cs) | Durable の状態保存、再配信と返信の取り扱い |
| 待ち時間と講師の見せ方 | [LineMessaging](../functions/Learning/Messaging/LineMessaging.cs) | ローディング表示、sender、会話終了の postback |
| 会話分析を復習に接続 | [PostConversationWorkflow](../functions/Learning/Workflows/PostConversationWorkflow.cs)、[WorkflowEngine](../functions/Learning/Workflows/WorkflowEngine.cs) | 七段階、完了済みステップを除いた再実行、安定 ID |
| 発言に基づく分析 | [ConversationAnalysisService](../functions/Learning/Analysis/ConversationAnalysisService.cs) | 誤りの根拠が利用者の実発言に存在するか検証 |
| 障害を追える監視 | [LearningTelemetry](../functions/Learning/Observability/LearningTelemetry.cs)、[Application Insights](../infra/modules/appinsights.bicep) | 本文や秘密ではなく処理 ID と結果で追跡 |
| CI/CD と IaC | [SWA 配備](../.github/workflows/deploy-swa.yml)、[Functions 配備](../.github/workflows/deploy-functions.yml)、[初期投入](../.github/workflows/initialize-cosmos-seeds.yml) | 配備単位の分離、共有モデル変更、VNet 内での初期投入 |
| SwallowKit で契約のずれを減らす | [モデル](../shared/models/)、[契約出力](../shared/scripts/export-json-schemas.cjs)、[台帳](../.swallowkit/artifacts.json) | 共有 Zod を起点に生成。LINE と AI の業務処理は独自実装 |

SWA の API には要求時間制限があるため、問題生成を何度呼べるかだけでは時間内の完了を保証できません。
長い処理の非同期化、運用時の保存期間・削除、予算・送信制限は利用規模に合わせて設計してください。
現在の配備ワークフローにテスト実行は含まれません。変更時は SwallowKit verify を実行し、実機の認証と接続も確認します。
