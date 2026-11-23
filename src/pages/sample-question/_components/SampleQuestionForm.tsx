import { useState, FormEvent, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { z } from 'zod/v4';
import { SampleQuestion as sampleQuestionSchema } from '@line-language-learn/shared';

// Input schema: SwallowKit-managed fields (id, createdAt, updatedAt) are optional
// These fields are ignored by the backend and auto-managed
const SampleQuestionInputSchema = sampleQuestionSchema.partial({ id: true, createdAt: true, updatedAt: true });

type SampleQuestion = z.infer<typeof sampleQuestionSchema>;

interface SampleQuestionFormProps {
  initialData?: SampleQuestion;
  isEdit?: boolean;
}

export default function SampleQuestionForm({ initialData, isEdit = false }: SampleQuestionFormProps) {
  const router = useNavigate();
  const [loading, setLoading] = useState(false);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [grammarTopicOptions, setGrammarTopicOptions] = useState<Array<{ id: string; name: string }>>([]);

  
  const [formData, setFormData] = useState({
    language: initialData?.language ?? '',
    schemaVersion: initialData?.schemaVersion !== undefined ? String(initialData.schemaVersion) : '',
    grammarTopicId: initialData?.grammarTopicId ?? '',
    type: initialData?.type ?? '',
    difficulty: initialData?.difficulty !== undefined ? String(initialData.difficulty) : '',
    prompt: initialData?.prompt ?? '',
    choices: initialData?.choices ? (Array.isArray(initialData.choices) ? initialData.choices.join(', ') : '') : '',
    correctAnswer: initialData?.correctAnswer ?? '',
    acceptedAnswers: initialData?.acceptedAnswers ? (Array.isArray(initialData.acceptedAnswers) ? initialData.acceptedAnswers.join(', ') : '') : '',
    explanationJa: initialData?.explanationJa ?? '',
    ruleIds: initialData?.ruleIds ? (Array.isArray(initialData.ruleIds) ? initialData.ruleIds.join(', ') : '') : '',
    publication: initialData?.publication !== undefined ? JSON.stringify(initialData.publication, null, 2) : '',
  });

  // 参照データの選択肢を取得
  useEffect(() => {
    // GrammarTopic の一覧を取得
    fetch('/api/grammar-topic')
      .then(res => res.json())
      .then(data => setGrammarTopicOptions(data.map((item: Record<string, string>) => ({ id: item.id, name: item.name || item.title || item.id }))))
      .catch(err => console.error('Failed to load GrammarTopic options:', err));

  }, []);

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setLoading(true);
    setErrors({});

    try {
      // Array フィールドをカンマ区切りから配列に変換
      const submitData: Record<string, unknown> = { ...formData };
      // JSON フィールド（インラインオブジェクト）を解析
      if (submitData.publication === '') {
        submitData.publication = undefined;
      } else if (typeof submitData.publication === 'string') {
        try {
          submitData.publication = JSON.parse(submitData.publication);
        } catch {
          setErrors({ publication: 'Invalid JSON' });
          setLoading(false);
          return;
        }
      }
      if (typeof submitData.choices === 'string') {
        submitData.choices = submitData.choices.split(',').map((s: string) => s.trim()).filter((s: string) => s.length > 0);
      }
      if (typeof submitData.acceptedAnswers === 'string') {
        submitData.acceptedAnswers = submitData.acceptedAnswers.split(',').map((s: string) => s.trim()).filter((s: string) => s.length > 0);
      }
      if (typeof submitData.ruleIds === 'string') {
        submitData.ruleIds = submitData.ruleIds.split(',').map((s: string) => s.trim()).filter((s: string) => s.length > 0);
      }

      // Enum フィールドの空文字列を undefined に変換（.default() を有効にする）
      if (submitData.language === '') {
        submitData.language = undefined;
      }
      if (submitData.schemaVersion === '') {
        submitData.schemaVersion = undefined;
      }
      if (submitData.type === '') {
        submitData.type = undefined;
      }

      // Number フィールドを変換（空文字列 → undefined、文字列 → 数値）
      if (submitData.schemaVersion === '' || submitData.schemaVersion === null) {
        submitData.schemaVersion = undefined;
      } else if (typeof submitData.schemaVersion === 'string') {
        submitData.schemaVersion = Number(submitData.schemaVersion);
      }
      if (submitData.difficulty === '' || submitData.difficulty === null) {
        submitData.difficulty = undefined;
      } else if (typeof submitData.difficulty === 'string') {
        submitData.difficulty = Number(submitData.difficulty);
      }

      
      // Validate input (excluding SwallowKit-managed fields)
      SampleQuestionInputSchema.parse(submitData);

      const url = isEdit ? `/api/sampleQuestion/${initialData!.id}` : '/api/sampleQuestion';
      const method = isEdit ? 'PUT' : 'POST';

      const res = await fetch(url, {
        method,
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(submitData),
      });

      if (!res.ok) {
        const errorData = await res.json();
        throw new Error(errorData.error || 'Failed to save sampleQuestion');
      }

      router('/sample-question');
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
        <label htmlFor="grammarTopicId" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
          GrammarTopic *
        </label>
        <select
          id="grammarTopicId"
          name="grammarTopicId"
          value={formData.grammarTopicId}
          onChange={(e) => setFormData({ ...formData, grammarTopicId: e.target.value })}
          className="mt-1 block w-full rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 shadow-sm px-3 py-2 focus:border-blue-500 focus:ring-blue-500 dark:focus:border-blue-400 dark:focus:ring-blue-400"
          required
        >
          <option value="">選択してください</option>
          {grammarTopicOptions.map((option) => (
            <option key={option.id} value={option.id}>{option.name}</option>
          ))}
        </select>
        {errors.grammarTopicId && (
          <p className="mt-1 text-sm text-red-600 dark:text-red-400">{errors.grammarTopicId}</p>
        )}
      </div>

      <div>
        <label htmlFor="type" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
          type *
        </label>
        <select
          id="type"
          name="type"
          value={formData.type}
          onChange={(e) => setFormData({ ...formData, type: e.target.value })}
          className="mt-1 block w-full rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 shadow-sm px-3 py-2 focus:border-blue-500 focus:ring-blue-500 dark:focus:border-blue-400 dark:focus:ring-blue-400"
          required
        >
          <option value="">選択してください</option>
          <option value="multiple-choice">multiple-choice</option>
          <option value="cloze">cloze</option>
        </select>
        {errors.type && (
          <p className="mt-1 text-sm text-red-600 dark:text-red-400">{errors.type}</p>
        )}
      </div>

      <div>
        <label htmlFor="difficulty" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
          difficulty *
        </label>
        <input
          type="number"
          id="difficulty"
          name="difficulty"
          value={formData.difficulty}
          onChange={(e) => setFormData({ ...formData, difficulty: e.target.value })}
          className="mt-1 block w-full rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 shadow-sm px-3 py-2 focus:border-blue-500 focus:ring-blue-500 dark:focus:border-blue-400 dark:focus:ring-blue-400"
          required
        />
        {errors.difficulty && (
          <p className="mt-1 text-sm text-red-600 dark:text-red-400">{errors.difficulty}</p>
        )}
      </div>

      <div>
        <label htmlFor="prompt" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
          prompt *
        </label>
        <input
          type="text"
          id="prompt"
          name="prompt"
          value={formData.prompt}
          onChange={(e) => setFormData({ ...formData, prompt: e.target.value })}
          className="mt-1 block w-full rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 shadow-sm px-3 py-2 focus:border-blue-500 focus:ring-blue-500 dark:focus:border-blue-400 dark:focus:ring-blue-400"
          required
        />
        {errors.prompt && (
          <p className="mt-1 text-sm text-red-600 dark:text-red-400">{errors.prompt}</p>
        )}
      </div>

      <div>
        <label htmlFor="choices" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
          choices * <span className="text-xs text-gray-500">(カンマ区切りで入力)</span>
        </label>
        <input
          type="text"
          id="choices"
          name="choices"
          value={formData.choices}
          onChange={(e) => setFormData({ ...formData, choices: e.target.value })}
          placeholder="例: item1, item2, item3"
          className="mt-1 block w-full rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 shadow-sm px-3 py-2 focus:border-blue-500 focus:ring-blue-500 dark:focus:border-blue-400 dark:focus:ring-blue-400"
          required
        />
        {errors.choices && (
          <p className="mt-1 text-sm text-red-600 dark:text-red-400">{errors.choices}</p>
        )}
      </div>

      <div>
        <label htmlFor="correctAnswer" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
          correctAnswer *
        </label>
        <input
          type="text"
          id="correctAnswer"
          name="correctAnswer"
          value={formData.correctAnswer}
          onChange={(e) => setFormData({ ...formData, correctAnswer: e.target.value })}
          className="mt-1 block w-full rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 shadow-sm px-3 py-2 focus:border-blue-500 focus:ring-blue-500 dark:focus:border-blue-400 dark:focus:ring-blue-400"
          required
        />
        {errors.correctAnswer && (
          <p className="mt-1 text-sm text-red-600 dark:text-red-400">{errors.correctAnswer}</p>
        )}
      </div>

      <div>
        <label htmlFor="acceptedAnswers" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
          acceptedAnswers * <span className="text-xs text-gray-500">(カンマ区切りで入力)</span>
        </label>
        <input
          type="text"
          id="acceptedAnswers"
          name="acceptedAnswers"
          value={formData.acceptedAnswers}
          onChange={(e) => setFormData({ ...formData, acceptedAnswers: e.target.value })}
          placeholder="例: item1, item2, item3"
          className="mt-1 block w-full rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 shadow-sm px-3 py-2 focus:border-blue-500 focus:ring-blue-500 dark:focus:border-blue-400 dark:focus:ring-blue-400"
          required
        />
        {errors.acceptedAnswers && (
          <p className="mt-1 text-sm text-red-600 dark:text-red-400">{errors.acceptedAnswers}</p>
        )}
      </div>

      <div>
        <label htmlFor="explanationJa" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
          explanationJa *
        </label>
        <input
          type="text"
          id="explanationJa"
          name="explanationJa"
          value={formData.explanationJa}
          onChange={(e) => setFormData({ ...formData, explanationJa: e.target.value })}
          className="mt-1 block w-full rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 shadow-sm px-3 py-2 focus:border-blue-500 focus:ring-blue-500 dark:focus:border-blue-400 dark:focus:ring-blue-400"
          required
        />
        {errors.explanationJa && (
          <p className="mt-1 text-sm text-red-600 dark:text-red-400">{errors.explanationJa}</p>
        )}
      </div>

      <div>
        <label htmlFor="ruleIds" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
          ruleIds * <span className="text-xs text-gray-500">(カンマ区切りで入力)</span>
        </label>
        <input
          type="text"
          id="ruleIds"
          name="ruleIds"
          value={formData.ruleIds}
          onChange={(e) => setFormData({ ...formData, ruleIds: e.target.value })}
          placeholder="例: item1, item2, item3"
          className="mt-1 block w-full rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 shadow-sm px-3 py-2 focus:border-blue-500 focus:ring-blue-500 dark:focus:border-blue-400 dark:focus:ring-blue-400"
          required
        />
        {errors.ruleIds && (
          <p className="mt-1 text-sm text-red-600 dark:text-red-400">{errors.ruleIds}</p>
        )}
      </div>

      <div>
        <label htmlFor="publication" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
          publication <span className="text-xs text-gray-500">(JSON)</span>
        </label>
        <textarea
          id="publication"
          name="publication"
          rows={4}
          value={formData.publication}
          onChange={(e) => setFormData({ ...formData, publication: e.target.value })}
          className="mt-1 block w-full rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 shadow-sm px-3 py-2 font-mono focus:border-blue-500 focus:ring-blue-500 dark:focus:border-blue-400 dark:focus:ring-blue-400"
          
        />
        {errors.publication && (
          <p className="mt-1 text-sm text-red-600 dark:text-red-400">{errors.publication}</p>
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
          onClick={() => router('/sample-question')}
          className="bg-gray-300 hover:bg-gray-400 text-gray-800 px-4 py-2 rounded"
        >
          Cancel
        </button>
      </div>
    </form>
  );
}
