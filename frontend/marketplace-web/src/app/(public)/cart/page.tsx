/** The basket. A client island: the basket is live state and the totals are what is at stake. */

import type { Metadata } from "next";

import { CartView } from "@/features/cart/components/CartView";

export const metadata: Metadata = {
  title: "Your basket",
  description: "Review what you are buying, grouped by the store selling each item.",
  robots: { index: false, follow: true },
};

export default function CartPage() {
  // Everything below — heading, counts, skeleton, error, empty state — renders inside the view,
  // because only the live basket knows which of those the shopper should see.
  return <CartView />;
}
