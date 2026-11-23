import type { ButtonHTMLAttributes, HTMLAttributes, ReactNode } from 'react';
import { Link, type LinkProps } from 'react-router-dom';
import type { CefrLevel, MasteryStatus, ObservationCertainty, ReviewPriority, TopicRef } from '@line-language-learn/shared';
import { ApiRequestError } from '../api';
import { certaintyLabel, masteryStatusLabel, pct, priorityLabel } from '../format';

export function cx(...classes: Array<string | false | null | undefined>): string {
  return classes.filter(Boolean).join(' ');
}

export function Card({ className, children, ...rest }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cx(
        'rounded-2xl border border-gray-200 bg-white p-4 shadow-sm dark:border-neutral-800 dark:bg-neutral-900',
        className,
      )}
      {...rest}
    >
      {children}
    </div>
  );
}

export type Tone = 'gray' | 'green' | 'red' | 'amber' | 'blue' | 'purple';

const toneClass: Record<Tone, string> = {
  gray: 'bg-gray-100 text-gray-700 dark:bg-neutral-800 dark:text-neutral-300',
  green: 'bg-green-100 text-green-800 dark:bg-green-950 dark:text-green-300',
  red: 'bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-300',
  amber: 'bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-300',
  blue: 'bg-blue-100 text-blue-800 dark:bg-blue-950 dark:text-blue-300',
  purple: 'bg-purple-100 text-purple-800 dark:bg-purple-950 dark:text-purple-300',
};

export function Badge({ tone = 'gray', className, children, title }: { tone?: Tone; className?: string; children: ReactNode; title?: string }) {
  return (
    <span title={title} className={cx('inline-flex shrink-0 items-center gap-1 whitespace-nowrap rounded-full px-2 py-0.5 text-xs font-semibold', toneClass[tone], className)}>
      {children}
    </span>
  );
}

type Variant = 'primary' | 'secondary' | 'ghost' | 'danger';

const variantClass: Record<Variant, string> = {
  primary: 'bg-line text-white hover:bg-line-strong active:bg-line-strong shadow-sm',
  secondary:
    'border border-gray-300 bg-white text-gray-800 hover:bg-gray-50 dark:border-neutral-700 dark:bg-neutral-900 dark:text-neutral-100 dark:hover:bg-neutral-800',
  ghost: 'text-line hover:bg-green-50 dark:hover:bg-neutral-800',
  danger: 'border border-red-300 bg-white text-red-700 hover:bg-red-50 dark:border-red-900 dark:bg-neutral-900 dark:text-red-300 dark:hover:bg-red-950',
};

const buttonBase =
  'inline-flex min-h-11 items-center justify-center gap-2 rounded-xl px-4 py-2 text-sm font-semibold transition-colors disabled:cursor-not-allowed disabled:opacity-50 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-line';

export function Button({
  variant = 'primary',
  loading = false,
  block = false,
  className,
  children,
  disabled,
  type = 'button',
  ...rest
}: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: Variant; loading?: boolean; block?: boolean }) {
  return (
    <button
      type={type}
      className={cx(buttonBase, variantClass[variant], block && 'w-full', className)}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      {...rest}
    >
      {loading && <Spinner className="h-4 w-4" />}
      {children}
    </button>
  );
}

export function LinkButton({
  variant = 'primary',
  block = false,
  className,
  ...rest
}: LinkProps & { variant?: Variant; block?: boolean }) {
  return <Link className={cx(buttonBase, variantClass[variant], block && 'w-full', className)} {...rest} />;
}

export function Spinner({ className }: { className?: string }) {
  return (
    <svg className={cx('animate-spin', className ?? 'h-5 w-5')} viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
      <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z" />
    </svg>
  );
}

export function Skeleton({ className }: { className?: string }) {
  return <div className={cx('animate-pulse rounded-lg bg-gray-200 dark:bg-neutral-800', className ?? 'h-4 w-full')} />;
}

export function PageSkeleton({ cards = 3 }: { cards?: number }) {
  return (
    <div className="space-y-4" aria-busy="true" aria-label="読み込み中">
      {Array.from({ length: cards }, (_, i) => (
        <Card key={i} className="space-y-3">
          <Skeleton className="h-5 w-1/2" />
          <Skeleton className="h-4 w-full" />
          <Skeleton className="h-4 w-4/5" />
        </Card>
      ))}
    </div>
  );
}

export function ErrorState({ error, onRetry, title = '読み込みに失敗しました' }: { error: Error; onRetry?: () => void; title?: string }) {
  const detail = error instanceof ApiRequestError && error.code ? `code: ${error.code}` : null;
  return (
    <Card className="border-red-200 bg-red-50 dark:border-red-900 dark:bg-red-950/40" role="alert">
      <p className="font-semibold text-red-800 dark:text-red-300">⚠️ {title}</p>
      <p className="mt-1 break-words text-sm text-red-700 dark:text-red-300">{error.message}</p>
      {detail && <p className="mt-1 text-xs text-red-600/80 dark:text-red-400/80">{detail}</p>}
      {onRetry && (
        <Button variant="secondary" className="mt-3" onClick={onRetry}>
          再試行
        </Button>
      )}
    </Card>
  );
}

