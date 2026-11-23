import type { AzureFunction, Context, HttpRequest } from '@azure/functions';
import { z } from 'zod/v4';
import { GrammarTopic } from '@line-language-learn/shared';
import { callFunction, parseBody, json, handleError } from '../lib/call-function';

const InputSchema = GrammarTopic.omit({ id: true, createdAt: true, updatedAt: true });

const handler: AzureFunction = async (_context: Context, request: HttpRequest) => {
  try {
    const id = request.params.id;
    const path = '/api/grammarTopic' + (id ? '/' + encodeURIComponent(id) : '');
    if (request.method === 'GET' && !id) return callFunction({ request, method: 'GET', path, responseSchema: z.array(GrammarTopic) });
    if (request.method === 'GET' && id) return callFunction({ request, method: 'GET', path, responseSchema: GrammarTopic });
    if (request.method === 'POST' && !id) return callFunction({ request, method: 'POST', path, body: parseBody(request), inputSchema: InputSchema, responseSchema: GrammarTopic, successStatus: 201 });
    if (request.method === 'PUT' && id) return callFunction({ request, method: 'PUT', path, body: parseBody(request), inputSchema: InputSchema, responseSchema: GrammarTopic });
    if (request.method === 'DELETE' && id) return callFunction({ request, method: 'DELETE', path });
    return json({ error: 'Method not allowed' }, 405);
  } catch (error) { return handleError(error); }
};
export default handler;
