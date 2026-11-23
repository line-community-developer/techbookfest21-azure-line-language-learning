import { Spinner } from './ui';

// Full-screen blocking indicator for slow operations such as AI question generation.
export function BusyOverlay({ message, sub }: { message: string; sub?: string }) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-6 backdrop-blur-sm" role="status" aria-live="polite">
      <div className="w-full max-w-xs rounded-2xl bg-white p-6 text-center shadow-2xl dark:bg-neutral-900">
        <Spinner className="mx-auto h-10 w-10 text-line" />
        <p className="mt-4 font-semibold">{message}</p>
        {sub && <p className="mt-1 text-xs text-gray-500 dark:text-neutral-400">{sub}</p>}
      </div>
    </div>
  );
}
