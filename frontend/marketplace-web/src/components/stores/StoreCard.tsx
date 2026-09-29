import Link from "next/link";
import { Star, Store as StoreIcon } from "lucide-react";

import { formatNumber } from "@/lib/format";
import type { StoreDirectoryEntry } from "@/types/store";

/**
 * One store as it appears in the directory.
 *
 * A store card is identity first: the logo or banner, then the name and real catalogue signals.
 * There is deliberately no follow control here because following a store is not a backend
 * capability; a disabled heart would promise functionality that does not exist.
 */
export function StoreCard({ store }: { store: StoreDirectoryEntry }) {
  return (
    <article className="mp-card mp-card-hover h-100 d-flex flex-column overflow-hidden">
      <div
        style={{
          height: "5.5rem",
          background: store.bannerUrl
            ? `center/cover url(${store.bannerUrl})`
            : "linear-gradient(135deg, var(--brand-700), var(--brand-900))",
          flex: "none",
        }}
      />

      <div className="p-3 d-flex flex-column flex-grow-1">
        <div className="d-flex align-items-start" style={{ marginTop: "calc(-2.5rem)", gap: "var(--space-3)" }}>
          {store.logoUrl ? (
            // eslint-disable-next-line @next/next/no-img-element
            <img
              src={store.logoUrl}
              alt=""
              width={56}
              height={56}
              loading="lazy"
              decoding="async"
              style={{
                width: "3.5rem",
                height: "3.5rem",
                objectFit: "cover",
                borderRadius: "var(--radius)",
                border: "2px solid var(--bg-surface)",
                background: "var(--bg-surface)",
                flex: "none",
              }}
            />
          ) : (
            <span
              aria-hidden
              style={{
                width: "3.5rem",
                height: "3.5rem",
                borderRadius: "var(--radius)",
                border: "2px solid var(--bg-surface)",
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
        </div>

        <h2 style={{ fontSize: "var(--fs-h4)", margin: "var(--space-3) 0 0" }}>
          <Link href={`/stores/${store.slug}`} style={{ color: "var(--text)" }}>
            {store.name}
          </Link>
        </h2>

        <p style={{ margin: "0.2rem 0 0", color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
          {store.productCount} {store.productCount === 1 ? "product" : "products"}
          {store.ratingCount > 0 ? (
            <>
              {" · "}
              <Star size={12} aria-hidden style={{ color: "#f0a500", verticalAlign: "-1px" }} />
              {store.ratingAverage.toFixed(1)} ({formatNumber(store.ratingCount)})
            </>
          ) : (
            " · no reviews yet"
          )}
        </p>

        {store.description ? (
          <p
            style={{
              margin: "var(--space-2) 0 0",
              color: "var(--text-subtle)",
              fontSize: "var(--fs-xs)",
              display: "-webkit-box",
              WebkitLineClamp: 3,
              WebkitBoxOrient: "vertical",
              overflow: "hidden",
            }}
          >
            {store.description}
          </p>
        ) : null}

        <div className="mt-auto pt-3">
          <Link href={`/stores/${store.slug}`} className="btn btn-sm btn-outline-secondary w-100">
            Visit the store
          </Link>
        </div>
      </div>
    </article>
  );
}
