"use client";

/**
 * The buy box on a product page. Client because it holds the buy box: a variant has to be
 * chosen, a quantity entered, and the basket updated. Everything around it stays on the server.
 *
 * The heart lives here too, next to the button that buys it. A shopper deciding between this and
 * something else is deciding at the point where they can act on it, and it is the only place on
 * the page where saving something makes sense.
 */

import { WishlistButton } from "@/features/account/components/WishlistButton";
import { useSavedProductIds } from "@/features/account/api/useWishlist";
import { VariantPicker } from "@/features/products/components/VariantPicker";
import type { ProductDetail } from "@/types/product";

export function BuyBox({ product, initialVariantId }: { product: ProductDetail; initialVariantId?: string }) {
  const savedIds = useSavedProductIds();

  return (
    <div className="mp-card" style={{ padding: "var(--space-5)" }}>
      <div className="mp-spread mb-3">
        <span className="mp-metric-label">Options and delivery</span>
        <WishlistButton productId={product.id} saved={savedIds.has(product.id)} label="Save for later" />
      </div>

      <VariantPicker productId={product.id} variants={product.variants} initialVariantId={initialVariantId} />
    </div>
  );
}
