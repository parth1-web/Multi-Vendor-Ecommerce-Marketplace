/** Every listing on the marketplace, whichever state it is in. */

import type { Metadata } from "next";

import { AdminProducts } from "@/features/admin/components/AdminProducts";

export const metadata: Metadata = {
  title: "Products",
  robots: { index: false, follow: false },
};

export default function Page() {
  return (
    <div>
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Products</h1>
          <p className="mp-page-subtitle">
            Every listing, and the three decisions an administrator can make about one.
          </p>
        </div>
      </div>

      <AdminProducts />
    </div>
  );
}