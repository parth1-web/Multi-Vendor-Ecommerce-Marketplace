import Link from "next/link";
import { Star, Store as StoreIcon } from "lucide-react";

import { formatNumber } from "@/lib/format";

/**
 * Who sells this product, in one compact card.
 *
 * Only backend-provided facts: the store's name, logo, rating, and review count. Anything the
 * product payload does not carry — policies, shipping promises, support contacts — lives on the
 * store page this card links to, not here.
 */
export function SellerCard({
  storeName,
  storeSlug,
  storeLogoUrl,
  storeRating,
  storeRatingCount,
  sellerName,
}: {
  storeName: string;
  storeSlug: string;
  storeLogoUrl: string | null;
  storeRating: number;
  storeRatingCount: number;
  sellerName: string;
}) {
  return (
    <section aria-label="About the seller" className="mp-card" style={{ padding: "var(--space-4)" }}>
      <div className="d-flex align-items-center" style={{ gap: "var(--space-3)" }}>
        {storeLogoUrl ? (
          // eslint-disable-next-line @next/next/no-img-element
          <img
            src={storeLogoUrl}
            alt=""
            width={48}
            height={48}
            loading="lazy"
            decoding="async"
            style={{
              width: "3rem",
              height: "3rem",
              objectFit: "cover",
              borderRadius: "var(--radius-sm)",
              border: "1px solid var(--border)",
              background: "var(--bg-subtle)",
              flex: "none",
            }}
          />
        ) : (
          <span
            aria-hidden
            style={{
              width: "3rem",
              height: "3rem",
              borderRadius: "var(--radius-sm)",
              background: "var(--bg-subtle)",
              color: "var(--text-muted)",
              display: "grid",
              placeItems: "center",
              flex: "none",
            }}
          >
            <StoreIcon size={20} />
          </span>
        )}

        <div style={{ minWidth: 0 }}>
          <p className="mp-metric-label" style={{ margin: 0 }}>
            Sold by
          </p>
          <p className="mp-truncate" style={{ margin: 0, fontWeight: 600 }}>
            <Link href={`/stores/${storeSlug}`} style={{ color: "var(--text)" }}>
              {storeName}
            </Link>
          </p>
          <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-xs)" }}>
            {sellerName}
            {storeRatingCount > 0 ? (
              <>
                {" · "}
                <Star size={12} aria-hidden style={{ color: "#f0a500", verticalAlign: "-1px" }} />
                {storeRating.toFixed(1)} ({formatNumber(storeRatingCount)})
              </>
            ) : (
              " · no store reviews yet"
            )}
          </p>
        </div>

        <Link href={`/stores/${storeSlug}`} className="btn btn-sm btn-outline-secondary ms-auto" style={{ flex: "none" }}>
          View store
        </Link>
      </div>
    </section>
  );
}
