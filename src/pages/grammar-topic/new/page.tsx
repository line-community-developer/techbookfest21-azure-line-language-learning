import { useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import GrammarTopicForm from '../_components/GrammarTopicForm';
import { useAuth } from '@/lib/auth/schemes/admin/auth-context';

export default function NewGrammarTopicPage() {
  const router = useNavigate();
  const { hasAnyRole, loading } = useAuth();
  const canWrite = hasAnyRole(["administrator"]);

  useEffect(() => {
    if (!loading && !canWrite) {
      router('/grammar-topic');
    }
  }, [loading, canWrite, router]);

  if (loading) {
    return <div className="flex items-center justify-center min-h-screen"><div className="text-lg">Loading...</div></div>;
  }

  if (!canWrite) return null;

  return (
    <div className="container mx-auto px-4 py-8">
      <div className="max-w-2xl mx-auto">
        <h1 className="text-3xl font-bold mb-6">Create New GrammarTopic</h1>
        <div className="bg-white dark:bg-gray-800 shadow-md rounded-lg p-6">
          <GrammarTopicForm />
        </div>
      </div>
    </div>
  );
}
