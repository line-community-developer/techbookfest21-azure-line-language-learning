# Azure 本番環境への初期シード投入

[README](../README.md) / 前：[コードの配備](github-deployment.md) / 次：[Foundry の設定](foundry-agent-setup.md)

SwallowKit が用意した [Initialize Azure Cosmos DB seed data](../.github/workflows/initialize-cosmos-seeds.yml) を GitHub Actions から実行します。
投入用のスクリプトを自分で書く必要はありません。**本番初回は `production` を選びます。**

## 1. 前提を確認する

- 新規環境の provision と、通常の Functions 配備が成功している。
- リポジトリの `main` に [dev-seeds/production](../dev-seeds/production/) がある。
- Secrets の `AZURE_FUNCTIONAPP_NAME` と `AZURE_FUNCTIONAPP_PUBLISH_PROFILE` が設定済み。
- Function App の `COSMOS_DB_DATABASE_NAME` と `CosmosDBConnection__accountEndpoint` が対象の Cosmos DB を指す。
- 一時配備と復元で再起動するため、利用開始前または利用を止められる時間に実施する。

Cosmos DB の公開アクセスを有効にする必要はありません。
**VNet 内の既存 Functions とその Managed Identity で投入する**ため、開発 PC や Actions ランナーへ Cosmos DB の接続キーを配ることなく、通常と同じ権限・ネットワークで書き込めます。

## 2. ワークフローを実行する

GitHub の **Actions → Initialize Azure Cosmos DB seed data → Run workflow** を開きます。

| 入力 | 初回の値 |
| --- | --- |
| Branch | `main` |
| `seed_name` | `production` |
| `function_app_name` | Secret の `AZURE_FUNCTIONAPP_NAME` と完全一致する実リソース名 |
| `force` | **オフ（false）** |

**Run workflow** を押します。`production` は文法 15 件、講師 1 件、サンプル問題 69 件の計 85 件です。
実行一覧の新しい行を開き、「Validate, initialize, and restore Functions」を選ぶと進捗とログを確認できます。[手動実行の公式手順](https://docs.github.com/ja/actions/how-tos/manage-workflow-runs/manually-run-a-workflow)
学習プロフィール・会話・演習履歴は含みません。LINE 認証済みの利用者のプロフィールはアプリが作成します。
`a1-new` / `a2-*` は架空学習者を含むローカル・検証用シナリオです。

## 3. 成功を確認する

実行詳細で次を確認します。

1. **Validate selected seed and generate temporary Function** が成功。
2. **Initialize Cosmos DB** が成功し、`created / skipped / conflicts / failed / errorCategory` に競合・失敗がない。空 DB への初回なら計 85 件を作成。
3. **Restore ordinary Functions package** が成功。

投入と通常パッケージへの復元が完了したら [Foundry の設定](foundry-agent-setup.md)へ進みます。

ワークフローは通常パッケージを保持し、検証済みデータと一時投入関数を含むパッケージを配備して投入し、その後通常パッケージへ戻します。
一時トークンは 15 分有効、ログではマスクされ、パッケージにはハッシュだけを埋め込みます。
投入関数は通常のコードに残りません。

## 再実行・失敗時

`force=false` では同じ ID・パーティションの同一内容は `skipped`、未作成なら `created`、異なる内容は 409 の競合です。
`force=true` はシード内の項目を作成・上書きします。シードにない項目は消しません。既存教材を意図せず戻すため、初回のチェックは付けません。

全コンテナーをまたぐ一括トランザクションではありません。失敗・タイムアウトでは一部作成済みの可能性があるため、結果を確認してから再実行を判断します。
上限は 500 文書、JSON 合計 4 MiB、投入処理 180 秒です。
復元に失敗した場合は **Deploy Azure Functions** を `main` から手動実行し、通常コードへ戻ったことを確認します。

復元対象は実行時の main をビルドしたものです。直前に Azure で稼働していた旧バイナリーを保存する仕組みではありません。
通常の Functions 配備とは同じ concurrency group で直列化します。

参照マスターは読み込み後にキャッシュされます。管理画面から教材を更新した後は必要に応じて Functions を再起動します。
