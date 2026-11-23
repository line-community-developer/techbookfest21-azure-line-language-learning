'use client';
import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';

export interface AuthUser { id: string; loginId: string; name: string; email: string; roles: string[]; }
interface AuthContextValue { user: AuthUser | null; loading: boolean; login: () => void; logout: () => void; hasRole: (role: string) => boolean; hasAnyRole: (roles: string[]) => boolean; }
const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(null);
  const [loading, setLoading] = useState(true);
  useEffect(() => {
    fetch('/.auth/me')
      .then(async response => {
        if (!response.ok) throw new Error('SWA authentication is unavailable');
        const body: unknown = await response.json();
        if (!body || typeof body !== 'object' || !('clientPrincipal' in body)) return null;
        const principal = body.clientPrincipal;
        if (!principal || typeof principal !== 'object') return null;
        const value = principal as Record<string, unknown>;
        if (typeof value.userId !== 'string') return null;
        return {
          id: value.userId,
          loginId: typeof value.userDetails === 'string' ? value.userDetails : '',
          name: typeof value.userDetails === 'string' ? value.userDetails : '',
          email: typeof value.userDetails === 'string' ? value.userDetails : '',
          roles: Array.isArray(value.userRoles) ? value.userRoles.filter((role): role is string => typeof role === 'string') : [],
        } satisfies AuthUser;
      })
      .then(setUser)
      .catch(() => setUser(null))
      .finally(() => setLoading(false));
  }, []);
  const login = () => { window.location.href = '/.auth/login/github?post_login_redirect_uri=/admin'; };
  const logout = () => { window.location.href = '/.auth/logout?post_logout_redirect_uri=/admin/login'; };
  const hasRole = (role: string) => user?.roles.includes(role) ?? false;
  return <AuthContext.Provider value={{ user, loading, login, logout, hasRole, hasAnyRole: roles => roles.some(hasRole) }}>{children}</AuthContext.Provider>;
}
export function useAuth() { const value = useContext(AuthContext); if (!value) throw new Error('useAuth must be used within an AuthProvider'); return value; }
