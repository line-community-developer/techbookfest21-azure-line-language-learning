# LINEミニアプリと Bot の設定

[README](../README.md) / 前：[Azure リソースの構築](azure-deployment.md) / 次：[コードの配備](github-deployment.md)

作成した Azure リソースの URL を使い、LINE のチャネルとアプリ設定を登録します。

**ミニアプリと Messaging API は同じプロバイダーに作成します。**
同一プロバイダーなら同じ利用者のユーザー ID が一致し、演習と会話の記録を結び付けられます。
ただし本人確認は別途必要です。このアプリはミニアプリの ID トークンと Bot の Webhook 署名を独立 Functions で検証します。
[LINE のユーザー ID の仕様](https://developers.line.biz/ja/docs/basics/user-id/)

## 1. チャネルを用意する

[LINE Developers コンソール](https://developers.line.biz/console/)でプロバイダーを選びます。

### Messaging API

1. LINE公式アカウントを作成し、公式アカウントマネージャーで Messaging API を有効にします。対象プロバイダーを選択します。
2. Developers コンソールの Messaging API チャネルの基本設定から **チャネルシークレット** を控えます。
3. Messaging API 設定で **チャネルアクセストークン（長期）** を発行します。
4. Webhook URL を `https://<実際の Functions の Default domain>/api/line/webhook` に設定します。SWA の URL ではありません。
5. 公式アカウントの応答設定で自動の応答メッセージ・あいさつメッセージをオフにします。アプリが返答します。
6. 友だち追加用 URL（`https://line.me/R/ti/p/@...`）を控えます。Webhook の有効化・検証はコード配備後に行います。

[Messaging API の開始手順](https://developers.line.biz/ja/docs/messaging-api/getting-started/)

### LINEミニアプリ

1. 同じプロバイダーに LINEミニアプリチャネルを作成し、表示名を **Language Learning** にします。
2. ウェブアプリ設定で、開発用のエンドポイント URL を `https://<SWA のホスト名>/miniapp` にします。
3. 開発用の **チャネル ID、LIFF ID、ミニアプリ URL** を控えます。`openid` と `profile` のスコープを確認します。

ミニアプリは開発用・審査用・公開用で内部チャネルが分かれます。ID と URL を異なる環境から混ぜないでください。今回は、開発用のみを使用します。
もし、実際にミニアプリを開発した際に一般利用者への公開を行う場合は LINE 側の公開手続きも必要です。
[ミニアプリ開発の流れ](https://developers.line.biz/ja/docs/line-mini-app/develop/develop-overview/) / [コンソールの環境区分](https://developers.line.biz/en/docs/line-mini-app/discover/console-guide/)

## 2. Function App の設定

Azure ポータル → 対象の Function App → **Settings → Environment variables → App settings** に追加し、Apply します。
「追加」で名前と値を一組ずつ入力します。画面によってはダイアログの保存後、一覧の Apply も必要です。変更確認を承認し、保存完了と再起動を待ちます。
この場所は Function App の設定です。SWA や GitHub Variables と取り違えないでください。[Functions の設定](https://learn.microsoft.com/ja-jp/azure/azure-functions/functions-how-to-use-azure-function-app-settings)

| 設定 | 値 |
| --- | --- |
| `Learning__Line__LoginChannelId` | ミニアプリの開発用のチャネル ID（LIFF ID ではない） |
| `Learning__Line__ChannelSecret` | Messaging API チャネルシークレット |
| `Learning__Line__ChannelAccessToken` | Messaging API 長期アクセストークン |
| `Learning__Line__MiniAppUrl` | 開発用のミニアプリ URL |
| `Learning__Line__TutorIconBaseUrl` | 画像公開先。`https://raw.githubusercontent.com/line-community-developer/techbookfest21-azure-line-language-learning/refs/heads/main/docs/images/` を入力すると、本リポジトリに配置している画像を参照させることもできます。空でも動作 |

講師名が Giulia なら画像は `<base URL>/giulia.png`。LINE が認証なしで取得できる HTTPS URL を指定します。
シークレットとトークンは Functions にだけ保存し、Git やフロントの環境変数には置きません。

Functions の Webhook は公開 HTTPS で LINE のイベントを受け付けます。
Webhook は LINE 署名、学習 API は関数キーと利用者認証、MCP は専用システムキーで保護します。
**LINE の入口を増やしても、学習ロジックとデータを同じ独立 Functions に集約できます。**

## 3. GitHub Variables と配備

GitHub → **Settings → Secrets and variables → Actions → Variables** に登録します。

| 変数 | 値 |
| --- | --- |
| `LIFF_ID` | 開発用の LIFF ID（LIFF URL の `https://miniapp.line.me/` 以降の文字列） |
| `LINE_OFFICIAL_ACCOUNT_URL` | 友だち追加用 URL（Official Account Manager の「友だちを増やす」から取得できます） |

これらはどちらも公開値です。SPA ビルドで `VITE_LIFF_ID` / `VITE_LINE_OFFICIAL_ACCOUNT_URL` に埋め込まれます。

New repository variable で Name と Value を入力し、Add variable で保存します。Secrets タブとは別です。[GitHub の構成変数](https://docs.github.com/ja/actions/how-tos/write-workflows/choose-what-workflows-do/store-information-in-variables)
登録できたら [コードの配備](github-deployment.md)へ進みます。

## 困ったとき

| 症状 | 確認 |
| --- | --- |
| Webhook 401 / 503 | URL と ChannelSecret。Application Insights の LineWebhook |
| 返信が来ない | ChannelAccessToken、Durable と LINE 送信の失敗 |
| 講師が返答できない | Foundry の保存済み版・MCP 接続・権限 |
| ミニアプリで認証エラー | LoginChannelId / LIFF_ID が同じ環境か、SPA の再配備 |
| 会話が学習履歴につながらない | 2 チャネルが同一プロバイダーか |

ID トークンはブラウザー → BFF では `x-swallowkit-external-token`、BFF → 独立 Functions では Bearer として渡します。
SWA が扱う Authorization と分離する SwallowKit の認証トランスポートです。
