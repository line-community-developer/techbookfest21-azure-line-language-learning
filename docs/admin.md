# 管理画面

管理者は `/admin/login` から GitHub でログインし、`/admin` の管理者メニューから各機能を開きます。
標準 CRUD の URL はモデル名に基づくパスを保ち、全画面に共通の管理ナビゲーションを表示します。

## 画面と機能

| 画面 | URL | できること | SwallowKit のサポート範囲 |
| --- | --- | --- | --- |
| 管理者メニュー | `/admin` | 問題作成・各マスターへの移動、ログアウト | アプリ固有の React 実装。標準 CRUD と同じ Tailwind の配色・カード・ライト／ダーク表示に合わせた |
| AI 問題生成 | `/admin/questions/generate` | 候補の生成、AI 品質検証、人による確認、問題バンクへの保存 | カスタム UI・BFF・C# 業務 API |
| 文法マスター | `/grammar-topic` | 文法項目の一覧・詳細・追加・編集・削除 | Zod モデルから Scaffold が UI・BFF・C# CRUD・OpenAPI・C# スキーマを生成 |
| 問題マスター | `/sample-question` | 参照サンプルと公開問題の一覧・詳細・追加・編集・削除 | 同上。AI 生成・品質検証・公開の業務処理は含まない |
| 講師マスター | `/tutor-profile` | 講師の一覧・詳細・追加・編集・削除 | 同上 |
| 管理者ログイン | `/admin/login` | GitHub 認証の開始 | 画面はカスタム。認証基盤は SwallowKit の名前付き admin（SWA）スキーム |

標準 CRUD は英語ラベル、複合オブジェクト・オブジェクト配列は JSON 入力です。
文字列配列はカンマ区切りで入力します。
共通ヘッダー・ナビゲーションは `src/admin/` と `src/App.tsx` に定義しています。

## AI で問題を生成・保存する

1. メニューの「AI で問題を生成」を開き、文法項目と問題数（1〜10）を選んで「生成する」を押します。
2. 候補ごとに問題文、選択肢、正答、許容解答、解説を確認します。生成だけでは保存されません。
3. 「AI で品質検証」を実行するか、内容を確認して「人が確認して承認」にチェックします。
4. AI 検証に合格した候補、または人が確認した候補で「保存する」を押します。

保存時には、空欄、正答、選択肢、参照する文法ルールなど、出題に必要な条件をサーバー側でも検証します。
同じ候補の再保存は重複登録されません。保存先は Cosmos DB の `SampleQuestions` です。
生成・AI 品質検証には Foundry の設定が必要です。未接続・参照データ不足・通信失敗は画面に表示します。

ミニアプリは、条件に合う公開問題があればそれを使って新しい演習を開始します。
公開問題が指定件数より少ない場合は利用できる件数で開始し、該当する公開問題がない場合は AI 生成を試みます。
すでに開始した演習の問題は、マスター編集で差し替わりません。

## 文法マスター

保存先は `GrammarTopics`、パーティションキーは `/language` です。
`schemaVersion` は `1`、学習言語は `it` です。

- `rules` は `[{"id":"rule-1","summaryJa":"ルールの説明","pattern":null}]` のような JSON 配列です。
- `examples` は `[{"id":"example-1","text":"Il libro.","translationJa":"その本。","noteJa":null}]` のように入力します。
- `sourceReferences` は `[{"id":"source-1","kind":"original-note","label":"編集ノート","url":null}]` などです。
- `commonMistakes` を空にする場合は `[]` を入力します。`relatedTopics` と `keywords` はカンマ区切りです。
- `reviewStatus` は編集上の確認記録です。学習者への公開・非公開の切り替えではありません。

問題の `grammarTopicId`・`ruleIds`、公開問題の `publication.sourceIds` などが文法データを参照します。
標準 CRUD は参照整合性の検査や連動削除を行いません。利用中の項目・ルールの削除や ID の入れ替えは参照先を確認してから行ってください。
学習機能の読み込み時には参照整合性を検証し、不整合があると `reference-data-unavailable` になります。

## 問題マスター

保存先は `SampleQuestions`、パーティションキーは `/language` です。同じモデルに二種類の問題を保存します。

| 種類 | 見分け方 | 利用方法・編集時の注意 |
| --- | --- | --- |
| 参照サンプル | publication がない | AI 生成の参考にするデータ。標準 CRUD で追加するときは publication を空欄にする |
| 公開問題 | publication がある | 学習者の演習に使う問題バンク。AI 問題生成画面で作成する |

`grammarTopicId` は文法項目を選択し、`ruleIds` はその項目に存在するルール ID をカンマ区切りで指定します。
`acceptedAnswers` には少なくとも一つの許容解答が必要です。`choices` もカンマ区切りです。

