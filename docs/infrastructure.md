# 初期インフラと変更方法

[README](../README.md) / [Azure の構築手順](azure-deployment.md)

このアプリの Azure リソースは Bicep で定義しています。
初回の `pnpm exec swallowkit provision` で、新規リソースグループに以下の構成を作成します。

| 定義 | 内容 |
| --- | --- |
| [main.bicep](../infra/main.bicep) | SWA / Functions / Cosmos / Foundry / ネットワーク / 監視の依存関係 |
| [containers/](../infra/containers/) | モデルに対応する 11 コンテナー。main から直接参照 |
| [functions-flex.bicep](../infra/modules/functions-flex.bicep) | Flex Consumption、Managed Identity、Durable の Blob・Queue・Table 権限、公開 HTTPS |
| [foundry.bicep](../infra/modules/foundry.bicep) | AIServices アカウント、learning プロジェクトとモデル、用途別 RBAC |
| [staticwebapp-config.bicep](../infra/modules/staticwebapp-config.bicep) | BFF の接続 URL と関数キーをサーバー側に設定 |

参照マスターの GrammarTopics / TutorProfiles / SampleQuestions は `/language`、学習データの 8 コンテナーは `/userId` で分割します。
モデル推論ロールと Foundry Agent Consumer は Functions の identity に割り当て、講師の作成・編集は作業者が行います。
VNet 有効時は Cosmos DB の公開アクセスを無効にし、Functions が Private Endpoint で接続します。

## 初期設定

モデル直接呼び出しは `Foundry`、講師は保存済みエージェント登録まで `Unavailable`、文法検索は `Local` です。
Functions の入口は LINE から到達可能な公開 HTTPS とし、関数キー・MCP システムキー・LINE 署名を各入口で検証します。

講師の有効化は [Foundry の設定](foundry-agent-setup.md)、教材の投入は [初期シード投入](cloud-seed-initialization.md)を参照してください。

## 構築後の変更

新規モデルは `pnpm exec swallowkit create-model <model>`、カスタムリソースの追加は `pnpm exec swallowkit create-migration <slug>` を使います。
既存モデルのキーなども、適用済み baseline を変更せず新しいマイグレーションで扱います。

```sh
pnpm exec swallowkit status
pnpm exec swallowkit verify
```

ローカル定義を確認したら、`pnpm exec swallowkit provision --resource-group <RG名> --location <地域> --swa-location <SWAの地域>` で Azure に適用します。
`.swallowkit/state/provision/` は Git の対象外ですが、実行中の環境では消さずに保管します。
