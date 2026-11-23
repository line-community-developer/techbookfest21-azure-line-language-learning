import type { HttpRequest, HttpResponse } from '@azure/functions';
import { z } from 'zod/v4';

// SwallowKit BFF Call Function Helper (SWA managed Functions)
const forwardSwa = true;
const forwardExternalToken = true;
const forwardJwtCookie = false;
const authCookieName = 'line-language-learn-auth-token';

export class BffError extends Error {
  constructor(public status: number, message: string, public code?: string) { super(message); }
}

export function json(body: unknown, status = 200): HttpResponse {
  return { status, body, headers: { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' } };
}

export function parseBody(request: HttpRequest): unknown {
  if (request.body === undefined || request.body === '') throw new BffError(400, 'Request body is required');
  if (typeof request.body !== 'string') return request.body;
  try { return JSON.parse(request.body); }
  catch { throw new BffError(400, 'Invalid JSON body'); }
}

function credentialHeaders(request: HttpRequest): Record<string, string> {
  const result: Record<string, string> = {};
  // The managed API proxy can replace Authorization. Only application credentials are forwarded.
  const external = forwardExternalToken ? request.headers['x-swallowkit-external-token'] : undefined;
  if (external !== undefined) {
    if (!external || /[\s,]/.test(external)) throw new BffError(401, 'Invalid token', 'invalid_token');
    result.Authorization = 'Bearer ' + external;
  } else if (forwardJwtCookie) {
    const cookie = request.headers.cookie?.split(';').map(value => value.trim()).find(value => value.startsWith(authCookieName + '='));
    if (cookie) {
      try { result.Authorization = 'Bearer ' + decodeURIComponent(cookie.slice(authCookieName.length + 1)); }
      catch { throw new BffError(401, 'Invalid authentication cookie'); }
    }
  }
  // SWA injects this header at the trusted managed API boundary. Never expose the backend key to clients.
  if (forwardSwa && request.headers['x-ms-client-principal']) {
    const encoded = request.headers['x-ms-client-principal'];
    try {
      const decoded = Buffer.from(encoded, 'base64');
      if (decoded.toString('base64') !== encoded) throw new Error('Invalid encoding');
      const value = JSON.parse(decoded.toString('utf8'));
      if (typeof value.identityProvider !== 'string' || !value.identityProvider ||
          typeof value.userId !== 'string' || !value.userId ||
          !Array.isArray(value.userRoles) || !value.userRoles.includes('authenticated')) {
        throw new Error('Invalid principal');
      }
      result['x-ms-client-principal'] = Buffer.from(JSON.stringify({
        identityProvider: value.identityProvider, userId: value.userId,
        userDetails: typeof value.userDetails === 'string' ? value.userDetails : '',
        userRoles: value.userRoles.filter((role: unknown) => typeof role === 'string'),
      })).toString('base64');
    } catch {
      throw new BffError(401, 'Invalid SWA client principal');
    }
  }
  return result;
}

export async function backendRequest(request: HttpRequest, method: string, path: string, body?: unknown): Promise<Response> {
  const baseUrl = process.env.BACKEND_FUNCTIONS_BASE_URL || process.env.FUNCTIONS_BASE_URL || 'http://localhost:7071';
  const parsedUrl = new URL(baseUrl);
  const local = parsedUrl.protocol === 'http:' && ['localhost', '127.0.0.1', '[::1]'].includes(parsedUrl.hostname);
  const key = process.env.BACKEND_FUNCTIONS_KEY;
  if (!key && !local) throw new Error('BACKEND_FUNCTIONS_KEY is required for non-local Functions calls');
  if (!local && parsedUrl.protocol !== 'https:') throw new Error('Remote Functions calls require HTTPS');
  return fetch(baseUrl.replace(/\/$/, '') + path, {
    method,
    headers: { 'Content-Type': 'application/json', ...credentialHeaders(request), ...(key ? { 'x-functions-key': key } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
    redirect: 'error',
  });
}

interface CallFunctionConfig {
  request: HttpRequest;
  method: 'GET' | 'POST' | 'PUT' | 'DELETE';
  path: string;
  body?: unknown;
  inputSchema?: z.ZodType;
  responseSchema?: z.ZodType;
  successStatus?: number;
}

export async function callFunction(config: CallFunctionConfig): Promise<HttpResponse> {
  try {
    let body = config.body;
    if (config.inputSchema) {
      const result = config.inputSchema.safeParse(body);
      if (!result.success) return json({ error: 'Validation failed', details: result.error.issues }, 400);
      body = result.data;
    }
    const response = await backendRequest(config.request, config.method, config.path, body);
    if (response.status === 204) return { status: 204 };
    if (!response.ok) {
      return backendError(response);
    }
    const data: unknown = await response.json();
    if (config.responseSchema) {
      const result = config.responseSchema.safeParse(data);
      if (!result.success) {
        console.error('[BFF] Backend response validation failed');
        return json({ error: 'Invalid backend response' }, 502);
      }
      return json(result.data, config.successStatus ?? 200);
    }
    return json(data, config.successStatus ?? 200);
  } catch (error) {
    return handleError(error);
  }
}

// Forward only recognized status/code pairs. Backend error messages may contain sensitive details.
export async function backendError(response: Response): Promise<HttpResponse> {
  return json(await safeBackendError(response), response.status);
}

async function safeBackendError(response: Response): Promise<{ error: string; code?: string }> {
  const failures: Record<string, [number, string]> = {
    unauthenticated: [401, 'Authentication required'],
    invalid_token: [401, 'Invalid token'],
    expired_token: [401, 'Token expired'],
    identity_provider_unavailable: [503, 'Identity provider unavailable'],
    verifier_not_configured: [500, 'External token verifier is not configured'],
    verifier_unavailable: [503, 'External token verifier unavailable'],
  };
  if ([401, 500, 503].includes(response.status)) {
    const body: unknown = await response.json().catch(() => null);
    if (body && typeof body === 'object' && 'code' in body && typeof body.code === 'string' &&
        Object.prototype.hasOwnProperty.call(failures, body.code)) {
      const [status, error] = failures[body.code];
      if (status === response.status) return { code: body.code, error };
    }
  }
  return { error: 'Backend request failed' };
}


export function handleError(error: unknown): HttpResponse {
  if (error instanceof BffError) return json({ error: error.message, ...(error.code ? { code: error.code } : {}) }, error.status);
  console.error('[BFF] Request failed:', error instanceof Error ? error.name : 'UnknownError');
  return json({ error: 'Backend request failed' }, 502);
}
