// Auth state for the SPA. Source of truth for "am I logged in" is the presence
// of a valid bearer token plus a verified user resolved via GET /api/auth/me
// (ADR-0001). On boot we bootstrap from the token mirror in localStorage by
// calling /me; a 401 there (handled in the client) clears the token.

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { authApi } from '@/api/endpoints';
import { ApiError } from '@/api/client';
import { clearToken, getToken, setToken, subscribeToken } from '@/api/tokenStore';
import type { AuthUser, LoginResponse } from '@/api/types';

type AuthStatus = 'loading' | 'authenticated' | 'unauthenticated';

interface AuthContextValue {
  status: AuthStatus;
  user: AuthUser | null;
  /** Persist a successful login response and enter the app. */
  signIn: (response: LoginResponse) => void;
  /** Invalidate the session server-side (best effort) and clear local state. */
  signOut: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<AuthStatus>(getToken() ? 'loading' : 'unauthenticated');
  const [user, setUser] = useState<AuthUser | null>(null);

  // Bootstrap: if we have a mirrored token, validate it against /me.
  useEffect(() => {
    const token = getToken();
    if (!token) {
      setStatus('unauthenticated');
      return;
    }
    const controller = new AbortController();
    let active = true;
    authApi
      .me(controller.signal)
      .then((me) => {
        if (!active) return;
        if (me.emailVerified) {
          setUser(me);
          setStatus('authenticated');
        } else {
          // A token whose user is somehow unverified: treat as not logged in.
          clearToken();
          setUser(null);
          setStatus('unauthenticated');
        }
      })
      .catch((err) => {
        if (!active) return;
        if (controller.signal.aborted) return;
        // 401 already cleared the token in the client; any failure -> logged out.
        if (err instanceof ApiError && err.code !== 'unauthorized') {
          // Non-auth failure (e.g. network): also drop to unauthenticated so the
          // user can retry from /login rather than being stuck on a spinner.
          clearToken();
        }
        setUser(null);
        setStatus('unauthenticated');
      });
    return () => {
      active = false;
      controller.abort();
    };
  }, []);

  // React to token clears triggered elsewhere (e.g. a 401 on any request).
  useEffect(() => {
    return subscribeToken((token) => {
      if (token === null) {
        setUser(null);
        setStatus('unauthenticated');
        queryClient.clear();
      }
    });
  }, [queryClient]);

  const signIn = useCallback(
    (response: LoginResponse) => {
      setToken(response.token);
      setUser(response.user);
      setStatus('authenticated');
    },
    [],
  );

  const signOut = useCallback(async () => {
    try {
      await authApi.logout();
    } catch {
      // Even if the server call fails, we still drop the local token.
    } finally {
      clearToken();
      setUser(null);
      setStatus('unauthenticated');
      queryClient.clear();
    }
  }, [queryClient]);

  const value = useMemo<AuthContextValue>(
    () => ({ status, user, signIn, signOut }),
    [status, user, signIn, signOut],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within an AuthProvider');
  return ctx;
}
