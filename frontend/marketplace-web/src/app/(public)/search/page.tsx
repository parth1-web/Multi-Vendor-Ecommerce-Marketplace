import Link from "next/link";
import { Suspense } from "react";

import { ListingHeader } from "@/components/products/ListingHeader";
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
        <ListingHeader
          breadcrumbs={[{ name: "Home", href: "/" }, { name: "Search" }]}
          title="Search"
          subtitle="Type a product name, description, or SKU in the search box above."
        />

        <EmptyState
          title="What are you looking for?"
          body="Search looks across product names, descriptions, and variant SKUs."
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
      <ListingHeader
        breadcrumbs={[{ name: "Home", href: "/" }, { name: "Search" }]}
        title={`Results for “${searched}”`}
        subtitle="Matching products across independent stores."
      />

      <Suspense fallback={<ListingSkeleton />}>
        <ProductBrowser
          query={query}
          basePath="/search"
          paginationQuery={{ ...query, search: undefined, q: searched }}
          searchParamName="q"
        />
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
