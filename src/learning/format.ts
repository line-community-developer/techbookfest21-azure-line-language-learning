import type {
  CefrLevel,
  ComponentMode,
  GrammarTopic,
  MasteryStatus,
  ObservationCertainty,
  ObservationType,
  ReviewPriority,
  StartPracticeRequest,
  WorkflowStepState,
} from '@line-language-learn/shared';

export const LEVELS: readonly CefrLevel[] = ['A1', 'A2', 'B1', 'B2', 'C1', 'C2'];
export const PRACTICE_LEVELS: readonly CefrLevel[] = ['A1', 'A2', 'B1', 'B2'];

export function pct(value: number): string {
  if (!Number.isFinite(value)) return '-';
  return `${Math.round(Math.min(1, Math.max(0, value)) * 100)}%`;
}

const dateTimeFormat = new Intl.DateTimeFormat('ja-JP', { month: 'numeric', day: 'numeric', hour: '2-digit', minute: '2-digit' });

export function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return '-';
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? iso : dateTimeFormat.format(date);
}

export const masteryStatusLabel: Record<MasteryStatus, string> = {
  unknown: '未測定',
  weak: '苦手',
  developing: '練習中',
  strong: '得意',
};

export const priorityLabel: Record<ReviewPriority, string> = {
  high: '優先度 高',
  medium: '優先度 中',
  low: '優先度 低',
};

export const certaintyLabel: Record<ObservationCertainty, string> = {
  'ai-inferred': 'AI推定',
  deterministic: '確定',
};

export const practiceModeLabel: Record<StartPracticeRequest['mode'], string> = {
  recommended: 'おすすめ',
  topic: '項目指定',
  review: '復習',
};

export const observationTypeLabel: Record<ObservationType, string> = {
  'correct-answer': '正解',
  'incorrect-answer': '不正解',
  'grammar-error': '文法の誤り',
  'possible-misunderstanding': '理解不足の可能性',
  'correct-use': '正しい使用',
  'new-expression': '新しい表現',
};

export const sourceTypeLabel: Record<'practice' | 'conversation', string> = {
  practice: '演習',
  conversation: '会話',
};

export const categoryLabel: Record<GrammarTopic['category'], string> = {
  article: '冠詞',
  preposition: '前置詞',
  'verb-pattern': '動詞の型',
  tense: '時制',
  pronoun: '代名詞',
  construction: '構文',
};

export const componentLabel: Record<ComponentMode['component'], string> = {
  'ai-model': 'AI モデル',
  'tutor-agent': '講師エージェント',
  retrieval: '文法検索 (RAG)',
  storage: 'ストレージ',
  identity: 'ユーザー識別',
  messaging: 'メッセージング (LINE)',
  workflow: 'ワークフロー',
};

export const stepStatusLabel: Record<WorkflowStepState['status'], string> = {
  pending: '待機中',
  running: '実行中',
  completed: '完了',
  failed: '失敗',
  skipped: 'スキップ',
};

export const toolStatusLabel: Record<'succeeded' | 'failed' | 'rejected', string> = {
  succeeded: '成功',
  failed: '失敗',
  rejected: '拒否',
};
