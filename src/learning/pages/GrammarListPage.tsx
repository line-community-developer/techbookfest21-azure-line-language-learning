import { useMemo, useState, type FormEvent } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import type { CefrLevel, GrammarSearchView, GrammarTopic } from '@line-language-learn/shared';
import { api } from '../api';
import { Badge, Button, Card, EmptyState, ErrorState, LevelBadge, PageSkeleton, PageTitle, SectionHeader, Spinner } from '../components/ui';
import { LEVELS, categoryLabel } from '../format';
import { useApi } from '../hooks';

function isLevel(value: string | null): value is CefrLevel {
  return value !== null && (LEVELS as readonly string[]).includes(value);
}

export default function GrammarListPage() {
  const [params, setParams] = useSearchParams();
  const q = params.get('q')?.trim() ?? '';
  const levelParam = params.get('level');
  const level = isLevel(levelParam) ? levelParam : undefined;
  const [draft, setDraft] = useState(q);
  const [draftLevel, setDraftLevel] = useState<CefrLevel | ''>(level ?? '');

  const list = useApi((signal) => api.grammarList(signal), []);
  const search = useApi<GrammarSearchView | null>(
    (signal) => (q ? api.grammarSearch({ q, level, limit: 5 }, signal) : Promise.resolve(null)),
    [q, level],
  );

  const grouped = useMemo(() => {
    const map = new Map<CefrLevel, GrammarTopic[]>();
    for (const topic of list.data?.topics ?? []) {
      const items = map.get(topic.level) ?? [];
      items.push(topic);
      map.set(topic.level, items);
    }
    return LEVELS.filter((l) => map.has(l)).map((l) => ({ level: l, topics: map.get(l) ?? [] }));
  }, [list.data]);

  const onSearch = (event: FormEvent) => {
    event.preventDefault();
    const next = new URLSearchParams();
    const text = draft.trim();
    if (text) next.set('q', text);
    if (text && draftLevel) next.set('level', draftLevel);
    setParams(next);
  };

  const clearSearch = () => {
    setDraft('');
    setDraftLevel('');
    setParams(new URLSearchParams());
  };

  return (
    <div>
      <PageTitle sub="イタリア語の文法をレベル別に確認できます。">文法ガイド</PageTitle>

      <Card>
        <form onSubmit={onSearch} role="search" className="space-y-2">
          <label htmlFor="grammar-q" className="text-sm font-bold">
            文法を検索
          </label>
          <div className="flex gap-2">
            <input
              id="grammar-q"
              type="search"
              value={draft}
              onChange={(e) => setDraft(e.target.value)}
              placeholder="例: 冠詞の前置詞、passato prossimo"
              maxLength={200}
              className="min-h-11 min-w-0 flex-1 rounded-xl border-2 border-gray-300 bg-white px-3 text-sm outline-none focus:border-line dark:border-neutral-700 dark:bg-neutral-900"
            />
            <select
              aria-label="レベルで絞り込み"
              value={draftLevel}
              onChange={(e) => setDraftLevel(isLevel(e.target.value) ? e.target.value : '')}
              className="min-h-11 rounded-xl border-2 border-gray-300 bg-white px-2 text-sm dark:border-neutral-700 dark:bg-neutral-900"
            >
              <option value="">全レベル</option>
              {LEVELS.map((l) => (
                <option key={l} value={l}>
                  {l}
                </option>
              ))}
            </select>
          </div>
          <div className="flex gap-2">
            <Button type="submit" className="flex-1" disabled={!draft.trim()}>
              🔍 検索
            </Button>
            {q && (
              <Button variant="secondary" onClick={clearSearch}>
                クリア
              </Button>
            )}
          </div>
        </form>
      </Card>

      {q && (
        <>
          <SectionHeader title={`「${q}」の検索結果`} action={search.loading ? <Spinner className="h-4 w-4 text-line" /> : undefined} />
          {search.error ? (
            <ErrorState error={search.error} onRetry={search.reload} title="検索に失敗しました" />
          ) : !search.data ? (
            <PageSkeleton cards={2} />
          ) : (
            <div className="space-y-2">
              <p className="text-xs text-gray-500 dark:text-neutral-400">
                検索エンジン（Retriever）: <span className="font-mono">{search.data.retriever}</span>
                {level && ` ・ レベル ${level}`}
              </p>
              {search.data.results.length === 0 ? (
                <EmptyState>該当する文法項目は見つかりませんでした。</EmptyState>
              ) : (
                search.data.results.map((r) => (
                  <Card key={r.topic.id} className="p-0">
                    <Link to={`/grammar/${encodeURIComponent(r.topic.id)}`} className="block p-3 hover:bg-gray-50 dark:hover:bg-neutral-800/50">
                      <div className="flex items-center gap-2">
                        <LevelBadge level={r.topic.level} />
                        <span className="min-w-0 flex-1 truncate text-sm font-semibold">{r.topic.titleJa}</span>
                        <Badge tone="blue" title="検索スコア">
                          score {r.score.toFixed(2)}
                        </Badge>
                      </div>
                      <p className="mt-0.5 text-xs text-gray-500 dark:text-neutral-400">{r.topic.title}</p>
                      {r.matchedTerms.length > 0 && (
                        <div className="mt-2 flex flex-wrap items-center gap-1">
                          <span className="text-[11px] text-gray-500">一致語:</span>
                          {r.matchedTerms.map((term) => (
                            <Badge key={term} tone="green">
                              {term}
                            </Badge>
                          ))}
                        </div>
                      )}
                      {r.sourceIds.length > 0 && (
                        <div className="mt-1.5 flex flex-wrap items-center gap-1">
                          <span className="text-[11px] text-gray-500">根拠ソース:</span>
                          {r.sourceIds.map((id) => (
                            <span key={id} className="rounded bg-gray-100 px-1.5 py-0.5 font-mono text-[10px] text-gray-600 dark:bg-neutral-800 dark:text-neutral-400">
                              {id}
                            </span>
                          ))}
                        </div>
                      )}
                    </Link>
                  </Card>
                ))
              )}
            </div>
          )}
        </>
      )}

      <SectionHeader title="レベル別の文法項目" />
      {!list.data ? (
        list.error ? <ErrorState error={list.error} onRetry={list.reload} /> : <PageSkeleton cards={3} />
      ) : grouped.length === 0 ? (
        <EmptyState>文法項目がありません。</EmptyState>
      ) : (
        <div className="space-y-4">
          {grouped.map((group) => (
            <section key={group.level}>
              <h3 className="mb-2 flex items-center gap-2 text-sm font-bold">
                <LevelBadge level={group.level} large /> <span className="text-gray-500">{group.topics.length} 項目</span>
              </h3>
              <Card className="p-0">
                <ul className="divide-y divide-gray-100 dark:divide-neutral-800">
                  {group.topics.map((topic) => (
                    <li key={topic.id}>
                      <Link to={`/grammar/${encodeURIComponent(topic.id)}`} className="flex min-h-14 items-center gap-3 px-3 py-2 hover:bg-gray-50 dark:hover:bg-neutral-800/50">
                        <div className="min-w-0 flex-1">
                          <p className="truncate text-sm font-semibold">{topic.titleJa}</p>
                          <p className="truncate text-xs text-gray-500 dark:text-neutral-400">
                            {topic.title} ・ {categoryLabel[topic.category]}
                          </p>
                        </div>
                        {topic.reviewStatus === 'unreviewed' && <Badge tone="amber">未監修</Badge>}
                        <span className="text-gray-400" aria-hidden="true">
                          ›
                        </span>
                      </Link>
                    </li>
                  ))}
                </ul>
              </Card>
            </section>
          ))}
        </div>
      )}
    </div>
  );
}
