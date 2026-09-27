"use client";

/**
 * The guard every signed-in page in the application uses.
 *
 * It is one component because there are about eight of them and a copy per page is how one of
 * them ends up skipping the redirect. The session is still loading on a cold load, so it shows
 * a skeleton rather than deciding: redirecting before hydration finishes would bounce everyone,
 * signed in or not, and showing the page before the check would flash somebody's orders at a
 * visitor who is not them.
 */

import { useRouter } from "next/navigation";
import { useEffect, type ReactNode } from "react";

import { useAuth } from "@/providers/AuthProvider";

export function RequireAuth({ children, returnTo }: { children: ReactNode; returnTo?: string }) {
  const router = useRouter();
  const { isAuthenticated, isHydrating } = useAuth();

  useEffect(() => {
    if (!isHydrating && !isAuthenticated) {
      const destination = returnTo ?? window.location.pathname;
      router.replace(`/login?returnUrl=${encodeURIComponent(destination)}`);
    }
  }, [isAuthenticated, isHydrating, returnTo, router]);

  if (!isAuthenticated) {
    return <AuthSkeleton />;
  }

  return children;
}

function AuthSkeleton() {
  return (
    <div className="mp-page" style={{ paddingBlock: "var(--space-5)" }}>
      <div className="mp-skeleton" style={{ height: "20rem", borderRadius: "var(--radius)" }} />
    </div>
  );
}
