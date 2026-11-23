import type { RouteObject } from 'react-router-dom';
import { Outlet } from 'react-router-dom';
import Layout from './layout';
import List from './page';
import Detail from './[id]/page';
import New from './new/page';
import Edit from './[id]/edit/page';

const routes: RouteObject[] = [{
  path: '/grammar-topic',
  element: <Layout><Outlet /></Layout>,
  children: [
    { index: true, element: <List /> },
    { path: 'new', element: <New /> },
    { path: ':id', element: <Detail /> },
    { path: ':id/edit', element: <Edit /> },
  ],
}];
export default routes;
