import { useEffect, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { Link } from 'react-router-dom';
import { z } from 'zod/v4';
import { SampleQuestion as sampleQuestionSchema } from '@line-language-learn/shared';
import { useAuth } from '@/lib/auth/schemes/admin/auth-context';

type SampleQuestion = z.infer<typeof sampleQuestionSchema>;

export default function SampleQuestionDetailPage() {
  const params = useParams();
  const router = useNavigate();
  const [sampleQuestion, setSampleQuestion] = useState<SampleQuestion | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [grammarTopicMap, setGrammarTopicMap] = useState<Record<string, string>>({});
  const { hasAnyRole } = useAuth();
  const canWrite = hasAnyRole(["administrator"]);

  useEffect(() => {
    const id = params?.id as string;
    if (!id) return;

    fetch(`/api/sampleQuestion/${id}`)
      .then((res) => {
        if (!res.ok) throw new Error('Failed to fetch sampleQuestion');
        return res.json();
      })
      .then((data) => {
        setSampleQuestion(data);
        setLoading(false);
      })
      .catch((err) => {
        setError(err.message);
        setLoading(false);
      });

    // Fetch foreign key reference data
    fetch('/api/grammarTopic')
      .then(res => res.json())
      .then((data: Array<{ id: string; name?: string; title?: string }>) => {
        const map: Record<string, string> = {};
        data.forEach(item => {
          map[item.id] = item.name || item.title || item.id;
        });
        setGrammarTopicMap(map);
      })
      .catch(err => console.error('Failed to fetch GrammarTopics:', err));
  }, [params.id]);

  const handleDelete = async () => {
    if (!confirm('Are you sure you want to delete this item?')) return;

    try {
      const res = await fetch(`/api/sampleQuestion/${params?.id}`, {
        method: 'DELETE',
      });

      if (!res.ok) throw new Error('Failed to delete sampleQuestion');

      router('/sample-question');
    } catch (err) {
      alert(`Error: ${err instanceof Error ? err.message : String(err)}`);
    }
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-lg text-gray-900 dark:text-gray-100">Loading...</div>
      </div>
    );
  }

  if (error || !sampleQuestion) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-red-600 dark:text-red-400">Error: {error || 'SampleQuestion not found'}</div>
      </div>
    );
  }

  return (
    <div className="container mx-auto px-4 py-8">
      <div className="max-w-2xl mx-auto">
        <div className="flex justify-between items-center mb-6">
          <h1 className="text-3xl font-bold text-gray-900 dark:text-gray-100">SampleQuestion Details</h1>
          {canWrite && (
            <div className="space-x-2">
              <Link
                to={`/sample-question/${sampleQuestion.id}/edit`}
                className="inline-flex items-center bg-green-600 hover:bg-green-700 dark:bg-green-500 dark:hover:bg-green-600 text-white px-4 py-2 rounded"
              >
                Edit
              </Link>
              <button
                onClick={handleDelete}
                className="inline-flex items-center bg-red-600 hover:bg-red-700 dark:bg-red-500 dark:hover:bg-red-600 text-white px-4 py-2 rounded"
              >
                Delete
              </button>
            </div>
          )}
        </div>

        <div className="bg-white dark:bg-gray-800 shadow-md rounded-lg p-6">
          <dl className="space-y-4">
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">id</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(sampleQuestion.id)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">language</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(sampleQuestion.language)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">schemaVersion</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(sampleQuestion.schemaVersion)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">GrammarTopic</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{grammarTopicMap[sampleQuestion.grammarTopicId] || sampleQuestion.grammarTopicId}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">type</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(sampleQuestion.type)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">difficulty</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(sampleQuestion.difficulty)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">prompt</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(sampleQuestion.prompt)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">choices</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(sampleQuestion.choices)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">correctAnswer</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(sampleQuestion.correctAnswer)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">acceptedAnswers</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(sampleQuestion.acceptedAnswers)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">explanationJa</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(sampleQuestion.explanationJa)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">ruleIds</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(sampleQuestion.ruleIds)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">publication</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100"><pre className="whitespace-pre-wrap">{sampleQuestion.publication === undefined ? '-' : JSON.stringify(sampleQuestion.publication, null, 2)}</pre></dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">createdAt</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(sampleQuestion.createdAt)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">updatedAt</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(sampleQuestion.updatedAt)}</dd>
            </div>
          </dl>
        </div>

        <div className="mt-6">
          <Link
            to="/sample-question"
            className="text-blue-600 dark:text-blue-400 hover:text-blue-900 dark:hover:text-blue-300"
          >
            &larr; Back to list
          </Link>
        </div>
      </div>
    </div>
  );
}
