/**
 * The directory of stores: every active storefront, one card each.
 *
 * The header has linked here since it was written, and a marketplace needs a way to find a shop
 * rather than only a way into one you already know about. Sorted by how well rated they are and
 * then by how much they sell, because a directory in no particular order is a directory nobody can
 * find anything in.
 *
 * A server render, for the same reason the storefront is one: the list is what people arrive to
 * see, and it changes when a seller is approved, not when somebody looks at it.
 */

import Link from "next/link";

import { EmptyState, ErrorState } from "@/components/shared/Feedback";
import { Pagination } from "@/components/navigation/Pagination";
import { StoreGrid } from "@/components/stores/StoreGrid";
import { pageMetadata } from "@/lib/seo";
import { serverGetQuietly } from "@/lib/serverApi";
import type { StoreDirectoryEntry } from "@/features/promotions/api/storeDirectoryApi";

export const revalidate = 120;

export const metadata = pageMetadata({
  title: "Stores",
  description: "Every independent store on the marketplace, with what each one sells and how it is rated.",
  path: "/stores",
});

type SearchParams = Promise<Record<string, string | string[] | undefined>>;

export default async function StoresPage({ searchParams }: { searchParams: SearchParams }) {
  const params = await searchParams;
  const page = positiveInt(params.page, 1);
  const search = typeof params.search === "string" ? params.search.trim() : "";

  const query = { page, pageSize: 24, search: search || undefined };
  const stores = await serverGetQuietly<StoreDirectoryPageShape>("/api/stores" + searchString(query));

  return (
    <div className="mp-page section">
      <header className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Stores</h1>
          <p className="mp-page-subtitle">
            {stores
              ? `${stores.totalCount} ${stores.totalCount === 1 ? "store" : "stores"} selling on the marketplace`
              : "Independent sellers on the marketplace"}
          </p>
        </div>

        <form method="get" action="/stores" className="d-flex" style={{ gap: "0.5rem" }} role="search">
          <label htmlFor="store-search" className="visually-hidden">
            Search stores
          </label>
          <input
            id="store-search"
            className="mp-input"
            type="search"
            name="search"
            defaultValue={search}
            placeholder="A store, or a city"
          />
          <button type="submit" className="btn btn-outline-secondary">
            Search
          </button>
        </form>
      </header>

      {stores === null ? (
        <ErrorState message="We could not load the stores. The catalogue is still browsable." />
      ) : stores.items.length === 0 ? (
        <EmptyState
          title={search ? `Nothing matches ${search}` : "No stores yet"}
          body={search ? "Try a shorter search, or browse the catalogue." : "When a seller is approved their store appears here."}
          action={
            search ? (
              <Link href="/stores" className="btn btn-sm btn-primary">
                Clear the search
              </Link>
            ) : (
              <Link href="/products" className="btn btn-sm btn-primary">
                Browse the catalogue
              </Link>
            )
          }
        />
      ) : (
        <>
          <StoreGrid stores={stores.items} />

          {stores.totalPages > 1 ? (
            <Pagination page={stores.page} totalPages={stores.totalPages} query={{ search }} basePath="/stores" />
          ) : null}
        </>
      )}
    </div>
  );
}

type StoreDirectoryPageShape = {
  items: StoreDirectoryEntry[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

function searchString(query: { page: number; pageSize: number; search?: string }): string {
  const params = new URLSearchParams({ page: String(query.page), pageSize: String(query.pageSize) });

  if (query.search) {
    params.set("search", query.search);
  }

  return `?${params.toString()}`;
}

function positiveInt(candidate: string | string[] | undefined, fallback: number): number {
  const value = Array.isArray(candidate) ? candidate[0] : candidate;
  const parsed = Number(value);

  return Number.isInteger(parsed) && parsed > 0 ? parsed : fallback;
}
