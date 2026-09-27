/** The admin console: a different chrome from the shop, and gated before anything renders. */

import type { Metadata } from "next";

import { AdminLayout } from "@/features/admin/components/AdminLayout";

export const metadata: Metadata = {
  title: "Admin console",
  robots: { index: false, follow: false },
};

export default function AdminLayoutPage({ children }: { children: React.ReactNode }) {
  return <AdminLayout>{children}</AdminLayout>;
}
