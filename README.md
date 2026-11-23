# Language Learning

LINE ミニアプリで問題を解き、LINE Bot の AI 講師と外国語で会話するサンプルアプリです。初期教材は日本語話者向けのイタリア語です。
書籍『LINEでできること地図』第3章「LINEとAzureサーバーレスでつくるAI外国語学習アプリ」の参照実装です。

章の最後で紹介した SwallowKit を使って初期化されたプロジェクトに対し、GitHub Copilot / Codex を活用して実装を進めたアプリです。

ミニアプリの演習・採点・履歴と、Bot の会話・弱点分析・復習問題を、同じ学習者の記録として扱います。
React / TypeScript、C# / .NET 10、Azure Static Web Apps、Azure Functions、Cosmos DB、Microsoft Foundry を使用します。

## Azure で動かす

以下の順に進めてください。

1. [ハンズオンの準備](docs/handson-preparation.md) — Fork と Codespaces の起動
2. [Azure リソースの構築](docs/azure-deployment.md) — SwallowKit で配備先を作成
3. [LINE の設定](docs/line-setup.md) — チャネル作成と接続設定
4. [コードの配備](docs/github-deployment.md) — GitHub Actions の実行と管理者の招待
5. [初期シード投入](docs/cloud-seed-initialization.md) — 教材を Cosmos DB に登録
6. [Foundry の設定](docs/foundry-agent-setup.md) — AI 講師と MCP の接続
7. [動作確認と片付け](docs/real-device-test.md) — LINE で演習・会話・復習を試す

Codespaces を使わず、自分の PC から進めたい場合は [ローカル PC の準備](docs/local-handson-preparation.md)を参照してください。

SwallowKit が生成した基盤を使うことで、画面・BFF・独立 Functions の配備とインフラ管理をそろえられます。

利用量に応じた Azure の料金が発生します。モデルの地域・クォータを事前に確認し、検証後は不要な検証用リソースを削除してください。

## 書籍とコードを読み比べる

- [書籍の設計とコードの対応表](docs/book-code-guide.md) — Azure のメリットとアプリ固有の工夫を追う入口
- [アーキテクチャ](docs/architecture.md) / [全体構成図](public/architecture/index.html)
- [機能ガイドと SwallowKit の役割](docs/features.md)
- [管理画面](docs/admin.md) — GitHub 認証、教材 CRUD、AI 問題生成
- [初期インフラの構成と変更方法](docs/infrastructure.md)

## ローカル環境でのデバッグ実行（任意）

手元でアプリを起動・変更する場合は [ローカル開発手順](docs/local-development.md)を参照してください。
起動方法、開発用データ、ビルド・テストの実行方法をまとめています。

## ディレクトリ

| 場所 | 役割 |
| --- | --- |
| `src/`、`lib/` | 学習・管理画面と認証 |
| `bff/` | SWA の HTTP 専用中継層。DB に直接アクセスしない |
| `functions/Learning/` | 学習・LINE・AI・MCP・Durable の業務処理 |
| `shared/models/`、`shared/contracts/` | Zod モデルと API・AI・ツールの契約 |
| `infra/` | 初回構築用 Bicep と SwallowKit 台帳 |
| `.github/workflows/` | SWA/BFF 配備、独立 Functions 配備、初期シード投入 |
| `.devcontainer/` | Codespaces のツールと初期セットアップ |

[SwallowKit](https://github.com/himanago/swallowkit) は共有スキーマを起点に生成・検証する開発ツールです。このサンプルは `2.0.0` に固定しています。
LINE・AI の業務処理はアプリ固有の実装です。変更方法は [ローカル開発](docs/local-development.md)と[インフラ](docs/infrastructure.md)を参照してください。
