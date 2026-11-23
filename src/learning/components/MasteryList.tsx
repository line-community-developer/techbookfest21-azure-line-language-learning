import { Link } from 'react-router-dom';
import type { MasteryView } from '@line-language-learn/shared';
import { formatDateTime } from '../format';
import { LevelBadge, MasteryBar, MasteryStatusBadge } from './ui';

export function MasteryRow({ item }: { item: MasteryView }) {
  return (
    <li className="py-3 first:pt-0 last:pb-0">
      <Link to={`/grammar/${encodeURIComponent(item.topicId)}`} className="block rounded-lg hover:opacity-80">
        <div className="mb-1.5 flex items-center gap-2">
          <LevelBadge level={item.level} />
          <span className="min-w-0 flex-1 truncate text-sm font-semibold">{item.titleJa}</span>
          <MasteryStatusBadge status={item.status} />
        </div>
        <MasteryBar score={item.score} status={item.status} />
        <p className="mt-1 text-xs text-gray-500 dark:text-neutral-400">
          {item.title} ・ 正解 {item.correct}/{item.attempts}
          {item.conversationErrors > 0 && ` ・ 会話でのミス ${item.conversationErrors}回`}
          {item.lastObservedAt && ` ・ ${formatDateTime(item.lastObservedAt)}`}
        </p>
      </Link>
    </li>
  );
}

export function MasteryList({ items }: { items: MasteryView[] }) {
  return (
    <ul className="divide-y divide-gray-100 dark:divide-neutral-800">
      {items.map((item) => (
        <MasteryRow key={item.topicId} item={item} />
      ))}
    </ul>
  );
}
