import Link from "next/link";
import { ArrowRight, ShieldCheck, Store, Truck } from "lucide-react";

import { serverGet, serverGetQuietly } from "@/lib/serverApi";
import { ProductCard } from "@/features/products/components/ProductCard";
import type { Category } from "@/types/product";
import type { ProductSummary } from "@/types/product";

/**
 * The homepage, rendered on the server.
 *
 * Every section fetches its own slice and each one is allowed to come back empty: a rail that
 * fails is dropped rather than allowed to take the page down with it. The cards are the same
 * ProductCard the catalogue grid uses, so a shopper moving from here into the grid is not
 * looking at a different product.
 */
export default async function HomePage() {
  const [deals, newArrivals, bestSellers, categories] = await Promise.all([
    rail("/api/products?onSale=true&sort=Discount&pageSize=8", "Deals", "Marked down by their store", "/products?onSale=true&sort=Discount"),
    rail("/api/products/new-arrivals?take=8", "New arrivals", "Just listed by independent sellers", "/products?sort=Newest"),
    rail("/api/products/best-sellers?take=8", "Best sellers", "What other shoppers are buying", "/products?sort=Popular"),
    categoryTiles(),
  ]);

  return (
    <div className="mp-page" style={{ paddingBlock: "var(--space-6)" }}>
      <Hero />

      {categories.length > 0 && (
        <section aria-labelledby="shop-by-category" style={{ marginTop: "var(--space-7)" }}>
          <h2 className="mp-section-title" id="shop-by-category">
            Shop by category
          </h2>
          <div className="row g-3" style={{ marginTop: "var(--space-4)" }}>
            {categories.map((category) => (
              <div key={category.id} className="col-6 col-md-4 col-lg-3 col-xl-2">
                <Link
                  href={`/categories/${category.slug}`}
                  className="mp-card d-block h-100"
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
        {deals.products.length > 0 && <ProductRail {...deals} />}
        {newArrivals.products.length > 0 && <ProductRail {...newArrivals} />}
        {bestSellers.products.length > 0 && <ProductRail {...bestSellers} />}
      </div>

      <SellCallToAction />
    </div>
  );
}

function Hero() {
  return (
    <section
      className="mp-card"
      style={{
        padding: "var(--space-7)",
        marginBottom: "var(--space-6)",
        background: "linear-gradient(135deg, var(--brand-600), var(--brand-700))",
        color: "#fff",
        border: "none",
      }}
    >
      <p className="mp-metric-label" style={{ margin: 0, color: "rgba(255,255,255,0.75)" }}>
        Many independent stores, one checkout
      </p>

      <h1 className="mp-page-title" style={{ fontSize: "var(--fs-display)", color: "#fff", marginTop: "var(--space-2)" }}>
        Everything a marketplace has to get right
      </h1>

      <p style={{ color: "rgba(255,255,255,0.85)", maxWidth: "46rem", marginBottom: "var(--space-5)" }}>
        Buy from sellers who set their own prices and ship their own stock, and pay once at the
        end rather than at every till. Stock that cannot be oversold, payments that settle once,
        refunds that only appear once the goods have arrived.
      </p>

      <div className="d-flex flex-wrap" style={{ gap: "var(--space-3)" }}>
        <Link href="/products" className="btn btn-light" style={{ paddingInline: "var(--space-5)" }}>
          Browse all products
        </Link>
        <Link
          href="/deals"
          className="btn btn-outline-light"
          style={{ paddingInline: "var(--space-5)" }}
        >
          See this week&apos;s deals
        </Link>
      </div>

      <div className="row g-4" style={{ marginTop: "var(--space-6)", marginBottom: 0 }}>
        {[
          { icon: Store, title: "Sellers set their own prices", body: "A store is its own business, with its own catalogue and its own shipping." },
          { icon: ShieldCheck, title: "Pay once, settle once", body: "One order, one payment, and a ledger that agrees with your receipt." },
          { icon: Truck, title: "Refunds follow delivery", body: "A refund can only be raised for an order that has actually arrived." },
        ].map((item) => (
          <div key={item.title} className="col-12 col-md-4">
            <div className="d-flex" style={{ gap: "var(--space-3)" }}>
              <item.icon size={20} aria-hidden style={{ flexShrink: 0, marginTop: "0.15rem" }} />
              <div>
                <p style={{ margin: 0, fontWeight: 500 }}>{item.title}</p>
                <small style={{ color: "rgba(255,255,255,0.8)" }}>{item.body}</small>
              </div>
            </div>
          </div>
        ))}
      </div>
    </section>
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
  const tree = await serverGetQuietly<Category[]>("/api/categories");
  if (!tree) {
    return [];
  }

  // The tree is nested; the homepage shows the departments, and the count is the department's own
  // plus everything filed beneath it, so a shopper is not sent to a shelf that looks empty.
  return tree
    .map((category) => ({
      ...category,
      productCount: totalProducts(category),
    }))
    .filter((category) => category.isActive && category.productCount > 0)
    .sort((a, b) => b.productCount - a.productCount)
    .slice(0, 12);
}

function totalProducts(category: Category): number {
  const children = category.children ?? [];
  return category.productCount + children.reduce((sum, child) => sum + child.productCount, 0);
}

function ProductRail({ title, subtitle, products, href }: Rail) {
  const headingId = `rail-${title.replace(/\s+/g, "-").toLowerCase()}`;

  return (
    <section aria-labelledby={headingId}>
      <div className="mp-spread" style={{ marginBottom: "var(--space-4)" }}>
        <div>
          <h2 className="mp-section-title" id={headingId} style={{ margin: 0 }}>
            {title}
          </h2>
          <small style={{ color: "var(--text-muted)" }}>{subtitle}</small>
        </div>

        <Link href={href} style={{ color: "var(--brand-600)", fontSize: "var(--fs-sm)", fontWeight: 500 }}>
          See all
        </Link>
      </div>

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
