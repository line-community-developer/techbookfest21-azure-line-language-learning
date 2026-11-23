import { useState, FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { z } from 'zod/v4';
import { TutorProfile as tutorProfileSchema } from '@line-language-learn/shared';

// Input schema: SwallowKit-managed fields (id, createdAt, updatedAt) are optional
// These fields are ignored by the backend and auto-managed
const TutorProfileInputSchema = tutorProfileSchema.partial({ id: true, createdAt: true, updatedAt: true });

type TutorProfile = z.infer<typeof tutorProfileSchema>;

interface TutorProfileFormProps {
  initialData?: TutorProfile;
  isEdit?: boolean;
}

export default function TutorProfileForm({ initialData, isEdit = false }: TutorProfileFormProps) {
  const router = useNavigate();
  const [loading, setLoading] = useState(false);
  const [errors, setErrors] = useState<Record<string, string>>({});


  
  const [formData, setFormData] = useState({
    language: initialData?.language ?? '',
    schemaVersion: initialData?.schemaVersion !== undefined ? String(initialData.schemaVersion) : '',
    name: initialData?.name ?? '',
    avatarEmoji: initialData?.avatarEmoji ?? '',
    personaJa: initialData?.personaJa ?? '',
    speakingStyle: initialData?.speakingStyle ?? '',
    greeting: initialData?.greeting ?? '',
    correctionPolicy: initialData?.correctionPolicy !== undefined ? JSON.stringify(initialData.correctionPolicy, null, 2) : '',
    promptTemplateId: initialData?.promptTemplateId ?? '',
  });

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setLoading(true);
    setErrors({});

    try {
      // Array フィールドをカンマ区切りから配列に変換
      const submitData: Record<string, unknown> = { ...formData };
      // JSON フィールド（インラインオブジェクト）を解析
      if (submitData.correctionPolicy === '') {
        submitData.correctionPolicy = undefined;
      } else if (typeof submitData.correctionPolicy === 'string') {
        try {
          submitData.correctionPolicy = JSON.parse(submitData.correctionPolicy);
        } catch {
          setErrors({ correctionPolicy: 'Invalid JSON' });
          setLoading(false);
          return;
        }
      }


      // Enum フィールドの空文字列を undefined に変換（.default() を有効にする）
      if (submitData.language === '') {
        submitData.language = undefined;
      }
      if (submitData.schemaVersion === '') {
        submitData.schemaVersion = undefined;
      }

      // Number フィールドを変換（空文字列 → undefined、文字列 → 数値）
      if (submitData.schemaVersion === '' || submitData.schemaVersion === null) {
        submitData.schemaVersion = undefined;
      } else if (typeof submitData.schemaVersion === 'string') {
        submitData.schemaVersion = Number(submitData.schemaVersion);
      }

      
      // Validate input (excluding SwallowKit-managed fields)
      TutorProfileInputSchema.parse(submitData);

      const url = isEdit ? `/api/tutorProfile/${initialData!.id}` : '/api/tutorProfile';
      const method = isEdit ? 'PUT' : 'POST';

      const res = await fetch(url, {
        method,
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(submitData),
      });

      if (!res.ok) {
        const errorData = await res.json();
        throw new Error(errorData.error || 'Failed to save tutorProfile');
      }

      router('/tutor-profile');
    } catch (err) {
      if (err instanceof z.ZodError) {
        // Zod validation errors
        const fieldErrors: Record<string, string> = {};
        err.issues.forEach((issue) => {
          const field = String(issue.path[0]);
          fieldErrors[field] = issue.message;
        });
        setErrors(fieldErrors);
      } else {
        alert(`Error: ${err instanceof Error ? err.message : String(err)}`);
      }
      setLoading(false);
    }
  };

  return (
    <form onSubmit={handleSubmit} className="space-y-6">
      <div>
        <label htmlFor="language" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
          language *
        </label>
        <select
          id="language"
          name="language"
          value={formData.language}
          onChange={(e) => setFormData({ ...formData, language: e.target.value })}
          className="mt-1 block w-full rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 shadow-sm px-3 py-2 focus:border-blue-500 focus:ring-blue-500 dark:focus:border-blue-400 dark:focus:ring-blue-400"
          required
        >
          <option value="">選択してください</option>
          <option value="it">it</option>
          <option value="en">en</option>
        </select>
        {errors.language && (
          <p className="mt-1 text-sm text-red-600 dark:text-red-400">{errors.language}</p>
        )}
      </div>

      <div>
        <label htmlFor="schemaVersion" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
          schemaVersion *
        </label>
        <select
          id="schemaVersion"
          name="schemaVersion"
          value={formData.schemaVersion}
          onChange={(e) => setFormData({ ...formData, schemaVersion: e.target.value })}
          className="mt-1 block w-full rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 shadow-sm px-3 py-2 focus:border-blue-500 focus:ring-blue-500 dark:focus:border-blue-400 dark:focus:ring-blue-400"
          required
        >
          <option value="">選択してください</option>
          <option value="1">1</option>
        </select>
        {errors.schemaVersion && (
          <p className="mt-1 text-sm text-red-600 dark:text-red-400">{errors.schemaVersion}</p>
        )}
      </div>

      <div>
        <label htmlFor="name" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
          name *
        </label>
        <input
          type="text"
          id="name"
          name="name"
          value={formData.name}
          onChange={(e) => setFormData({ ...formData, name: e.target.value })}
          className="mt-1 block w-full rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 shadow-sm px-3 py-2 focus:border-blue-500 focus:ring-blue-500 dark:focus:border-blue-400 dark:focus:ring-blue-400"
          required
        />
        {errors.name && (
          <p className="mt-1 text-sm text-red-600 dark:text-red-400">{errors.name}</p>
        )}
      </div>

      <div>
        <label htmlFor="avatarEmoji" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
          avatarEmoji *
        </label>
        <input
          type="text"
          id="avatarEmoji"
          name="avatarEmoji"
          value={formData.avatarEmoji}
          onChange={(e) => setFormData({ ...formData, avatarEmoji: e.target.value })}
          className="mt-1 block w-full rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 shadow-sm px-3 py-2 focus:border-blue-500 focus:ring-blue-500 dark:focus:border-blue-400 dark:focus:ring-blue-400"
          required
        />
        {errors.avatarEmoji && (
          <p className="mt-1 text-sm text-red-600 dark:text-red-400">{errors.avatarEmoji}</p>
        )}
      </div>

      <div>
        <label htmlFor="personaJa" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
          personaJa *
        </label>
        <input
          type="text"
          id="personaJa"
          name="personaJa"
          value={formData.personaJa}
          onChange={(e) => setFormData({ ...formData, personaJa: e.target.value })}
          className="mt-1 block w-full rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 shadow-sm px-3 py-2 focus:border-blue-500 focus:ring-blue-500 dark:focus:border-blue-400 dark:focus:ring-blue-400"
          required
        />
        {errors.personaJa && (
          <p className="mt-1 text-sm text-red-600 dark:text-red-400">{errors.personaJa}</p>
        )}
      </div>

      <div>
        <label htmlFor="speakingStyle" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
          speakingStyle *
        </label>
        <input
          type="text"
          id="speakingStyle"
          name="speakingStyle"
          value={formData.speakingStyle}
          onChange={(e) => setFormData({ ...formData, speakingStyle: e.target.value })}
          className="mt-1 block w-full rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 shadow-sm px-3 py-2 focus:border-blue-500 focus:ring-blue-500 dark:focus:border-blue-400 dark:focus:ring-blue-400"
          required
        />
        {errors.speakingStyle && (
          <p className="mt-1 text-sm text-red-600 dark:text-red-400">{errors.speakingStyle}</p>
        )}
      </div>

      <div>
        <label htmlFor="greeting" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
          greeting *
        </label>
        <input
          type="text"
          id="greeting"
          name="greeting"
          value={formData.greeting}
          onChange={(e) => setFormData({ ...formData, greeting: e.target.value })}
          className="mt-1 block w-full rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 shadow-sm px-3 py-2 focus:border-blue-500 focus:ring-blue-500 dark:focus:border-blue-400 dark:focus:ring-blue-400"
          required
        />
        {errors.greeting && (
          <p className="mt-1 text-sm text-red-600 dark:text-red-400">{errors.greeting}</p>
        )}
      </div>

      <div>
        <label htmlFor="correctionPolicy" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
          correctionPolicy * <span className="text-xs text-gray-500">(JSON)</span>
        </label>
        <textarea
          id="correctionPolicy"
          name="correctionPolicy"
          rows={4}
          value={formData.correctionPolicy}
          onChange={(e) => setFormData({ ...formData, correctionPolicy: e.target.value })}
          className="mt-1 block w-full rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 shadow-sm px-3 py-2 font-mono focus:border-blue-500 focus:ring-blue-500 dark:focus:border-blue-400 dark:focus:ring-blue-400"
          required
        />
        {errors.correctionPolicy && (
          <p className="mt-1 text-sm text-red-600 dark:text-red-400">{errors.correctionPolicy}</p>
        )}
      </div>

      <div>
        <label htmlFor="promptTemplateId" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
          promptTemplateId *
        </label>
        <input
          type="text"
          id="promptTemplateId"
          name="promptTemplateId"
          value={formData.promptTemplateId}
          onChange={(e) => setFormData({ ...formData, promptTemplateId: e.target.value })}
          className="mt-1 block w-full rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 shadow-sm px-3 py-2 focus:border-blue-500 focus:ring-blue-500 dark:focus:border-blue-400 dark:focus:ring-blue-400"
          required
        />
        {errors.promptTemplateId && (
          <p className="mt-1 text-sm text-red-600 dark:text-red-400">{errors.promptTemplateId}</p>
        )}
      </div>

      <div className="flex gap-4">
        <button
          type="submit"
          disabled={loading}
          className="bg-blue-600 hover:bg-blue-700 text-white px-4 py-2 rounded disabled:opacity-50"
        >
          {loading ? 'Saving...' : isEdit ? 'Update' : 'Create'}
        </button>
        <button
          type="button"
          onClick={() => router('/tutor-profile')}
          className="bg-gray-300 hover:bg-gray-400 text-gray-800 px-4 py-2 rounded"
        >
          Cancel
        </button>
      </div>
    </form>
  );
}
