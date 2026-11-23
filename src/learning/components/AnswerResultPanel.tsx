import { Link } from 'react-router-dom';
import type { AnswerResult } from '@line-language-learn/shared';
import { pct } from '../format';
import { Card, LevelBadge, cx } from './ui';

function feedbackHeading(result: AnswerResult): { icon: string; title: string; tone: 'green' | 'red' | 'amber' } {
  if (result.feedbackCode === 'accent-mismatch') {
    return { icon: '⚠️', title: result.isCorrect ? '正解（アクセントに注意）' : '惜しい！', tone: 'amber' };
  }
  return result.isCorrect ? { icon: '⭕', title: '正解！', tone: 'green' } : { icon: '❌', title: '不正解', tone: 'red' };
}

const headingTone = {
  green: 'border-green-300 bg-green-50 text-green-800 dark:border-green-800 dark:bg-green-950/50 dark:text-green-300',
  red: 'border-red-300 bg-red-50 text-red-800 dark:border-red-900 dark:bg-red-950/50 dark:text-red-300',
  amber: 'border-amber-300 bg-amber-50 text-amber-800 dark:border-amber-900 dark:bg-amber-950/50 dark:text-amber-300',
} as const;

export function AnswerResultPanel({ result }: { result: AnswerResult }) {
  const heading = feedbackHeading(result);
  const otherAccepted = result.acceptedAnswers.filter((a) => a !== result.correctAnswer);
  const delta = result.mastery.after - result.mastery.before;

  return (
    <div className="space-y-3" aria-live="polite">
      <div className={cx('rounded-2xl border-2 p-4', headingTone[heading.tone])}>
        <p className="text-lg font-bold">
          <span aria-hidden="true">{heading.icon}</span> {heading.title}
        </p>
        {result.feedbackCode === 'accent-mismatch' && <p className="mt-1 text-sm font-semibold">アクセント記号を確認しましょう</p>}
        {result.alreadyAnswered && <p className="mt-1 text-xs opacity-80">この問題はすでに回答済みのため、前回の結果を表示しています。</p>}
        <dl className="mt-3 grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-sm">
          <dt className="opacity-80">あなたの回答</dt>
          <dd className="break-words font-semibold">{result.submittedAnswer || '（空欄）'}</dd>
          <dt className="opacity-80">正解</dt>
          <dd className="break-words font-bold">{result.correctAnswer}</dd>
          {otherAccepted.length > 0 && (
            <>
              <dt className="opacity-80">別解</dt>
              <dd className="break-words">{otherAccepted.join(' / ')}</dd>
            </>
          )}
        </dl>
      </div>

      <Card>
        <h3 className="text-sm font-bold">解説</h3>
        <p className="mt-1 whitespace-pre-wrap text-sm text-gray-700 dark:text-neutral-300">{result.explanationJa}</p>
        <div className="mt-3 flex items-center gap-2 rounded-xl bg-gray-50 px-3 py-2 text-sm dark:bg-neutral-800/60">
          <span className="text-xs text-gray-500 dark:text-neutral-400">習熟度</span>
          <span className="tabular-nums">{pct(result.mastery.before)}</span>
          <span aria-hidden="true">→</span>
          <span className="font-bold tabular-nums">{pct(result.mastery.after)}</span>
          <span
            className={cx(
              'ml-auto text-xs font-semibold tabular-nums',
              delta > 0 ? 'text-green-700 dark:text-green-400' : delta < 0 ? 'text-red-600 dark:text-red-400' : 'text-gray-500',
            )}
          >
            {delta > 0 ? '▲' : delta < 0 ? '▼' : '±'}
            {Math.abs(Math.round(delta * 100))}pt
          </span>
        </div>
      </Card>

      {result.grammarReferences.map((ref) => (
        <Card key={ref.topicId} className="border-green-200 dark:border-green-900">
          <div className="flex items-center gap-2">
            <span aria-hidden="true">📘</span>
            <LevelBadge level={ref.level} />
            <h3 className="min-w-0 flex-1 truncate text-sm font-bold">{ref.titleJa}</h3>
          </div>
          <p className="mt-0.5 text-xs text-gray-500 dark:text-neutral-400">{ref.title}</p>
          {ref.rules.length > 0 && (
            <ul className="mt-2 list-disc space-y-1 pl-5 text-sm">
              {ref.rules.map((rule) => (
                <li key={rule.id}>{rule.summaryJa}</li>
              ))}
            </ul>
          )}
          {ref.example && (
            <div className="mt-2 rounded-xl bg-green-50 p-2.5 text-sm dark:bg-green-950/30">
              <p className="font-semibold" lang="it">
                {ref.example.text}
              </p>
              <p className="text-xs text-gray-600 dark:text-neutral-400">{ref.example.translationJa}</p>
            </div>
          )}
          <Link to={`/grammar/${encodeURIComponent(ref.topicId)}`} className="mt-2 inline-block text-sm font-semibold text-line">
            文法解説を詳しく見る →
          </Link>
        </Card>
      ))}
    </div>
  );
}
