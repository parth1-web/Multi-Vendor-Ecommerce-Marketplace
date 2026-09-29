/**
 * Query client defaults.
 *
 * The defaults are deliberately per-domain rather than global: a product list tolerates a
 * slightly stale answer, a cart total and a payment do not.
 */

"use client";

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useState, type ReactNode } from "react";

import { STALE_TIME } from "@/lib/constants";

export function QueryProvider({ children }: { children: ReactNode }) {
  // One client per browser session, created lazily so a server render never shares state.
  const [client] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            staleTime: STALE_TIME.catalogue,
            refetchOnWindowFocus: false,
            retry: (failureCount, error) => {
              // Axios failures are normalized to ApiErrorShape by the response interceptor, so
              // check that envelope as well as the raw Axios envelope.
              const normalized = error as { response?: { status?: number }; status?: number };
              const status = normalized?.response?.status ?? normalized?.status;

              // Retrying a 404 or a 403 only produces the same answer, more slowly.
              if (status && status >= 400 && status < 500) {
                return false;
              }

              return failureCount < 2;
            },
          },
          mutations: {
            retry: false,
          },
        },
      }),
  );

  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}
