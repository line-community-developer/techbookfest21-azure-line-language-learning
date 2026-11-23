import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { loadCurrentUser } from './load-user';
import * as adapter from './token-adapter';
export interface AuthUser { id: string; loginId: string; name: string; email: string; roles: string[]; }
type LoadUser = (signal: AbortSignal) => Promise<AuthUser | null>;
interface Value { user: AuthUser | null; loading: boolean; error: Error | null; refresh: () => void; login: () => Promise<void>; logout: () => Promise<void>; hasRole: (role: string) => boolean; hasAnyRole: (roles: string[]) => boolean; }
const Context = createContext<Value | null>(null);
export function AuthProvider({ children, loadUser = loadCurrentUser, identityVersion }: {
  children: ReactNode;
  /** Return null for a signed-out session. Throw for endpoint or transport failures. */
  loadUser?: LoadUser;
  /** Change when the IdP SDK reports a session or identity change. */
  identityVersion?: string | number;
}) {
  const [revision, setRevision] = useState(0);
  const [result, setResult] = useState<{ revision: number; identityVersion?: string | number; loader: LoadUser; user: AuthUser | null; error: Error | null } | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    setResult(null);
    const save = (user: AuthUser | null, error: Error | null) => {
      if (!controller.signal.aborted) setResult({ revision, identityVersion, loader: loadUser, user, error });
    };
    Promise.resolve().then(() => loadUser(controller.signal)).then(user => save(user, null), () => save(null, new Error('Unable to load the current user')));
    return () => controller.abort();
  }, [revision, identityVersion, loadUser]);
  const current = result?.revision === revision && result.identityVersion === identityVersion && result.loader === loadUser ? result : null;
  const user = current?.user ?? null;
  const refresh = () => setRevision(value => value + 1);
  const login = async () => { await adapter.login(); refresh(); };
  const logout = async () => { await adapter.logout(); refresh(); };
  const hasRole = (role: string) => user?.roles.includes(role) ?? false;
  return <Context.Provider value={{ user, loading: !current, error: current?.error ?? null, refresh, login, logout, hasRole, hasAnyRole: roles => roles.some(hasRole) }}>{children}</Context.Provider>;
}
export function useAuth() { const value = useContext(Context); if (!value) throw new Error('useAuth must be used within an AuthProvider'); return value; }
