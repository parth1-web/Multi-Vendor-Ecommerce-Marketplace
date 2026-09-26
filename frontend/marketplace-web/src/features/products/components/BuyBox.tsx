"use client";

/**
 * The buy box on a product page. Client because it holds the buy box: a variant has to be
 * chosen, a quantity entered, and the basket updated. Everything around it stays on the server.
 */

import { VariantPicker } from "@/features/products/components/VariantPicker";
import type { ProductDetail } from "@/types/product";

export function BuyBox({ product }: { product: ProductDetail }) {
  return (
    <div className="mp-card" style={{ padding: "var(--space-5)" }}>
      <VariantPicker productId={product.id} variants={product.variants} />
    </div>
  );
}
