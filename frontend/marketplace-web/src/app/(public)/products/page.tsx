/**
 * The product listing.
 *
 * The page itself is a server component: it parses the URL, renders the title and hands the
 * query to one client island. Nothing here fetches, because the results belong to the island
 * that can change them without a navigation.
 */

import type { Metadata } from "next";
import { Suspense } from "react";

import { ProductBrowser } from "@/features/products/components/ProductBrowser";
import { PAGE_SIZE } from "@/lib/constants";
import type { ProductQuery, ProductSort } from "@/types/product";

export const metadata: Metadata = {
  title: "All products",
  description: "Browse everything on sale across the marketplace's independent stores.",
};

/** Next 16 hands search params to the page as a promise. */
type SearchParams = Promise<Record<string, string | string[] | undefined>>;

export default async function ProductsPage({ searchParams }: { searchParams: SearchParams }) {
  const query = readQuery(await searchParams);

  return (
    <div className="mp-page" style={{ paddingBlock: "var(--space-5)" }}>
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">All products</h1>
          <p className="mp-page-subtitle">
            {query.search ? `Results for “${query.search}”` : "Everything currently listed by our sellers."}
          </p>
        </div>
      </div>

      <Suspense fallback={<ProductBrowserSkeleton />}>
        <ProductBrowser query={query} />
      </Suspense>
    </div>
  );
}

function ProductBrowserSkeleton() {
  return (
    <div className="row g-4">
      <div className="col-12 col-lg-3">
        <div className="mp-skeleton" style={{ height: "20rem", borderRadius: "var(--radius)" }} />
      </div>
      <div className="col-12 col-lg-9">
        <div className="row g-3">
          {Array.from({ length: 8 }, (_, index) => (
            <div key={index} className="col-6 col-md-4 col-xl-3">
              <div className="mp-skeleton" style={{ height: "16rem", borderRadius: "var(--radius)" }} />
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

/** Turns a URL into a typed query, ignoring anything unparseable rather than sending NaN. */
export function readQuery(params: Record<string, string | string[] | undefined>): ProductQuery {
  const value = (key: string): string | undefined => {
    const raw = params[key];
    return Array.isArray(raw) ? raw[0] : raw;
  };

  const number = (key: string): number | undefined => {
    const raw = value(key);
    const parsed = raw === undefined ? Number.NaN : Number(raw);

    return Number.isFinite(parsed) ? parsed : undefined;
  };

  const flag = (key: string): boolean | undefined => {
    const raw = value(key);
    return raw === undefined ? undefined : raw === "true";
  };

  return {
    page: number("page") ?? 1,
    pageSize: number("pageSize") ?? PAGE_SIZE.default,
    search: value("search"),
    categorySlug: value("categorySlug"),
    minPrice: number("minPrice"),
    maxPrice: number("maxPrice"),
    minRating: number("minRating"),
    inStock: flag("inStock"),
    onSale: flag("onSale"),
    sort: value("sort") as ProductSort | undefined,
  };
}
