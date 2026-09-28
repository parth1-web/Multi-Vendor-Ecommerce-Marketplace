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
import { serverGetQuietly } from "@/lib/serverApi";
import type { PagedResult } from "@/types/api";
import type { Category } from "@/types/product";
import type { StoreDirectoryEntry } from "@/types/store";

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

  // The figures on the left are the real ones, fetched quietly: a sign-in page that failed
  // because a count would not load would be the worst possible place for that to happen.
  const [products, stores, categories] = await Promise.all([
    serverGetQuietly<PagedResult<unknown>>("/api/products?pageSize=1"),
    serverGetQuietly<PagedResult<StoreDirectoryEntry>>("/api/stores?pageSize=1"),
    serverGetQuietly<Category[]>("/api/categories"),
  ]);

  return (
    <AnonymousOnly>
      <LoginForm
        returnUrl={safeReturnUrl(returnUrl)}
        reason={reasonFrom(params.reason)}
        stats={
          products && stores && categories
            ? { products: products.totalCount, stores: stores.totalCount, categories: categories.length }
            : null
        }
      />
    </AnonymousOnly>
  );
}

/** Why somebody is being asked to sign in, in words rather than a code the page has to decode. */
function reasonFrom(candidate: string | string[] | undefined): string | null {
  const reasons: Record<string, string> = {
    "password-changed": "Your password has been changed, so this device has been signed out too. Sign in with the new one.",
    expired: "Your session has ended. Sign in again to carry on.",
  };

  return typeof candidate === "string" ? (reasons[candidate] ?? null) : null;
}

/** Accepts only a path on this site; anything else is dropped rather than followed. */
function safeReturnUrl(candidate: string | null): string {
  if (!candidate || !candidate.startsWith("/") || candidate.startsWith("//")) {
    return "/";
  }

  return candidate;
}
