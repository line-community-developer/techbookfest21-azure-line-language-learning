import { useEffect, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import GrammarTopicForm from '../../_components/GrammarTopicForm';
import { z } from 'zod/v4';
import { GrammarTopic as grammarTopicSchema } from '@line-language-learn/shared';
import { useAuth } from '@/lib/auth/schemes/admin/auth-context';

type GrammarTopic = z.infer<typeof grammarTopicSchema>;

export default function EditGrammarTopicPage() {
  const params = useParams();
  const router = useNavigate();
  const [grammarTopic, setGrammarTopic] = useState<GrammarTopic | null>(null);
  const [loading, setLoading] = useState(true);
  const { hasAnyRole, loading: authLoading } = useAuth();
  const canWrite = hasAnyRole(["administrator"]);

  useEffect(() => {
    if (!authLoading && !canWrite) {
      router('/grammar-topic');
    }
  }, [authLoading, canWrite, router]);


  useEffect(() => {
    const id = params?.id as string;
    if (!id) return;

    fetch(`/api/grammarTopic/${id}`)
      .then((res) => res.json())
      .then((data) => {
        setGrammarTopic(data);
        setLoading(false);
      })
      .catch(() => setLoading(false));
  }, [params.id]);

  if (loading) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-lg">Loading...</div>
      </div>
    );
  }

  if (!grammarTopic) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-red-600">GrammarTopic not found</div>
      </div>
    );
  }

  return (
    <div className="container mx-auto px-4 py-8">
      <div className="max-w-2xl mx-auto">
        <h1 className="text-3xl font-bold mb-6">Edit GrammarTopic</h1>
        <div className="bg-white dark:bg-gray-800 shadow-md rounded-lg p-6">
          <GrammarTopicForm initialData={grammarTopic} isEdit={true} />
        </div>
      </div>
    </div>
  );
}
