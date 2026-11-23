# ローカル PC からハンズオンを進める

[準備ガイド](handson-preparation.md)で Codespaces を使う代わりに、自分の PC にツールを導入して進める場合の手順です。
先に準備ガイドのアカウントと料金を確認してください。

## 1. ツールを導入する

以下の公式手順で導入します。導入後はターミナルを開き直します。

| ツール | 導入先 | 用途 |
| --- | --- | --- |
| Git | [公式ダウンロード](https://git-scm.com/downloads) | リポジトリの取得・変更保存 |
| Node.js | [公式ダウンロード](https://nodejs.org/en/download) | Node.js 22.14 以上。フロントと SwallowKit の実行 |
| pnpm | [公式インストール](https://pnpm.io/installation) | 本プロジェクトでは 11 または 12 を使用 |
| .NET SDK | [.NET 10 ダウンロード](https://dotnet.microsoft.com/ja-jp/download/dotnet/10.0) | Runtime だけでなく SDK を導入 |
| Azure CLI | [OS 別の導入手順](https://learn.microsoft.com/ja-jp/cli/azure/install-azure-cli) | Azure にログインして構築 |

自分の OS に合うインストーラーと CPU の種類（例：Apple Silicon は Arm64）を選びます。[.NET の OS 別導入手順](https://learn.microsoft.com/ja-jp/dotnet/core/install/)も参照できます。
ターミナルで以下を一行ずつ実行し、バージョンが表示されることを確認します。

```sh
git --version
node --version
pnpm --version
dotnet --list-sdks
az version
az bicep install
az bicep version
```

Windows の PowerShell で `pnpm.ps1` が実行ポリシーで拒否されたら `pnpm.cmd` を使います。`command not found` や「認識されません」なら、導入とターミナルの開き直しを確認します。
Azure に配置して動かすだけなら Cosmos DB Emulator・Azurite・Functions Core Tools の導入は不要です。これらはローカルでアプリを起動する場合に使います。

## 2. Fork と clone

1. [本リポジトリ](https://github.com/line-community-developer/techbookfest21-azure-line-language-learning)を Fork し、自分のアカウントへ複製します。
2. 自分のリポジトリの Code → HTTPS から URL をコピーし、作業フォルダーで次を実行します。

```sh
git clone <自分のリポジトリの HTTPS URL>
cd techbookfest21-azure-line-language-learning
git branch --show-current
pnpm install --frozen-lockfile
```

フォルダー名が異なる場合は実際の名前へ移動します。`package.json` と `README.md` がある場所が以後のコマンドの実行場所です。
既定ブランチは `main` を使用します。Fork の Actions タブで実行が無効なら、有効化の案内を確認します。

[Fork の作成](https://docs.github.com/ja/pull-requests/collaborating-with-pull-requests/working-with-forks/fork-a-repo) / [リポジトリの clone](https://docs.github.com/ja/repositories/creating-and-managing-repositories/cloning-a-repository)


準備ができたら [Azure の構築](azure-deployment.md)へ進みます。ログインは通常の `az login` でも行えます。
アプリ自体を手元で起動する場合は [ローカル開発](local-development.md)も参照してください。
