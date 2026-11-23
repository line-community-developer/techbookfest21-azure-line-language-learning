import { useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';
import { MasteryList } from '../components/MasteryList';
import { PromptText } from '../components/PromptText';
import {
  Badge,
  Card,
  CertaintyBadge,
  EmptyState,
  ErrorState,
  LevelBadge,
  PageSkeleton,
  PageTitle,
  PriorityBadge,
  SectionHeader,
  TopicChip,
  cx,
} from '../components/ui';
import { formatDateTime, observationTypeLabel, pct, practiceModeLabel, sourceTypeLabel } from '../format';
import { useApi } from '../hooks';

const PREVIEW = 10;

export default function HistoryPage() {
  const history = useApi((signal) => api.history(signal), []);
  const [showAllAttempts, setShowAllAttempts] = useState(false);
  const [showAllObservations, setShowAllObservations] = useState(false);

  if (!history.data) {
    return history.error ? <ErrorState error={history.error} onRetry={history.reload} /> : <PageSkeleton cards={4} />;
  }

  const { mastery, attempts, observations, sessions, reviewPriorities } = history.data;
  const sortedMastery = [...mastery].sort((a, b) => a.score - b.score);
  const visibleAttempts = showAllAttempts ? attempts : attempts.slice(0, PREVIEW);
  const visibleObservations = showAllObservations ? observations : observations.slice(0, PREVIEW);

  return (
    <div>
      <PageTitle sub="演習と会話から記録された学習の足あとです。">学習履歴</PageTitle>
      {history.error && <ErrorState error={history.error} onRetry={history.reload} title="最新データの取得に失敗しました" />}

      <SectionHeader title="文法項目ごとの習熟度" action={<span className="text-xs text-gray-500">苦手な順</span>} />
      <Card>{sortedMastery.length > 0 ? <MasteryList items={sortedMastery} /> : <EmptyState>まだ記録がありません。</EmptyState>}</Card>

      <SectionHeader title="復習の優先度" />
      {reviewPriorities.length > 0 ? (
        <Card className="p-0">
          <ul className="divide-y divide-gray-100 dark:divide-neutral-800">
            {reviewPriorities.map((item) => (
              <li key={item.topicId} className="p-3">
                <div className="flex items-center gap-2">
                  <LevelBadge level={item.level} />
                  <Link to={`/grammar/${encodeURIComponent(item.topicId)}`} className="min-w-0 flex-1 truncate text-sm font-semibold hover:underline">
                    {item.titleJa}
                  </Link>
                  <PriorityBadge priority={item.priority} />
                </div>
                <p className="mt-1 text-xs text-gray-600 dark:text-neutral-300">{item.reasonJa}</p>
              </li>
            ))}
          </ul>
        </Card>
      ) : (
        <EmptyState>現在、優先して復習する項目はありません。</EmptyState>
      )}

      <SectionHeader title="これまでの演習" />
      {sessions.length > 0 ? (
        <Card className="p-0">
          <ul className="divide-y divide-gray-100 dark:divide-neutral-800">
            {sessions.map((s) => (
              <li key={s.id}>
                <Link to={`/practice/${encodeURIComponent(s.id)}`} className="block p-3 hover:bg-gray-50 dark:hover:bg-neutral-800/50">
                  <div className="flex items-center gap-2">
                    <Badge tone={s.mode === 'review' ? 'purple' : 'green'}>{practiceModeLabel[s.mode]}</Badge>
                    {s.status === 'in-progress' ? <Badge tone="amber">途中</Badge> : <Badge tone="gray">完了</Badge>}
                    <span className="ml-auto text-sm font-bold tabular-nums">
                      {s.summary ? `${s.summary.correct}/${s.summary.total}（${pct(s.summary.accuracy)}）` : '—'}
                    </span>
                  </div>
                  <div className="mt-2 flex flex-wrap gap-1">
                    {s.topics.map((t) => (
                      <TopicChip key={t.topicId} topic={t} />
                    ))}
                  </div>
                  <p className="mt-1.5 text-[11px] text-gray-400">
                    {formatDateTime(s.createdAt)}
                    {s.completedAt && ` 〜 ${formatDateTime(s.completedAt)}`}
                  </p>
                </Link>
              </li>
            ))}
          </ul>
        </Card>
      ) : (
        <EmptyState>まだ演習の記録がありません。</EmptyState>
      )}

      <SectionHeader title="最近の回答" />
      {attempts.length > 0 ? (
        <Card className="p-0">
          <ul className="divide-y divide-gray-100 dark:divide-neutral-800">
            {visibleAttempts.map((a) => (
              <li key={`${a.sessionId}:${a.questionId}`} className="flex gap-3 p-3">
                <span
                  className={cx(
                    'flex h-6 w-6 shrink-0 items-center justify-center rounded-full text-xs font-bold text-white',
                    a.isCorrect ? 'bg-line' : 'bg-red-500',
                  )}
                  aria-label={a.isCorrect ? '正解' : '不正解'}
                >
                  {a.isCorrect ? '✓' : '✗'}
                </span>
                <div className="min-w-0 flex-1 text-sm">
                  <p className="text-xs text-gray-500 dark:text-neutral-400">
                    {a.topicTitle} ・ {formatDateTime(a.answeredAt)}
                  </p>
                  <p lang="it" className="mt-0.5 leading-relaxed">
                    <PromptText prompt={a.prompt} />
                  </p>
                  <p className="mt-0.5 text-xs">
                    <span className={a.isCorrect ? 'text-green-700 dark:text-green-400' : 'text-red-600 dark:text-red-400'}>
                      あなた: {a.answer || '（空欄）'}
                    </span>
                    {!a.isCorrect && <span className="ml-2 text-gray-700 dark:text-neutral-300">正解: {a.correctAnswer}</span>}
                  </p>
                </div>
              </li>
            ))}
          </ul>
          {attempts.length > PREVIEW && (
            <button type="button" className="min-h-11 w-full border-t border-gray-100 text-sm font-semibold text-line dark:border-neutral-800" onClick={() => setShowAllAttempts((v) => !v)}>
              {showAllAttempts ? '折りたたむ' : `すべて表示（${attempts.length} 件）`}
            </button>
          )}
        </Card>
      ) : (
        <EmptyState>まだ回答の記録がありません。</EmptyState>
      )}

      <SectionHeader title="学習の観察記録" />
      <p className="mb-2 text-xs text-gray-500 dark:text-neutral-400">
        「確定」は採点結果から、「AI推定」は会話内容から AI が推定した記録です。
      </p>
      {observations.length > 0 ? (
        <Card className="p-0">
          <ul className="divide-y divide-gray-100 dark:divide-neutral-800">
            {visibleObservations.map((o) => (
              <li key={o.id} className="p-3">
                <div className="flex flex-wrap items-center gap-1.5">
                  <Badge tone={o.sourceType === 'conversation' ? 'purple' : 'gray'}>{sourceTypeLabel[o.sourceType]}</Badge>
                  <CertaintyBadge certainty={o.certainty} />
                  <Badge tone="gray">{observationTypeLabel[o.observationType] ?? o.observationType}</Badge>
                  <span className="ml-auto text-[11px] tabular-nums text-gray-500" title="確信度">
                    確信度 {pct(o.confidence)}
                  </span>
                </div>
                {o.topicTitle && <p className="mt-1 text-xs font-semibold text-gray-700 dark:text-neutral-300">{o.topicTitle}</p>}
                <blockquote className="mt-1 border-l-2 border-gray-300 pl-2 text-sm text-gray-700 dark:border-neutral-600 dark:text-neutral-300">
                  {o.evidenceText}
                </blockquote>
                <p className="mt-1 text-[11px] text-gray-400">{formatDateTime(o.occurredAt)}</p>
              </li>
            ))}
          </ul>
          {observations.length > PREVIEW && (
            <button type="button" className="min-h-11 w-full border-t border-gray-100 text-sm font-semibold text-line dark:border-neutral-800" onClick={() => setShowAllObservations((v) => !v)}>
              {showAllObservations ? '折りたたむ' : `すべて表示（${observations.length} 件）`}
            </button>
          )}
        </Card>
      ) : (
        <EmptyState>まだ観察記録がありません。</EmptyState>
      )}

    </div>
  );
}
