# ローカル開発

Azure に配備するハンズオンは [Codespaces の準備](handson-preparation.md)から進められます。このページはアプリを手元で起動・開発する場合の案内です。
ツール導入とリポジトリ取得は [ローカル PC の準備](local-handson-preparation.md)を参照してください。

保存先は Azure Cosmos DB が既定です。ローカルでは Cosmos DB Emulator を使い、
文法・講師・サンプル問題も DB から読みます。AI は未接続が既定で、生成・会話・分析を
要求すると `ai-not-configured` エラーになります。
単体テストは InMemory を使い、外部接続なしで実行します。

## 必要なツール

| ツール | バージョン |
| --- | --- |
| Node.js | 24.21.0（`>=22.14`） |
| pnpm | 12.4.2（`>=11 <13`） |
| .NET SDK | 10.0.401 |
| Azure Functions Core Tools | 4.14.0 |

ローカルのアプリ起動には、接続設定と起動済みの Cosmos DB Emulator が必要です。
起動済みの Emulator の接続先を確認してから、テスト専用 DB を使用してください。

**ローカルでも Functions の起動に Azurite が必要です。** Durable Functions は起動時に
`AzureWebJobsStorage` のストレージへ接続します。`functions/local.settings.json` に
`"AzureWebJobsStorage": "UseDevelopmentStorage=true"` を設定し、`swallowkit dev` の前に
Azurite（Blob・Queue・Table）を起動してください。`swallowkit dev` は Azurite を起動しません。
Azurite の導入方法は Microsoft Learn の「Azurite エミュレーターを使用したローカルでの Azure Storage の開発」を参照してください。
テストとオフラインのシード生成は、同じステップを同期実行するため Azurite は不要です。

## セットアップと起動

```sh
pnpm install
pnpm exec swallowkit dev --seed-env a2-review-ready # 初回のみ。テスト専用 DB で実行
```

シード投入後、そのまま起動したアプリを操作できます。終了するときはターミナルで Ctrl+C を押します。
次回からは、データを保持して起動します。

```sh
pnpm exec swallowkit dev
```

- ブラウザで <http://localhost:4280/miniapp>（SWA 経由で同一オリジンの BFF を通ります）を開きます。
- `swallowkit dev` は、Vite（3000）、BFF（7073）、Functions（7071）、SWA エミュレーター（4280）を起動します。
- 初回リクエストでは空の学習者プロフィールだけが作られます。演習・会話・復習プランのデモデータは自動投入しません。
- `swallowkit dev` が用意する Cosmos 接続設定と実効 DB 名をアプリも使います。対象の DB 名を起動ログで確認してください。
- 参照データは最初の認証済み学習 API リクエストで DB から検証して読み込みます。空・不正・接続失敗なら `503 / reference-data-unavailable` を返し、次のリクエストで再試行します。成功した読み込みはキャッシュするため、その後のマスター変更にはバックエンドの再起動が必要です。

### データの保存場所と初期化

シナリオを選んで確認する場合は、[シナリオ別シード](../dev-seeds/README.md) の
`pnpm exec swallowkit dev --seed-env <環境名>` を使います。シード適用は対象 DB の
データを置き換えるため、接続先がローカルのテスト専用 DB であることを先に確認してください。

| 設定（環境変数） | 既定値 | 説明 |
| --- | --- | --- |
| `COSMOS_DB_DATABASE_NAME` | なし | SwallowKit 標準の実効 DB 名。必須 |
| `CosmosDBConnection__accountEndpoint` / `CosmosDBConnection` | なし | SwallowKit 標準の endpoint + identity、または接続文字列。必須 |
| `Learning__Storage` | `Cosmos` | 通常は設定不要。`InMemory` はテスト用で、Azure 上では禁止 |
| `Learning__AiProvider` | `Unavailable` | 未接続時は `503 / ai-not-configured`。`Foundry` を選ぶ場合は接続設定が必須 |
| `Learning__TutorAgent` | `Unavailable` | 未接続時は LINE での会話に講師が応答できない旨を返す。`FoundryAgentService` を選ぶ場合は以下の保存済み agent 設定が必須 |
| `Learning__Foundry__ProjectEndpoint` | 未設定 | `https://<account>.services.ai.azure.com/api/projects/learning`。モデル直接呼び出しの `Endpoint` とは区別する |
| `Learning__Foundry__AgentName` / `AgentVersion` | 未設定 | 保存済み agent 名と固定バージョン。`latest` は不可 |
| `Learning__Retrieval` | `Local` | `FoundryIq` にすると Foundry IQ を使う |
| `Learning__Identity` | ローカル：`LocalDevelopment`、Azure：`Line` | ローカルは固定の開発用利用者。Azure 上で `LocalDevelopment` は起動エラー |
| `Learning__Messaging` | ローカル：`Mock`、Azure：`Line` | `Mock` は LINE に送らずメモリに記録する。LINE 連携は Azure 上で確認する |
| `Learning__Line__LoginChannelId` など | 未設定 | LINE のチャネル情報。[LINE の設定](line-setup.md) を参照 |

