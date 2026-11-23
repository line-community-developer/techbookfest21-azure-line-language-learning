import { useEffect, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { Link } from 'react-router-dom';
import { z } from 'zod/v4';
import { GrammarTopic as grammarTopicSchema } from '@line-language-learn/shared';
import { useAuth } from '@/lib/auth/schemes/admin/auth-context';

type GrammarTopic = z.infer<typeof grammarTopicSchema>;

export default function GrammarTopicDetailPage() {
  const params = useParams();
  const router = useNavigate();
  const [grammarTopic, setGrammarTopic] = useState<GrammarTopic | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const { hasAnyRole } = useAuth();
  const canWrite = hasAnyRole(["administrator"]);

  useEffect(() => {
    const id = params?.id as string;
    if (!id) return;

    fetch(`/api/grammarTopic/${id}`)
      .then((res) => {
        if (!res.ok) throw new Error('Failed to fetch grammarTopic');
        return res.json();
      })
      .then((data) => {
        setGrammarTopic(data);
        setLoading(false);
      })
      .catch((err) => {
        setError(err.message);
        setLoading(false);
      });


  }, [params.id]);

  const handleDelete = async () => {
    if (!confirm('Are you sure you want to delete this item?')) return;

    try {
      const res = await fetch(`/api/grammarTopic/${params?.id}`, {
        method: 'DELETE',
      });

      if (!res.ok) throw new Error('Failed to delete grammarTopic');

      router('/grammar-topic');
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

  if (error || !grammarTopic) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-red-600 dark:text-red-400">Error: {error || 'GrammarTopic not found'}</div>
      </div>
    );
  }

  return (
    <div className="container mx-auto px-4 py-8">
      <div className="max-w-2xl mx-auto">
        <div className="flex justify-between items-center mb-6">
          <h1 className="text-3xl font-bold text-gray-900 dark:text-gray-100">GrammarTopic Details</h1>
          {canWrite && (
            <div className="space-x-2">
              <Link
                to={`/grammar-topic/${grammarTopic.id}/edit`}
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
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(grammarTopic.id)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">language</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(grammarTopic.language)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">schemaVersion</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(grammarTopic.schemaVersion)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">title</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(grammarTopic.title)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">titleJa</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(grammarTopic.titleJa)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">level</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(grammarTopic.level)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">category</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(grammarTopic.category)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">description</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(grammarTopic.description)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">usageJa</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(grammarTopic.usageJa)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">rules</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100"><pre className="whitespace-pre-wrap">{grammarTopic.rules === undefined ? '-' : JSON.stringify(grammarTopic.rules, null, 2)}</pre></dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">examples</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100"><pre className="whitespace-pre-wrap">{grammarTopic.examples === undefined ? '-' : JSON.stringify(grammarTopic.examples, null, 2)}</pre></dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">commonMistakes</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100"><pre className="whitespace-pre-wrap">{grammarTopic.commonMistakes === undefined ? '-' : JSON.stringify(grammarTopic.commonMistakes, null, 2)}</pre></dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">relatedTopics</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(grammarTopic.relatedTopics)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">keywords</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(grammarTopic.keywords)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">sourceReferences</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100"><pre className="whitespace-pre-wrap">{grammarTopic.sourceReferences === undefined ? '-' : JSON.stringify(grammarTopic.sourceReferences, null, 2)}</pre></dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">reviewStatus</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(grammarTopic.reviewStatus)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">reviewNoteJa</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(grammarTopic.reviewNoteJa)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">createdAt</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(grammarTopic.createdAt)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">updatedAt</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(grammarTopic.updatedAt)}</dd>
            </div>
          </dl>
        </div>

        <div className="mt-6">
          <Link
            to="/grammar-topic"
            className="text-blue-600 dark:text-blue-400 hover:text-blue-900 dark:hover:text-blue-300"
          >
            &larr; Back to list
          </Link>
        </div>
      </div>
    </div>
  );
}
