import type { AzureFunction, Context, HttpRequest, HttpResponse } from '@azure/functions';
import { z } from 'zod/v4';
import {
  AnswerResult,
  ApiError,
  DashboardView,
  GrammarListView,
  GrammarSearchView,
  GrammarTopic,
  HistoryView,
  PracticeSessionView,
  StartPracticeRequest,
  SubmitAnswerRequest,
  SystemStatus,
  TutorSessionListView,
  TutorSessionView,
  WorkflowRun,
} from '@line-language-learn/shared';
import { backendRequest, BffError, handleError, json, parseBody } from '../lib/call-function';

// AI-authored BFF route for the learning API. Pure proxy: validates request/response contracts with the
// shared Zod schemas and forwards to the standalone Functions backend. The LIFF ID token arrives via
// x-swallowkit-external-token and is forwarded as Bearer for backend verification. No business logic, no user ids.

interface Route {
  method: 'GET' | 'POST';
  pattern: RegExp;
  input?: z.ZodType;
  response: z.ZodType;
  query?: string[];
}

const id = '([A-Za-z0-9_-]{1,120})';
const routes: Route[] = [
  { method: 'GET', pattern: /^status$/, response: SystemStatus },
  { method: 'GET', pattern: /^dashboard$/, response: DashboardView },
  { method: 'GET', pattern: /^history$/, response: HistoryView },
  { method: 'GET', pattern: /^grammar$/, response: GrammarListView },
  { method: 'GET', pattern: /^grammar-search$/, response: GrammarSearchView, query: ['q', 'level', 'topicId', 'limit'] },
  { method: 'GET', pattern: new RegExp(`^grammar/${id}$`), response: GrammarTopic },
  { method: 'POST', pattern: /^practice\/sessions$/, input: StartPracticeRequest, response: PracticeSessionView },
  { method: 'GET', pattern: new RegExp(`^practice/sessions/${id}$`), response: PracticeSessionView },
  { method: 'POST', pattern: new RegExp(`^practice/sessions/${id}/answers$`), input: SubmitAnswerRequest, response: AnswerResult },
  { method: 'POST', pattern: new RegExp(`^practice/sessions/${id}/complete$`), response: PracticeSessionView },
  { method: 'GET', pattern: /^tutor\/sessions$/, response: TutorSessionListView },
  { method: 'GET', pattern: new RegExp(`^tutor/sessions/${id}$`), response: TutorSessionView },
  { method: 'GET', pattern: new RegExp(`^workflows/${id}$`), response: WorkflowRun },
  { method: 'POST', pattern: new RegExp(`^workflows/${id}/retry$`), response: WorkflowRun },
];

function queryString(request: HttpRequest, allowed: string[] | undefined): string {
  if (!allowed) return '';
  const params = new URLSearchParams();
  for (const key of allowed) {
    const value = request.query[key];
    if (typeof value === 'string' && value.length > 0) params.set(key, value);
  }
  const text = params.toString();
  return text ? `?${text}` : '';
}

async function forward(request: HttpRequest, route: Route, path: string): Promise<HttpResponse> {
  let body: unknown;
  if (route.input) {
    const parsed = route.input.safeParse(parseBody(request));
    if (!parsed.success) return json({ error: 'Validation failed', code: 'invalid-request', details: parsed.error.issues }, 400);
    body = parsed.data;
  }
  const response = await backendRequest(request, route.method, `/api/learning/${path}${queryString(request, route.query)}`, body);
  const data: unknown = await response.json().catch(() => undefined);
  if (!response.ok) {
    // Backend errors use a fixed, non-sensitive shape; anything else becomes a generic error.
    const error = ApiError.safeParse(data);
    return json(error.success ? { error: error.data.error, code: error.data.code } : { error: 'Backend request failed' }, response.status);
  }
  const result = route.response.safeParse(data);
  if (!result.success) {
    console.error('[BFF] learning response validation failed for', route.pattern.source);
    return json({ error: 'Invalid backend response', code: 'invalid-backend-response' }, 502);
  }
  return json(result.data, response.status);
}

const handler: AzureFunction = async (_context: Context, request: HttpRequest) => {
  try {
    const path = String(request.params.path ?? '').replace(/^\/+|\/+$/g, '');
    const method = request.method === 'POST' ? 'POST' : request.method === 'GET' ? 'GET' : undefined;
    if (!method) throw new BffError(405, 'Method not allowed');
    const route = routes.find((r) => r.method === method && r.pattern.test(path));
    if (!route) {
      const exists = routes.some((r) => r.pattern.test(path));
      return json({ error: exists ? 'Method not allowed' : 'Not found' }, exists ? 405 : 404);
    }
    return await forward(request, route, path);
  } catch (error) {
    return handleError(error);
  }
};

export default handler;
