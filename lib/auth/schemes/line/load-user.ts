import type { AuthUser } from './auth-context';

// If you use AuthProvider, call an application-owned endpoint with authenticatedFetch.
// Return the server-verified user, null for signed out, or throw for request/endpoint failures.
export async function loadCurrentUser(_signal: AbortSignal): Promise<AuthUser | null> {
  throw new Error('Named external-token AuthProvider requires a current-user loader');
}
