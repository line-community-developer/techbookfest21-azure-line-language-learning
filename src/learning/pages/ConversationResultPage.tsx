import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import type { WorkflowRun } from '@line-language-learn/shared';
import { api } from '../api';
import { TalkToTutorLink } from '../components/TalkToTutorLink';
import { WorkflowRunPanel } from '../components/WorkflowRunPanel';
import { Badge, Card, ErrorState, PageSkeleton, PageTitle, Spinner, TopicChip } from '../components/ui';
import { formatDateTime } from '../format';
import { useApi } from '../hooks';

/** Read-only result of a LINE conversation: its analysis workflow and the review it produced. */
export default function ConversationResultPage() {
  const { sessionId = '' } = useParams();
  const session = useApi((signal) => api.tutorSession(sessionId, signal), [sessionId]);
  const runId = session.data?.analysisWorkflowRunId ?? null;
  const existingRun = useApi((signal) => (runId ? api.workflow(runId, signal) : Promise.resolve(null)), [runId]);
  const [run, setRun] = useState<WorkflowRun | null>(null);
  const workflowRun = run ?? existingRun.data ?? null;

  if (!session.data) {
    return session.error ? <ErrorState error={session.error} onRetry={session.reload} /> : <PageSkeleton cards={2} />;
  }
  const data = session.data;
  const ended = data.status === 'ended';

  return (
    <div>
      <Link to="/history" className="mb-2 inline-flex min-h-9 items-center text-sm font-semibold text-line">
        ← 学習履歴
      </Link>
      <PageTitle sub={formatDateTime(data.createdAt)}>講師との会話のふりかえり</PageTitle>

      <Card>
        <div className="flex items-center gap-3">
          <span className="flex h-12 w-12 shrink-0 items-center justify-center rounded-full bg-green-100 text-3xl dark:bg-green-950" aria-hidden="true">
            {data.tutor.avatarEmoji}
          </span>
          <div className="min-w-0 flex-1">
            <p className="truncate font-bold">{data.tutor.name}</p>
            <p className="truncate text-xs text-gray-500 dark:text-neutral-400">{data.theme || '自由会話'}</p>
          </div>
          <Badge tone={ended ? 'gray' : 'green'}>{ended ? '終了' : '会話中'}</Badge>
        </div>
        {data.focusTopics.length > 0 && (
          <div className="mt-3 flex flex-wrap gap-1.5">
            {data.focusTopics.map((t) => (
              <TopicChip key={t.topicId} topic={t} to={`/grammar/${encodeURIComponent(t.topicId)}`} />
            ))}
          </div>
        )}
      </Card>

      <div className="mt-4">
        {!ended ? (
          <Card>
            <p className="text-sm">この会話は LINE のトークで続いています。トークで「終了」と送るか「会話を終わる」を押すと、苦手な文法の分析が始まります。</p>
            <TalkToTutorLink className="mt-3" />
          </Card>
        ) : workflowRun ? (
          <WorkflowRunPanel run={workflowRun} onChange={setRun} />
        ) : existingRun.error ? (
          <ErrorState error={existingRun.error} onRetry={existingRun.reload} title="分析結果の取得に失敗しました" />
        ) : runId ? (
          <p className="flex items-center gap-2 text-sm">
            <Spinner className="h-4 w-4 text-line" /> 分析結果を読み込んでいます…
          </p>
        ) : (
          <Card>
            <p className="text-sm text-gray-600 dark:text-neutral-300">この会話の分析はありません。</p>
          </Card>
        )}
      </div>
    </div>
  );
}
