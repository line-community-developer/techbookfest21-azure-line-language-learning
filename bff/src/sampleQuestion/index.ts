import type { AzureFunction, Context, HttpRequest } from '@azure/functions';
import { z } from 'zod/v4';
import { SampleQuestion } from '@line-language-learn/shared';
import { callFunction, parseBody, json, handleError } from '../lib/call-function';

const InputSchema = SampleQuestion.omit({ id: true, createdAt: true, updatedAt: true });

const handler: AzureFunction = async (_context: Context, request: HttpRequest) => {
  try {
    const id = request.params.id;
    const path = '/api/sampleQuestion' + (id ? '/' + encodeURIComponent(id) : '');
    if (request.method === 'GET' && !id) return callFunction({ request, method: 'GET', path, responseSchema: z.array(SampleQuestion) });
    if (request.method === 'GET' && id) return callFunction({ request, method: 'GET', path, responseSchema: SampleQuestion });
    if (request.method === 'POST' && !id) return callFunction({ request, method: 'POST', path, body: parseBody(request), inputSchema: InputSchema, responseSchema: SampleQuestion, successStatus: 201 });
    if (request.method === 'PUT' && id) return callFunction({ request, method: 'PUT', path, body: parseBody(request), inputSchema: InputSchema, responseSchema: SampleQuestion });
    if (request.method === 'DELETE' && id) return callFunction({ request, method: 'DELETE', path });
    return json({ error: 'Method not allowed' }, 405);
  } catch (error) { return handleError(error); }
};
export default handler;
