/**
 * Keeps a signed-in person off the pages that only make sense when signed out.
 *
 * Without this, an already-authenticated visitor who follows a stale "sign in" link fills in a
 * form they did not need to fill in, and the form appears to work while changing nothing.
 *
 * The children render during hydration rather than being replaced by a placeholder. A skeleton
 * here would mean the sign-in form exists only after JavaScript runs, which is a worse answer
 * for a page whose whole job is a form than the brief flash a signed-in visitor gets before
 * being redirected.
 */

"use client";

import { useRouter } from "next/navigation";
import { useEffect, type ReactNode } from "react";

import { useAuth } from "@/providers/AuthProvider";

export function AnonymousOnly({ children }: { children: ReactNode }) {
  const router = useRouter();
  const { isAuthenticated } = useAuth();

  useEffect(() => {
    if (isAuthenticated) {
      router.replace("/");
    }
  }, [isAuthenticated, router]);

  return children;
}
