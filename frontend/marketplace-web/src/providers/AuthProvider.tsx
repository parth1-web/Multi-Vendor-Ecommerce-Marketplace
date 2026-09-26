/**
 * Keeps the session in step with the API.
 *
 * On mount it asks who the caller is. The access token is in memory, so a reload starts with
 * nothing: this is the call that re-establishes the session from the refresh cookie. If it
 * fails, there is no session, and that is a normal state rather than an error to show.
 */

"use client";

import { useQueryClient } from "@tanstack/react-query";
import { createContext, useCallback, useContext, useEffect, useMemo, type ReactNode } from "react";

import { configureApi } from "@/api/axiosClient";
import { authApi } from "@/features/auth/api/authApi";
import { readAccessToken, useAuthStore, type SessionUser } from "@/store/authStore";
import { useSessionStore } from "@/store/sessionStore";

interface AuthContextValue {
  user: SessionUser | null;
  isAuthenticated: boolean;
  isHydrating: boolean;
  signIn: (token: string, user: SessionUser) => void;
  signOut: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const { user, accessToken, isHydrating, setSession, setToken, setHydrating, clear } = useAuthStore();

  // The interceptors need to read the token and be able to end the session, but they live
  // outside React. Wiring them here keeps the store the single owner of both.
  useEffect(() => {
    configureApi({
      readToken: readAccessToken,
      invalidateSession: () => {
        useAuthStore.getState().clear();
        useSessionStore.getState().markEnded();
        queryClient.clear();
      },
    });
  }, [queryClient]);

  useEffect(() => {
    if (accessToken) {
      setToken(accessToken);
    }
  }, [accessToken, setToken]);

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      try {
        const me = await authApi.me();
        if (!cancelled) {
          setSession(useAuthStore.getState().accessToken ?? "", me);
        }
      } catch {
        // No session. The refresh cookie was absent, expired, or the account is gone.
        if (!cancelled) {
          useAuthStore.getState().clear();
        }
      } finally {
        if (!cancelled) {
          setHydrating(false);
        }
      }
    })();

    return () => {
      cancelled = true;
    };
    // Runs once: the session is established on load, not re-fetched on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const signIn = useCallback(
    (token: string, nextUser: SessionUser) => {
      setSession(token, nextUser);
      useSessionStore.getState().clear();
      queryClient.clear();
    },
    [queryClient, setSession],
  );

  const signOut = useCallback(async () => {
    try {
      await authApi.logout();
    } catch {
      // Signing out locally matters more than the server acknowledging it.
    } finally {
      clear();
      useSessionStore.getState().markEnded();
      queryClient.clear();
    }
  }, [clear, queryClient]);

  const value = useMemo<AuthContextValue>(
    () => ({ user, isAuthenticated: Boolean(user), isHydrating, signIn, signOut }),
    [user, isHydrating, signIn, signOut],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);

  if (!context) {
    throw new Error("useAuth must be used inside AuthProvider");
  }

  return context;
}
