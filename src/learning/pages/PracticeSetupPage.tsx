import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import type { CefrLevel, GrammarTopic, StartPracticeRequest } from '@line-language-learn/shared';
import { api } from '../api';
import { BusyOverlay } from '../components/BusyOverlay';
import { Badge, Button, Card, EmptyState, ErrorState, InlineError, LevelBadge, PageSkeleton, PageTitle, SectionHeader, cx } from '../components/ui';
import { LEVELS, PRACTICE_LEVELS, categoryLabel } from '../format';
import { useAction, useApi } from '../hooks';

const MAX_TOPICS = 3;

function defaultLevel(level: CefrLevel | undefined): CefrLevel {
  if (!level) return 'A2';
  return PRACTICE_LEVELS.includes(level) ? level : 'B2';
}

export default function PracticeSetupPage() {
  const navigate = useNavigate();
  const data = useApi(async (signal) => {
    const [dashboard, grammar] = await Promise.all([api.dashboard(signal), api.grammarList(signal)]);
    return { learnerLevel: dashboard.learner.currentLevel, topics: grammar.topics };
  }, []);
  const [selectedLevel, setSelectedLevel] = useState<CefrLevel | null>(null);
  const [selectedTopics, setSelectedTopics] = useState<string[]>([]);
  const [pendingMode, setPendingMode] = useState<StartPracticeRequest['mode'] | null>(null);

  const start = useAction(async (body: StartPracticeRequest) => {
    setPendingMode(body.mode);
    const session = await api.startPractice(body);
    navigate(`/practice/${encodeURIComponent(session.id)}`);
  });

  const grouped = useMemo(() => {
    const map = new Map<CefrLevel, GrammarTopic[]>();
    for (const topic of data.data?.topics ?? []) {
      const list = map.get(topic.level) ?? [];
      list.push(topic);
      map.set(topic.level, list);
    }
    return LEVELS.filter((level) => map.has(level)).map((level) => ({ level, topics: map.get(level) ?? [] }));
  }, [data.data]);

  if (!data.data) {
    return data.error ? <ErrorState error={data.error} onRetry={data.reload} /> : <PageSkeleton />;
  }

  const level = selectedLevel ?? defaultLevel(data.data.learnerLevel);
  const topicById = new Map(data.data.topics.map((t) => [t.id, t]));

  const toggleTopic = (id: string) => {
    setSelectedTopics((current) => {
      if (current.includes(id)) return current.filter((t) => t !== id);
      if (current.length >= MAX_TOPICS) return current;
      return [...current, id];
    });
  };

  return (
    <div>
      {start.pending && <BusyOverlay message="問題を生成しています…" sub="AI が文法知識をもとに問題を作り、内容を検証しています。" />}
      <PageTitle sub="レベルと文法項目を選んで、あなた専用の問題を作成します。">演習をはじめる</PageTitle>

      <Card>
        <h2 className="text-sm font-bold">レベル</h2>
        <p className="mt-0.5 text-xs text-gray-500 dark:text-neutral-400">現在のレベル: CEFR {data.data.learnerLevel}</p>
        <div className="mt-3 grid grid-cols-4 gap-2" role="radiogroup" aria-label="レベル">
          {PRACTICE_LEVELS.map((l) => (
            <button
              key={l}
              type="button"
              role="radio"
              aria-checked={level === l}
              onClick={() => setSelectedLevel(l)}
              className={cx(
                'min-h-11 rounded-xl border text-sm font-bold transition-colors',
                level === l
                  ? 'border-line bg-line text-white'
                  : 'border-gray-300 bg-white hover:bg-gray-50 dark:border-neutral-700 dark:bg-neutral-900 dark:hover:bg-neutral-800',
              )}
            >
              {l}
            </button>
          ))}
        </div>
        <Button
          block
          className="mt-4 min-h-12 text-base"
          loading={start.pending && pendingMode === 'recommended'}
          disabled={start.pending}
          onClick={() => void start.run({ mode: 'recommended', level })}
        >
          ✨ おすすめ問題を自動生成
        </Button>
        <p className="mt-1.5 text-center text-xs text-gray-500 dark:text-neutral-400">苦手な項目や復習の必要な項目から自動で選びます。</p>
        {pendingMode === 'recommended' && <InlineError error={start.error} />}
      </Card>

      <SectionHeader
        title="文法項目を選ぶ"
        action={
          <Badge tone={selectedTopics.length === MAX_TOPICS ? 'amber' : 'gray'}>
            {selectedTopics.length} / {MAX_TOPICS}
          </Badge>
        }
      />
      <p className="mb-2 text-xs text-gray-500 dark:text-neutral-400">最大 {MAX_TOPICS} 項目まで選べます。</p>

      {grouped.length === 0 ? (
        <EmptyState>文法項目がありません。</EmptyState>
      ) : (
        <div className="space-y-3">
          {grouped.map((group) => (
            <Card key={group.level} className={cx(group.level === level && 'border-green-300 dark:border-green-800')}>
              <div className="mb-2 flex items-center gap-2">
                <LevelBadge level={group.level} large />
                {group.level === level && <span className="text-xs text-line">選択中のレベル</span>}
              </div>
              <ul className="space-y-2">
                {group.topics.map((topic) => {
                  const checked = selectedTopics.includes(topic.id);
                  const disabled = !checked && selectedTopics.length >= MAX_TOPICS;
                  return (
                    <li key={topic.id}>
                      <button
                        type="button"
                        role="checkbox"
                        aria-checked={checked}
                        disabled={disabled}
                        onClick={() => toggleTopic(topic.id)}
                        className={cx(
                          'flex min-h-12 w-full items-center gap-3 rounded-xl border px-3 py-2 text-left transition-colors disabled:opacity-40',
                          checked
                            ? 'border-line bg-green-50 dark:bg-green-950/40'
                            : 'border-gray-200 hover:bg-gray-50 dark:border-neutral-700 dark:hover:bg-neutral-800',
                        )}
                      >
                        <span
                          aria-hidden="true"
                          className={cx(
                            'flex h-6 w-6 shrink-0 items-center justify-center rounded-md border-2 text-sm font-bold',
                            checked ? 'border-line bg-line text-white' : 'border-gray-300 dark:border-neutral-600',
                          )}
                        >
                          {checked ? '✓' : ''}
                        </span>
                        <span className="min-w-0 flex-1">
                          <span className="block text-sm font-semibold">{topic.titleJa}</span>
                          <span className="block text-xs text-gray-500 dark:text-neutral-400">
                            {topic.title} ・ {categoryLabel[topic.category]}
                          </span>
                        </span>
                      </button>
                    </li>
                  );
                })}
              </ul>
            </Card>
          ))}
        </div>
      )}

      <div className="sticky bottom-20 z-20 mt-4 rounded-2xl bg-gray-50/90 py-2 backdrop-blur dark:bg-neutral-950/90">
        {selectedTopics.length > 0 && (
          <p className="mb-2 truncate px-1 text-xs text-gray-600 dark:text-neutral-300">
            選択中: {selectedTopics.map((id) => topicById.get(id)?.titleJa ?? id).join('、')}
          </p>
        )}
        <Button
          block
          className="min-h-12 text-base"
          disabled={selectedTopics.length === 0 || start.pending}
          loading={start.pending && pendingMode === 'topic'}
          onClick={() => void start.run({ mode: 'topic', topicIds: selectedTopics, level })}
        >
          選んだ項目で演習（{selectedTopics.length}）
        </Button>
        {pendingMode === 'topic' && <InlineError error={start.error} />}
      </div>
    </div>
  );
}