会話は LINE のトークで行うため、ミニアプリに会話画面はありません。
講師との会話は [Azure へのデプロイ](azure-deployment.md)と [LINE の設定](line-setup.md)を済ませて確認します。
フロントの `VITE_LIFF_ID` を空にすると、ミニアプリは LIFF を使わずに開発用利用者で動きます。

講師は [Foundry の設定手順](foundry-agent-setup.md)で保存済み agent と MCP 接続を準備してください。
Azure 上の Functions は Managed Identity で Foundry を呼び出します。
会話履歴は Cosmos DB に保存します。
会話終了後の学習分析は Durable Functions のオーケストレーションで実行します（Azure Storage provider）。

Emulator の endpoint と接続プロトコルは、使用する Emulator の起動設定に合わせます。
Azurite が起動していないと、Durable Functions の初期化に失敗し、会話終了後の分析が開始されません。

Foundry / Foundry IQ や Cosmos DB の接続設定が欠けている場合は、バックエンドの起動時にエラーになります。

## テストと検証

```sh
pnpm run test                      # xUnit（Mock Provider + InMemory、外部接続なし）
pnpm run contracts:check           # Zod から書き出した JSON Schema が最新か確認
pnpm run build                     # shared → SPA → BFF
pnpm exec swallowkit verify
pnpm exec swallowkit verify --checks "structure,drift,typecheck,backend-build,build,contracts,backend-test"
```

- `swallowkit.config.js` の `verify.checks` に、`contracts` と `backend-test` を登録しています。既定の `verify` でもこの 2 つが実行されます。
- テストを実行すると、評価データセットの結果が `tests/LineLanguageLearn.Tests/bin/Debug/net10.0/TestResults/evaluation/*.jsonl` に出力されます。
- ビルドや単体テストの成功だけでは Emulator 接続の確認になりません。実際に `dev` を起動し、SWA/BFF 経由の読み書きとブラウザ表示を確認してください。

## 共有スキーマを変更したとき

`shared/models` または `shared/contracts` を変更したら、次を実行します。

```sh
pnpm run contracts:export
```

`functions/Learning/Contracts/Schemas/*.schema.json` が更新され、C# 側の実行時検証と契約テストに反映されます。C# のレコードも合わせて修正してください。ずれていればテストが失敗します。

`LearningProfile.masteryByTopic` と `WorkflowStepState.output` は
`[{ key, value }]` 形式です。C# 内部の辞書は専用 converter でこの形式へ保存します。


永続モデルは `shared/models/`、親文書に内包される問題・会話メッセージは
`shared/contracts/` に置きます。既存モデルのコンテナー追加には
`pnpm exec swallowkit migrations add-model <model> --dry-run` で変更予定を確認し、
`pnpm exec swallowkit migrations add-model <model>` でローカル定義を登録します。
Azure への適用は `pnpm exec swallowkit provision` で行います。

## 動作確認できる機能（デモシナリオ）

1. ホーム画面で、レベル（A2）、苦手な項目（a / di、近過去の助動詞）、得意な項目、会話から生まれた復習プランを確認する。
2. 「続きから演習する」で、会話分析から生成された復習演習を解く。
   - 正誤、解説、文法の根拠、習熟度の変化が表示されます。
   - 再読み込みしても続きから再開できます。
3. 演習を終了し、結果のサマリーを確認する。
4. 「履歴」で、習熟度、回答履歴、観測記録（確定 / AI 推定）、復習の優先度を確認する。
5. 「演習」で新規生成を要求し、AI 未接続のエラーを確認する。既存のシード演習は再開・採点できる。
6. 「文法」で、ローカル RAG による検索結果（スコア、ソース ID）と、監修状態つきの解説を見る。
7. ホームや履歴から保存済みの会話と講師名を確認する。新しい会話には LINE Bot と Foundry の設定が必要です。
8. 未分析の会話の分析・失敗した分析の再試行は `ai-not-configured` の失敗状態になる。
