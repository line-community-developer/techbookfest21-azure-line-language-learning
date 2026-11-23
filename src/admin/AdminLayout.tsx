import type { ReactNode } from 'react';
import { Link, NavLink, useLocation } from 'react-router-dom';
import { AuthProvider, useAuth } from '@/lib/auth/schemes/admin/auth-context';
import { adminFeatures } from './navigation';

const linkClass = 'rounded-lg px-3 py-2 text-sm font-medium transition-colors hover:bg-gray-100 dark:hover:bg-gray-800';

export function AdminLayout({ children }: { children: ReactNode }) {
  return <AuthProvider><AdminShell>{children}</AdminShell></AuthProvider>;
}

function AdminShell({ children }: { children: ReactNode }) {
  const { user, loading, hasRole } = useAuth();
  const { pathname } = useLocation();
  const feature = adminFeatures.find(item => pathname === item.path || pathname.startsWith(item.path + '/'));
  return (
    <div className="min-h-screen bg-gray-50 text-gray-900 dark:bg-gray-900 dark:text-gray-100">
      <header className="border-b border-gray-200 bg-white dark:border-gray-700 dark:bg-gray-800">
        <div className="container mx-auto flex flex-wrap items-center justify-between gap-4 px-4 py-4">
          <Link className="text-xl font-bold" to="/admin">Language Learning 管理</Link>
          <div className="flex flex-wrap items-center gap-4 text-sm text-gray-600 dark:text-gray-300">
            <a className="hover:underline" href="/miniapp">学習アプリを開く</a>
            {user && <><span>{user.loginId}</span><a className="hover:underline" href="/.auth/logout?post_logout_redirect_uri=/admin/login">ログアウト</a></>}
          </div>
        </div>
      </header>
      {loading ? <p className="container mx-auto px-4 py-8" role="status">ログイン状態を確認しています…</p>
        : !hasRole('administrator') ? (
          <main className="container mx-auto px-4 py-8">
            <h1 className="mb-4 text-3xl font-bold">管理者権限が必要です</h1>
            <p role="alert" className="mb-6">管理者として招待された GitHub アカウントでログインしてください。</p>
            <Link className="text-blue-600 hover:underline dark:text-blue-400" to="/admin/login">ログイン画面へ</Link>
          </main>
        ) : <>
          <nav aria-label="管理者メニュー" className="container mx-auto flex flex-wrap gap-2 px-4 py-4">
            <NavLink end to="/admin" className={({ isActive }) => `${linkClass} ${isActive ? 'bg-blue-100 text-blue-800 dark:bg-blue-900 dark:text-blue-100' : ''}`}>管理メニュー</NavLink>
            {adminFeatures.map(item => <NavLink key={item.path} to={item.path}
              className={({ isActive }) => `${linkClass} ${isActive ? 'bg-blue-100 text-blue-800 dark:bg-blue-900 dark:text-blue-100' : ''}`}>
              {item.title}
            </NavLink>)}
          </nav>
          {feature && <aside aria-label="機能の説明" className="container mx-auto px-4 pt-2">
            <div className="space-y-2 rounded-xl border border-gray-200 bg-white p-5 text-sm dark:border-gray-700 dark:bg-gray-800">
              <p className="font-semibold">{feature.title}：{feature.description}</p>
              <p className="text-gray-600 dark:text-gray-300">{feature.note}</p>
            </div>
          </aside>}
          {children}
        </>}
    </div>
  );
}
