import { useEffect, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import TutorProfileForm from '../../_components/TutorProfileForm';
import { z } from 'zod/v4';
import { TutorProfile as tutorProfileSchema } from '@line-language-learn/shared';
import { useAuth } from '@/lib/auth/schemes/admin/auth-context';

type TutorProfile = z.infer<typeof tutorProfileSchema>;

export default function EditTutorProfilePage() {
  const params = useParams();
  const router = useNavigate();
  const [tutorProfile, setTutorProfile] = useState<TutorProfile | null>(null);
  const [loading, setLoading] = useState(true);
  const { hasAnyRole, loading: authLoading } = useAuth();
  const canWrite = hasAnyRole(["administrator"]);

  useEffect(() => {
    if (!authLoading && !canWrite) {
      router('/tutor-profile');
    }
  }, [authLoading, canWrite, router]);


  useEffect(() => {
    const id = params?.id as string;
    if (!id) return;

    fetch(`/api/tutorProfile/${id}`)
      .then((res) => res.json())
      .then((data) => {
        setTutorProfile(data);
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

  if (!tutorProfile) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-red-600">TutorProfile not found</div>
      </div>
    );
  }

  return (
    <div className="container mx-auto px-4 py-8">
      <div className="max-w-2xl mx-auto">
        <h1 className="text-3xl font-bold mb-6">Edit TutorProfile</h1>
        <div className="bg-white dark:bg-gray-800 shadow-md rounded-lg p-6">
          <TutorProfileForm initialData={tutorProfile} isEdit={true} />
        </div>
      </div>
    </div>
  );
}
