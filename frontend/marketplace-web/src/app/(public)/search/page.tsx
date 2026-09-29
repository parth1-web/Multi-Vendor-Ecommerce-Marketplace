import Link from "next/link";
import { Suspense } from "react";

import { EmptyState } from "@/components/shared/Feedback";
import { readProductQuery } from "@/features/products/api/readProductQuery";
import { ListingSkeleton } from "@/features/products/components/ListingSkeleton";
import { ProductBrowser } from "@/features/products/components/ProductBrowser";

/**
 * Search results.
 *
 * The header points here rather than overloading the catalogue route. It parses the same query
 * language as /products, but keeps `q` in the URL so a search remains shareable and the browser
 * back button behaves the way a shopper expects.
 */

type SearchParams = Promise<Record<string, string | string[] | undefined>>;

export async function generateMetadata({ searchParams }: { searchParams: SearchParams }) {
  const query = searchQuery(await searchParams);

  return {
    title: query ? `Search: ${query}` : "Search",
    description: query
      ? `Products matching “${query}” across the marketplace's independent stores.`
      : "Search every product on the marketplace.",
    robots: { index: false, follow: true },
  };
}

export default async function SearchPage({ searchParams }: { searchParams: SearchParams }) {
  const params = await searchParams;
  const searched = searchQuery(params);
  const query = { ...readProductQuery(params), search: searched };

  if (!searched) {
    return (
      <div className="mp-page" style={{ paddingBlock: "var(--space-5)" }}>
        <div className="mp-page-header">
          <div>
            <h1 className="mp-page-title">Search</h1>
            <p className="mp-page-subtitle">Type a product, brand, or store in the search box above.</p>
          </div>
        </div>

        <EmptyState
          title="What are you looking for?"
          body="Search looks across product names, brands, and seller catalogues."
          action={
            <Link href="/products" className="btn btn-sm btn-primary">
              Browse all products
            </Link>
          }
        />
      </div>
    );
  }

  return (
    <div className="mp-page" style={{ paddingBlock: "var(--space-5)" }}>
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Results for “{searched}”</h1>
          <p className="mp-page-subtitle">Matching products across independent stores.</p>
        </div>
      </div>

      <Suspense fallback={<ListingSkeleton />}>
        <ProductBrowser query={query} basePath="/search" paginationQuery={{ ...query, search: undefined, q: searched }} />
      </Suspense>
    </div>
  );
}

function searchQuery(params: Record<string, string | string[] | undefined>): string | undefined {
  const raw = params.q;
  const value = Array.isArray(raw) ? raw[0] : raw;
  const trimmed = value?.trim();

  return trimmed || undefined;
}
