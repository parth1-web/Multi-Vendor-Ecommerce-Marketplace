/**
 * A product card. Deliberately a server component: nothing here reads the session or runs a
 * handler, so it can be rendered on the server and reused inside a client grid without pulling
 * the whole card across the boundary. The one interactive part, the add-to-cart button, is a
 * leaf client component inside it.
 */

import Link from "next/link";
import { Store } from "lucide-react";

import { DiscountBadge, RatingStars } from "@/components/shared/RatingStars";
import { PriceDisplay } from "@/components/shared/PriceDisplay";
import type { ProductSummary } from "@/types/product";

export function ProductCard({ product }: { product: ProductSummary }) {
  return (
    <article className="mp-card h-100 d-flex flex-column" style={{ padding: "var(--space-3)", position: "relative" }}>
      <Link href={`/products/${product.slug}`} className="d-block" style={{ position: "relative" }}>
        {product.primaryImageUrl ? (
          // A plain img rather than next/image: these are seller-supplied URLs on a host we do
          // not control, and the optimiser would add a hop for no gain at this size.
          // eslint-disable-next-line @next/next/no-img-element
          <img
            src={product.primaryImageUrl}
            alt={product.primaryImageAlt ?? product.name}
            loading="lazy"
            width={320}
            height={320}
            style={{ width: "100%", aspectRatio: "1 / 1", objectFit: "cover", borderRadius: "var(--radius-sm)" }}
          />
        ) : (
          <div
            className="mp-skeleton"
            aria-hidden
            style={{ width: "100%", aspectRatio: "1 / 1", borderRadius: "var(--radius-sm)" }}
          />
        )}

        <DiscountBadge percentage={product.discountPercentage} />
      </Link>

      <div className="d-flex flex-column flex-grow-1" style={{ gap: "var(--space-1)", padding: "var(--space-2) var(--space-1) 0" }}>
        <Link href={`/stores/${product.storeSlug}`} className="d-inline-flex align-items-center" style={{ gap: "0.25rem", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
          <Store size={12} aria-hidden />
          {product.storeName}
        </Link>

        <h3 className="mp-clamp-2" style={{ fontSize: "var(--fs-sm)", fontWeight: 500, margin: 0, lineHeight: 1.4 }}>
          <Link href={`/products/${product.slug}`} style={{ color: "var(--text)" }}>
            {product.name}
          </Link>
        </h3>

        <RatingStars rating={product.ratingAverage} count={product.ratingCount} />
      </div>

      <div className="mp-spread" style={{ padding: "var(--space-2) var(--space-1) 0" }}>
        <PriceDisplay price={product.basePrice} compareAtPrice={product.compareAtPrice} discountPercentage={product.discountPercentage} size="sm" />

        {/*
          A listing carries no variant, so a card cannot add one straight to the basket: adding a
          blind choice is how a customer ends up with the wrong size. The button goes to the
          detail page, which is where the variants are known.
        */}
        <Link
          href={`/products/${product.slug}`}
          className="btn btn-sm btn-primary"
          style={{ fontSize: "var(--fs-xs)", opacity: product.isInStock ? 1 : 0.6 }}
        >
          {product.isInStock ? "Choose" : "Sold out"}
        </Link>
      </div>
    </article>
  );
}
