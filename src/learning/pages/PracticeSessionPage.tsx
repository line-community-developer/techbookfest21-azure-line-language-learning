import { useRef, useState, type FormEvent } from 'react';
import { Link, useParams } from 'react-router-dom';
import type { AnswerResult, PracticeQuestionView, PracticeSessionView } from '@line-language-learn/shared';
import { api, newId } from '../api';
import { AnswerResultPanel } from '../components/AnswerResultPanel';
import { BusyOverlay } from '../components/BusyOverlay';
import { PracticeSummaryView } from '../components/PracticeSummaryView';
import { PromptText } from '../components/PromptText';
import { Badge, Button, Card, ErrorState, InlineError, LevelBadge, PageSkeleton, TopicChip, cx } from '../components/ui';
import { practiceModeLabel } from '../format';
import { useAction, useApi } from '../hooks';

const ACCENTS = ['à', 'è', 'é', 'ì', 'ò', 'ù'] as const;

export default function PracticeSessionPage() {
  const { sessionId = '' } = useParams();
  return <PracticeSession key={sessionId} sessionId={sessionId} />;
}

function firstUnanswered(questions: PracticeQuestionView[]): number {
  return questions.findIndex((q) => q.result === null);
}

function PracticeSession({ sessionId }: { sessionId: string }) {
  const session = useApi((signal) => api.practiceSession(sessionId, signal), [sessionId]);
  const [index, setIndex] = useState<number | null>(null);
  const complete = useAction(async () => {
    const result = await api.completePractice(sessionId);
    session.setData(result);
  });

  if (!session.data) {
    return session.error ? <ErrorState error={session.error} onRetry={session.reload} /> : <PageSkeleton cards={2} />;
  }

  const data = session.data;
  if (data.status === 'completed' && data.summary) {
    return <PracticeSummaryView session={data} summary={data.summary} />;
  }

  const questions = [...data.questions].sort((a, b) => a.order - b.order);
  if (questions.length === 0) {
    return <ErrorState error={new Error('この演習には問題がありません。')} onRetry={session.reload} />;
  }
  const nextOpen = firstUnanswered(questions);
  const allAnswered = nextOpen === -1;
  const currentIndex = Math.min(index ?? (allAnswered ? questions.length - 1 : nextOpen), questions.length - 1);
  const current = questions[currentIndex];
  const answeredCount = questions.filter((q) => q.result !== null).length;

  const onAnswered = (questionId: string, result: AnswerResult) => {
    // Stay on the answered question so the result panel is shown until the learner taps "次の問題へ".
    setIndex(questions.findIndex((q) => q.id === questionId));
    session.setData((prev: PracticeSessionView | undefined) =>
      prev ? { ...prev, questions: prev.questions.map((q) => (q.id === questionId ? { ...q, result } : q)) } : prev,
    );
  };

  const goNext = () => {
    const after = questions.findIndex((q, i) => i > currentIndex && q.result === null);
    setIndex(after !== -1 ? after : nextOpen !== -1 ? nextOpen : currentIndex);
  };

  return (
    <div>
      {complete.pending && <BusyOverlay message="結果を集計しています…" />}

      <div className="mb-3 flex flex-wrap items-center gap-2">
        <Badge tone={data.mode === 'review' ? 'purple' : 'green'}>{practiceModeLabel[data.mode]}演習</Badge>
        <LevelBadge level={data.level} large />
        {data.status === 'completed' && <Badge tone="gray">終了済み</Badge>}
      </div>

      <div className="mb-1 flex items-center justify-between text-sm">
        <span className="font-semibold">
          問題 <span className="tabular-nums">{currentIndex + 1}</span> / <span className="tabular-nums">{questions.length}</span>
        </span>
        <span className="text-xs text-gray-500 dark:text-neutral-400">回答済み {answeredCount} 問</span>
      </div>
      <ol className="mb-4 flex gap-1" aria-label="進捗">
        {questions.map((q, i) => {
          const reachable = q.result !== null || i === nextOpen;
          return (
            <li key={q.id} className="flex-1">
              <button
                type="button"
                disabled={!reachable}
                onClick={() => setIndex(i)}
                aria-label={`問題 ${i + 1}${q.result ? (q.result.isCorrect ? '（正解）' : '（不正解）') : ''}`}
                aria-current={i === currentIndex ? 'step' : undefined}
                className={cx(
                  'block h-2.5 w-full rounded-full transition-colors',
                  q.result ? (q.result.isCorrect ? 'bg-line' : 'bg-red-400') : 'bg-gray-200 dark:bg-neutral-800',
                  i === currentIndex && 'ring-2 ring-gray-800 ring-offset-1 dark:ring-white dark:ring-offset-neutral-950',
                )}
              />
            </li>
          );
        })}
      </ol>

      <QuestionView key={current.id} sessionId={sessionId} question={current} onAnswered={onAnswered} />

      {current.result && (
        <div className="mt-4">
          {allAnswered || data.status === 'completed' ? (
            data.status === 'completed' ? (
              <Button block className="min-h-12 text-base" onClick={session.reload}>
                結果を読み込む
              </Button>
            ) : (
              <Button block className="min-h-12 text-base" loading={complete.pending} onClick={() => void complete.run()}>
                🏁 演習を終了して結果を見る
              </Button>
            )
          ) : (
            <Button block className="min-h-12 text-base" onClick={goNext}>
              次の問題へ →
            </Button>
          )}
          <InlineError error={complete.error} />
        </div>
      )}

      {data.topics.length > 0 && (
        <div className="mt-6">
          <p className="mb-1.5 text-xs text-gray-500 dark:text-neutral-400">この演習の文法項目</p>
          <div className="flex flex-wrap gap-1.5">
            {data.topics.map((t) => (
              <TopicChip key={t.topicId} topic={t} to={`/grammar/${encodeURIComponent(t.topicId)}`} />
            ))}
          </div>
        </div>
      )}
      <Link to="/" className="mt-6 block text-center text-sm text-gray-500 dark:text-neutral-400">
        中断してホームに戻る（あとで再開できます）
      </Link>
    </div>
  );
}

