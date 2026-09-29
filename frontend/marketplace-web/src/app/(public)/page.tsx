import Link from "next/link";
import { ArrowRight } from "lucide-react";

import { PageHero } from "@/components/shared/PageHero";
import { ProductPhoto } from "@/components/products/ProductPhoto";
import { SectionHeader } from "@/components/shared/SectionHeader";
import { StoreGrid } from "@/components/stores/StoreGrid";
import { TrustStrip } from "@/components/shared/TrustStrip";
import { ProductCard } from "@/features/products/components/ProductCard";
import type { StoreDirectoryPage } from "@/features/promotions/api/storeDirectoryApi";
import { formatCurrency } from "@/lib/format";
import { selectStorefrontCategories } from "@/lib/categories";
import { serverGet, serverGetQuietly } from "@/lib/serverApi";
import type { Category } from "@/types/product";
import type { ProductSummary } from "@/types/product";

/**
 * The homepage, rendered on the server.
 *
 * Every section fetches its own slice and each one is allowed to come back empty: a rail that
 * fails is dropped rather than allowed to take the page down with it. The cards are the same
 * ProductCard and StoreCard used elsewhere, so a shopper moving from here into the catalogue or
 * directory is not looking at a different product or store.
 */
export default async function HomePage() {
  const [deals, newArrivals, bestSellers, categories, stores] = await Promise.all([
    rail("/api/products?onSale=true&sort=Discount&pageSize=8", "Deals", "Marked down by their store", "/products?onSale=true&sort=Discount"),
    rail("/api/products/new-arrivals?take=8", "New arrivals", "Just listed by independent sellers", "/products?sort=Newest"),
    rail("/api/products/best-sellers?take=8", "Best sellers", "What other shoppers are buying", "/products?sort=Popular"),
    categoryTiles(),
    popularStores(),
  ]);
  const showcase = showcaseProducts([deals.products, newArrivals.products, bestSellers.products]);

  return (
    <div className="mp-page" style={{ paddingBlock: "var(--space-6)" }}>
      <PageHero
        id="marketplace-hero"
        eyebrow="Multi-vendor marketplace"
        title="Everything you need. From sellers you trust."
        description="Discover quality products from independent sellers. Compare listings, keep one basket, and check out once."
        primaryAction={{ href: "/products", label: "Shop Now" }}
        secondaryAction={{ href: "/stores", label: "Explore Stores" }}
        media={showcase.length > 0 ? <HeroShowcase products={showcase} /> : undefined}
      />

      <TrustStrip />

      {categories.length > 0 && (
        <section aria-labelledby="shop-by-category" style={{ marginTop: "var(--space-7)" }}>
          <SectionHeader
            id="shop-by-category"
            title="Shop by category"
            description="Browse departments with live listings."
            actionHref="/categories"
          />
          <div className="row g-3" style={{ marginTop: "var(--space-4)" }}>
            {categories.map((category) => (
              <div key={category.id} className="col-6 col-md-4 col-lg-3 col-xl-2">
                <Link
                  href={`/categories/${category.slug}`}
                  className="mp-card mp-card-hover d-block h-100"
                  style={{ padding: "var(--space-4)", color: "inherit", textDecoration: "none" }}
                >
                  <span style={{ display: "block", fontWeight: 500 }}>{category.name}</span>
                  <small style={{ color: "var(--text-muted)" }}>
                    {category.productCount} {category.productCount === 1 ? "product" : "products"}
                  </small>
                </Link>
              </div>
            ))}
          </div>
        </section>
      )}

      <div className="mp-stack" style={{ gap: "var(--space-7)", marginTop: "var(--space-7)" }}>
        {newArrivals.products.length > 0 && <ProductRail {...newArrivals} />}
        {bestSellers.products.length > 0 && <ProductRail {...bestSellers} />}
        {stores.length > 0 && (
          <section aria-labelledby="highest-rated-stores">
            <SectionHeader
              id="highest-rated-stores"
              title="Highest-rated stores"
              description="Active storefronts, best rated first."
              actionHref="/stores"
            />
            <StoreGrid stores={stores} />
          </section>
        )}
        {deals.products.length > 0 && <ProductRail {...deals} />}
      </div>

      <SellCallToAction />
    </div>
  );
}