export function InlineError({ error }: { error: Error | undefined }) {
  if (!error) return null;
  return (
    <p role="alert" className="mt-2 break-words rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950/50 dark:text-red-300">
      ⚠️ {error.message}
    </p>
  );
}

export function EmptyState({ children }: { children: ReactNode }) {
  return <p className="rounded-xl border border-dashed border-gray-300 p-4 text-center text-sm text-gray-500 dark:border-neutral-700 dark:text-neutral-400">{children}</p>;
}

export function SectionHeader({ title, action, id }: { title: ReactNode; action?: ReactNode; id?: string }) {
  return (
    <div className="mb-2 mt-6 flex items-center justify-between gap-2 first:mt-0" id={id}>
      <h2 className="text-base font-bold">{title}</h2>
      {action}
    </div>
  );
}

export function PageTitle({ children, sub }: { children: ReactNode; sub?: ReactNode }) {
  return (
    <div className="mb-4">
      <h1 className="text-xl font-bold">{children}</h1>
      {sub && <p className="mt-1 text-sm text-gray-500 dark:text-neutral-400">{sub}</p>}
    </div>
  );
}

const levelTone: Record<CefrLevel, string> = {
  A1: 'bg-sky-500',
  A2: 'bg-cyan-600',
  B1: 'bg-emerald-600',
  B2: 'bg-green-700',
  C1: 'bg-violet-600',
  C2: 'bg-fuchsia-700',
};

export function LevelBadge({ level, large = false }: { level: CefrLevel; large?: boolean }) {
  return (
    <span
      className={cx(
        'inline-flex shrink-0 items-center justify-center rounded-md font-bold text-white',
        levelTone[level],
        large ? 'px-2.5 py-1 text-sm' : 'px-1.5 py-0.5 text-[11px] leading-none',
      )}
      title={`CEFR ${level}`}
    >
      {level}
    </span>
  );
}

const masteryTone: Record<MasteryStatus, Tone> = { unknown: 'gray', weak: 'red', developing: 'amber', strong: 'green' };
const masteryBar: Record<MasteryStatus, string> = {
  unknown: 'bg-gray-400',
  weak: 'bg-red-500',
  developing: 'bg-amber-500',
  strong: 'bg-line',
};

export function MasteryStatusBadge({ status }: { status: MasteryStatus }) {
  return <Badge tone={masteryTone[status]}>{masteryStatusLabel[status]}</Badge>;
}

export function MasteryBar({ score, status }: { score: number; status: MasteryStatus }) {
  const width = Math.round(Math.min(1, Math.max(0, score)) * 100);
  return (
    <div className="flex items-center gap-2">
      <div
        className="h-2.5 flex-1 overflow-hidden rounded-full bg-gray-200 dark:bg-neutral-800"
        role="progressbar"
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={width}
      >
        <div className={cx('h-full rounded-full transition-all', masteryBar[status])} style={{ width: `${width}%` }} />
      </div>
      <span className="w-10 text-right text-xs font-semibold tabular-nums text-gray-600 dark:text-neutral-300">{pct(score)}</span>
    </div>
  );
}

const priorityTone: Record<ReviewPriority, Tone> = { high: 'red', medium: 'amber', low: 'blue' };

export function PriorityBadge({ priority }: { priority: ReviewPriority }) {
  return <Badge tone={priorityTone[priority]}>{priorityLabel[priority]}</Badge>;
}

export function CertaintyBadge({ certainty }: { certainty: ObservationCertainty }) {
  return certainty === 'ai-inferred' ? (
    <Badge tone="purple" title="AI が会話から推定した観察です">🤖 {certaintyLabel[certainty]}</Badge>
  ) : (
    <Badge tone="blue" title="採点など確定的な処理で得られた観察です">✔ {certaintyLabel[certainty]}</Badge>
  );
}

export function TopicChip({ topic, to }: { topic: Pick<TopicRef, 'titleJa' | 'level'> & { topicId?: string }; to?: string }) {
  const content = (
    <>
      <LevelBadge level={topic.level} />
      <span>{topic.titleJa}</span>
    </>
  );
  const className =
    'inline-flex items-center gap-1.5 rounded-full border border-green-200 bg-green-50 px-2.5 py-1 text-xs font-medium text-green-900 dark:border-green-900 dark:bg-green-950/40 dark:text-green-200';
  return to ? (
    <Link to={to} className={cx(className, 'hover:bg-green-100 dark:hover:bg-green-900/40')}>
      {content}
    </Link>
  ) : (
    <span className={className}>{content}</span>
  );
}

export function StatTile({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className="rounded-xl bg-gray-50 p-3 text-center dark:bg-neutral-800/60">
      <div className="text-lg font-bold tabular-nums">{value}</div>
      <div className="text-xs text-gray-500 dark:text-neutral-400">{label}</div>
    </div>
  );
}
