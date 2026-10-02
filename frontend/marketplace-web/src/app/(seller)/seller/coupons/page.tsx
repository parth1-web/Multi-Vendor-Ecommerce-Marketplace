/**
 * The seller's own discount codes.
 *
 * Created here rather than in the form because a code the API refused to persist — a duplicate, an
 * end date in the past — is not a code, and a seller who is told it exists will look for it.
 */

import type { Metadata } from "next";

import { SellerCoupons } from "@/features/seller/components/SellerCoupons";

export const metadata: Metadata = {
  title: "Discount codes",
  robots: { index: false, follow: false },
};

export default function SellerCouponsPage() {
  return (
    <div>
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Discount codes</h1>
          <p className="mp-page-subtitle">
            Codes shoppers can enter at checkout, and when each one stops working.
          </p>
        </div>
      </div>

      <SellerCoupons />
    </div>
  );
}