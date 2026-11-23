# 機能ガイドと SwallowKit の役割

サンプルコードを読む方向けに、利用者が行う操作とコードの役割をまとめます。
SwallowKit はアプリの構造・認証導入・CRUD・インフラ定義・検証を支えます。
学習や AI の業務処理は、このプロジェクトが実装したものです。

## 利用者向け機能

| 機能 | 入口 | 振る舞い | 主なコード |
| --- | --- | --- | --- |
| ホーム・復習案内 | `/miniapp` | 習熟度・弱点・復習プランから次の演習へ進む | カスタム React、`src/learning/` |
| 問題演習 | `/miniapp/practice` | 文法・レベルなどを選び、公開問題または AI 生成問題に回答。決定論的に採点し履歴と習熟度を更新 | カスタム UI と `functions/Learning/Practice/` |
| 学習履歴 | `/miniapp/history` | 演習結果と会話のふりかえりを確認 | カスタム UI・学習者ごとに絞り込む API |
| 文法の参照 | `/miniapp/grammar` | 文法項目・説明・例文・根拠を閲覧 | カスタム UI と文法検索 |
| AI 講師との会話 | LINE 公式アカウントのトーク | 講師と対話し、MCP ツール経由で学習データを参照・更新 | `functions/Learning/Messaging/`・`Tutor/`・保存済み Foundry Agent。アプリ独自実装 |
| 会話のふりかえり | LINE の会話終了後、`/miniapp/conversations/:sessionId` | 会話から弱点候補を分析し、根拠を確認して復習プラン・問題を作成 | カスタム分析・Durable ワークフロー。`Analysis/`・`Workflows/` |

LIFF の初期化・LINE ID トークン検証はアプリが実装しています。
認証の枠組みは SwallowKit の名前付き external-token スキームを使い、LINE の ID トークンを検証します。
業務 API は検証済みの主体から学習者 ID を導きます。標準 CRUD はユーザーごとにデータを絞り込まないため、個人の履歴には使っていません。

## 管理者向け機能

| 機能 | 入口 | 主なコード |
| --- | --- | --- |
| 管理者ログイン・メニュー | `/admin/login` → `/admin` | 認証基盤は SwallowKit の SWA スキーム。画面・共通ナビゲーション・SWA ルート設定はアプリが実装 |
| AI 問題生成・品質検証・公開保存 | `/admin/questions/generate` | カスタム UI・BFF・C# 業務 API |
| 文法マスター CRUD | `/grammar-topic` | SwallowKit Scaffold |
| 問題マスター CRUD | `/sample-question` | SwallowKit Scaffold |
| 講師マスター CRUD | `/tutor-profile` | SwallowKit Scaffold |

操作方法、入力例、編集反映の条件、権限、再生成手順は [管理画面](admin.md) を参照してください。

## コードを読むときの境界

| 対象 | SwallowKit が担う範囲 | プロジェクトが担う範囲 |
| --- | --- | --- |
| 共有モデル | Zod を解析し CRUD・OpenAPI・C# スキーマへ展開 | `shared/models/` の業務フィールド・認可ポリシー |
| 管理 CRUD | `src/pages/`、`bff/src/{model}/`、`functions/Crud/` を scaffold で生成 | 管理メニュー、シェル、機能説明、SWA での保護・ルート合成 |
| 学習・AI・LINE | React/BFF/Functions の基盤、認証の足場 | `src/learning/`、`bff/src/learning/`、`functions/Learning/` の業務実装 |
| 契約検証 | Scaffold が標準 CRUD の検証コードを生成 | `shared/contracts/` と JSON Schema 書き出しによるカスタム API・保存データ・AI 出力の検証 |
| Cosmos DB・Azure | Bicep、モデルのコンテナ migration、provision の承認フロー | 追加連携の migration、環境設定、承認後の配備・外部サービス設定 |
| シード | `dev --seed-env` による投入 | シナリオ・参照データの内容、`tools/SeedData/` による生成 |
| 検証 | `pnpm exec swallowkit verify` による構造・drift・型・バックエンドビルド | 業務テスト・契約テスト・シード検査を verify に追加。実機での LINE・Azure 確認 |

マスター CRUD の業務上の参照整合性、AI 品質検証、公開条件、連動削除は Scaffold が保証するものではありません。
生成済みファイルは直接修正せず、モデルや設定を変更して `pnpm exec swallowkit scaffold <model>` で再生成します。`pnpm exec swallowkit status --artifacts` で生成物の管理情報を確認できます。

## 実行と検証

AI 接続が未設定の場合、新しい問題の生成・講師との会話・会話分析は利用できません。
保存済みの公開問題による演習と、履歴・マスターの参照はそれぞれ必要なデータが揃っていれば利用できます。

- [ローカル開発](local-development.md)：起動・データ投入・検証。
- [アーキテクチャ](architecture.md)：レイヤーの責務・シーケンス・データモデル。
- [LINE 設定](line-setup.md)・[Foundry 設定](foundry-agent-setup.md)：外部サービスの接続。
- [実機確認](real-device-test.md)：ローカル検証だけでは確認できない配備先での操作。
