import { useEffect, useState, type FormEvent } from 'react';
import {
  AdminQuestionGenerationResponse, AdminQuestionPublishResponse, GrammarListView, QuestionValidation,
  type AdminQuestionGenerationRequest, type GrammarTopic,
} from '@line-language-learn/shared';

const buttonClass = 'rounded-lg bg-blue-600 px-4 py-2 font-medium text-white transition-colors hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-50 dark:bg-blue-500 dark:hover:bg-blue-600';
const fieldClass = 'rounded-lg border border-gray-300 bg-white p-2 text-gray-900 focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500 dark:border-gray-600 dark:bg-gray-700 dark:text-gray-100';

export default function AdminPage({ login }: { login: boolean }) {
  return (
    <main className="container mx-auto space-y-6 px-4 py-8 text-gray-900 dark:text-gray-100">
      <h1 className="text-3xl font-bold">{login ? '管理者ログイン' : 'AI で問題を生成'}</h1>
      {login ? (
        <section className="space-y-4 rounded-xl border border-gray-200 bg-white p-6 dark:border-gray-700 dark:bg-gray-800">
          <p>管理者として招待された GitHub アカウントでログインしてください。</p>
          <a className={buttonClass + ' inline-block'} href="/.auth/login/github?post_login_redirect_uri=/admin">
            GitHub でログイン
          </a>
        </section>
      ) : <QuestionGenerator />}
    </main>
  );
}

type ReviewQuestion = AdminQuestionGenerationResponse['questions'][number] & {
  validation: QuestionValidation | null;
  humanApproved: boolean;
  saved: boolean;
  publishError: string;
};

type ReviewBatch = { topicId: string; questions: ReviewQuestion[] };

