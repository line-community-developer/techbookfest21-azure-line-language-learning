import { useEffect, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import SampleQuestionForm from '../../_components/SampleQuestionForm';
import { z } from 'zod/v4';
import { SampleQuestion as sampleQuestionSchema } from '@line-language-learn/shared';
import { useAuth } from '@/lib/auth/schemes/admin/auth-context';

type SampleQuestion = z.infer<typeof sampleQuestionSchema>;

export default function EditSampleQuestionPage() {
  const params = useParams();
  const router = useNavigate();
  const [sampleQuestion, setSampleQuestion] = useState<SampleQuestion | null>(null);
  const [loading, setLoading] = useState(true);
  const { hasAnyRole, loading: authLoading } = useAuth();
  const canWrite = hasAnyRole(["administrator"]);

  useEffect(() => {
    if (!authLoading && !canWrite) {
      router('/sample-question');
    }
  }, [authLoading, canWrite, router]);


  useEffect(() => {
    const id = params?.id as string;
    if (!id) return;

    fetch(`/api/sampleQuestion/${id}`)
      .then((res) => res.json())
      .then((data) => {
        setSampleQuestion(data);
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

  if (!sampleQuestion) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-red-600">SampleQuestion not found</div>
      </div>
    );
  }

  return (
    <div className="container mx-auto px-4 py-8">
      <div className="max-w-2xl mx-auto">
        <h1 className="text-3xl font-bold mb-6">Edit SampleQuestion</h1>
        <div className="bg-white dark:bg-gray-800 shadow-md rounded-lg p-6">
          <SampleQuestionForm initialData={sampleQuestion} isEdit={true} />
        </div>
      </div>
    </div>
  );
}
