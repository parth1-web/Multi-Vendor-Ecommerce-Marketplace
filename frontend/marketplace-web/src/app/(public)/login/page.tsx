/**
 * Signing in.
 *
 * A server page, so the form is in the HTML: a page whose whole job is a form should not need
 * JavaScript to exist. The only thing the server does is read where the visitor was heading,
 * and check it is a path on this site — a crafted ?returnUrl= must not be able to bounce a
 * signed-in person somewhere else wearing this session.
 */

import type { Metadata } from "next";

import { AnonymousOnly } from "@/features/auth/components/AnonymousOnly";
import { LoginForm } from "@/features/auth/components/LoginForm";

export const metadata: Metadata = {
  title: "Sign in",
  description: "Sign in to your marketplace account.",
  robots: { index: false, follow: true },
};

type SearchParams = Promise<Record<string, string | string[] | undefined>>;

export default async function LoginPage({ searchParams }: { searchParams: SearchParams }) {
  const params = await searchParams;
  const candidate = params.returnUrl;
  const returnUrl = typeof candidate === "string" ? candidate : null;

  return (
    <AnonymousOnly>
      <LoginForm returnUrl={safeReturnUrl(returnUrl)} />
    </AnonymousOnly>
  );
}

/** Accepts only a path on this site; anything else is dropped rather than followed. */
function safeReturnUrl(candidate: string | null): string {
  if (!candidate || !candidate.startsWith("/") || candidate.startsWith("//")) {
    return "/";
  }

  return candidate;
}
