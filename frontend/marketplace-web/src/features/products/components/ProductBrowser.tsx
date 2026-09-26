"use client";

/**
 * The interactive half of the listing: filters, results and pagination in one island.
 *
 * The page around it is a server component that only parses the URL, so the shell, the title
 * and the metadata are rendered on the server while everything that reacts to a filter change
 * lives here. Filters are written to the URL rather than to component state, which is what
 * makes a filtered view shareable, bookmarkable and reachable with the back button.
 */

import Link from "next/link";

import { useProducts } from "@/features/products/api/useProducts";
import { ProductCard } from "@/features/products/components/ProductCard";
import { ProductFilters, SortSelect } from "@/features/products/components/ProductFilters";
import { EmptyState, ErrorState } from "@/components/shared/Feedback";
import { cx } from "@/lib/format";
import type { ProductQuery } from "@/types/product";

export function ProductBrowser({ query }: { query: ProductQuery }) {
  const { data, isPending, isError, error, isPlaceholderData } = useProducts(query);

  return (
    <div className="row g-4">
      <div className="col-12 col-lg-3">
        <div className="mp-card" style={{ padding: "var(--space-4)" }}>
          <ProductFilters totalCount={data?.totalCount ?? 0} />
        </div>
      </div>

      <div className="col-12 col-lg-9">
        <div className="mp-spread mb-3">
          <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            {isPending ? "Loading products…" : `${data?.totalCount ?? 0} products`}
          </p>
          <SortSelect />
        </div>

        <div style={{ opacity: isPlaceholderData ? 0.6 : 1, transition: "opacity 160ms" }}>
          {isPending ? (
            <ProductGridSkeleton />
          ) : isError ? (
            <ErrorState message={error.message || "We could not load these products."} />
          ) : !data || data.items.length === 0 ? (
            <EmptyState
              title="No products match those filters"
              body="Try widening the price range, or clear a filter or two."
              action={
                <Link href="/products" className="btn btn-sm btn-primary">
                  Clear all filters
                </Link>
              }
            />
          ) : (
            <>
              <div className="row g-3">
                {data.items.map((product) => (
                  <div key={product.id} className="col-6 col-md-4 col-xl-3">
                    <ProductCard product={product} />
                  </div>
                ))}
              </div>

              <Pagination page={data.page} totalPages={data.totalPages} query={query} />
            </>
          )}
        </div>
      </div>
    </div>
  );
}

function Pagination({ page, totalPages, query }: { page: number; totalPages: number; query: ProductQuery }) {
  if (totalPages <= 1) {
    return null;
  }

  const href = (target: number) => {
    const search = new URLSearchParams();

    for (const [key, value] of Object.entries(query)) {
      if (value !== undefined && key !== "page") {
        search.set(key, String(value));
      }
    }

    search.set("page", String(target));

    return `/products?${search.toString()}`;
  };

  return (
    <nav aria-label="Pagination" className="d-flex justify-content-between align-items-center mt-4">
      <a className={cx("btn btn-sm btn-outline-secondary", page <= 1 && "disabled")} href={href(Math.max(1, page - 1))} aria-disabled={page <= 1}>
        Previous
      </a>
      <span style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
        Page {page} of {totalPages}
      </span>
      <a
        className={cx("btn btn-sm btn-outline-secondary", page >= totalPages && "disabled")}
        href={href(Math.min(totalPages, page + 1))}
        aria-disabled={page >= totalPages}
      >
        Next
      </a>
    </nav>
  );
}

function ProductGridSkeleton() {
  return (
    <div className="row g-3" aria-hidden>
      {Array.from({ length: 8 }, (_, index) => (
        <div key={index} className="col-6 col-md-4 col-xl-3">
          <div className="mp-card" style={{ padding: "var(--space-3)" }}>
            <div className="mp-skeleton" style={{ aspectRatio: "1 / 1", borderRadius: "var(--radius-sm)" }} />
            <div className="mp-skeleton mt-2" style={{ height: "0.75rem", width: "70%" }} />
            <div className="mp-skeleton mt-2" style={{ height: "0.75rem", width: "40%" }} />
          </div>
        </div>
      ))}
    </div>
  );
}
