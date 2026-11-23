# アーキテクチャ

LINE ミニアプリ（問題演習）と Messaging API Bot（AI 講師との会話）を組み合わせた外国語学習サービスです。
SwallowKit が生成した構成（React + Vite SPA / SWA managed Functions の BFF / C# の standalone Azure Functions / 共有 Zod スキーマ）を使っています。SwallowKit のバージョンは `2.0.0` です。

## 全体構成

[Azure SVG アイコン付き構成図](../public/architecture/index.html) と
[Mermaid ソース](../public/architecture/overview.mmd) を参照できます。
アプリ起動中は <http://localhost:4280/architecture/> で表示できます。
表示ページは Mermaid 11.12.0 を CDN から読み込むため、ネット接続が必要です。
アイコン本体は [MS Icons](https://msicons.com/) から取得して同梱し、
製品名を併記しています（[出典・利用条件](../public/architecture/icons/ATTRIBUTION.md)）。
「SVG を保存」はアイコンを埋め込んだ単独 SVG を保存します。
下の図は内部サービスの責務を示します。

```mermaid
flowchart TB
  subgraph Client["LINE ミニアプリ (LIFF, src/learning)"]
    UI["ホーム / 演習 / 履歴 / 文法 / 会話のふりかえり（読み取り専用）"]
  end
  LINE["LINE プラットフォーム<br/>公式アカウントのトーク"]
  subgraph BFF["SWA managed Functions BFF (bff/src/learning)"]
    Proxy["learning/{*path}<br/>Zod で要求・応答を検証して中継"]
  end
  subgraph Backend["Azure Functions (C# isolated, functions/Learning)"]
    API["Api: LearningFunctions + ApiPipeline<br/>サーバー側で学習者IDを確定・契約検証"]
    subgraph App["Application Services"]
      Practice["Practice<br/>PracticeService / AnswerGrader<br/>MasteryCalculator / LearningProfileService"]
      QGen["QuestionGenerationService<br/>QuestionQualityEvaluator"]
      Tutor["Tutor<br/>TutorService / ToolDispatcher / ToolRegistry"]
      MCP["公式 Functions MCP binding<br/>System key 認証 / ターン参照・共有予算"]
      Analysis["Analysis<br/>ConversationAnalysisService / ReviewPlanner"]
      WF["Workflows<br/>WorkflowRunner / PostConversationWorkflow"]
      Msg["Messaging<br/>LineBotFunctions（Webhook・Durable）/ LineConversationService"]
    end
    subgraph Ports["差し替え可能なアダプター"]
      Model["ILearningModel<br/>Unavailable (既定) / Foundry Models"]
      Agent["ITutorAgentRuntime<br/>Unavailable (既定) / 保存済み Foundry Agent Service"]
      Ret["IGrammarRetriever<br/>Local / Foundry IQ"]
      Store["IDocumentStore<br/>Cosmos DB（既定）/ InMemory（テスト用）"]
      Id["IRequestIdentityResolver<br/>LINE（LIFF ID トークン検証）/ Local Development（ローカル専用）"]
      Out["IOutboundMessenger<br/>LINE Messaging API / Mock（テスト・ローカル）"]
    end
  end
  Shared["shared/models・shared/contracts (Zod)<br/>→ JSON Schema (functions/Learning/Contracts/Schemas)"]

  UI -->|"/api/learning/*<br/>x-swallowkit-external-token: LIFF ID トークン"| Proxy -->|"Bearer token + Function key"| API
  LINE -->|"/api/line/webhook（署名検証）"| Msg
  API --> Practice & Tutor & WF
  Practice --> QGen
  Tutor --> Agent
  Foundry["Foundry Agent Service<br/>保存済み Prompt Agent / 固定バージョン"]
  Agent -->|"agent_reference / structured_inputs"| Foundry
  Foundry -->|"接続のサービスキー + ターン参照ヘッダー"| MCP --> Tutor
  Tutor -->|tools| Practice
  WF --> Analysis --> Model
  WF --> QGen --> Model
  QGen --> Ret
  Tutor --> Ret
  Practice & Tutor & WF --> Store
  API --> Id
  Msg --> Tutor
  Msg --> WF
  Msg -->|"Reply・ローディング表示"| Out --> LINE
  Shared -. 型・検証 .-> UI & Proxy & API & Model & Agent
```

### 責務の分離

| レイヤー | 置き場所 | 責務 |
| --- | --- | --- |
| UI | `src/learning/` | 表示と操作のみ。データはすべて API から取得し、業務ロジックや正答は持たない |
| BFF | `bff/src/learning/index.ts` | ルート表に従い、共有 Zod スキーマで要求と応答を検証して中継する。業務ロジックとユーザーIDは扱わない |
| API | `functions/Learning/Api/` | 認証コンテキストの解決、要求と応答の契約検証（JSON Schema）、エラーの HTTP への変換 |
| アプリケーションサービス | `functions/Learning/{Practice,Tutor,Analysis,Workflows,Messaging}` | ドメインロジック。AI SDK には直接依存しない |
| アダプター | `functions/Learning/{Ai,Knowledge,Persistence,Identity}` | 未接続エラー、Foundry、DB から読み込んだ文法検索、Cosmos の実装 |
| 契約 | `shared/models/`, `shared/contracts/` | 正本（Zod）。`pnpm run contracts:export` で JSON Schema に書き出し、C# 側に埋め込む |

### 設計判断

1. **管理マスターには Scaffold、学習者データにはカスタム API を使います**
   - 文法・問題・講師は、管理者限定の標準 CRUD を SwallowKit の scaffold で生成します。`authPolicy` は GitHub の SWA スキームと `administrator` ロールを要求する `adminOnly` です。
   - 学習者データ（プロフィール、観測、演習、会話、復習プラン、ワークフロー）はすべてユーザー単位です。生成 CRUD は所有者で絞り込まないため、これらには学習者を認証して対象を制限するカスタム API を使います。
   - 採点・会話・習熟度更新などの業務操作はカスタムエンドポイントに置き、検証済み LINE の主体から絞り込みキーを導きます。LIFF 未設定のローカル開発では固定の開発ユーザーを使います。
   - 保存先は本番で Azure Cosmos DB、ローカルで Cosmos DB Emulator です。シードは SwallowKit の `dev --seed-env` で投入し、リクエスト処理では投入しません。
   - モデルは `shared/models/*.ts` が正本です（`id` 必須、`createdAt` / `updatedAt` 任意）。マスターは `/language`、学習者データは各モデルの所有者キーでパーティションを分けます。
2. **契約の二重管理を避けています**
   - C# のレコードは手書きですが、Zod から書き出した JSON Schema を実行時と単体テストの両方で検証しています。どちらか一方だけを変更すると、`contracts` チェックか契約テストで検出されます。
   - 対象は次のとおりです。
     - API の要求と応答（ApiPipeline）
     - 保存ドキュメント（テスト）
     - Structured Outputs（StructuredOutput.Parse）
     - ツール引数と出力（ToolDispatcher）
3. **AI の判断と、決定論的な更新を分けています**
   - 採点：`AnswerGrader` が、保存済みの正答と明示的な正規化ルールで判定します。
   - 習熟度：`MasteryCalculator` が、観測結果を順に再生して計算するルールベースの投影です。
   - AI が出すのは観測の「候補」だけです。証拠（学習者の発言の一部）が実在することをアプリケーションが確かめてから採用します。
   - 観測には `certainty` を付け、`deterministic`（誤答から確定）と `ai-inferred`（AI の推定）を区別します。AI の推定は重みを下げて習熟度に反映します（`0.6 × confidence`）。
4. **冪等性**
   - 観測 ID、演習セッション ID、復習プラン ID、ワークフロー実行 ID は、発生源から決定的に作ります。保存は `TryCreate`（存在しなければ作成）です。
   - 同じ回答、同じ会話イベント、終了処理の再実行、ツールとワークフローの両方による同じ指摘の記録が、重複して数えられることはありません。
5. **AI の接続設定とエラー処理**
   - 接続未設定時の AI 呼び出しは `503 / ai-not-configured` を返します。分析ワークフローでは失敗状態を記録します。
   - Foundry モードの接続設定が欠けている場合は、バックエンドの起動時にエラーになります。
   - `/api/learning/status` で接続設定の状態を確認できます。
6. **参照マスターも Cosmos DB を使います**
   - `ReferenceDataLoader` が最初の認証済み学習 API リクエスト、または MCP ツール呼び出しで `GrammarTopics` / `TutorProfiles` / `SampleQuestions` を読み、共有スキーマと参照整合性を検証します。成功した読み込みはキャッシュし、その後の変更反映にはバックエンドの再起動が必要です。
   - 空・不正なデータや DB 接続失敗では学習 API が `503 / reference-data-unavailable` を返し、次のリクエストで再試行します。
   - シードの生成元は `tools/SeedData/Data`、投入用配列は `dev-seeds/<環境名>` に置きます。プロンプトのテンプレートと JSON Schema は Functions に埋め込みます。

## 問題演習のシーケンス

```mermaid
sequenceDiagram
  actor L as 学習者 (ミニアプリ)
  participant UI as React SPA
  participant B as BFF
  participant A as Functions API
  participant P as PracticeService
  participant G as QuestionGenerationService
  participant R as IGrammarRetriever
  participant M as ILearningModel
  participant E as QuestionQualityEvaluator
  participant S as LearningStore

  L->>UI: おすすめ演習を開始
  UI->>B: POST /api/learning/practice/sessions {mode}
  B->>A: Zod 検証して中継
  A->>A: 学習者IDをサーバー側で確定（LIFF ID トークンを検証。ローカルは開発用利用者）
  A->>P: StartAsync
  P->>G: GenerateValidatedAsync(苦手項目, レベル)
  G->>R: 指定項目の文法知識を検索（RAG）
  G->>M: Structured Outputs で問題生成
  M-->>G: JSON
  G->>G: JSON Schema 検証（構造）
  G->>E: 内容検証（決定論的チェック + 意味的評価）
  E-->>G: 合格した問題のみ（不足分は再生成）
  G-->>P: 検証済みの問題
  P->>S: PracticeSession 保存（正答はサーバーのみ）
  A-->>UI: 正答なしのビュー
  L->>UI: 回答
  UI->>B: POST .../answers {questionId, answer}
  B->>A: 中継
  A->>P: SubmitAnswerAsync
  P->>P: AnswerGrader（正規化して決定論的に採点）
  P->>S: LearningObservation（決定的ID、存在すれば作成しない）
  P->>S: プロフィール再計算（MasteryCalculator）
  A-->>UI: 正誤・解説・文法根拠・習熟度変化
```

## AI 講師（Tool Calling）のシーケンス

```mermaid
sequenceDiagram
  actor L as 学習者 (LINE のトーク)
  participant WH as LineWebhook
  participant O as RelayConversationOrchestrator (Durable)
  participant E as ReplyRelay (Durable Entity)
  participant T as TutorService
  participant AG as ITutorAgentRuntime
  participant R as Foundry Agent Service (保存済み agent)
  participant MCP as Functions MCP binding
  participant C as McpTurnService
  participant D as ToolDispatcher
  participant Tools as Tools (アプリケーションサービスを呼ぶ)
  participant S as LearningStore
  participant WF as WorkflowOrchestrator (Durable)

  L->>WH: テキストイベント (webhookEventId, userId, replyToken)
  WH->>WH: 生のバイト列で署名検証
  WH->>O: インスタンス ID = chat-{userId}-{webhookEventId} で開始（再送は開始しない）
  WH-->>L: 200
  O->>E: 受け付けを判定し、replyToken を保存または入れ替え
  break すでに処理中の場合
    O->>L: 直前の replyToken で待機案内。追送の本文は保留しない
  end
  O->>O: 学習者単位の entity ロックで会話処理を保護
  O->>L: ローディング表示（失敗しても続行）
  O->>T: SaveLearnerMessage（同じ eventId は再保存しない）
  T->>S: 学習者メッセージを保存
  O->>T: CallTutorAgent → RespondAsync
  T->>AG: RunTurnAsync(履歴, プロンプトテンプレート)
  AG->>C: 非資格情報のターン参照キーを発行
  AG->>R: agent_reference（名前・バージョン）/ history / structured_inputs
  loop ツール呼び出し（1ターンの上限 MaxToolCallsPerTurn）
    R->>MCP: tools/call + 接続の system key + ターン参照ヘッダー
    MCP->>C: 利用者・会話・期限・失効を検証、ETag で予算予約
    C->>D: get_learning_profile / get_recent_mistakes / search_grammar_rules / record_learning_observation / ...
    D->>D: 予算確認 → 引数の JSON Schema 検証（userId は拒否） → 実行 → 出力検証 → ログ
    D->>Tools: 認証済みの学習者として実行
    Tools->>S: サービス経由で読み書き
    D-->>C: 結果（エラーもデータとして返す）
    C->>S: 実行記録・検索根拠IDを保存
    C-->>R: MCP tool result
  end
  R-->>AG: 返信
  AG->>C: 成功・失敗・キャンセル時に失効し、監査記録を取得
  AG-->>T: 返信と訂正
  T->>S: 講師メッセージ（使ったツール、RAG の根拠ID）を保存
  O->>E: 処理完了。最新の replyToken を取得して状態を解放
  O->>L: SendReply（取得した replyToken を使用）
  L->>WH: 「会話を終わる」（postback）または「終了」
  WH->>O: EndConversation（会話を終了し、分析の実行を作成）
  O->>WF: StartAnalysis（[DurableClient] で学習分析を開始）
  O->>L: 終了のメッセージ（ミニアプリの URL）
  WF->>WF: 会話取得 → 弱点抽出（証拠を検証） → プロフィール更新 → 復習プラン → 問題生成 → 品質検証 → 保存
```

## 学習分析ワークフロー

```mermaid
stateDiagram-v2
  [*] --> Pending
  Pending --> Running: Schedule
  Running --> Completed: 全ステップ完了
  Running --> Failed: ステップ失敗（エラーと試行回数を保存）
  Failed --> Running: Retry（完了済みのステップは飛ばす）
  Completed --> [*]
```

| ステップ | 処理 | 出力（再開に必要な情報） |
| --- | --- | --- |
| load-conversation | 会話の取得と終了確認 | 発話数 |
| extract-observations | `ConversationAnalysisService`（モデル出力 → 証拠の検証） | 採用した観測と却下した観測 |
| update-profile | 観測を冪等に記録し、習熟度を再計算 | 作成数と重複数 |
| plan-review | `ReviewPlanner`（会話の誤り + 長期的な苦手） | reviewPlanId と topicIds |
| generate-questions | 演習と同じ `QuestionGenerationService` | 候補の問題 |
| validate-questions | `QuestionQualityEvaluator`（不合格分は再生成） | 合格した問題 |
| save-practice-session | 復習セッションを保存し、プランにリンク | practiceSessionId |

ワークフローの本体（`IWorkflowDefinition` / `IWorkflowStep`）と、ホスティング（`IWorkflowScheduler`）は分けてあります。
Function App では `DurableWorkflowScheduler` が `WorkflowOrchestrator` を開始し、各 step を Durable Functions のアクティビティとして実行します。
各 step の状態は画面の進捗表示と再実行のため `WorkflowRun` にも保存します。テストとオフラインのシード生成は `InProcessWorkflowScheduler` で同じ step を同期実行します。

## データモデル（`shared/models`）

| モデル | パーティション | 説明 |
| --- | --- | --- |
| LearningProfile | /userId | レベル、目標、`masteryByTopic`、苦手と得意。観測から計算した投影 |
| LearningObservation | /userId | 演習や会話からの観測。`certainty`、`confidence`、`evidence`、発生源の ID を持つ |
| PracticeSession（PracticeQuestion を内包） | /userId | 問題（正答と検証結果を含む）、回答、サマリー、生成元（学習者・ワークフロー・ツール） |
| TutorSession（TutorMessage を内包） | /userId | 会話、訂正、ツール呼び出しの記録、RAG の根拠 ID、分析ワークフローの ID |
| ReviewPlan | /userId | 優先度付きの復習項目（理由と根拠となる観測 ID）と、関連する演習 |
| WorkflowRun | /userId | 状態、ステップごとの試行回数、出力、エラー |
| GrammarTopic | /language | 文法ナレッジ（マスターデータ、監修状態つき） |
| TutorProfile | /language | 講師キャラクター（プロンプトテンプレート ID、訂正方針） |
| SampleQuestion | /language | 問題生成の参照サンプルと、演習で出題する公開済み問題。文法・ルールへの参照を持つ |
| ProcessedEvent | /userId | Webhook イベントの処理状態と重複処理の制御 |
| TutorToolTurn | /userId | MCP のターン参照、期限、ツール実行予算と監査 |

すべての文書が `schemaVersion` を持ちます。`createdAt` と `updatedAt` はサーバーが設定します。

## Observability

- `LearningTelemetry`：`ILogger` の構造化イベントと `ActivitySource("LineLanguageLearn.Learning")` で、次の情報を記録します。
  - AI 呼び出し（プロバイダー、モデル、処理時間、結果）
  - ツール呼び出し
  - RAG のヒット ID
  - 問題評価の結果
  - ワークフローのステップ
  - 却下した AI 出力
- 学習者の発言やプロンプトの本文はログに出しません。
- 会話ごとのツール呼び出し履歴と RAG の根拠 ID は `TutorMessage` に、問題の生成元と評価結果は `PracticeQuestion` に保存します。
- Azure 上では、Application Insights に `ILogger` の出力が流れます。`ActivitySource` を使ったトレースの収集には OpenTelemetry 等の exporter の設定が必要です。
