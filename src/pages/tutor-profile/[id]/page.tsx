import { useEffect, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { Link } from 'react-router-dom';
import { z } from 'zod/v4';
import { TutorProfile as tutorProfileSchema } from '@line-language-learn/shared';
import { useAuth } from '@/lib/auth/schemes/admin/auth-context';

type TutorProfile = z.infer<typeof tutorProfileSchema>;

export default function TutorProfileDetailPage() {
  const params = useParams();
  const router = useNavigate();
  const [tutorProfile, setTutorProfile] = useState<TutorProfile | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const { hasAnyRole } = useAuth();
  const canWrite = hasAnyRole(["administrator"]);

  useEffect(() => {
    const id = params?.id as string;
    if (!id) return;

    fetch(`/api/tutorProfile/${id}`)
      .then((res) => {
        if (!res.ok) throw new Error('Failed to fetch tutorProfile');
        return res.json();
      })
      .then((data) => {
        setTutorProfile(data);
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
      const res = await fetch(`/api/tutorProfile/${params?.id}`, {
        method: 'DELETE',
      });

      if (!res.ok) throw new Error('Failed to delete tutorProfile');

      router('/tutor-profile');
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

  if (error || !tutorProfile) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-red-600 dark:text-red-400">Error: {error || 'TutorProfile not found'}</div>
      </div>
    );
  }

  return (
    <div className="container mx-auto px-4 py-8">
      <div className="max-w-2xl mx-auto">
        <div className="flex justify-between items-center mb-6">
          <h1 className="text-3xl font-bold text-gray-900 dark:text-gray-100">TutorProfile Details</h1>
          {canWrite && (
            <div className="space-x-2">
              <Link
                to={`/tutor-profile/${tutorProfile.id}/edit`}
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
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(tutorProfile.id)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">language</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(tutorProfile.language)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">schemaVersion</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(tutorProfile.schemaVersion)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">name</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(tutorProfile.name)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">avatarEmoji</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(tutorProfile.avatarEmoji)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">personaJa</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(tutorProfile.personaJa)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">speakingStyle</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(tutorProfile.speakingStyle)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">greeting</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(tutorProfile.greeting)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">correctionPolicy</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100"><pre className="whitespace-pre-wrap">{tutorProfile.correctionPolicy === undefined ? '-' : JSON.stringify(tutorProfile.correctionPolicy, null, 2)}</pre></dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">promptTemplateId</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(tutorProfile.promptTemplateId)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">createdAt</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(tutorProfile.createdAt)}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-gray-500 dark:text-gray-400">updatedAt</dt>
              <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">{String(tutorProfile.updatedAt)}</dd>
            </div>
          </dl>
        </div>

        <div className="mt-6">
          <Link
            to="/tutor-profile"
            className="text-blue-600 dark:text-blue-400 hover:text-blue-900 dark:hover:text-blue-300"
          >
            &larr; Back to list
          </Link>
        </div>
      </div>
    </div>
  );
}
