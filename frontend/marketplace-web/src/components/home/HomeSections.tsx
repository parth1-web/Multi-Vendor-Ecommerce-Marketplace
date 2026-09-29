import Link from "next/link";

import { CategoryTile } from "@/components/categories/CategoryTile";
import { ProductPhoto } from "@/components/products/ProductPhoto";
import { PromoBanner } from "@/components/shared/PromoBanner";
import { SectionHeader } from "@/components/shared/SectionHeader";
import { StoreGrid } from "@/components/stores/StoreGrid";
import { ProductCard } from "@/features/products/components/ProductCard";
import type { StoreDirectoryPage } from "@/features/promotions/api/storeDirectoryApi";
import type { PublicCoupon } from "@/features/promotions/api/publicCouponApi";
import { formatCurrency } from "@/lib/format";
import { selectStorefrontCategories } from "@/lib/categories";
import { serverGet, serverGetQuietly } from "@/lib/serverApi";
import type { Category } from "@/types/product";
import type { ProductSummary } from "@/types/product";

interface ProductRailData {
  title: string;
  subtitle: string;
  href: string;
  products: ProductSummary[];
}

/**
 * Homepage sections as independent server boundaries.
 *
 * Each section fetches only its own slice and fails to an omitted section rather than a broken
 * homepage. Identical server requests are deduplicated by Next, so shared reads do not become
 * duplicate backend calls.
 */
export async function HeroShowcaseSection() {
  const products = await serverGetQuietly<ProductSummary[]>("/api/products/new-arrivals?take=6");
  const showcase = showcaseProducts([products ?? []]).slice(0, 3);

  if (showcase.length === 0) {
    return null;
  }

  const [main, ...rest] = showcase;

  return (
    <div className="mp-hero-showcase">
      <Link
        href={`/products/${main.slug}`}
        className="mp-hero-main"
        aria-label={`${main.name}, sold by ${main.storeName}, ${formatCurrency(main.basePrice)}`}
      >
        <ProductPhoto
          src={main.primaryImageUrl}
          alt={main.name}
          width={960}
          height={720}
          aspectRatio="4 / 3"
          eager
          fetchPriority="high"
        />
        <span className="mp-hero-caption" aria-hidden>
          <span className="mp-truncate">{main.name}</span>
          <span>{formatCurrency(main.basePrice)}</span>
        </span>
      </Link>

      {rest.length > 0 ? (
        <div className="mp-hero-thumbs">
          {rest.map((product) => (
            <Link
              key={product.id}
              href={`/products/${product.slug}`}
              className="mp-hero-thumb"
              aria-label={`${product.name}, sold by ${product.storeName}, ${formatCurrency(product.basePrice)}`}
            >
              <ProductPhoto src={product.primaryImageUrl} alt={product.name} width={480} height={480} />
            </Link>
          ))}
        </div>
      ) : null}
    </div>
  );
}

export async function CategoryRailSection() {
  const tree = await serverGetQuietly<Category[]>("/api/categories", 300);
  const categories = selectStorefrontCategories(tree, 12);

  if (categories.length === 0) {
    return null;
  }

  return (
    <section aria-labelledby="shop-by-category">
      <SectionHeader
        id="shop-by-category"
        title="Shop by category"
        description="Browse departments with live listings."
        actionHref="/categories"
      />
      <div className="row g-3">
        {categories.map((category) => (
          <div key={category.id} className="col-6 col-md-4 col-lg-3 col-xl-2">
            <CategoryTile category={category} />
          </div>
        ))}
      </div>
    </section>
  );
}

export async function TrendingRailSection() {
  const rail = await readProductRail(
    "/api/products/best-sellers?take=6",
    "Trending now",
    "Most purchased across independent stores",
    "/products?sort=Popular",
  );

  if (rail.products.length === 0) {
    return null;
  }

  return <ProductRail {...rail} columns={6} />;
}

export async function DealsSpotlightSection() {
  const [coupons, deals] = await Promise.all([
    serverGetQuietly<PublicCoupon[]>("/api/coupons/public", 300),
    readProductRail(
      "/api/products?onSale=true&sort=Discount&pageSize=4",
      "Discounted now",
      "Marked down by their store",
      "/products?onSale=true&sort=Discount",
    ),
  ]);
  const coupon = coupons?.find((candidate) => candidate.code && candidate.discountLabel) ?? null;
  const spotlight = deals.products.find((product) => product.primaryImageUrl) ?? null;

  if (!coupon && deals.products.length === 0) {
    return null;
  }

  return (
    <div className="mp-stack" style={{ gap: "var(--space-5)" }}>
      {coupon ? (
        <PromoBanner
          title={`Save with ${coupon.code}`}
          description={coupon.description ?? coupon.discountLabel}
          couponCode={coupon.code}
          couponLabel={coupon.discountLabel}
          endsAt={coupon.endsAt}
          actionHref="/deals"
          product={spotlight}
        />
      ) : null}

      {deals.products.length > 0 ? <ProductRail {...deals} columns={4} /> : null}
    </div>
  );
}

