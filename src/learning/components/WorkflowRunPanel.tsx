import { useEffect, useRef } from 'react';
import { Link } from 'react-router-dom';
import type { WorkflowRun, WorkflowStepState } from '@line-language-learn/shared';
import { api } from '../api';
import { stepStatusLabel } from '../format';
import { useAction } from '../hooks';
import { Badge, Button, Card, InlineError, Spinner, cx, type Tone } from './ui';

const stepTone: Record<WorkflowStepState['status'], Tone> = {
  pending: 'gray',
  running: 'blue',
  completed: 'green',
  failed: 'red',
  skipped: 'gray',
};

const stepIcon: Record<WorkflowStepState['status'], string> = {
  pending: '○',
  running: '◐',
  completed: '✓',
  failed: '✗',
  skipped: '–',
};

const runTone: Record<WorkflowRun['status'], Tone> = { pending: 'gray', running: 'blue', completed: 'green', failed: 'red' };
const runLabel: Record<WorkflowRun['status'], string> = { pending: '待機中', running: '実行中', completed: '完了', failed: '失敗' };

// Analysis runs asynchronously on Durable Functions (AI analysis, generation and evaluation): poll up to 3 minutes.
const MAX_POLLS = 90;

function OutputValue({ name, value }: { name: string; value: string }) {
  if (name === 'practiceSessionId' && value) {
    return (
      <Link to={`/practice/${encodeURIComponent(value)}`} className="font-semibold text-line underline">
        復習演習を開く →
      </Link>
    );
  }
  if (name === 'reviewPlanId' && value) {
    return (
      <Link to="/#review-plans" className="font-semibold text-line underline">
        ホームで復習プランを見る →
      </Link>
    );
  }
  // Steps persist serialized intermediate results (JSON) for resumability; show only their size here.
  const trimmed = value.trim();
  if (trimmed.startsWith('[') || trimmed.startsWith('{')) {
    try {
      const parsed: unknown = JSON.parse(trimmed);
      const label = Array.isArray(parsed) ? `${parsed.length} 件（中間データ）` : '（中間データ）';
      return <span className="text-gray-500 dark:text-neutral-400">{label}</span>;
    } catch {
      // fall through to plain text
    }
  }
  return <span className="break-all font-mono">{value.length > 120 ? `${value.slice(0, 120)}…` : value}</span>;
}

export function WorkflowRunPanel({ run, onChange }: { run: WorkflowRun; onChange: (run: WorkflowRun) => void }) {
  const polls = useRef(0);
  const retry = useAction(async () => {
    const next = await api.retryWorkflow(run.id);
    polls.current = 0;
    onChange(next);
  });
  const onChangeRef = useRef(onChange);
  onChangeRef.current = onChange;
  const active = run.status === 'pending' || run.status === 'running';

  useEffect(() => {
    if (!active || polls.current >= MAX_POLLS) return;
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      polls.current += 1;
      api.workflow(run.id, controller.signal).then(
        (next) => onChangeRef.current(next),
        () => undefined,
      );
    }, 2000);
    return () => {
      window.clearTimeout(timer);
      controller.abort();
    };
  }, [active, run]);

  return (
    <Card className="border-blue-200 dark:border-blue-900">
      <div className="flex items-center gap-2">
        <span aria-hidden="true">⚙️</span>
        <h3 className="min-w-0 flex-1 truncate text-sm font-bold">会話分析ワークフロー</h3>
        {active && <Spinner className="h-4 w-4 text-blue-500" />}
        <Badge tone={runTone[run.status]}>{runLabel[run.status]}</Badge>
      </div>
      <p className="mt-0.5 font-mono text-[10px] text-gray-400">
        {run.workflowName} ・ 実行回数 {run.runCount}
      </p>

      <ol className="mt-3 space-y-2">
        {run.steps.map((step) => {
          const outputs = step.output;
          return (
            <li key={step.name} className="rounded-xl bg-gray-50 p-2.5 dark:bg-neutral-800/60">
              <div className="flex items-center gap-2">
                <span
                  className={cx(
                    'flex h-5 w-5 shrink-0 items-center justify-center rounded-full text-[11px] font-bold',
                    step.status === 'completed' && 'bg-line text-white',
                    step.status === 'failed' && 'bg-red-500 text-white',
                    step.status === 'running' && 'bg-blue-500 text-white',
                    (step.status === 'pending' || step.status === 'skipped') && 'bg-gray-200 text-gray-500 dark:bg-neutral-700',
                  )}
                  aria-hidden="true"
                >
                  {stepIcon[step.status]}
                </span>
                <span className="min-w-0 flex-1 truncate font-mono text-xs font-semibold">{step.name}</span>
                {step.attempts > 1 && <span className="text-[10px] text-gray-500">試行 {step.attempts}</span>}
                <Badge tone={stepTone[step.status]}>{stepStatusLabel[step.status]}</Badge>
              </div>
              {outputs.length > 0 && (
                <dl className="mt-1.5 space-y-0.5 pl-7 text-xs">
                  {outputs.map(({ key, value }) => (
                    <div key={key} className="flex flex-wrap gap-x-2">
                      <dt className="text-gray-500 dark:text-neutral-400">{key}:</dt>
                      <dd className="min-w-0">
                        <OutputValue name={key} value={value} />
                      </dd>
                    </div>
                  ))}
                </dl>
              )}
              {step.error && (
                <p className="mt-1.5 pl-7 text-xs text-red-600 dark:text-red-400">
                  {step.error.message} <span className="font-mono opacity-70">({step.error.code})</span>
                </p>
              )}
            </li>
          );
        })}
      </ol>

      {run.status === 'failed' && (
        <div className="mt-3">
          {run.error && (
            <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950/50 dark:text-red-300">
              ⚠️ {run.error.message} <span className="font-mono text-xs opacity-70">({run.error.code})</span>
            </p>
          )}
          <Button className="mt-2" block loading={retry.pending} onClick={() => void retry.run()}>
            🔄 再実行
          </Button>
          <InlineError error={retry.error} />
        </div>
      )}
    </Card>
  );
}
