/** One of your orders: what is in it, where it is going, and what you can do next. */

import type { Metadata } from "next";

import { SellerOrderDetail } from "@/features/seller/components/SellerOrderDetail";

export const metadata: Metadata = {
  title: "Your order",
  robots: { index: false, follow: false },
};

export default function Page() {
  return <SellerOrderDetail />;
}
