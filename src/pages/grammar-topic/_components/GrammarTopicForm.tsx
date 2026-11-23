import { useState, FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { z } from 'zod/v4';
import { GrammarTopic as grammarTopicSchema } from '@line-language-learn/shared';

// Input schema: SwallowKit-managed fields (id, createdAt, updatedAt) are optional
// These fields are ignored by the backend and auto-managed
const GrammarTopicInputSchema = grammarTopicSchema.partial({ id: true, createdAt: true, updatedAt: true });

type GrammarTopic = z.infer<typeof grammarTopicSchema>;

interface GrammarTopicFormProps {
  initialData?: GrammarTopic;
  isEdit?: boolean;
}

export default function GrammarTopicForm({ initialData, isEdit = false }: GrammarTopicFormProps) {
  const router = useNavigate();
  const [loading, setLoading] = useState(false);
  const [errors, setErrors] = useState<Record<string, string>>({});


  
  const [formData, setFormData] = useState({
    language: initialData?.language ?? '',
    schemaVersion: initialData?.schemaVersion !== undefined ? String(initialData.schemaVersion) : '',
    title: initialData?.title ?? '',
    titleJa: initialData?.titleJa ?? '',
    level: initialData?.level ?? '',
    category: initialData?.category ?? '',
    description: initialData?.description ?? '',
    usageJa: initialData?.usageJa ?? '',
    rules: initialData?.rules !== undefined ? JSON.stringify(initialData.rules, null, 2) : '',
    examples: initialData?.examples !== undefined ? JSON.stringify(initialData.examples, null, 2) : '',
    commonMistakes: initialData?.commonMistakes !== undefined ? JSON.stringify(initialData.commonMistakes, null, 2) : '',
    relatedTopics: initialData?.relatedTopics ? (Array.isArray(initialData.relatedTopics) ? initialData.relatedTopics.join(', ') : '') : '',
    keywords: initialData?.keywords ? (Array.isArray(initialData.keywords) ? initialData.keywords.join(', ') : '') : '',
    sourceReferences: initialData?.sourceReferences !== undefined ? JSON.stringify(initialData.sourceReferences, null, 2) : '',
    reviewStatus: initialData?.reviewStatus ?? '',
    reviewNoteJa: initialData?.reviewNoteJa ?? '',
  });

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setLoading(true);
    setErrors({});

    try {
      // Array フィールドをカンマ区切りから配列に変換
      const submitData: Record<string, unknown> = { ...formData };
      // JSON フィールド（インラインオブジェクト）を解析
      if (submitData.rules === '') {
        submitData.rules = undefined;
      } else if (typeof submitData.rules === 'string') {
        try {
          submitData.rules = JSON.parse(submitData.rules);
        } catch {
          setErrors({ rules: 'Invalid JSON' });
          setLoading(false);
          return;
        }
      }
      if (submitData.examples === '') {
        submitData.examples = undefined;
      } else if (typeof submitData.examples === 'string') {
        try {
          submitData.examples = JSON.parse(submitData.examples);
        } catch {
          setErrors({ examples: 'Invalid JSON' });
          setLoading(false);
          return;
        }
      }
      if (submitData.commonMistakes === '') {
        submitData.commonMistakes = undefined;
      } else if (typeof submitData.commonMistakes === 'string') {
        try {
          submitData.commonMistakes = JSON.parse(submitData.commonMistakes);
        } catch {
          setErrors({ commonMistakes: 'Invalid JSON' });
          setLoading(false);
          return;
        }
      }
      if (submitData.sourceReferences === '') {
        submitData.sourceReferences = undefined;
      } else if (typeof submitData.sourceReferences === 'string') {
        try {
          submitData.sourceReferences = JSON.parse(submitData.sourceReferences);
        } catch {
          setErrors({ sourceReferences: 'Invalid JSON' });
          setLoading(false);
          return;
        }
      }
      if (typeof submitData.relatedTopics === 'string') {
        submitData.relatedTopics = submitData.relatedTopics.split(',').map((s: string) => s.trim()).filter((s: string) => s.length > 0);
      }
      if (typeof submitData.keywords === 'string') {
        submitData.keywords = submitData.keywords.split(',').map((s: string) => s.trim()).filter((s: string) => s.length > 0);
      }

      // Enum フィールドの空文字列を undefined に変換（.default() を有効にする）
      if (submitData.language === '') {
        submitData.language = undefined;
      }
      if (submitData.schemaVersion === '') {
        submitData.schemaVersion = undefined;
      }
      if (submitData.level === '') {
        submitData.level = undefined;
      }
      if (submitData.category === '') {
        submitData.category = undefined;
      }
      if (submitData.reviewStatus === '') {
        submitData.reviewStatus = undefined;
      }

      // Number フィールドを変換（空文字列 → undefined、文字列 → 数値）
      if (submitData.schemaVersion === '' || submitData.schemaVersion === null) {
        submitData.schemaVersion = undefined;
      } else if (typeof submitData.schemaVersion === 'string') {
        submitData.schemaVersion = Number(submitData.schemaVersion);
      }

      
      // Validate input (excluding SwallowKit-managed fields)
      GrammarTopicInputSchema.parse(submitData);

      const url = isEdit ? `/api/grammarTopic/${initialData!.id}` : '/api/grammarTopic';
      const method = isEdit ? 'PUT' : 'POST';

      const res = await fetch(url, {
        method,
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(submitData),
      });

      if (!res.ok) {
        const errorData = await res.json();
        throw new Error(errorData.error || 'Failed to save grammarTopic');
      }

      router('/grammar-topic');
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
        <label htmlFor="title" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
          title *
        </label>
        <input
          type="text"
          id="title"
          name="title"
          value={formData.title}
          onChange={(e) => setFormData({ ...formData, title: e.target.value })}
          className="mt-1 block w-full rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 shadow-sm px-3 py-2 focus:border-blue-500 focus:ring-blue-500 dark:focus:border-blue-400 dark:focus:ring-blue-400"
          required
        />
        {errors.title && (
          <p className="mt-1 text-sm text-red-600 dark:text-red-400">{errors.title}</p>
        )}
      </div>

      <div>
        <label htmlFor="titleJa" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
          titleJa *
        </label>
        <input
          type="text"
          id="titleJa"
          name="titleJa"
          value={formData.titleJa}
          onChange={(e) => setFormData({ ...formData, titleJa: e.target.value })}
          className="mt-1 block w-full rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 shadow-sm px-3 py-2 focus:border-blue-500 focus:ring-blue-500 dark:focus:border-blue-400 dark:focus:ring-blue-400"
          required
        />
        {errors.titleJa && (
          <p className="mt-1 text-sm text-red-600 dark:text-red-400">{errors.titleJa}</p>
        )}
      </div>

      <div>
        <label htmlFor="level" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
          level *
        </label>
        <select
          id="level"
          name="level"
          value={formData.level}
          onChange={(e) => setFormData({ ...formData, level: e.target.value })}
          className="mt-1 block w-full rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 shadow-sm px-3 py-2 focus:border-blue-500 focus:ring-blue-500 dark:focus:border-blue-400 dark:focus:ring-blue-400"
          required
        >
          <option value="">選択してください</option>
          <option value="A1">A1</option>
          <option value="A2">A2</option>
          <option value="B1">B1</option>
          <option value="B2">B2</option>
          <option value="C1">C1</option>
          <option value="C2">C2</option>
        </select>
        {errors.level && (
          <p className="mt-1 text-sm text-red-600 dark:text-red-400">{errors.level}</p>
        )}
      </div>

      <div>
        <label htmlFor="category" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
          category *
        </label>
        <select
          id="category"
          name="category"
          value={formData.category}
          onChange={(e) => setFormData({ ...formData, category: e.target.value })}
          className="mt-1 block w-full rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 shadow-sm px-3 py-2 focus:border-blue-500 focus:ring-blue-500 dark:focus:border-blue-400 dark:focus:ring-blue-400"
          required
        >
          <option value="">選択してください</option>
          <option value="article">article</option>
          <option value="preposition">preposition</option>
          <option value="verb-pattern">verb-pattern</option>
          <option value="tense">tense</option>
          <option value="pronoun">pronoun</option>
          <option value="construction">construction</option>
        </select>
        {errors.category && (
          <p className="mt-1 text-sm text-red-600 dark:text-red-400">{errors.category}</p>
        )}
      </div>

      <div>
        <label htmlFor="description" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
          description *
        </label>
        <input
          type="text"
          id="description"
          name="description"
          value={formData.description}
          onChange={(e) => setFormData({ ...formData, description: e.target.value })}
          className="mt-1 block w-full rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 shadow-sm px-3 py-2 focus:border-blue-500 focus:ring-blue-500 dark:focus:border-blue-400 dark:focus:ring-blue-400"
          required
        />
        {errors.description && (
          <p className="mt-1 text-sm text-red-600 dark:text-red-400">{errors.description}</p>
        )}
      </div>

      <div>
        <label htmlFor="usageJa" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
          usageJa *
        </label>
        <input
          type="text"
          id="usageJa"
          name="usageJa"
          value={formData.usageJa}
          onChange={(e) => setFormData({ ...formData, usageJa: e.target.value })}
          className="mt-1 block w-full rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 shadow-sm px-3 py-2 focus:border-blue-500 focus:ring-blue-500 dark:focus:border-blue-400 dark:focus:ring-blue-400"
          required
        />
        {errors.usageJa && (
          <p className="mt-1 text-sm text-red-600 dark:text-red-400">{errors.usageJa}</p>
        )}
      </div>

      <div>
        <label htmlFor="rules" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
          rules * <span className="text-xs text-gray-500">(JSON)</span>
        </label>
        <textarea
          id="rules"
          name="rules"
          rows={4}
          value={formData.rules}
          onChange={(e) => setFormData({ ...formData, rules: e.target.value })}
          className="mt-1 block w-full rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 shadow-sm px-3 py-2 font-mono focus:border-blue-500 focus:ring-blue-500 dark:focus:border-blue-400 dark:focus:ring-blue-400"
          required
        />
        {errors.rules && (
          <p className="mt-1 text-sm text-red-600 dark:text-red-400">{errors.rules}</p>
        )}
      </div>

      <div>
        <label htmlFor="examples" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
          examples * <span className="text-xs text-gray-500">(JSON)</span>
        </label>
        <textarea
          id="examples"
          name="examples"
          rows={4}
          value={formData.examples}
          onChange={(e) => setFormData({ ...formData, examples: e.target.value })}
          className="mt-1 block w-full rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 shadow-sm px-3 py-2 font-mono focus:border-blue-500 focus:ring-blue-500 dark:focus:border-blue-400 dark:focus:ring-blue-400"
          required
        />
        {errors.examples && (
          <p className="mt-1 text-sm text-red-600 dark:text-red-400">{errors.examples}</p>
        )}
      </div>

      <div>
        <label htmlFor="commonMistakes" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
          commonMistakes * <span className="text-xs text-gray-500">(JSON)</span>
        </label>
        <textarea
          id="commonMistakes"
          name="commonMistakes"
          rows={4}
          value={formData.commonMistakes}
          onChange={(e) => setFormData({ ...formData, commonMistakes: e.target.value })}
          className="mt-1 block w-full rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 shadow-sm px-3 py-2 font-mono focus:border-blue-500 focus:ring-blue-500 dark:focus:border-blue-400 dark:focus:ring-blue-400"
          required
        />
        {errors.commonMistakes && (
          <p className="mt-1 text-sm text-red-600 dark:text-red-400">{errors.commonMistakes}</p>
        )}
      </div>

      <div>
        <label htmlFor="relatedTopics" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
          relatedTopics * <span className="text-xs text-gray-500">(カンマ区切りで入力)</span>
        </label>
        <input
          type="text"
          id="relatedTopics"
          name="relatedTopics"
          value={formData.relatedTopics}
          onChange={(e) => setFormData({ ...formData, relatedTopics: e.target.value })}
          placeholder="例: item1, item2, item3"
          className="mt-1 block w-full rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 shadow-sm px-3 py-2 focus:border-blue-500 focus:ring-blue-500 dark:focus:border-blue-400 dark:focus:ring-blue-400"
          required
        />
        {errors.relatedTopics && (
          <p className="mt-1 text-sm text-red-600 dark:text-red-400">{errors.relatedTopics}</p>
        )}
      </div>

      <div>
        <label htmlFor="keywords" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
          keywords * <span className="text-xs text-gray-500">(カンマ区切りで入力)</span>
        </label>
        <input
          type="text"
          id="keywords"
          name="keywords"
          value={formData.keywords}
          onChange={(e) => setFormData({ ...formData, keywords: e.target.value })}
          placeholder="例: item1, item2, item3"
          className="mt-1 block w-full rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 shadow-sm px-3 py-2 focus:border-blue-500 focus:ring-blue-500 dark:focus:border-blue-400 dark:focus:ring-blue-400"
          required
        />
        {errors.keywords && (
          <p className="mt-1 text-sm text-red-600 dark:text-red-400">{errors.keywords}</p>
        )}
      </div>

      <div>
        <label htmlFor="sourceReferences" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
          sourceReferences * <span className="text-xs text-gray-500">(JSON)</span>
        </label>
        <textarea
          id="sourceReferences"
          name="sourceReferences"
          rows={4}
          value={formData.sourceReferences}
          onChange={(e) => setFormData({ ...formData, sourceReferences: e.target.value })}
          className="mt-1 block w-full rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 shadow-sm px-3 py-2 font-mono focus:border-blue-500 focus:ring-blue-500 dark:focus:border-blue-400 dark:focus:ring-blue-400"
          required
        />
        {errors.sourceReferences && (
          <p className="mt-1 text-sm text-red-600 dark:text-red-400">{errors.sourceReferences}</p>
        )}
      </div>

      <div>
        <label htmlFor="reviewStatus" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
          reviewStatus *
        </label>
        <select
          id="reviewStatus"
          name="reviewStatus"
          value={formData.reviewStatus}
          onChange={(e) => setFormData({ ...formData, reviewStatus: e.target.value })}
          className="mt-1 block w-full rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 shadow-sm px-3 py-2 focus:border-blue-500 focus:ring-blue-500 dark:focus:border-blue-400 dark:focus:ring-blue-400"
          required
        >
          <option value="">選択してください</option>
          <option value="unreviewed">unreviewed</option>
          <option value="reviewed">reviewed</option>
        </select>
        {errors.reviewStatus && (
          <p className="mt-1 text-sm text-red-600 dark:text-red-400">{errors.reviewStatus}</p>
        )}
      </div>

      <div>
        <label htmlFor="reviewNoteJa" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
          reviewNoteJa *
        </label>
        <input
          type="text"
          id="reviewNoteJa"
          name="reviewNoteJa"
          value={formData.reviewNoteJa}
          onChange={(e) => setFormData({ ...formData, reviewNoteJa: e.target.value })}
          className="mt-1 block w-full rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 shadow-sm px-3 py-2 focus:border-blue-500 focus:ring-blue-500 dark:focus:border-blue-400 dark:focus:ring-blue-400"
          required
        />
        {errors.reviewNoteJa && (
          <p className="mt-1 text-sm text-red-600 dark:text-red-400">{errors.reviewNoteJa}</p>
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
          onClick={() => router('/grammar-topic')}
          className="bg-gray-300 hover:bg-gray-400 text-gray-800 px-4 py-2 rounded"
        >
          Cancel
        </button>
      </div>
    </form>
  );
}
