/** Theme, held in a persisted store because a preference is not state worth losing on reload. */

"use client";

import { useEffect } from "react";
import { create } from "zustand";
import { persist } from "zustand/middleware";

export type Theme = "light" | "dark";

interface ThemeState {
  theme: Theme;
  toggle: () => void;
  set: (theme: Theme) => void;
}

export const useThemeStore = create<ThemeState>()(
  persist(
    (set) => ({
      theme: "light",
      toggle: () => set((state) => ({ theme: state.theme === "dark" ? "light" : "dark" })),
      set: (theme) => set({ theme }),
    }),
    {
      name: "mp-theme",
      // Only the choice is stored, never anything derived from a session.
      partialize: (state) => ({ theme: state.theme }),
    },
  ),
);

export function ThemeProvider({ children }: { children: React.ReactNode }) {
  const theme = useThemeStore((state) => state.theme);

  useEffect(() => {
    // The document head already set this before the first paint, so the page never flashes the
    // wrong theme. Setting it again here is what makes a toggle take effect: the store changes,
    // this runs, and the attribute changes. It also keeps the store in charge if the script was
    // blocked, which is what "prefers light" is for.
    document.documentElement.dataset.theme = theme;
  }, [theme]);

  return children;
}
