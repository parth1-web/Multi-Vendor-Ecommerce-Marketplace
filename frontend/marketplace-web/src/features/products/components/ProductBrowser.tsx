/**
 * The interactive half of a listing: filters, results and pagination in one island.
 *
 * The page around it is a server component that only parses the URL, so the shell, the title
 * and the metadata are rendered on the server while everything that reacts to a filter change
 * lives here. Filters are written to the URL rather than to component state, which is what
 * makes a filtered view shareable, bookmarkable and reachable with the back button.
 */

import Link from "next/link";

import { EmptyState, ErrorState } from "@/components/shared/Feedback";
import { useProducts } from "@/features/products/api/useProducts";
import { listingHref } from "@/features/products/api/readProductQuery";
import { ProductCard } from "@/features/products/components/ProductCard";
import { ProductFilters, SortSelect } from "@/features/products/components/ProductFilters";
import { cx } from "@/lib/format";
import type { ProductQuery } from "@/types/product";

interface ProductBrowserProps {
  query: ProductQuery;

  /**
   * Where this listing lives, so that pagination and "clear" go back to this page rather than
   * to the global catalogue. A category page that paginates into /products has quietly
   * stopped being a category page.
   */
  basePath?: string;
}

export function ProductBrowser({ query, basePath = "/products" }: ProductBrowserProps) {
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

        {/* The previous page stays on screen while the next one loads: blanking the grid on
            every filter change reads as "no results" when it is really "loading". */}
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
                <Link href={basePath} className="btn btn-sm btn-primary">
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

              <Pagination page={data.page} totalPages={data.totalPages} query={query} basePath={basePath} />
            </>
          )}
        </div>
      </div>
    </div>
  );
}

function Pagination({ page, totalPages, query, basePath }: { page: number; totalPages: number; query: ProductQuery; basePath: string }) {
  if (totalPages <= 1) {
    return null;
  }

  return (
    <nav aria-label="Pagination" className="d-flex justify-content-between align-items-center mt-4">
      <Link
        className={cx("btn btn-sm btn-outline-secondary", page <= 1 && "disabled")}
        href={listingHref(basePath, query, Math.max(1, page - 1))}
        aria-disabled={page <= 1}
        scroll={false}
      >
        Previous
      </Link>
      <span style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
        Page {page} of {totalPages}
      </span>
      <Link
        className={cx("btn btn-sm btn-outline-secondary", page >= totalPages && "disabled")}
        href={listingHref(basePath, query, Math.min(totalPages, page + 1))}
        aria-disabled={page >= totalPages}
        scroll={false}
      >
        Next
      </Link>
    </nav>
  );
}

export function ProductGridSkeleton({ count = 8 }: { count?: number }) {
  return (
    <div className="row g-3" aria-hidden>
      {Array.from({ length: count }, (_, index) => (
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
