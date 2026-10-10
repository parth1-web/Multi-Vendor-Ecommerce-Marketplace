/** Every variant you sell, read as a report rather than worked on. */

import type { Metadata } from "next";

import { SellerInventoryReport } from "@/features/seller/components/SellerInventoryReport";

export const metadata: Metadata = {
  title: "Stock report",
  robots: { index: false, follow: false },
};

export default function Page() {
  return (
    <div>
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Stock report</h1>
          <p className="mp-page-subtitle">
            Every variant you sell, lowest stock first. To change a quantity, use Stock.
          </p>
        </div>
      </div>

      <SellerInventoryReport />
    </div>
  );
}