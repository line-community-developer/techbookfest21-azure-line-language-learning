# シナリオ別シード

Azure への初期投入は [専用の手動 CI/CD](../docs/cloud-seed-initialization.md) を使えます。
ローカルの `dev --seed-env` のようなコンテナー再作成ではなく、同一文書はスキップし、
内容が違う既存文書では競合として停止する方式です。

SwallowKit の `dev --seed-env` で Cosmos DB Emulator に投入する合成データです。
生成元はオフライン専用の `tools/SeedData`、正本の型は `shared/models` です。

| 環境名 | 初期状態 | 確認する動作 |
| --- | --- | --- |
| `a1-new` | A1、履歴・習熟度・復習プランなし | 初回のおすすめ演習、空の履歴、文法検索 |
| `a2-review-ready` | 基礎が得意、a/di・近過去が苦手、会話分析済み、復習は未回答 | ホームの弱点・根拠、会話から作られた復習への導線 |
| `a2-review-in-progress` | 同じ弱点・会話、復習を 2 問正解済み | 再読み込み・再起動後の再開、既回答と未回答の区別 |
| `a2-review-completed` | 復習を全問正解して終了 | 結果・履歴、復習プランの完了、次のおすすめ |

すべて合成データで、学習者 ID は `local-demo-learner` です。
文法・講師・サンプル問題もそれぞれ `GrammarTopics` / `TutorProfiles` / `SampleQuestions` に投入し、
バックエンドは最初の学習 API リクエストで DB から検証して読み込みます。
空・不正なマスターなら学習 API は `503 / reference-data-unavailable` を返しますが、
host と初期投入関数は起動できます。投入後は次のリクエストで読み込みを再試行します。
`PracticeQuestion` / `TutorMessage` は親文書に内包されるため、独立した
シードファイルは作りません。Messaging API を通していないため、
`ProcessedEvents` のシードも不要です。

## SwallowKit の Cosmos DB シード

JSON は `dev-seeds/<environment>/<model-kebab-case>.json` に配列として保存します。
ファイル名はモデルの単数 kebab-case 名です。`learning-profile.json`、
`learning-observation.json`、`practice-session.json`、`tutor-session.json`、
`review-plan.json`、`workflow-run.json`、`grammar-topic.json`、`tutor-profile.json`、
`sample-question.json` の 9 ファイルを使います。参照データの partition は `language` です。
`LearningProfiles.json` のようなコンテナー名では SwallowKit がモデルに対応付けられません。

適用は対象コンテナーを再作成するため、その中の全学習者データが失われます。
既存データのある DB や本番には実行しないでください。

まず `functions/local.settings.json` の接続先が localhost の
Emulator で、`COSMOS_DB_DATABASE_NAME` が**テスト専用 DB**であることを確認します。
起動ログで対象の DB 名を確認してください。

PowerShell では次を実行します。

```powershell
$env:Learning__LocalUserId = 'local-demo-learner'
pnpm exec swallowkit dev --seed-env a2-review-ready
```

zsh / Bash では次を実行します。

```sh
Learning__LocalUserId=local-demo-learner pnpm exec swallowkit dev --seed-env a2-review-ready
```

アプリも SwallowKit 標準の `COSMOS_DB_DATABASE_NAME` を使います。
コンテナーは SwallowKit がモデル定義から作成します。シードがないコンテナーの既存データは変更しません。

## 再生成・検査

```sh
pnpm run seeds:export
pnpm run seeds:check
```

export は既存のシード JSON を再生成しますが、実行中のアプリや DB は変更しません。
テスト専用の固定応答と InMemory でシナリオを作り、共有 JSON Schema で検証し、
別の InMemory ストアへ JSON を読み戻してレベル・回答数・復習状態を確認します。
時計は固定し、シードに保存する tool call の処理時間は 0 に正規化します。
check は Zod 検証に加え、学習者 ID、重複 ID、復習の参照整合性を検査します。
文法・ルール・講師への参照と、上記 9 モデルの単数 kebab-case ファイル名も検査します。
生成された JSON はレビューしてコミットし、個人データを混ぜないでください。

シードの生成元は `tools/SeedData/Data/*.json` と `tools/SeedData` の固定応答です。

## 本番の教材セット

`production` は文法・講師・サンプル問題だけです。架空学習者の履歴は含みません。[GitHub Actions での初期投入](../docs/cloud-seed-initialization.md)で使用します。
