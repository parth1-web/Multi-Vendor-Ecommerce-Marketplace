"use client";

/**
 * Routes to sign-in when a session ends mid-visit.
 *
 * The API client clears the session when a refresh fails for good, but it cannot navigate: it
 * lives outside React, and a hard redirect would throw away every in-memory cache and hijack
 * whatever the user was reading. Instead the store records that the session *ended* — as
 * opposed to never having existed — and this component does the routing declaratively, keeping
 * the page the user was on in `returnUrl`.
 */

import { useRouter } from "next/navigation";
import { useEffect } from "react";

import { useAuth } from "@/providers/AuthProvider";
import { useSessionStore } from "@/store/sessionStore";

export function SessionRedirect() {
  const router = useRouter();
  const sessionEnded = useSessionStore((state) => state.sessionEnded);
  const { isAuthenticated } = useAuth();

  useEffect(() => {
    if (!sessionEnded || isAuthenticated) {
      return;
    }

    const returnUrl = `${window.location.pathname}${window.location.search}`;
    router.replace(`/login?returnUrl=${encodeURIComponent(returnUrl)}`);
  }, [isAuthenticated, router, sessionEnded]);

  return null;
}
