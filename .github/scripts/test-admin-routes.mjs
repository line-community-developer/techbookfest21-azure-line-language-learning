import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const config = JSON.parse(readFileSync(new URL('../../staticwebapp.config.json', import.meta.url), 'utf8'));
const matches = (pattern, pathname) => pattern.endsWith('*')
  ? pathname.startsWith(pattern.slice(0, -1)) : pathname === pattern;
const routeFor = pathname => config.routes.find(route => matches(route.route, pathname));

// SWA uses the first matching route. Broad anonymous routes must not shadow
// protected pages or either side of an API rewrite.
const protectedPaths = [
  '/admin', '/admin/questions/generate',
  ...['grammar-topic', 'sample-question', 'tutor-profile'].flatMap(model =>
    [`/${model}`, `/${model}/new`, `/${model}/record`, `/${model}/record/edit`]),
  ...['grammarTopic', 'sampleQuestion', 'tutorProfile'].flatMap(model =>
    [`/api/${model}`, `/api/${model}/record`]),
  '/api/grammar-topic', '/api/admin/grammar', '/api/management/grammar',
  ...['generate', 'validate', 'publish'].flatMap(action =>
    [`/api/admin/questions/${action}`, `/api/management/questions/${action}`]),
];

for (const pathname of protectedPaths) {
  const route = routeFor(pathname);
  assert.deepEqual(route?.allowedRoles, ['administrator'], `${pathname} must require administrator`);
  if (!pathname.startsWith('/api/')) {
    assert.equal(route.rewrite, '/index.html', `${pathname} must support direct SPA navigation`);
    assert(config.navigationFallback.exclude.some(pattern => matches(pattern, pathname)),
      `${pathname} must not bypass authorization through navigationFallback`);
  }
}

assert.equal(routeFor('/admin/login').rewrite, '/index.html');
assert(!routeFor('/admin/login').allowedRoles?.includes('administrator'), 'Login must be reachable before authentication');
assert.equal(routeFor('/api/grammar-topic').rewrite, '/api/grammarTopic', 'Scaffold reference selector must reach the registered CRUD route');
for (const [source, destination] of [
  ['/api/admin/grammar', '/api/management/grammar'],
  ...['generate', 'validate', 'publish'].map(action => [`/api/admin/questions/${action}`, `/api/management/questions/${action}`]),
]) assert.equal(routeFor(source).rewrite, destination);
assert.deepEqual(routeFor('/miniapp').allowedRoles, ['anonymous'], 'Learner authentication belongs to the LINE application');
console.log(`PASS: ${protectedPaths.length} protected admin paths, login, API rewrites, and learner entry`);
