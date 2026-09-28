/**
 * Every provider the app needs, composed in one place so the root layout stays declarative
 * and the nesting order is visible at a glance: the query client wraps auth, because signing
 * out has to be able to clear the cache, and auth wraps realtime, because the connection is
 * opened with a session token and torn down when the session ends.
 */

"use client";

import type { ReactNode } from "react";

import { AuthProvider } from "@/providers/AuthProvider";
import { QueryProvider } from "@/providers/QueryProvider";
import { RealtimeProvider } from "@/providers/RealtimeProvider";
import { ThemeProvider } from "@/providers/ThemeProvider";
import { ToastProvider } from "@/providers/ToastProvider";

export function AppProviders({ children }: { children: ReactNode }) {
  return (
    <QueryProvider>
      <ThemeProvider>
        <ToastProvider>
          <AuthProvider>
            <RealtimeProvider>{children}</RealtimeProvider>
          </AuthProvider>
        </ToastProvider>
      </ThemeProvider>
    </QueryProvider>
  );
}
