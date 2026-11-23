import { useRoutes, type RouteObject } from 'react-router-dom';
import { LearningLayout } from './components/Layout';
import ConversationResultPage from './pages/ConversationResultPage';
import GrammarDetailPage from './pages/GrammarDetailPage';
import GrammarListPage from './pages/GrammarListPage';
import HistoryPage from './pages/HistoryPage';
import HomePage from './pages/HomePage';
import PracticeSessionPage from './pages/PracticeSessionPage';
import PracticeSetupPage from './pages/PracticeSetupPage';

const learningRoutes: RouteObject[] = [
  {
    path: '/',
    element: <LearningLayout />,
    children: [
      { index: true, element: <HomePage /> },
      { path: 'practice', element: <PracticeSetupPage /> },
      { path: 'practice/:sessionId', element: <PracticeSessionPage /> },
      { path: 'history', element: <HistoryPage /> },
      { path: 'grammar', element: <GrammarListPage /> },
      { path: 'grammar/:topicId', element: <GrammarDetailPage /> },
      // Conversations happen in the LINE talk; the mini app shows only their analysis results.
      { path: 'conversations/:sessionId', element: <ConversationResultPage /> },
    ],
  },
  { path: '*', element: <main>Page not found</main> },
];

export default function LearningApp() {
  return useRoutes(learningRoutes);
}
