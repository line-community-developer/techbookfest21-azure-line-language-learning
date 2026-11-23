import { Link, NavLink, Outlet } from 'react-router-dom';
import { cx } from './ui';

const tabs = [
  { to: '/', label: 'ホーム', icon: '🏠', end: true },
  { to: '/practice', label: '演習', icon: '✏️', end: false },
  { to: '/history', label: '履歴', icon: '📈', end: false },
  { to: '/grammar', label: '文法', icon: '📘', end: false },
] as const;

export function LearningLayout() {
  return (
    <div className="min-h-screen bg-gray-50 dark:bg-neutral-950">
      <div className="mx-auto flex min-h-screen w-full max-w-[480px] flex-col bg-gray-50 dark:bg-neutral-950">
        <header className="sticky top-0 z-30 border-b border-gray-200 bg-white/95 backdrop-blur dark:border-neutral-800 dark:bg-neutral-900/95">
          <div className="flex h-14 items-center justify-between gap-2 px-4">
            <Link to="/" className="flex items-center gap-2 font-bold">
              <span className="flex h-8 w-8 items-center justify-center rounded-lg bg-line text-sm text-white" aria-hidden="true">
                🇮🇹
              </span>
              <span className="text-base">
                Language <span className="text-line">Learning</span>
              </span>
            </Link>
          </div>
        </header>

        <main className="flex-1 px-4 pb-28 pt-4">
          <Outlet />
        </main>

        <nav
          aria-label="メインメニュー"
          className="fixed bottom-0 left-1/2 z-30 w-full max-w-[480px] -translate-x-1/2 border-t border-gray-200 bg-white/95 pb-[env(safe-area-inset-bottom)] backdrop-blur dark:border-neutral-800 dark:bg-neutral-900/95"
        >
          <ul className="grid grid-cols-4">
            {tabs.map((tab) => (
              <li key={tab.to}>
                <NavLink
                  to={tab.to}
                  end={tab.end}
                  className={({ isActive }) =>
                    cx(
                      'flex min-h-16 flex-col items-center justify-center gap-0.5 text-[11px] font-semibold transition-colors',
                      isActive ? 'text-line' : 'text-gray-500 hover:text-gray-800 dark:text-neutral-400 dark:hover:text-neutral-100',
                    )
                  }
                >
                  {({ isActive }) => (
                    <>
                      <span className={cx('text-xl leading-none', !isActive && 'opacity-60 grayscale')} aria-hidden="true">
                        {tab.icon}
                      </span>
                      {tab.label}
                    </>
                  )}
                </NavLink>
              </li>
            ))}
          </ul>
        </nav>
      </div>
    </div>
  );
}
