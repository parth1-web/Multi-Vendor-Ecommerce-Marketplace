/**
 * Choosing a new password from a reset link.
 *
 * The token is read here, on the server, and handed to the form. It is not kept in client state
 * and it is not put in a query cache, and once it has been used the address bar is cleaned so a
 * spent link is not left sitting in history.
 */

import type { Metadata } from "next";

import { AnonymousOnly } from "@/features/auth/components/AnonymousOnly";
import { ResetPasswordForm } from "@/features/auth/components/ResetPasswordForm";

export const metadata: Metadata = {
  title: "Choose a new password",
  robots: { index: false, follow: false },
};

type SearchParams = Promise<Record<string, string | string[] | undefined>>;

export default async function ResetPasswordPage({ searchParams }: { searchParams: SearchParams }) {
  const params = await searchParams;
  const token = typeof params.token === "string" ? params.token : null;

  return (
    <AnonymousOnly>
      <ResetPasswordForm token={token} />
    </AnonymousOnly>
  );
}
