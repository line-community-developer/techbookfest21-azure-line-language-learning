import liff from '@line/liff';

// LIFF ID is public (it identifies the LIFF app, not a credential). Unset locally to use the
// backend's local development identity; the backend always requires a LINE ID token on Azure.
const liffId = import.meta.env.VITE_LIFF_ID as string | undefined;

let ready: Promise<void> | null = null;

export const liffEnabled = Boolean(liffId);

/** Initializes LIFF once and starts LINE Login when needed (redirects back to the current page). */
export function initLiff(): Promise<void> {
  if (!liffId) return Promise.resolve();
  ready ??= liff.init({ liffId }).then(() => {
    if (!liff.isLoggedIn()) liff.login({ redirectUri: window.location.href });
  });
  return ready;
}

function expired(token: string): boolean {
  try {
    const payload = JSON.parse(atob(token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/'))) as { exp?: number };
    return typeof payload.exp !== 'number' || payload.exp * 1000 <= Date.now() + 30_000;
  } catch {
    return true;
  }
}

/** Returns the LINE ID token. The backend verifies it; the decoded profile is never trusted here. */
export async function getToken(): Promise<string | null> {
  if (!liffId) return null;
  await initLiff();
  if (!liff.isLoggedIn()) return null;
  const token = liff.getIDToken();
  if (!token || expired(token)) {
    await login();
    return null;
  }
  return token;
}

export async function login(): Promise<void> {
  if (!liffId) return;
  await initLiff();
  if (liff.isLoggedIn()) liff.logout();
  liff.login({ redirectUri: window.location.href });
}

export async function logout(): Promise<void> {
  if (!liffId) return;
  await initLiff();
  if (liff.isLoggedIn()) liff.logout();
}
