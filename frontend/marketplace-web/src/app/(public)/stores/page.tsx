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
import { Star, Store } from "lucide-react";

import { EmptyState, ErrorState } from "@/components/shared/Feedback";
import { Pagination } from "@/components/navigation/Pagination";
import { formatNumber } from "@/lib/format";
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
          <div className="row g-3">
            {stores.items.map(store => (
              <div key={store.storeId} className="col-12 col-sm-6 col-lg-4 col-xl-3">
                <StoreCard store={store} />
              </div>
            ))}
          </div>

          {stores.totalPages > 1 ? (
            <Pagination page={stores.page} totalPages={stores.totalPages} query={{ search }} basePath="/stores" />
          ) : null}
        </>
      )}
    </div>
  );
}

function StoreCard({ store }: { store: StoreDirectoryEntry }) {
  return (
    <article className="mp-card h-100 d-flex flex-column overflow-hidden">
      <div
        style={{
          height: "5.5rem",
          background: store.bannerUrl
            ? `center/cover url(${store.bannerUrl})`
            : "linear-gradient(135deg, var(--brand-500), var(--brand-700))",
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
              <Store size={20} />
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
