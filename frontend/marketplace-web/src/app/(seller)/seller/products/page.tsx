/** A seller's catalogue, filtered by what a moderator has and has not approved. */

import { SellerProducts } from "@/features/seller/components/SellerProducts";

export default function Page() {
  return (
    <div>
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Your products</h1>
          <p className="mp-page-subtitle">Drafts, live listings, and anything waiting on a moderator.</p>
        </div>
      </div>

      <SellerProducts />
    </div>
  );
}
