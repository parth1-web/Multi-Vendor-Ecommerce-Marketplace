/** Discount codes: what they are for, when they run, and what they take off. */

import type { Metadata } from "next";

import { AdminCoupons } from "@/features/admin/components/AdminCoupons";

export const metadata: Metadata = {
  title: "Discount codes",
  robots: { index: false, follow: false },
};

export default function Page() {
  return <AdminCoupons />;
}
