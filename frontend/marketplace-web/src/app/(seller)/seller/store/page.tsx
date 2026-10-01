/**
 * The seller's own storefront settings: identity first, then the editable storefront details.
 *
 * Identity comes from the seller profile endpoint and the editable fields from the store
 * profile endpoint, because the backend keeps the two apart: who the seller is cannot be
 * edited here, and what shoppers see can.
 */

import type { Metadata } from "next";

import { SellerStorePage } from "@/features/seller/components/SellerStore";

export const metadata: Metadata = {
  title: "Store settings",
  robots: { index: false, follow: false },
};

export default function SellerStoreSettingsPage() {
  return (
    <div>
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Store settings</h1>
          <p className="mp-page-subtitle">What shoppers see on the storefront, and who the seller is.</p>
        </div>
      </div>

      <SellerStorePage />
    </div>
  );
}
