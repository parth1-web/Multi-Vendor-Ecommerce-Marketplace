/** A seller's orders: their own half of each order, and the only half they can move. */

import { SellerOrders } from "@/features/seller/components/SellerOrders";

export default function Page() {
  return (
    <div>
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Orders</h1>
          <p className="mp-page-subtitle">Your part of every order, and what you earn from it.</p>
        </div>
      </div>

      <SellerOrders />
    </div>
  );
}
