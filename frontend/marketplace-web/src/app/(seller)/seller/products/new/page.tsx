/** Listing a new product: the same questions the edit form asks, for a product that does not exist yet. */

import type { Metadata } from "next";

import { ProductEditor } from "@/features/seller/components/ProductEditor";

export const metadata: Metadata = {
  title: "List a product",
  robots: { index: false, follow: false },
};

export default function NewProductPage() {
  return (
    <div>
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">List a product</h1>
          <p className="mp-page-subtitle">
            A moderator reviews every new listing before shoppers can see it. That is what makes a marketplace worth
            shopping in.
          </p>
        </div>
      </div>

      <ProductEditor />
    </div>
  );
}
