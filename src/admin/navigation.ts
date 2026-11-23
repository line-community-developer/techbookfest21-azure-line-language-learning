export const adminFeatures = [
  {
    path: '/admin/questions/generate', title: 'AI で問題を生成', category: '問題作成',
    description: '文法項目から問題候補を生成し、品質を確認して問題バンクへ保存します。',
    note: 'AI 検証または人による確認のあと、サーバーで出題に必要な条件を検証して保存します。',
  },
  {
    path: '/grammar-topic', title: '文法マスター', category: 'マスターデータ',
    description: '文法の説明・ルール・例文・よくある間違い・出典を管理します。',
    note: '問題の ruleIds・sourceIds はこのマスターの ID を参照します。利用中の項目やルールを削除する前に参照先を確認してください。変更の学習機能への反映にはバックエンドの再起動が必要です。',
  },
  {
    path: '/sample-question', title: '問題マスター', category: 'マスターデータ',
    description: '参照用サンプルと、AI で生成・保存した公開問題を管理します。',
    note: 'publication のない問題は AI 向けの参照サンプルです。公開問題は AI 問題生成画面から作成してください。標準編集では AI 品質検証を再実行しません。公開問題の編集・削除は次回の演習から、参照サンプルの変更はバックエンド再起動後に反映されます。',
  },
  {
    path: '/tutor-profile', title: '講師マスター', category: 'マスターデータ',
    description: '講師の名前・挨拶・性格・話し方・添削方針を管理します。',
    note: '会話履歴が講師 ID を参照するため、利用中の講師は編集で管理してください。変更の反映にはバックエンドの再起動が必要です。',
  },
];
