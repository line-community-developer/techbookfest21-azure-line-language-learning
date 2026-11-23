import { Link, useNavigate, useParams } from 'react-router-dom';
import { api } from '../api';
import { BusyOverlay } from '../components/BusyOverlay';
import { Badge, Button, Card, ErrorState, InlineError, LevelBadge, PageSkeleton, SectionHeader } from '../components/ui';
import { categoryLabel } from '../format';
import { useAction, useApi } from '../hooks';

const sourceKindLabel = {
  'original-note': 'オリジナルノート',
  textbook: '教科書',
  web: 'Web',
  'native-review': 'ネイティブ確認',
} as const;

export default function GrammarDetailPage() {
  const { topicId = '' } = useParams();
  const navigate = useNavigate();
  const topic = useApi((signal) => api.grammarTopic(topicId, signal), [topicId]);
  // Used only to resolve related topic titles; failures fall back to showing ids.
  const list = useApi((signal) => api.grammarList(signal), []);
  const practice = useAction(async (id: string) => {
    const session = await api.startPractice({ mode: 'topic', topicIds: [id] });
    navigate(`/practice/${encodeURIComponent(session.id)}`);
  });

  if (!topic.data) {
    return topic.error ? <ErrorState error={topic.error} onRetry={topic.reload} /> : <PageSkeleton cards={4} />;
  }

  const t = topic.data;
  const titles = new Map((list.data?.topics ?? []).map((x) => [x.id, x]));

  return (
    <div>
      {practice.pending && <BusyOverlay message="問題を生成しています…" sub="AI が文法知識をもとに問題を作り、内容を検証しています。" />}
      <Link to="/grammar" className="mb-2 inline-flex min-h-9 items-center text-sm font-semibold text-line">
        ← 文法ガイド
      </Link>

      <Card>
        <div className="flex flex-wrap items-center gap-2">
          <LevelBadge level={t.level} large />
          <Badge tone="gray">{categoryLabel[t.category]}</Badge>
          {t.reviewStatus === 'unreviewed' ? <Badge tone="amber">⚠ 未監修</Badge> : <Badge tone="green">監修済み</Badge>}
        </div>
        <h1 className="mt-2 text-xl font-bold">{t.titleJa}</h1>
        <p lang="it" className="text-sm text-gray-500 dark:text-neutral-400">
          {t.title}
        </p>
        {t.reviewStatus === 'unreviewed' && (
          <div className="mt-3 rounded-xl border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900 dark:border-amber-900 dark:bg-amber-950/40 dark:text-amber-200">
            <p className="font-semibold">この解説はまだ専門家の監修を受けていません。</p>
            {t.reviewNoteJa && <p className="mt-1 text-xs">{t.reviewNoteJa}</p>}
          </div>
        )}
        <p className="mt-3 whitespace-pre-wrap text-sm leading-relaxed">{t.usageJa}</p>
        {t.description && t.description !== t.usageJa && (
          <p lang="en" className="mt-2 text-xs text-gray-500 dark:text-neutral-400">
            {t.description}
          </p>
        )}
      </Card>

      <SectionHeader title="ルール" />
      <Card>
        <ol className="space-y-3">
          {t.rules.map((rule, i) => (
            <li key={rule.id} className="flex gap-3">
              <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-line text-xs font-bold text-white">{i + 1}</span>
              <div className="min-w-0 flex-1">
                <p className="text-sm">{rule.summaryJa}</p>
                {rule.pattern && (
                  <code lang="it" className="mt-1 inline-block rounded-lg bg-gray-100 px-2 py-1 text-xs font-semibold dark:bg-neutral-800">
                    {rule.pattern}
                  </code>
                )}
              </div>
            </li>
          ))}
        </ol>
      </Card>

      <SectionHeader title="例文" />
      <div className="space-y-2">
        {t.examples.map((ex) => (
          <Card key={ex.id} className="border-l-4 border-l-line">
            <p lang="it" className="font-semibold">
              {ex.text}
            </p>
            <p className="mt-0.5 text-sm text-gray-600 dark:text-neutral-300">{ex.translationJa}</p>
            {ex.noteJa && <p className="mt-1 text-xs text-gray-500 dark:text-neutral-400">💡 {ex.noteJa}</p>}
          </Card>
        ))}
      </div>

      {t.commonMistakes.length > 0 && (
        <>
          <SectionHeader title="よくある間違い" />
          <div className="space-y-2">
            {t.commonMistakes.map((m) => (
              <Card key={m.id}>
                <p lang="it" className="text-sm">
                  <span className="text-red-600 line-through decoration-2 dark:text-red-400">✗ {m.incorrect}</span>
                </p>
                <p lang="it" className="mt-1 text-sm font-semibold text-green-700 dark:text-green-400">
                  ✓ {m.correct}
                </p>
                <p className="mt-1.5 text-xs text-gray-600 dark:text-neutral-300">{m.explanationJa}</p>
              </Card>
            ))}
          </div>
        </>
      )}

      {t.relatedTopics.length > 0 && (
        <>
          <SectionHeader title="関連する文法" />
          <div className="flex flex-wrap gap-2">
            {t.relatedTopics.map((id) => {
              const related = titles.get(id);
              return (
                <Link
                  key={id}
                  to={`/grammar/${encodeURIComponent(id)}`}
                  className="inline-flex min-h-9 items-center gap-1.5 rounded-full border border-green-200 bg-green-50 px-3 text-xs font-medium text-green-900 hover:bg-green-100 dark:border-green-900 dark:bg-green-950/40 dark:text-green-200"
                >
                  {related && <LevelBadge level={related.level} />}
                  {related?.titleJa ?? id}
                </Link>
              );
            })}
          </div>
        </>
      )}

      {t.sourceReferences.length > 0 && (
        <>
          <SectionHeader title="出典" />
          <Card>
            <ul className="space-y-1.5 text-xs text-gray-600 dark:text-neutral-300">
              {t.sourceReferences.map((s) => (
                <li key={s.id} className="flex flex-wrap items-center gap-1.5">
                  <Badge tone="gray">{sourceKindLabel[s.kind] ?? s.kind}</Badge>
                  {s.url ? (
                    <a href={s.url} target="_blank" rel="noreferrer noopener" className="text-line underline">
                      {s.label}
                    </a>
                  ) : (
                    <span>{s.label}</span>
                  )}
                  <span className="font-mono text-[10px] text-gray-400">{s.id}</span>
                </li>
              ))}
            </ul>
          </Card>
        </>
      )}

      <div className="mt-6">
        <Button block className="min-h-12 text-base" loading={practice.pending} onClick={() => void practice.run(t.id)}>
          ✏️ この文法を演習する
        </Button>
        <InlineError error={practice.error} />
      </div>
    </div>
  );
}
