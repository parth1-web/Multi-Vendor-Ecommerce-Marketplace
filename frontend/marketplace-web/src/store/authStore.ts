/**
 * The session: who is signed in, and the access token that proves it.
 *
 * The token is held in memory and nowhere else. It is never written to storage and never put
 * in a query key, so a shared link, a server render or a devtools snapshot cannot leak a
 * session. A page reload re-reads the identity from the API, which is the trade: one extra
 * request on load in exchange for a token that cannot be stolen from disk.
 */

import { create } from "zustand";

import type { UserRole } from "@/types/auth";

export interface SessionUser {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  fullName: string;
  phoneNumber?: string | null;
  role: UserRole;
  isEmailConfirmed: boolean;
  isActive: boolean;
  sellerId: string | null;
  sellerStatus: string | null;
  createdAt: string;
  lastLoginAt: string | null;
}

interface AuthState {
  user: SessionUser | null;
  accessToken: string | null;
  /** True until the first identity check finishes, so the shell can hold back a redirect. */
  isHydrating: boolean;
  setSession: (token: string, user: SessionUser) => void;
  setToken: (token: string) => void;
  setHydrating: (hydrating: boolean) => void;
  clear: () => void;
}

export const useAuthStore = create<AuthState>((set) => ({
  user: null,
  accessToken: null,
  isHydrating: true,

  setSession: (accessToken, user) => set({ accessToken, user, isHydrating: false }),

  setToken: (accessToken) => set({ accessToken }),

  setHydrating: (isHydrating) => set({ isHydrating }),

  clear: () => set({ accessToken: null, user: null, isHydrating: false }),
}));

/** Read outside React, for the axios interceptor. */
export const readAccessToken = (): string | null => useAuthStore.getState().accessToken;

export const isSeller = (user: SessionUser | null): boolean => user?.role === "Seller";
export const isAdmin = (user: SessionUser | null): boolean => user?.role === "Admin" || user?.role === "SuperAdmin";
