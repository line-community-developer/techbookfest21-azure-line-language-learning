import type { AzureFunction, Context, HttpRequest } from '@azure/functions';
import { callFunction, parseBody, handleError } from '../lib/call-function';

const handler: AzureFunction = async (_context: Context, request: HttpRequest) => {
  try {
    const body = request.method === 'POST' ? parseBody(request) : undefined;
    return callFunction({
      request, method: request.method === 'POST' ? 'POST' : 'GET',
      path: '/api/greet?name=' + encodeURIComponent(request.query.name || 'World'), body,
    });
  } catch (error) { return handleError(error); }
};
export default handler;