function QuestionView({
  sessionId,
  question,
  onAnswered,
}: {
  sessionId: string;
  question: PracticeQuestionView;
  onAnswered: (questionId: string, result: AnswerResult) => void;
}) {
  const [answer, setAnswer] = useState('');
  const attemptId = useRef(newId());
  const inputRef = useRef<HTMLInputElement>(null);
  const submit = useAction(async (value: string) => {
    const result = await api.submitAnswer(sessionId, { questionId: question.id, answer: value, attemptId: attemptId.current });
    onAnswered(question.id, result);
  });
  const result = question.result;
  const isChoice = question.type === 'multiple-choice' && question.choices.length > 0;

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    const value = answer.trim();
    if (!value || submit.pending || result) return;
    void submit.run(value);
  };

  const insertAccent = (char: string) => {
    const input = inputRef.current;
    const start = input?.selectionStart ?? answer.length;
    const end = input?.selectionEnd ?? answer.length;
    const next = answer.slice(0, start) + char + answer.slice(end);
    setAnswer(next.slice(0, 200));
    requestAnimationFrame(() => {
      input?.focus();
      input?.setSelectionRange(start + char.length, start + char.length);
    });
  };

  return (
    <div>
      <Card>
        <div className="flex flex-wrap items-center gap-2">
          <TopicChip topic={question.topic} />
          <span className="ml-auto text-xs text-amber-500" title={`難易度 ${question.difficulty} / 5`} aria-label={`難易度 ${question.difficulty} / 5`}>
            {'★'.repeat(question.difficulty)}
            <span className="text-gray-300 dark:text-neutral-700">{'★'.repeat(Math.max(0, 5 - question.difficulty))}</span>
          </span>
        </div>
        <p className="mt-3 text-sm text-gray-600 dark:text-neutral-300">{question.instructionJa}</p>
        <p lang="it" className="mt-3 text-xl font-semibold leading-relaxed">
          <PromptText
            prompt={question.prompt}
            fill={result ? result.correctAnswer : undefined}
          />
        </p>

        <form onSubmit={onSubmit} className="mt-4">
          {isChoice ? (
            <div className="grid gap-2" role="radiogroup" aria-label="選択肢">
              {question.choices.map((choice) => {
                const selected = (result ? result.submittedAnswer : answer) === choice;
                const isCorrectChoice = result !== null && result.acceptedAnswers.includes(choice);
                return (
                  <button
                    key={choice}
                    type="button"
                    role="radio"
                    aria-checked={selected}
                    lang="it"
                    disabled={result !== null || submit.pending}
                    onClick={() => setAnswer(choice)}
                    className={cx(
                      'flex min-h-12 items-center justify-between rounded-xl border-2 px-4 text-left text-base font-semibold transition-colors',
                      result
                        ? isCorrectChoice
                          ? 'border-line bg-green-50 dark:bg-green-950/40'
                          : selected
                            ? 'border-red-400 bg-red-50 dark:bg-red-950/40'
                            : 'border-gray-200 opacity-60 dark:border-neutral-800'
                        : selected
                          ? 'border-line bg-green-50 dark:bg-green-950/40'
                          : 'border-gray-200 bg-white hover:border-gray-300 dark:border-neutral-700 dark:bg-neutral-900',
                    )}
                  >
                    <span>{choice}</span>
                    {result && isCorrectChoice && <span aria-hidden="true">✓</span>}
                    {result && selected && !isCorrectChoice && <span aria-hidden="true">✗</span>}
                  </button>
                );
              })}
            </div>
          ) : (
            <div>
              <input
                ref={inputRef}
                type="text"
                lang="it"
                inputMode="text"
                autoCapitalize="off"
                autoComplete="off"
                autoCorrect="off"
                spellCheck={false}
                maxLength={200}
                value={result ? result.submittedAnswer : answer}
                onChange={(e) => setAnswer(e.target.value)}
                disabled={result !== null || submit.pending}
                placeholder="空欄に入る語を入力"
                aria-label="回答"
                className="min-h-12 w-full rounded-xl border-2 border-gray-300 bg-white px-4 text-lg outline-none focus:border-line disabled:opacity-70 dark:border-neutral-700 dark:bg-neutral-900"
              />
              {!result && (
                <div className="mt-2 flex flex-wrap gap-1.5" aria-label="アクセント付き文字">
                  {ACCENTS.map((char) => (
                    <button
                      key={char}
                      type="button"
                      onMouseDown={(e) => e.preventDefault()}
                      onClick={() => insertAccent(char)}
                      disabled={submit.pending}
                      className="h-10 min-w-10 rounded-lg border border-gray-300 bg-white text-lg font-semibold hover:bg-gray-50 dark:border-neutral-700 dark:bg-neutral-900 dark:hover:bg-neutral-800"
                    >
                      {char}
                    </button>
                  ))}
                </div>
              )}
            </div>
          )}

          {!result && (
            <>
              <Button type="submit" block className="mt-4 min-h-12 text-base" disabled={!answer.trim()} loading={submit.pending}>
                回答する
              </Button>
              <InlineError error={submit.error} />
            </>
          )}
        </form>
      </Card>

      {result && (
        <div className="mt-4">
          <AnswerResultPanel result={result} />
        </div>
      )}
    </div>
  );
}