公開情報には指示文、出典、生成元、品質検証結果を保持します。
標準 CRUD での編集は Zod の構造検証のみで、AI 品質検証や公開時の業務検証を再実行しません。
公開問題を直接編集する場合は、正答・選択肢・参照ルール・公開情報を合わせて確認してください。
公開データの新規作成は、サーバー側の業務検証を通す AI 問題生成画面を推奨します。

## 講師マスター

保存先は `TutorProfiles`、パーティションキーは `/language` です。

- `schemaVersion` は `1`、`promptTemplateId` は同梱の `tutor.it-giulia.system` を指定します。
- `correctionPolicy` の例は `{"maxCorrectionsPerTurn":1,"style":"recast","explainInJapanese":true}` です。
- 会話履歴が講師 ID を参照するため、利用済みの講師は編集で管理してください。各学習言語に少なくとも一人の講師が必要です。
- 名前を変更し、LINE アイコンを使用している場合は、新しい名前を小文字にしたファイル名の PNG をアイコン配信先に用意します。

## 保存後の反映

文法・講師・参照サンプルは `ReferenceDataLoader` が初回読み込みのあとキャッシュします。
CRUD で保存した内容を学習機能や問題生成に反映するには、standalone Functions の再起動が必要です。
CRUD の一覧・詳細は DB から取得するため、保存した内容をそのまま確認できます。
公開問題は演習開始時に DB から読むため、その編集・削除は次回演習から反映されます。

シード JSON の編集と管理画面からの DB 編集は別の操作です。DB の変更は `dev-seeds/` に自動書き戻しされません。
`dev --seed-env` は対象 DB のデータを置き換えるため、管理画面で編集したデータがある環境では対象を確認してください。

## 権限と API

すべてのマスターで `authPolicy = { policy: 'adminOnly' }` を宣言しています。
adminOnly は admin スキームと administrator ロールを要求します。LINE 学習者のトークンだけではアクセスできません。
SWA の画面・API ルート、React の管理シェル、バックエンドの生成された認可チェックで保護します。
生成 CRUD は管理者に全レコードの操作を許可します。所有者による絞り込みは行わないため、学習履歴などの個人データには公開していません。

| 公開 API | 用途 |
| --- | --- |
| /api/grammarTopic、/api/grammarTopic/:id | 文法 CRUD |
| /api/sampleQuestion、/api/sampleQuestion/:id | 問題 CRUD |
| /api/tutorProfile、/api/tutorProfile/:id | 講師 CRUD |
| /api/admin/grammar | 問題生成に使う文法一覧 |
| /api/admin/questions/generate・validate・publish | 候補生成・品質検証・公開保存 |

Azure Functions が予約している admin を避けるため、SWA は `/api/admin/*` の既存四経路を `/api/management/*` に書き換えます。
両方の経路に管理者権限が必要です。
標準の問題フォームが文法の参照候補を取得する `/api/grammar-topic` は、SWA で `/api/grammarTopic` に書き換えます。
参照候補の取得要求は、このルート設定で各モデルの CRUD API へ転送します。

## 管理者の招待

1. Azure ポータルの対象 Static Web Apps の **Role Management** から **Invite** を選びます。
2. プロバイダー **GitHub**、対象ユーザー、サイトのドメイン、ロール administrator を指定します。
3. 対象ユーザーが招待を受け入れ、サイトの `/admin/login` からログインします。

## ローカルでの開発・テスト（任意）

起動済みの Cosmos DB Emulator と、対象 DB に投入済みの参照データを用意します。
`pnpm exec swallowkit dev` で起動し、`http://localhost:4280/admin/login` を開きます。
SWA CLI の認証エミュレーターで GitHub ユーザーに administrator を付けます。
Vite の 3000 番ポートでは SWA の認証・書き換えを通らないため、必ず 4280 を使ってください。

コードを変更した場合は次を確認します。

- 未ログイン・管理者ロールなしの GitHub ユーザーが、管理画面と全 CRUD API で拒否される。
- 管理者が各画面へ直接アクセス・再読み込みでき、共通メニューから往復できる。
- テスト専用 DB で各マスターの作成・詳細・編集・削除、必須入力・JSON 不正時のエラーを確認する。
- AI 候補生成、AI 検証失敗、人による確認、保存、新しい演習での出題を確認する。
- 既存 LINE ログイン・ミニアプリ・Bot の操作を確認する。

実際の GitHub 招待・LINE 連携は配備先で確認が必要です。

## モデルから CRUD を再生成する

モデルのスキーマと認可方針は shared/models/ を編集します。管理者シェルと業務 API はアプリ固有コードです。

```sh
pnpm exec swallowkit status
pnpm exec swallowkit scaffold grammar-topic
pnpm exec swallowkit scaffold sample-question
pnpm exec swallowkit scaffold tutor-profile
pnpm run build
pnpm exec swallowkit verify
```

変更内容を確認してから生成を実行してください。生成済みファイルを直接修正せず、モデルや設定を変更して再生成します。
責務の詳細は [アーキテクチャ](architecture.md)を参照してください。
