import type { ReviewPlanView } from '@line-language-learn/shared';
import { formatDateTime, sourceTypeLabel } from '../format';
import { Badge, Button, Card, CertaintyBadge, InlineError, LevelBadge, LinkButton, PriorityBadge } from './ui';

export function ReviewPlanCard({
  plan,
  onStart,
  starting,
  error,
}: {
  plan: ReviewPlanView;
  onStart: (plan: ReviewPlanView) => void;
  starting: boolean;
  error?: Error;
}) {
  const sessionPath = plan.practiceSessionId ? `/practice/${encodeURIComponent(plan.practiceSessionId)}` : null;
  return (
    <Card>
      <div className="flex flex-wrap items-center gap-2">
        <Badge tone={plan.sourceType === 'conversation' ? 'purple' : 'blue'}>{sourceTypeLabel[plan.sourceType]}から</Badge>
        {plan.status === 'completed' && <Badge tone="green">完了</Badge>}
        <span className="ml-auto text-xs text-gray-400">{formatDateTime(plan.createdAt)}</span>
      </div>
      <p className="mt-2 text-sm font-semibold">{plan.sourceLabel}</p>

      <ul className="mt-3 space-y-3">
        {plan.items.map((item) => (
          <li key={item.topicId} className="rounded-xl bg-gray-50 p-3 dark:bg-neutral-800/60">
            <div className="flex items-center gap-2">
              <LevelBadge level={item.level} />
              <span className="min-w-0 flex-1 truncate text-sm font-semibold">{item.titleJa}</span>
              <PriorityBadge priority={item.priority} />
            </div>
            <p className="mt-1.5 text-sm text-gray-700 dark:text-neutral-300">{item.reasonJa}</p>
            {item.evidence.length > 0 && (
              <ul className="mt-2 space-y-1.5">
                {item.evidence.map((ev) => (
                  <li key={ev.observationId} className="flex items-start gap-2">
                    <CertaintyBadge certainty={ev.certainty} />
                    <blockquote className="min-w-0 flex-1 border-l-2 border-gray-300 pl-2 text-xs italic text-gray-600 dark:border-neutral-600 dark:text-neutral-400">
                      “{ev.text}”
                    </blockquote>
                  </li>
                ))}
              </ul>
            )}
          </li>
        ))}
      </ul>

      <div className="mt-3 flex flex-col gap-2">
        {sessionPath && plan.practiceSessionStatus === 'in-progress' ? (
          <LinkButton to={sessionPath} block>
            復習演習を続ける
          </LinkButton>
        ) : (
          <>
            {sessionPath && plan.practiceSessionStatus === 'completed' && (
              <LinkButton to={sessionPath} variant="secondary" block>
                前回の復習結果を見る
              </LinkButton>
            )}
            <Button block loading={starting} onClick={() => onStart(plan)}>
              {sessionPath ? 'もう一度復習する' : 'この内容で復習演習を始める'}
            </Button>
          </>
        )}
        <InlineError error={error} />
      </div>
    </Card>
  );
}
