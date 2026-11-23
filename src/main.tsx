import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import App from './App';
import LearningApp from './learning/routes';
import { initLiff } from '@/lib/auth/schemes/line/token-adapter';
import './style.css';

const root = createRoot(document.getElementById('root')!);

const miniAppPath = '/miniapp';
const { pathname, search, hash } = window.location;
const path = pathname.replace(/\/+$/, '');
if (path === miniAppPath || path.startsWith(miniAppPath + '/')) {
  // LIFF must be initialized before routing so LINE Login can redirect back to the same page.
  initLiff().then(
    () => root.render(<StrictMode><BrowserRouter basename={miniAppPath}><LearningApp /></BrowserRouter></StrictMode>),
    () => root.render(
      <main className="mx-auto max-w-md p-6 text-sm">
        <p className="font-semibold">LINE との連携を開始できませんでした。</p>
        <p className="mt-2">LINE アプリから開き直してください。</p>
      </main>,
    ),
  );
} else if (path === '' || /^\/(practice|history|grammar|conversations)(?:\/|$)/.test(path)) {
  // Preserve bookmarks and links to previously shared learner pages.
  window.location.replace(miniAppPath + (path ? pathname : '') + search + hash);
} else {
  root.render(<StrictMode><BrowserRouter><App /></BrowserRouter></StrictMode>);
}