export async function StoreRailSection() {
  // The directory endpoint already returns active storefronts best rated first, so this rail can
  // trust the order instead of inventing its own popularity score.
  const page = await serverGetQuietly<StoreDirectoryPage>("/api/stores?page=1&pageSize=4", 300);
  const stores = page?.items ?? [];

  if (stores.length === 0) {
    return null;
  }

  return (
    <section aria-labelledby="highest-rated-stores">
      <SectionHeader
        id="highest-rated-stores"
        title="Highest-rated stores"
        description="Active storefronts, best rated first."
        actionHref="/stores"
      />
      <StoreGrid stores={stores} />
    </section>
  );
}

export async function NewArrivalsSection() {
  const rail = await readProductRail(
    "/api/products/new-arrivals?take=6",
    "New arrivals",
    "Just listed by independent sellers",
    "/products?sort=Newest",
  );

  if (rail.products.length === 0) {
    return null;
  }

  return <ProductRail {...rail} columns={6} />;
}

async function readProductRail(path: string, title: string, subtitle: string, href: string): Promise<ProductRailData> {
  try {
    // Home rails are plain lists, while the deals rail is a filtered page. A rail that cannot
    // load is omitted rather than allowed to fail the page.
    const body = await serverGet<ProductSummary[] | { items: ProductSummary[] }>(path);
    const products = Array.isArray(body) ? body : body.items;

    return { title, subtitle, href, products };
  } catch {
    return { title, subtitle, href, products: [] };
  }
}

function showcaseProducts(rails: ProductSummary[][]): ProductSummary[] {
  const seen = new Set<string>();
  const spotlight: ProductSummary[] = [];

  for (const products of rails) {
    for (const product of products) {
      if (!product.primaryImageUrl || seen.has(product.id)) {
        continue;
      }

      seen.add(product.id);
      spotlight.push(product);
    }
  }

  return spotlight;
}

function ProductRail({ title, subtitle, href, products, columns = 6 }: ProductRailData & { columns?: 4 | 6 }) {
  const headingId = `rail-${title.replace(/\s+/g, "-").toLowerCase()}`;
  const gridItemClass = columns === 4 ? "col-6 col-md-4 col-lg-3" : "col-6 col-md-4 col-lg-3 col-xl-2";

  return (
    <section aria-labelledby={headingId}>
      <SectionHeader id={headingId} title={title} description={subtitle} actionHref={href} />

      <div className="row g-3">
        {products.map((product) => (
          <div key={product.id} className={gridItemClass}>
            <ProductCard product={product} />
          </div>
        ))}
      </div>
    </section>
  );
}

export function HeroMediaSkeleton() {
  return (
    <div className="mp-hero-showcase" aria-hidden>
      <div className="mp-skeleton" style={{ aspectRatio: "4 / 3", borderRadius: "var(--radius)" }} />
      <div className="mp-hero-thumbs">
        <div className="mp-skeleton" style={{ aspectRatio: "1 / 1", borderRadius: "var(--radius)" }} />
        <div className="mp-skeleton" style={{ aspectRatio: "1 / 1", borderRadius: "var(--radius)" }} />
      </div>
    </div>
  );
}

export function CategoryRailSkeleton() {
  return (
    <section aria-hidden>
      <div className="mp-skeleton" style={{ height: "1.5rem", width: "12rem", marginBottom: "var(--space-4)" }} />
      <div className="row g-3">
        {Array.from({ length: 6 }, (_, index) => (
          <div key={index} className="col-6 col-md-4 col-lg-3 col-xl-2">
            <div className="mp-skeleton" style={{ height: "5.5rem", borderRadius: "var(--radius)" }} />
          </div>
        ))}
      </div>
    </section>
  );
}

export function ProductRailSkeleton({ count = 6 }: { count?: number }) {
  return (
    <section aria-hidden>
      <div className="mp-skeleton" style={{ height: "1.5rem", width: "12rem", marginBottom: "var(--space-4)" }} />
      <div className="row g-3">
        {Array.from({ length: count }, (_, index) => (
          <div key={index} className="col-6 col-md-4 col-lg-3 col-xl-2">
            <div className="mp-card" style={{ padding: "var(--space-3)" }}>
              <div className="mp-skeleton" style={{ aspectRatio: "1 / 1", borderRadius: "var(--radius-sm)" }} />
              <div className="mp-skeleton mt-2" style={{ height: "0.75rem", width: "70%" }} />
              <div className="mp-skeleton mt-2" style={{ height: "0.75rem", width: "40%" }} />
            </div>
          </div>
        ))}
      </div>
    </section>
  );
}

export function DealsSpotlightSkeleton() {
  return (
    <div className="mp-stack" style={{ gap: "var(--space-5)" }} aria-hidden>
      <div className="mp-skeleton" style={{ height: "12rem", borderRadius: "var(--radius-lg)" }} />
      <ProductRailSkeleton count={4} />
    </div>
  );
}

export function StoreRailSkeleton() {
  return (
    <section aria-hidden>
      <div className="mp-skeleton" style={{ height: "1.5rem", width: "12rem", marginBottom: "var(--space-4)" }} />
      <div className="mp-store-grid">
        {Array.from({ length: 4 }, (_, index) => (
          <div key={index} className="mp-skeleton" style={{ height: "12rem", borderRadius: "var(--radius)" }} />
        ))}
      </div>
    </section>
  );
}
