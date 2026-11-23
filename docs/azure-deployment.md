# Azure リソースを構築する

[README](../README.md) / 前：[ハンズオンの準備](handson-preparation.md) / 次：[LINE の設定](line-setup.md)

自分の GitHub リポジトリと、新規の Azure リソースグループに Language Learning を構築します。
SwallowKit の provision で、アプリの配備先となる Azure リソースを作成します。

## 1. 前提条件

| 項目 | 条件 |
| --- | --- |
| Azure | リソース作成とロール割り当てができるサブスクリプション。例：対象範囲の Contributor と Role Based Access Control Administrator |
| Foundry | 選択地域のモデル利用権・クォータ。プロジェクト作成後に、講師を登録する作業者へ開発権限を設定できること |
| Node.js / pnpm | Node.js 22.14 以上、pnpm 11 または 12。Actions は Node.js 22 / pnpm 11 |
| .NET SDK | 10.0 |
| Azure CLI / Bicep | Azure へのログイン、テンプレート検証・配備 |
| LINE | Developers と公式アカウントを設定できるアカウント |

コマンドは Codespaces のリポジトリ直下のターミナルで実行します。自分の PC を使う場合も、[ローカル PC の準備](local-handson-preparation.md)を済ませれば同じ手順で進められます。

```sh
pnpm exec swallowkit --version
```

続けて Azure にログインします。

```sh
az login --use-device-code
```

表示された URL をブラウザーで開き、表示されたコードを入力して Azure のアカウントでログインします。
ターミナルで使用するサブスクリプションの選択が求められたら、準備ガイドで確認したサブスクリプションの番号を指定してください。

以下のコマンドで、サブスクリプションの選択状況と Bicep のバージョンを確認してください。

```sh
az account show --query "{name:name,id:id}" -o table
az bicep version
```

組織のポリシーでデバイスコードのログインが禁止されている場合は、管理者に利用可能な認証方法を確認してください。
[Azure CLI の対話的ログイン](https://learn.microsoft.com/cli/azure/authenticate-azure-cli-interactively)

Bicep が未導入なら `az bicep install` で準備します。

## 2. 初回パラメーターを設定する

[`infra/main.parameters.json`](../infra/main.parameters.json) の `parameters.projectName.value` を自分だけの識別名に変えます。
例：`ll-yourname-01`。小文字英数字とハイフン、英数字で開始・終了、3〜30 文字にします。
Functions・Cosmos DB 等に使う名前なので、他の利用者と重複しないものを選びます。

既定は SWA Free、Cosmos DB Free Tier、VNet 有効です。Cosmos DB については、Free Tier は 1 つのサブスクリプションで 1 アカウントまでしか使用できません。そのため、すでに Free Tier を有効にした Cosmos DB アカウントが存在する場合は、`cosmosDbMode` の値を `serverless` に変更してください。

パラメーターを保存したら、初回構築用の台帳を初期化します。

```sh
pnpm exec swallowkit migrations init --legacy-baseline
```

次の手順でリソースを作成します。

```sh
git add infra/main.parameters.json
git commit -m "Configure my Azure environment"
git push origin main
```

## 3. SwallowKit で構築する

リソースグループには**まだ存在しない名前**を指定します。次の例の `rg-ll-yourname-01` を自分の名前に置き換えてください。空の RG を先に作る必要はありません。

```sh
pnpm exec swallowkit provision --resource-group rg-ll-yourname-01 --location japaneast --swa-location eastasia
```

地域は例です。SWA / Flex Consumption の提供地域、Foundry のクォータに合わせて選びます。
一度の provision で、Bicep の依存関係に従って以下を作ります。

- SWA、独立 Functions の Flex Consumption、BFF 用のサーバー側接続設定
- Cosmos DB と 11 コンテナー、VNet と Private Endpoint
- Functions の Managed Identity、Cosmos DB / Storage Blob・Queue・Table の権限
- Foundry アカウント、プロジェクト `learning`、モデル `learning`、用途別 RBAC
- Application Insights と Log Analytics

**Azure に実行基盤の運用を任せながら、ネットワークと権限までコードで再現できます。**
講師の指示と MCP 接続は [Foundry の設定](foundry-agent-setup.md)で登録します。

### provision 完了直後：GitHub Actions の Secrets を登録する

成功時にターミナルへ表示される `CI/CD settings` の 3 項目を、ここで自分の GitHub リポジトリに登録します。SwallowKit は表示した認証情報を保存しないため、ターミナルを閉じる前に登録してください。

1. GitHub で自分のリポジトリを開き、**Settings → Secrets and variables → Actions** を選びます。
2. **Secrets** タブの **Repository secrets → New repository secret** を開きます。Codespaces 用の Secrets や Actions の Variables ではありません。
3. 下表の Name と値を入力して **Add secret** を押し、3 件とも登録します。角括弧付きの見出しは値に含めません。

| Name | Secret に貼り付ける値 |
| --- | --- |
| `AZURE_STATIC_WEB_APPS_API_TOKEN` | 同名の見出しの下に表示されたトークン全体 |
| `AZURE_FUNCTIONAPP_NAME` | 同名の見出しの下に表示された Function App 名。URL ではなく `func-…` のリソース名 |
| `AZURE_FUNCTIONAPP_PUBLISH_PROFILE` | 同名の見出しの下に表示された `<publishData>` から `</publishData>` までの XML 全文。パスワード部分だけではなく、全プロファイルを含める |

Repository secrets にこの 3 つの名前が並んでいることを確認します。保存後の秘密値は再表示できません。
[GitHub の Secrets 登録](https://docs.github.com/ja/actions/how-tos/write-workflows/choose-what-workflows-do/use-secrets)

なお、provision コマンドおよび Secrets の登録だけではアプリのコードはデプロイされません。続く [LINE の設定](line-setup.md)で GitHub Variables も登録してから、[コードの配備](github-deployment.md#2-配備ワークフローを実行する)で **Deploy Azure Functions** と **Deploy React SPA and managed BFF** を `main` から手動実行したときに、はじめてデプロイされます。

### Azure リソースの URL を控える

Azure ポータルで対象のリソースグループを開きます。「デプロイ」で成功を確認し、一覧から各リソースを開いて次の値を控えます。

| 値 | 取得する場所 | 使用する手順 |
| --- | --- | --- |
| SWA の URL | `swa-…` → Overview → URL | LINE ミニアプリのエンドポイント設定 |
| Function App 名・Default domain | `func-…` → Overview | 配備用 Secret・LINE Webhook 設定 |
| Foundry Project endpoint | Foundry でプロジェクト `learning` を開く → 概要 | 講師エージェントの設定 |

Functions の URL は名前から推測せず、実際のドメインを使います。

### 中断・タイムアウトした場合

中断・タイムアウトは Azure の失敗を意味するとは限りません。出力にある実行 ID（runId）を控え、同じターミナルから状態を確認します。

```sh
pnpm exec swallowkit provision --status <runId>
# 状態を確認し、元の処理を継続する場合
pnpm exec swallowkit provision --resume <runId>
```

`<runId>` は表示された ID に置き換えます。再開は同じ構築処理を追跡します。新しい provision を繰り返して実行しないでください。
既存リソースの採用や再構築を求められた場合は、指定したサブスクリプションと RG 名を確認してください。初回構築では、まだ存在しない RG 名を使います。

構築が完了したら [LINE の設定](line-setup.md)へ進みます。
