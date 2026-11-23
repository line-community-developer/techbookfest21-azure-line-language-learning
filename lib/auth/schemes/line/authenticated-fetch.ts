import { getToken } from './token-adapter';

export async function authenticatedFetch(input: RequestInfo | URL, init: RequestInit = {}): Promise<Response> {
  const url = new URL(input instanceof Request ? input.url : input, window.location.href);
  if (url.origin !== window.location.origin || !url.pathname.startsWith('/api/')) {
    throw new Error('External tokens can only be sent to the same-origin API');
  }
  const token = await getToken();
  if (!token) return new Response(JSON.stringify({ code: 'unauthenticated', error: 'Authentication required' }), { status: 401, headers: { 'content-type': 'application/json', 'cache-control': 'no-store' } });
  const headers = new Headers(init.headers ?? (input instanceof Request ? input.headers : undefined));
  headers.delete('Authorization');
  headers.set('x-swallowkit-external-token', token);
  return fetch(input, { ...init, headers, cache: 'no-store', redirect: 'error' });
}
