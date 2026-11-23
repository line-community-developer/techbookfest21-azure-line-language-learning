# ハンズオンを始める前に

[README](../README.md) → **準備** → [Azure の構築](azure-deployment.md)

Web アプリのコードは用意済みなので、自分の GitHub と Azure に配置し、LINE と接続して動かします。
Azure のアカウント作成・ツール導入・権限申請・クォータ申請には待ち時間が生じることがあります。準備を先に済ませてください。

## 1. アカウントと料金

1. Azure アカウントがなければ [Azure 無料アカウント](https://azure.microsoft.com/ja-jp/free/)の条件を確認して作成します。このサンプル全体が無料になるわけではありません。
2. [Azure ポータル](https://portal.azure.com/)へログインし、上部検索から「サブスクリプション」を開きます。使用する契約名とサブスクリプション ID を確認します。
3. 対象サブスクリプション →「アクセス制御 (IAM)」→「アクセスの確認」で自分の権限を確認します。新規リソースグループ作成とロール割り当てが必要です。会社・学校の環境では管理者に [Azure 手順の前提条件](azure-deployment.md#1-前提条件)を伝えて確認します。
4. 必要に応じて、対象サブスクリプションの Cost Management から予算と通知先を設定します。通知は支出の自動停止ではありません。

[権限の確認](https://learn.microsoft.com/ja-jp/azure/role-based-access-control/check-access) / [予算の作成](https://learn.microsoft.com/ja-jp/azure/cost-management-billing/costs/tutorial-acm-create-budgets)

「サブスクリプション」は利用契約・課金の単位、「リソースグループ」は対象のサービス一式をまとめる箱、「リソース」は Function App やデータベース等の個々のサービスです。
「リージョン」は配置地域、「Managed Identity」は Azure サービス自身に与える ID、「RBAC」はその ID や利用者に操作権限を割り当てる仕組みです。

## 2. Fork して Codespaces を起動する

1. [本リポジトリ](https://github.com/line-community-developer/techbookfest21-azure-line-language-learning)を Fork し、自分のアカウントへ複製します。既定ブランチは main を使います。
2. **自分の Fork** の Code → Codespaces → Create codespace on main を選びます。
3. ブラウザーでエディターが開いたら、コンテナーの作成と依存パッケージのインストールが終わるまで待ちます。
4. Terminal → New Terminal でターミナルを開きます。package.json と README.md があるリポジトリ直下で、以下を実行します。

```sh
node --version
pnpm --version
dotnet --list-sdks
az version
az bicep version
git branch --show-current
```

Node.js 22、pnpm 11、.NET SDK 10.0 と Azure CLI / Bicep が表示され、ブランチが main であることを確認します。
必要なツールは [.devcontainer/devcontainer.json](../.devcontainer/devcontainer.json) で導入され、依存パッケージのインストールと共有パッケージのビルドも自動で行います。

起動時にエラーが出た場合はセットアップのログを確認します。ツールは使えるもののパッケージの取得に失敗した場合は、ターミナルで `pnpm install --frozen-lockfile`、続けて `pnpm --filter shared run build` を実行します。
コンテナー設定を変更した場合は、コマンドパレットから Codespaces: Rebuild Container を実行します。

自分の PC で進める場合は [ローカル PC の準備](local-handson-preparation.md)を参照してください。

Codespaces の利用にはコンピューティングとストレージの使用量が計上されます。[利用枠と料金](https://docs.github.com/ja/billing/concepts/product-billing/github-codespaces)を確認してください。
[Fork の作成](https://docs.github.com/ja/pull-requests/collaborating-with-pull-requests/working-with-forks/fork-a-repo) / [Codespaces の作成](https://docs.github.com/ja/codespaces/developing-in-a-codespace/creating-a-codespace-for-a-repository)

## 3. GitHub Actions を有効にする

自分の Fork の Actions タブを開きます。ワークフローが無効になっている場合は、表示される案内に従って有効化します。

準備ができたら [Azure の構築](azure-deployment.md)へ進みます。

## 作業を終えるとき

編集したファイルがあれば commit / push してから、[Codespaces 一覧](https://github.com/codespaces)で対象のメニューから Stop codespace を選びます。ブラウザーを閉じるだけで即停止するとは限りません。
続きは一覧から同じ Codespace を開きます。Azure のログインが切れている場合はログインし直します。
provision の途中で切断した場合も同じ Codespace を再開し、[Azure 手順](azure-deployment.md)の状態確認・再開コマンドを使います。
作業中の Codespace を削除すると、未 push の変更や構築処理のチェックポイントも失われます。
すべての作業が終わり必要な変更を push したら、不要な Codespace を削除します。停止中もストレージの使用量は計上されます。
[停止・再起動](https://docs.github.com/ja/codespaces/developing-in-a-codespace/stopping-and-starting-a-codespace) / [削除](https://docs.github.com/ja/codespaces/developing-in-a-codespace/deleting-a-codespace)
