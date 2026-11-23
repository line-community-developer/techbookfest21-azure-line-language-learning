import type { AzureFunction } from '@azure/functions';
import type { ZodType } from 'zod/v4';
import {
  AdminQuestionGenerationRequest, AdminQuestionGenerationResponse, AdminQuestionValidationRequest,
  AdminQuestionPublishRequest, AdminQuestionPublishResponse, GrammarListView, QuestionValidation,
} from '@line-language-learn/shared';
import { callFunction, handleError, json, parseBody } from '../lib/call-function';

const questionRoutes = new Map<string, { inputSchema: ZodType; responseSchema: ZodType }>([
  ['questions/generate', { inputSchema: AdminQuestionGenerationRequest, responseSchema: AdminQuestionGenerationResponse }],
  ['questions/validate', { inputSchema: AdminQuestionValidationRequest, responseSchema: QuestionValidation }],
  ['questions/publish', { inputSchema: AdminQuestionPublishRequest, responseSchema: AdminQuestionPublishResponse }],
]);

const handler: AzureFunction = async (_context, request) => {
  try {
    const path = request.params.path;
    if (path === 'grammar' && request.method === 'GET') {
      return callFunction({ request, method: 'GET', path: '/api/management/grammar', responseSchema: GrammarListView });
    }
    const route = questionRoutes.get(path);
    if (route && request.method === 'POST') {
      const contentType = request.headers['content-type']?.split(';', 1)[0].trim().toLowerCase();
      if (contentType !== 'application/json') return json({ error: 'Expected application/json' }, 415);
      return callFunction({
        request, method: 'POST', path: `/api/management/${path}`,
        body: parseBody(request), ...route,
      });
    }
    const exists = path === 'grammar' || route !== undefined;
    return json({ error: exists ? 'Method not allowed' : 'Not found' }, exists ? 405 : 404);
  } catch (error) {
    return handleError(error);
  }
};

export default handler;
