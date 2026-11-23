import { useEffect, useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import type { ActivityItem, ReviewPlanView, StartPracticeRequest } from '@line-language-learn/shared';
import { api } from '../api';
import { BusyOverlay } from '../components/BusyOverlay';
import { MasteryList } from '../components/MasteryList';
import { ReviewPlanCard } from '../components/ReviewPlanCard';
import { TalkToTutorLink } from '../components/TalkToTutorLink';
import {
  Badge,
  Button,
  Card,
  EmptyState,
  ErrorState,
  InlineError,
  LinkButton,
  PageSkeleton,
  SectionHeader,
  TopicChip,
} from '../components/ui';
import { formatDateTime, pct } from '../format';
import { useAction, useApi } from '../hooks';

function activityPath(item: ActivityItem): string {
  return item.kind === 'practice' ? `/practice/${encodeURIComponent(item.id)}` : `/conversations/${encodeURIComponent(item.id)}`;
}

export default function HomePage() {
  const navigate = useNavigate();
  const dashboard = useApi((signal) => api.dashboard(signal), []);
  const [actionKey, setActionKey] = useState<string | null>(null);
  const start = useAction(async (key: string, body: StartPracticeRequest) => {
    setActionKey(key);
    const session = await api.startPractice(body);
    navigate(`/practice/${encodeURIComponent(session.id)}`);
  });
  const startingKey = start.pending ? actionKey : null;
  const errorFor = (key: string) => (actionKey === key ? start.error : undefined);
  const { hash } = useLocation();
  const loaded = dashboard.data !== undefined;

  useEffect(() => {
    if (!loaded || !hash) return;
    document.getElementById(decodeURIComponent(hash.slice(1)))?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }, [loaded, hash]);

  if (!dashboard.data) {
    return dashboard.error ? <ErrorState error={dashboard.error} onRetry={dashboard.reload} /> : <PageSkeleton cards={4} />;
  }

  const { learner, stats, recommendation, weakTopics, strongTopics, recentActivity, reviewPlans } = dashboard.data;

  const startRecommendation = () => {
    if (recommendation.practiceSessionId) {
      navigate(`/practice/${encodeURIComponent(recommendation.practiceSessionId)}`);
    } else if (recommendation.mode === 'review' && recommendation.reviewPlanId) {
      void start.run('recommendation', { mode: 'review', reviewPlanId: recommendation.reviewPlanId });
    } else {
      void start.run('recommendation', { mode: 'recommended' });
    }
  };

  const startReview = (plan: ReviewPlanView) => {
    void start.run(`plan:${plan.id}`, { mode: 'review', reviewPlanId: plan.id });
  };

  const ctaLabel = recommendation.practiceSessionId
    ? '続きから演習する'
    : recommendation.mode === 'review'
      ? '復習演習を始める'
      : 'おすすめ演習を始める';

  return (
    <div>
      {start.pending && <BusyOverlay message="問題を準備しています…" sub="AI が問題を生成・検証しています。少し時間がかかることがあります。" />}

      <Card className="bg-gradient-to-br from-line to-emerald-600 text-white dark:from-green-800 dark:to-emerald-900">
        <p className="text-xs opacity-90">Ciao! 👋</p>
        <div className="mt-1 flex items-center gap-2">
          <h1 className="text-xl font-bold">{learner.learnerName} さん</h1>
          <span className="rounded-md bg-white/20 px-2 py-0.5 text-xs font-bold">CEFR {learner.currentLevel}</span>
        </div>
        {learner.learningGoals.length > 0 && <p className="mt-1 text-xs opacity-90">目標: {learner.learningGoals.join(' / ')}</p>}
        <div className="mt-3 grid grid-cols-3 gap-2 text-center">
          <div className="rounded-xl bg-white/15 p-2">
            <div className="text-lg font-bold tabular-nums">{stats.answered}</div>
            <div className="text-[11px] opacity-90">回答数</div>
          </div>
          <div className="rounded-xl bg-white/15 p-2">
            <div className="text-lg font-bold tabular-nums">{stats.correct}</div>
            <div className="text-[11px] opacity-90">正解数</div>
          </div>
          <div className="rounded-xl bg-white/15 p-2">
            <div className="text-lg font-bold tabular-nums">{pct(stats.accuracy)}</div>
            <div className="text-[11px] opacity-90">正答率</div>
          </div>
        </div>
      </Card>

      <SectionHeader title="今日のおすすめ演習" />
      <Card className="border-green-200 dark:border-green-900">
        <div className="flex items-center gap-2">
          <Badge tone={recommendation.mode === 'review' ? 'purple' : 'green'}>
            {recommendation.mode === 'review' ? '復習' : 'おすすめ'}
          </Badge>
          {recommendation.practiceSessionId && <Badge tone="amber">途中から再開</Badge>}
        </div>
        <p className="mt-2 text-sm text-gray-700 dark:text-neutral-300">{recommendation.reasonJa}</p>
        {recommendation.topics.length > 0 && (
          <div className="mt-3 flex flex-wrap gap-1.5">
            {recommendation.topics.map((topic) => (
              <TopicChip key={topic.topicId} topic={topic} to={`/grammar/${encodeURIComponent(topic.topicId)}`} />
            ))}
          </div>
        )}
        <Button block className="mt-4 min-h-12 text-base" loading={startingKey === 'recommendation'} disabled={start.pending} onClick={startRecommendation}>
          {ctaLabel}
        </Button>
        <InlineError error={errorFor('recommendation')} />
        <Link to="/practice" className="mt-3 block text-center text-sm font-semibold text-line">
          レベルや文法項目を選んで演習する →
        </Link>
      </Card>

      <SectionHeader title="苦手な文法項目" action={<Link to="/history" className="text-xs font-semibold text-line">すべて見る</Link>} />
      <Card>{weakTopics.length > 0 ? <MasteryList items={weakTopics} /> : <EmptyState>苦手な項目はまだありません。</EmptyState>}</Card>

      <SectionHeader title="AI講師との会話から見つかった復習" id="review-plans" />
      {reviewPlans.length > 0 ? (
        <div className="space-y-3">
          {reviewPlans.map((plan) => (
            <ReviewPlanCard key={plan.id} plan={plan} onStart={startReview} starting={startingKey === `plan:${plan.id}`} error={errorFor(`plan:${plan.id}`)} />
          ))}
        </div>
      ) : (
        <>
          <EmptyState>復習プランはまだありません。LINE のトークで講師との会話を終えると、つまずいた項目がここに表示されます。</EmptyState>
          <TalkToTutorLink className="mt-3" />
        </>
      )}

      {strongTopics.length > 0 && (
        <>
          <SectionHeader title="得意な文法項目" />
          <Card>
            <MasteryList items={strongTopics} />
          </Card>
        </>
      )}

      <SectionHeader title="最近の学習" />
      {recentActivity.length > 0 ? (
        <Card className="p-0">
          <ul className="divide-y divide-gray-100 dark:divide-neutral-800">
            {recentActivity.map((item) => (
              <li key={`${item.kind}:${item.id}`}>
                <Link to={activityPath(item)} className="flex items-start gap-3 p-3 hover:bg-gray-50 dark:hover:bg-neutral-800/50">
                  <span className="mt-0.5 text-xl" aria-hidden="true">
                    {item.kind === 'practice' ? '✏️' : '💬'}
                  </span>
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm font-semibold">{item.title}</p>
                    <p className="text-xs text-gray-500 dark:text-neutral-400">{item.detail}</p>
                  </div>
                  <span className="shrink-0 text-[11px] text-gray-400">{formatDateTime(item.occurredAt)}</span>
                </Link>
              </li>
            ))}
          </ul>
        </Card>
      ) : (
        <EmptyState>まだ学習記録がありません。</EmptyState>
      )}

      <div className="mt-6 grid grid-cols-2 gap-2">
        <LinkButton to="/practice" variant="secondary">
          ✏️ 演習を選ぶ
        </LinkButton>
        <TalkToTutorLink label="💬 講師と話す" />
      </div>
    </div>
  );
}
