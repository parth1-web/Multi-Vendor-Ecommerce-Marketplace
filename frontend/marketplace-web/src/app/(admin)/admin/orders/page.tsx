/** Every order on the marketplace, and the only place one can be moved as a whole. */

import type { Metadata } from "next";

import { AdminOrders } from "@/features/admin/components/AdminOrders";

export const metadata: Metadata = {
  title: "Orders",
  robots: { index: false, follow: false },
};

export default function Page() {
  return <AdminOrders />;
}
