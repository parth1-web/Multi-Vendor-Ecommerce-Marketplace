/** The order history: the signed-in person's own orders, newest first. */

import type { Metadata } from "next";
import { Suspense } from "react";

import { OrderList } from "@/features/orders/components/OrderList";

export const metadata: Metadata = {
  title: "Your orders",
  description: "Every order you have placed, and how far along it is.",
  robots: { index: false, follow: false },
};

export default function OrdersPage() {
  return (
    <div className="mp-page" style={{ paddingBlock: "var(--space-5)" }}>
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Your orders</h1>
          <p className="mp-page-subtitle">Every order you have placed, and how far along it is.</p>
        </div>
      </div>

      {/* The list reads the address bar, and address-bar readers suspend while navigating. */}
      <Suspense
        fallback={<div className="mp-skeleton" style={{ height: "16rem", borderRadius: "var(--radius)" }} aria-hidden />}
      >
        <OrderList />
      </Suspense>
    </div>
  );
}
