/**
 * The product listing.
 *
 * The page itself is a server component: it parses the URL, renders the title and hands the
 * query to one client island. Nothing here fetches, because the results belong to the island
 * that can change them without a navigation.
 */

import type { Metadata } from "next";
import { Suspense } from "react";

import { ListingHeader } from "@/components/products/ListingHeader";
import { readProductQuery } from "@/features/products/api/readProductQuery";
import { ListingSkeleton } from "@/features/products/components/ListingSkeleton";
import { ProductBrowser } from "@/features/products/components/ProductBrowser";
import { pageMetadata } from "@/lib/seo";

export const metadata: Metadata = pageMetadata({
  title: "All products",
  description: "Browse everything on sale across the marketplace's independent stores.",
  path: "/products",
});

/** Next 16 hands search params to the page as a promise. */
type SearchParams = Promise<Record<string, string | string[] | undefined>>;

export default async function ProductsPage({ searchParams }: { searchParams: SearchParams }) {
  const query = readProductQuery(await searchParams);

  return (
    <div className="mp-page" style={{ paddingBlock: "var(--space-5)" }}>
      <ListingHeader
        breadcrumbs={[{ name: "Home", href: "/" }, { name: "All products" }]}
        title="All products"
        subtitle={
          query.search ? `Results for “${query.search}”` : "Everything currently listed by our sellers."
        }
      />

      <Suspense fallback={<ListingSkeleton />}>
        <ProductBrowser query={query} />
      </Suspense>
    </div>
  );
}
