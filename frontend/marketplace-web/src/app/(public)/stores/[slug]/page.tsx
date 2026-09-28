/**
 * A storefront: who this seller is, what they promise, and what they sell.
 *
 * The profile and its policies are fetched on the server, because a shopper deciding whether to
 * buy from a store is reading them, not clicking them. The products are the same client island
 * the catalogue uses, scoped to this store.
 */

import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { Mail, Phone, ShieldCheck, Truck } from "lucide-react";

import { RatingStars } from "@/components/shared/RatingStars";
import { readProductQuery } from "@/features/products/api/readProductQuery";
import { ListingSkeleton } from "@/features/products/components/ListingSkeleton";
import { ProductBrowser } from "@/features/products/components/ProductBrowser";
import { ApiError, serverGet } from "@/lib/serverApi";
import { formatDate } from "@/lib/format";
import { breadcrumbJsonLd, jsonLdScript, pageMetadata } from "@/lib/seo";
import type { StoreProfile } from "@/types/store";

export const dynamic = "force-dynamic";

type Params = Promise<{ slug: string }>;
type SearchParams = Promise<Record<string, string | string[] | undefined>>;

const STORE_TTL = 300;

export async function generateMetadata({ params }: { params: Params }): Promise<Metadata> {
  const { slug } = await params;

  try {
    const store = await load(slug);

    return pageMetadata({
      title: store.name,
      description: store.description || `Browse everything ${store.name} sells.`,
      path: `/stores/${store.slug}`,
      image: store.bannerUrl ?? store.logoUrl,
    });
  } catch {
    return { title: "Store not found", robots: { index: false, follow: true } };
  }

}

export default async function StorePage({ params, searchParams }: { params: Params; searchParams: SearchParams }) {
  const { slug } = await params;
  const requested = readProductQuery(await searchParams);

  let store: StoreProfile;

  try {
    store = await load(slug);
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) {
      notFound();
    }

    throw error;
  }

  const query = { ...requested, sellerSlug: store.slug };

  return (
    <div style={{ paddingBlock: "var(--space-5)" }}>
      <header className="mp-container" style={{ marginBottom: "var(--space-5)" }}>
        <script
          type="application/ld+json"
          dangerouslySetInnerHTML={{
            __html: jsonLdScript(breadcrumbJsonLd([
              { name: "Home", path: "/" },
              { name: store.name, path: `/stores/${store.slug}` },

            ])),
          }}
        />
        {store.bannerUrl ? (
          // eslint-disable-next-line @next/next/no-img-element
          <img
            src={store.bannerUrl}
            alt=""
            width={1200}
            height={240}
            style={{ width: "100%", height: "9rem", objectFit: "cover", borderRadius: "var(--radius)" }}
          />
        ) : null}

        <div className="mp-card" style={{ padding: "var(--space-4)", marginTop: store.bannerUrl ? "-2.5rem" : 0, position: "relative" }}>
          <div className="d-flex align-items-center flex-wrap" style={{ gap: "var(--space-4)" }}>
            {store.logoUrl ? (
              // eslint-disable-next-line @next/next/no-img-element
              <img
                src={store.logoUrl}
                alt={store.name}
                width={72}
                height={72}
                style={{ width: "4.5rem", height: "4.5rem", borderRadius: "50%", objectFit: "cover", border: "2px solid var(--bg-surface)" }}
              />
            ) : (
              <div
                aria-hidden
                style={{
                  width: "4.5rem",
                  height: "4.5rem",
                  borderRadius: "50%",
                  backgroundColor: "var(--violet-bg)",
                  color: "var(--violet)",
                  display: "grid",
                  placeItems: "center",
                  fontWeight: 700,
                }}
              >
                {store.name.slice(0, 1).toUpperCase()}
              </div>
            )}

            <div style={{ flex: 1, minWidth: "12rem" }}>
              <h1 className="mp-page-title" style={{ fontSize: "var(--fs-h1)" }}>
                {store.name}
              </h1>
              <div className="d-flex align-items-center flex-wrap mt-1" style={{ gap: "var(--space-3)" }}>
                <RatingStars rating={store.ratingAverage} count={store.ratingCount} />
                <span style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
                  {store.productCount} {store.productCount === 1 ? "product" : "products"}
                </span>
                <span style={{ color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>Selling since {formatDate(store.createdAt)}</span>
              </div>
            </div>
          </div>

          {store.description ? <p style={{ margin: "var(--space-3) 0 0", color: "var(--text-muted)" }}>{store.description}</p> : null}

          <dl className="row g-3 mt-2 mb-0" style={{ fontSize: "var(--fs-sm)" }}>
            {store.shippingPolicy ? (
              <div className="col-12 col-md-6">
                <dt className="mp-metric-label d-flex align-items-center" style={{ gap: "0.35rem" }}>
                  <Truck size={14} aria-hidden /> Shipping
                </dt>
                <dd className="mb-0" style={{ color: "var(--text-muted)" }}>
                  {store.shippingPolicy}
                </dd>
              </div>
            ) : null}

            {store.returnPolicy ? (
              <div className="col-12 col-md-6">
                <dt className="mp-metric-label d-flex align-items-center" style={{ gap: "0.35rem" }}>
                  <ShieldCheck size={14} aria-hidden /> Returns
                </dt>
                <dd className="mb-0" style={{ color: "var(--text-muted)" }}>
                  {store.returnPolicy}
                </dd>
              </div>
            ) : null}

            {store.supportEmail ? (
              <div className="col-12 col-md-6">
                <dt className="mp-metric-label d-flex align-items-center" style={{ gap: "0.35rem" }}>
                  <Mail size={14} aria-hidden /> Email
                </dt>
                <dd className="mb-0">
                  <a href={`mailto:${store.supportEmail}`}>{store.supportEmail}</a>
                </dd>
              </div>
            ) : null}

            {store.supportPhone ? (
              <div className="col-12 col-md-6">
                <dt className="mp-metric-label d-flex align-items-center" style={{ gap: "0.35rem" }}>
                  <Phone size={14} aria-hidden /> Phone
                </dt>
                <dd className="mb-0">
                  <a href={`tel:${store.supportPhone}`}>{store.supportPhone}</a>
                </dd>
              </div>
            ) : null}
          </dl>
        </div>
      </header>

      <div className="mp-container">
        <Suspense fallback={<ListingSkeleton />}>
          <ProductBrowser query={query} basePath={`/stores/${store.slug}`} />
        </Suspense>
      </div>

      <div className="mp-container" style={{ marginTop: "var(--space-6)" }}>
        <Link href="/products" style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
          Browse every seller
        </Link>
      </div>
    </div>
  );
}

async function load(slug: string): Promise<StoreProfile> {
  return serverGet<StoreProfile>(`/api/stores/${encodeURIComponent(slug)}`, STORE_TTL);
}