function SellCallToAction() {
  return (
    <section
      className="mp-card"
      style={{
        padding: "var(--space-6)",
        marginTop: "var(--space-7)",
        display: "flex",
        flexWrap: "wrap",
        gap: "var(--space-4)",
        alignItems: "center",
        justifyContent: "space-between",
      }}
    >
      <div>
        <h2 className="mp-section-title" style={{ margin: 0 }}>
          Sell on the marketplace
        </h2>
        <p style={{ color: "var(--text-muted)", marginBottom: 0 }}>
          Open a store, list your catalogue, and take payment without building any of this yourself.
        </p>
      </div>

      {/* The registration form asks whether the shopper wants to sell, so the link goes to the
          form itself rather than pretending a query parameter can preselect it. */}
      <Link href="/register" className="btn btn-primary">
        Start selling <ArrowRight size={16} aria-hidden />
      </Link>
    </section>
  );
}

interface Rail {
  title: string;
  subtitle: string;
  href: string;
  products: ProductSummary[];
}

async function rail(path: string, title: string, subtitle: string, href: string): Promise<Rail> {
  try {
    // The rails are fetched per shape: the home rails are plain lists, while the deals rail is a
    // filtered page. A rail that cannot load is dropped rather than allowed to fail the page.
    const body = await serverGet<ProductSummary[] | { items: ProductSummary[] }>(path);
    const products = Array.isArray(body) ? body : body.items;

    return { title, subtitle, href, products };
  } catch {
    return { title, subtitle, href, products: [] };
  }
}

async function categoryTiles(): Promise<Category[]> {
  const tree = await serverGetQuietly<Category[]>("/api/categories", 300);

  return selectStorefrontCategories(tree, 12);
}

async function popularStores() {
  // The directory endpoint already returns active storefronts best rated first, so this rail can
  // trust the order instead of inventing its own popularity score.
  const page = await serverGetQuietly<StoreDirectoryPage>("/api/stores?page=1&pageSize=4", 300);

  return page?.items ?? [];
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

      if (spotlight.length >= 3) {
        return spotlight;
      }
    }
  }

  return spotlight;
}

function HeroShowcase({ products }: { products: ProductSummary[] }) {
  const [main, ...rest] = products;

  if (!main) {
    return null;
  }

  return (
    <div className="mp-hero-showcase">
      <Link
        href={`/products/${main.slug}`}
        className="mp-hero-main"
        aria-label={`${main.name}, ${formatCurrency(main.basePrice)}`}
      >
        <ProductPhoto
          src={main.primaryImageUrl}
          alt={main.name}
          width={960}
          height={720}
          aspectRatio="4 / 3"
          eager
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
              aria-label={`${product.name}, ${formatCurrency(product.basePrice)}`}
            >
              <ProductPhoto src={product.primaryImageUrl} alt={product.name} width={480} height={480} />
            </Link>
          ))}
        </div>
      ) : null}
    </div>
  );
}

function ProductRail({ title, subtitle, products, href }: Rail) {
  return (
    <section aria-labelledby={`rail-${title.replace(/\s+/g, "-").toLowerCase()}`}>
      <SectionHeader
        id={`rail-${title.replace(/\s+/g, "-").toLowerCase()}`}
        title={title}
        description={subtitle}
        actionHref={href}
      />

      <div className="row g-3">
        {products.map((product) => (
          <div key={product.id} className="col-6 col-md-4 col-lg-3 col-xl-2">
            <ProductCard product={product} />
          </div>
        ))}
      </div>
    </section>
  );
}
