import { cx } from './ui';

// Public URL of the LINE Official Account (for example https://line.me/R/ti/p/@xxxx). Not a secret.
const officialAccountUrl = import.meta.env.VITE_LINE_OFFICIAL_ACCOUNT_URL as string | undefined;

/** Conversations with the AI tutor happen in the LINE talk, not in the mini app. */
export function TalkToTutorLink({ className, label = '💬 LINE のトークで講師と話す' }: { className?: string; label?: string }) {
  if (!officialAccountUrl) {
    return <p className={cx('text-xs text-gray-500 dark:text-neutral-400', className)}>公式アカウントのトークで、AI講師と会話できます。</p>;
  }
  return (
    <a
      href={officialAccountUrl}
      className={cx(
        'flex min-h-11 items-center justify-center rounded-xl border border-gray-300 bg-white px-4 text-sm font-semibold text-gray-800 hover:bg-gray-50 dark:border-neutral-700 dark:bg-neutral-900 dark:text-neutral-100 dark:hover:bg-neutral-800',
        className,
      )}
    >
      {label}
    </a>
  );
}
