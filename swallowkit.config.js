module.exports = {
  frontend: {
    framework: 'react',
  },
  backend: {
    language: 'csharp',
  },
  functions: {
    baseUrl: process.env.BACKEND_FUNCTIONS_BASE_URL || process.env.FUNCTIONS_BASE_URL || 'http://localhost:7071',
  },
  deployment: {
    resourceGroup: process.env.AZURE_RESOURCE_GROUP || '',
    swaName: process.env.AZURE_SWA_NAME || '',
  },
  // Project-specific verification run by `swallowkit machine verify project`.
  verify: {
    checks: [
      { id: 'contracts', title: 'Shared Zod contracts exported to backend JSON Schemas', command: 'pnpm run contracts:check', timeoutMs: 120000 },
      { id: 'backend-test', title: 'Backend unit tests (mock AI, in-memory store)', command: 'pnpm run test', timeoutMs: 600000 },
      { id: 'seed-contracts', title: 'Scenario seeds match shared Zod models', command: 'pnpm run seeds:check', timeoutMs: 120000 },
      { id: 'seed-bootstrap', title: 'Standard cloud seed preparation and scenario validation', command: 'pnpm run seeds:bootstrap:check', timeoutMs: 120000 },
      { id: 'admin-routes', title: 'Admin page and API routing authorization', command: 'node .github/scripts/test-admin-routes.mjs', timeoutMs: 30000 },
    ],
  },
  auth: {
    schemes: {
      line: { provider: 'external-token' },
      "admin": { provider: 'swa', swa: { allowedProviders: ['github'] } },
    },
    // learner: mini app requests carry a LIFF ID token verified by the backend (functions/Auth/Schemes/line).
    authorization: {
      defaultPolicy: 'anonymous',
      policies: {
        learner: { schemes: ['line'] },
        adminOnly: { schemes: ['admin'], roles: ['administrator'] },
      },
    },
  },
}