function QuestionGenerator() {
  const [topics, setTopics] = useState<GrammarTopic[]>([]);
  const [loadingTopics, setLoadingTopics] = useState(true);
  const [topicError, setTopicError] = useState('');
  const [topicId, setTopicId] = useState('');
  const [questionCount, setQuestionCount] = useState(5);
  const [result, setResult] = useState<ReviewBatch | null>(null);
  const [pending, setPending] = useState<'generate' | { action: 'validate' | 'publish'; index: number } | null>(null);
  const [validationError, setValidationError] = useState<number | null>(null);
  const [error, setError] = useState('');

  useEffect(() => {
    const controller = new AbortController();
    fetch('/api/admin/grammar', { signal: controller.signal })
      .then(readJson)
      .then(data => setTopics(GrammarListView.parse(data).topics))
      .catch(() => {
        if (!controller.signal.aborted) setTopicError('文法項目を取得できませんでした。ページを再読み込みしてください。');
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoadingTopics(false);
      });
    return () => controller.abort();
  }, []);

  async function generate(input: AdminQuestionGenerationRequest) {
    if (pending !== null) return;
    setPending('generate');
    setError('');
    try {
      const response = await postJson('/api/admin/questions/generate', input);
      if (response.status === 503) {
        setError('AI サービスまたは文法データを利用できないか、問題候補を取得できませんでした。時間をおいて再試行してください。');
        return;
      }
      const batch = AdminQuestionGenerationResponse.parse(await readJson(response));
      setResult({
        topicId: batch.topicId,
        questions: batch.questions.map(question => ({ ...question, validation: null, humanApproved: false, saved: false, publishError: '' })),
      });
      setValidationError(null);
    } catch {
      setError('問題を生成できませんでした。ログイン状態や接続を確認して再試行してください。');
    } finally {
      setPending(null);
    }
  }

  function updateQuestion(index: number, change: Partial<Pick<ReviewQuestion, 'validation' | 'humanApproved' | 'saved' | 'publishError'>>) {
    setResult(batch => batch && {
      ...batch,
      questions: batch.questions.map((item, position) => position === index ? { ...item, ...change } : item),
    });
  }

  async function validate(index: number) {
    if (pending !== null || !result) return;
    const { question, generationId } = result.questions[index];
    setPending({ action: 'validate', index });
    setValidationError(null);
    try {
      const response = await postJson('/api/admin/questions/validate', { topicId: result.topicId, question, generationId });
      updateQuestion(index, { validation: QuestionValidation.parse(await readJson(response)) });
    } catch {
      setValidationError(index);
    } finally {
      setPending(null);
    }
  }

  async function publish(index: number) {
    if (pending !== null || !result) return;
    const { question, provider, saved, humanApproved, validation } = result.questions[index];
    if (saved || (!humanApproved && validation?.status !== 'passed')) return;
    setPending({ action: 'publish', index });
    updateQuestion(index, { publishError: '' });
    try {
      const response = await postJson('/api/admin/questions/publish', { topicId: result.topicId, question, provider });
      if (response.status === 400) {
        updateQuestion(index, { publishError: '出題に必要なチェックを通過しなかったため、保存できませんでした。' });
        return;
      }
      AdminQuestionPublishResponse.parse(await readJson(response));
      updateQuestion(index, { saved: true });
    } catch {
      updateQuestion(index, { publishError: '保存できませんでした。時間をおいて再試行してください。' });
    } finally {
      setPending(null);
    }
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    void generate({ topicId, questionCount });
  }

  const savedCount = result?.questions.filter(item => item.saved).length ?? 0;

  return (
    <>
      <form className="space-y-4 rounded-xl border border-gray-200 bg-white p-6 shadow-sm dark:border-gray-700 dark:bg-gray-800" onSubmit={submit}>
        <h2 className="text-xl font-semibold">生成条件</h2>
        <p className="text-sm text-gray-600 dark:text-gray-300">文法項目と問題数を選んで候補を生成し、内容を確認して保存します。</p>
        <fieldset className="flex flex-wrap items-end gap-4" disabled={pending !== null || loadingTopics || !!topicError}>
          <label className="grid gap-1">
            文法項目
            <select className={fieldClass} value={topicId} onChange={event => setTopicId(event.target.value)} required>
              <option value="">選択してください</option>
              {topics.map(topic => <option key={topic.id} value={topic.id}>{topic.level} · {topic.titleJa}</option>)}
            </select>
          </label>
          <label className="grid gap-1">
            問題数
            <input className={fieldClass + ' w-24'} type="number" min={1} max={10} step={1} value={Number.isNaN(questionCount) ? '' : questionCount}
              onChange={event => setQuestionCount(event.target.valueAsNumber)} required />
          </label>
          <button className={buttonClass} type="submit" disabled={!topicId}>{error ? '再試行' : '生成する'}</button>
        </fieldset>
      </form>
      {loadingTopics && <p role="status">文法項目を読み込んでいます…</p>}
      {!loadingTopics && !topicError && topics.length === 0 && <p>選択できる文法項目がありません。</p>}
      {pending === 'generate' && <p role="status">問題を生成しています…</p>}
      {(topicError || error) && <p role="alert" className="rounded-lg border border-red-200 bg-red-50 p-4 text-red-800 dark:border-red-800 dark:bg-red-900/20 dark:text-red-200">{topicError || error}</p>}
      {result && (
        <section className="space-y-4" aria-label="生成結果">
          <h2 className="text-xl font-semibold">生成結果</h2>
          <p role="status">{result.questions.length} 問を生成し、{savedCount} 問を保存しました。</p>
          <p>AI 検証に合格するか、人が確認して承認すると、ミニアプリで使う問題として保存できます。</p>
          <ol className="space-y-4">
            {result.questions.map(({ question, validation, generationId, humanApproved, saved, publishError }, index) => (
              <li className="space-y-4 rounded-xl border border-gray-200 bg-white p-6 shadow-sm dark:border-gray-700 dark:bg-gray-800" key={generationId + '-' + index}>
                <h3 className="font-semibold">{index + 1}. {question.prompt}</h3>
                <p>{question.instructionJa}</p>
                {question.choices.length > 0 && <p>選択肢　{question.choices.join(' / ')}</p>}
                <p>正答　{question.correctAnswer}</p>
                <p>許容解答　{question.acceptedAnswers.join(' / ')}</p>
                <p>解説　{question.explanationJa}</p>
                <div className="flex flex-wrap items-center gap-4">
                  <button className={buttonClass} disabled={saved || pending !== null} onClick={() => void validate(index)}>
                    {validation ? 'AI 品質検証をやり直す' : 'AI で品質検証'}
                  </button>
                  <label className="flex items-center gap-2">
                    <input type="checkbox" checked={humanApproved} disabled={saved || pending !== null}
                      onChange={event => updateQuestion(index, { humanApproved: event.target.checked })} />
                    人が確認して承認
                  </label>
                  <button className={buttonClass}
                    disabled={saved || pending !== null || (!humanApproved && validation?.status !== 'passed')}
                    onClick={() => void publish(index)}>
                    {saved ? '保存済み' : '保存する'}
                  </button>
                </div>
                {typeof pending === 'object' && pending?.index === index && (
                  <p role="status">{pending.action === 'validate' ? '品質を検証しています…' : '問題を保存しています…'}</p>
                )}
                {publishError && <p role="alert">{publishError}</p>}
                {validationError === index && <p role="alert">AI 品質検証に失敗しました。時間をおいて再試行するか、人が確認して承認できます。</p>}
                {!validation && <p>AI 品質検証は未実施です。</p>}
                {validation && <details open={validation.status === 'failed'}>
                  <summary className="cursor-pointer">AI 品質検証 · {validation.status === 'passed' ? '合格' : '不合格'}</summary>
                  <ul className="ml-5 list-disc">
                    {validation.checks.map(check => (
                      <li key={check.id}>{check.passed ? 'OK' : check.severity === 'warning' ? '警告' : 'NG'} · {check.messageJa}</li>
                    ))}
                  </ul>
                  {validation.semantic && <p>意味的評価　{validation.semantic.rationaleJa}</p>}
                </details>}
              </li>
            ))}
          </ol>
        </section>
      )}
    </>
  );
}

function postJson(path: string, body: unknown) {
  return fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
}

async function readJson(response: Response): Promise<unknown> {
  if (!response.ok) throw new Error('Admin API request failed');
  return response.json();
}
