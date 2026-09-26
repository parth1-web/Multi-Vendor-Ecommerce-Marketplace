/**
 * Session lifecycle events that are not the session itself.
 *
 * A visitor who has never signed in and a visitor whose session just expired need different
 * treatment: the first is browsing, the second has to be told. Keeping that distinction here
 * is what lets a 401 on a background poll be ignored by a browser and acted on by a customer.
 */

import { create } from "zustand";

interface SessionState {
  /** True once a refresh has failed for good, so the app can route to sign-in. */
  sessionEnded: boolean;
  markEnded: () => void;
  clear: () => void;
}

export const useSessionStore = create<SessionState>((set) => ({
  sessionEnded: false,
  markEnded: () => set({ sessionEnded: true }),
  clear: () => set({ sessionEnded: false }),
}));
