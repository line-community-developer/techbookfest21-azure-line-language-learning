import type { MasteryStatus, PracticeSessionView } from '@line-language-learn/shared';
import { pct } from '../format';
import { PromptText } from './PromptText';
import { Card, LevelBadge, LinkButton, MasteryBar, SectionHeader, cx } from './ui';

type Summary = NonNullable<PracticeSessionView['summary']>;

function statusFromScore(score: number): MasteryStatus {
  if (score < 0.4) return 'weak';
  if (score < 0.75) return 'developing';
  return 'strong';
}

export function PracticeSummaryView({ session, summary }: { session: PracticeSessionView; summary: Summary }) {
  const great = summary.accuracy >= 0.8;
  return (
    <div>
      <Card className="text-center">
        <p className="text-4xl" aria-hidden="true">
          {great ? '🎉' : summary.accuracy >= 0.5 ? '👍' : '💪'}
        </p>
        <h1 className="mt-2 text-lg font-bold">演習結果</h1>
        <p className="mt-2 text-5xl font-extrabold tabular-nums text-line">{pct(summary.accuracy)}</p>
        <p className="mt-1 text-sm text-gray-600 dark:text-neutral-300">
          {summary.total} 問中 {summary.correct} 問正解
          {summary.answered < summary.total && `（回答 ${summary.answered} 問）`}
        </p>
        <p className="mt-2 text-sm">{great ? 'Bravissimo! よくできました。' : 'Coraggio! 解説を見直してもう一度挑戦しましょう。'}</p>
      </Card>

      {summary.byTopic.length > 0 && (
        <>
          <SectionHeader title="文法項目ごとの結果" />
          <Card>
            <ul className="divide-y divide-gray-100 dark:divide-neutral-800">
              {summary.byTopic.map((topic) => (
                <li key={topic.topicId} className="py-3 first:pt-0 last:pb-0">
                  <div className="mb-1.5 flex items-center gap-2">
                    <LevelBadge level={topic.level} />
                    <span className="min-w-0 flex-1 truncate text-sm font-semibold">{topic.titleJa}</span>
                    <span className="text-xs tabular-nums text-gray-600 dark:text-neutral-300">
                      {topic.correct}/{topic.total} 正解
                    </span>
                  </div>
                  <p className="mb-1 text-xs text-gray-500 dark:text-neutral-400">演習後の習熟度</p>
                  <MasteryBar score={topic.masteryAfter} status={statusFromScore(topic.masteryAfter)} />
                </li>
              ))}
            </ul>
          </Card>
        </>
      )}

      <SectionHeader title="問題のふりかえり" />
      <Card className="p-0">
        <ol className="divide-y divide-gray-100 dark:divide-neutral-800">
          {session.questions.map((q, i) => (
            <li key={q.id} className="flex gap-3 p-3">
              <span
                className={cx(
                  'flex h-6 w-6 shrink-0 items-center justify-center rounded-full text-xs font-bold text-white',
                  !q.result ? 'bg-gray-400' : q.result.isCorrect ? 'bg-line' : 'bg-red-500',
                )}
                aria-label={!q.result ? '未回答' : q.result.isCorrect ? '正解' : '不正解'}
              >
                {!q.result ? '–' : q.result.isCorrect ? '✓' : '✗'}
              </span>
              <div className="min-w-0 flex-1 text-sm">
                <p className="text-xs text-gray-500 dark:text-neutral-400">
                  Q{i + 1} ・ {q.topic.titleJa}
                </p>
                <p lang="it" className="mt-0.5 leading-relaxed">
                  <PromptText prompt={q.prompt} fill={q.result?.correctAnswer} />
                </p>
                {q.result && !q.result.isCorrect && (
                  <p className="mt-0.5 text-xs text-red-600 dark:text-red-400">あなたの回答: {q.result.submittedAnswer || '（空欄）'}</p>
                )}
              </div>
            </li>
          ))}
        </ol>
      </Card>

      <div className="mt-6 grid grid-cols-2 gap-2">
        <LinkButton to="/history" variant="secondary">
          📈 履歴を見る
        </LinkButton>
        <LinkButton to="/">🏠 ホームへ</LinkButton>
      </div>
      <LinkButton to="/practice" variant="ghost" block className="mt-2">
        別の演習をする
      </LinkButton>
    </div>
  );
}
