/** The basket. A client page: the basket is live state and the totals are what is at stake. */

import type { Metadata } from "next";

import { CartView } from "@/features/cart/components/CartView";

export const metadata: Metadata = {
  title: "Your basket",
  description: "Review what you are buying, grouped by the store selling each item.",
  robots: { index: false, follow: true },
};

export default function CartPage() {
  return (
    <div className="mp-page" style={{ paddingBlock: "var(--space-5)" }}>
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Your basket</h1>
          <p className="mp-page-subtitle">Grouped by store, because that is how it ships.</p>
        </div>
      </div>

      <CartView />
    </div>
  );
}
