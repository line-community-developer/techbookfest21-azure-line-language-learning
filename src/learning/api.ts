import type {
  AnswerResult,
  CefrLevel,
  DashboardView,
  GrammarListView,
  GrammarSearchView,
  GrammarTopic,
  HistoryView,
  PracticeSessionView,
  StartPracticeRequest,
  SubmitAnswerRequest,
  SystemStatus,
  TutorSessionView,
  WorkflowRun,
} from '@line-language-learn/shared';
import { authenticatedFetch } from '@/lib/auth/schemes/line/authenticated-fetch';
import { liffEnabled } from '@/lib/auth/schemes/line/token-adapter';

// Typed client for the same-origin learning API (SWA → BFF → Functions).
// Identity is resolved server-side from the verified LINE ID token; never send a user id from the browser.

const BASE = '/api/learning';

export class ApiRequestError extends Error {
  readonly status: number;
  readonly code: string | undefined;

  constructor(message: string, status: number, code?: string) {
    super(message);
    this.name = 'ApiRequestError';
    this.status = status;
    this.code = code;
  }
}

interface RequestOptions {
  body?: unknown;
  query?: Record<string, string | number | undefined>;
  signal?: AbortSignal;
}

function statusMessage(status: number): string {
  if (status === 400) return 'リクエストの内容が正しくありません。';
  if (status === 401 || status === 403) return 'この操作を行う権限がありません。';
  if (status === 404) return 'データが見つかりませんでした。';
  if (status === 409) return '他の操作と競合しました。もう一度お試しください。';
  if (status === 429) return 'リクエストが多すぎます。少し待ってからお試しください。';
  if (status >= 500) return 'サーバーでエラーが発生しました。';
  return `予期しない応答です (HTTP ${status})。`;
}

function readErrorBody(data: unknown): { error?: string; code?: string } {
  if (typeof data !== 'object' || data === null) return {};
  const record = data as Record<string, unknown>;
  return {
    error: typeof record.error === 'string' ? record.error : undefined,
    code: typeof record.code === 'string' ? record.code : undefined,
  };
}

async function request<T>(method: 'GET' | 'POST', path: string, options: RequestOptions = {}): Promise<T> {
  let url = `${BASE}/${path}`;
  if (options.query) {
    const params = new URLSearchParams();
    for (const [key, value] of Object.entries(options.query)) {
      if (value !== undefined && value !== '') params.set(key, String(value));
    }
    const text = params.toString();
    if (text) url += `?${text}`;
  }

  const headers: Record<string, string> = { Accept: 'application/json' };
  const init: RequestInit = { method, signal: options.signal, headers };
  if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json';
    init.body = JSON.stringify(options.body);
  }

  let response: Response;
  try {
    // In LINE (LIFF), the ID token is sent as a Bearer token and verified by the backend.
    response = await (liffEnabled ? authenticatedFetch(url, init) : fetch(url, init));
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') throw error;
    throw new ApiRequestError('サーバーに接続できませんでした。ネットワークとローカルサーバーの起動状態を確認してください。', 0, 'network-error');
  }

  const text = await response.text();
  let data: unknown = undefined;
  if (text) {
    try {
      data = JSON.parse(text);
    } catch {
      data = undefined;
    }
  }

  if (!response.ok) {
    const body = readErrorBody(data);
    const message = body.error ? `${statusMessage(response.status)}（${body.error}）` : statusMessage(response.status);
    throw new ApiRequestError(message, response.status, body.code);
  }
  if (data === undefined) {
    throw new ApiRequestError('サーバーの応答を読み取れませんでした。', response.status, 'invalid-json');
  }
  return data as T;
}

const seg = (value: string) => encodeURIComponent(value);

export interface GrammarSearchParams {
  q: string;
  level?: CefrLevel;
  topicId?: string;
  limit?: number;
}

export const api = {
  status: (signal?: AbortSignal) => request<SystemStatus>('GET', 'status', { signal }),
  dashboard: (signal?: AbortSignal) => request<DashboardView>('GET', 'dashboard', { signal }),
  history: (signal?: AbortSignal) => request<HistoryView>('GET', 'history', { signal }),

  grammarList: (signal?: AbortSignal) => request<GrammarListView>('GET', 'grammar', { signal }),
  grammarTopic: (topicId: string, signal?: AbortSignal) => request<GrammarTopic>('GET', `grammar/${seg(topicId)}`, { signal }),
  grammarSearch: (params: GrammarSearchParams, signal?: AbortSignal) =>
    request<GrammarSearchView>('GET', 'grammar-search', {
      signal,
      query: { q: params.q, level: params.level, topicId: params.topicId, limit: params.limit },
    }),

  startPractice: (body: StartPracticeRequest) => request<PracticeSessionView>('POST', 'practice/sessions', { body }),
  practiceSession: (sessionId: string, signal?: AbortSignal) =>
    request<PracticeSessionView>('GET', `practice/sessions/${seg(sessionId)}`, { signal }),
  submitAnswer: (sessionId: string, body: SubmitAnswerRequest) =>
    request<AnswerResult>('POST', `practice/sessions/${seg(sessionId)}/answers`, { body }),
  completePractice: (sessionId: string) => request<PracticeSessionView>('POST', `practice/sessions/${seg(sessionId)}/complete`),

  // Conversations happen in the LINE talk; the mini app reads their results only.
  tutorSession: (sessionId: string, signal?: AbortSignal) =>
    request<TutorSessionView>('GET', `tutor/sessions/${seg(sessionId)}`, { signal }),

  workflow: (runId: string, signal?: AbortSignal) => request<WorkflowRun>('GET', `workflows/${seg(runId)}`, { signal }),
  retryWorkflow: (runId: string) => request<WorkflowRun>('POST', `workflows/${seg(runId)}/retry`),

};

export function toError(error: unknown): Error {
  if (error instanceof Error) return error;
  return new Error(typeof error === 'string' ? error : '不明なエラーが発生しました。');
}

export function newId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') return crypto.randomUUID();
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
}
