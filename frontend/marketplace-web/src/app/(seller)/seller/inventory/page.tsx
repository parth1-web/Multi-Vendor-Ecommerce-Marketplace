/** A seller's stock, with what is on hand next to what can actually be sold. */

import { SellerInventory } from "@/features/seller/components/SellerInventory";

export default function Page() {
  return (
    <div>
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Stock</h1>
          <p className="mp-page-subtitle">On hand, promised to a basket, and sellable.</p>
        </div>
      </div>

      <SellerInventory />
    </div>
  );
}
