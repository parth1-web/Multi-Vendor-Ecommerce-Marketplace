/**
 * The payment step a gateway sends the customer to. A sandbox provider redirects here; a real one
 * redirects to its own hosted page and never reaches us.
 */

import type { Metadata } from "next";
import { Suspense } from "react";

import { PaymentGate } from "@/features/orders/components/PaymentGate";

export const metadata: Metadata = {
  title: "Pay for your order",
  robots: { index: false, follow: false },
};

export default function Page() {
  return (
    <div className="container section">
      <Suspense fallback={<div className="mp-skeleton" style={{ height: "14rem", borderRadius: "var(--radius)" }} />}>
        <PaymentGate />
      </Suspense>
    </div>
  );
}
