/**
 * The account dashboard.
 *
 * The first page after signing in, and the answer to "what is happening with my orders" without
 * having to choose a section first.
 */

import type { Metadata } from "next";

import { RequireAuth } from "@/features/account/components/RequireAuth";
import { CustomerDashboard } from "@/features/account/components/CustomerDashboard";

export const metadata: Metadata = {
  title: "Your account",
  description: "Your orders, saved items and account details.",
  robots: { index: false, follow: false },
};

export default function Page() {
  return (
    <RequireAuth>
      <CustomerDashboard />
    </RequireAuth>
  );
}
