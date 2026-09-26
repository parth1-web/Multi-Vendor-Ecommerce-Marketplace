/** The order page, rendered on the server as a shell around a signed-in view. */

import type { Metadata } from "next";

import { OrderDetail } from "@/features/orders/components/OrderDetail";

export const metadata: Metadata = {
  title: "Your order",
  robots: { index: false, follow: false },
};

export default function OrderPage() {
  return <OrderDetail />;
}
