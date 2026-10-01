/** The wishlist: things kept for later, with the price and stock as they are now. */

import type { Metadata } from "next";

import { WishlistPage } from "@/features/account/components/WishlistView";
import { AccountNav } from "@/features/account/components/AccountNav";

export const metadata: Metadata = {
  title: "Saved items",
  robots: { index: false, follow: false },
};

export default function Page() {
  return (
    <div className="mp-page" style={{ paddingBlock: "var(--space-5)" }}>
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Saved items</h1>
          <p className="mp-page-subtitle">Kept for later, with what they cost today.</p>
        </div>
      </div>

      <AccountNav />
      <WishlistPage />
    </div>
  );
}
