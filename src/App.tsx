import { Navigate, useRoutes, type RouteObject } from 'react-router-dom';
import Home from './Home';
import AdminPage from './admin/AdminPage';
import AdminMenuPage from './admin/AdminMenuPage';
import { AdminLayout } from './admin/AdminLayout';

const modules = import.meta.glob<RouteObject[]>('./pages/**/routes.tsx', { eager: true, import: 'default' });
const pages = import.meta.glob<{ default: React.ComponentType }>('./pages/login.tsx', { eager: true });
const Login = pages['./pages/login.tsx']?.default;

export default function App() {
  return useRoutes([
    // Scaffold's Home links return to the menu within this admin router.
    { path: '/', element: <Navigate to="/admin" replace /> },
    { path: '/swallowkit', element: <Home /> },
    ...(Login ? [{ path: '/login', element: <Login /> }] : []),
    { path: '/admin/login', element: <AdminPage login /> },
    { path: '/admin', element: <AdminLayout><AdminMenuPage /></AdminLayout> },
    { path: '/admin/questions/generate', element: <AdminLayout><AdminPage login={false} /></AdminLayout> },
    ...Object.values(modules).flat().map(route => ({
      ...route,
      // Keep scaffold-owned pages intact; compose the admin shell at the app boundary.
      element: ['/grammar-topic', '/sample-question', '/tutor-profile'].includes(route.path ?? '')
        ? <AdminLayout>{route.element}</AdminLayout> : route.element,
    })),
    { path: '*', element: <main>Page not found</main> },
  ]);
}
