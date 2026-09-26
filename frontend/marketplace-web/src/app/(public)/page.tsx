import { serverGet } from "@/lib/serverApi";
import type { ProductPage } from "@/types/product";

/**
 * The homepage, rendered on the server.
 *
 * Three rails come straight from the API with a short cache: new arrivals, best sellers and
 * featured products. Each rail is a server component that fetches its own slice, so a slow or
 * failing one does not hold up the rest of the page.
 */
export default async function HomePage() {
  const [newArrivals, bestSellers, featured] = await Promise.all([
    rail("new-arrivals", "New arrivals", "Fresh from independent sellers"),
    rail("best-sellers", "Best sellers", "What other shoppers are buying"),
    rail("featured", "Featured", "Picked by the marketplace team"),
  ]);

  return (
    <div className="mp-page" style={{ paddingBlock: "var(--space-6)" }}>
      <section className="mp-card" style={{ padding: "var(--space-6)", marginBottom: "var(--space-6)" }}>
        <p className="mp-metric-label" style={{ margin: 0 }}>
          Multi-vendor marketplace
        </p>
        <h1 className="mp-page-title" style={{ fontSize: "var(--fs-display)", marginTop: "var(--space-2)" }}>
          Everything a marketplace has to get right
        </h1>
        <p style={{ color: "var(--text-muted)", maxWidth: "46rem", marginBottom: 0 }}>
          Stock that cannot be oversold, payments that settle once, refunds that only appear
          once the goods have arrived, and dashboards that agree with the ledger underneath them.
        </p>
      </section>

      <div className="mp-stack" style={{ gap: "var(--space-7)" }}>
        <ProductRail title={newArrivals.title} subtitle={newArrivals.subtitle} products={newArrivals.products} href="/products?sort=Newest" />
        <ProductRail title={bestSellers.title} subtitle={bestSellers.subtitle} products={bestSellers.products} href="/products?sort=Popular" />
        <ProductRail title={featured.title} subtitle={featured.subtitle} products={featured.products} href="/products" />
      </div>
    </div>
  );
}

interface Rail {
  title: string;
  subtitle: string;
  products: ProductPage["items"];
}

async function rail(path: string, title: string, subtitle: string): Promise<Rail> {
  try {
    const page = await serverGet<ProductPage>(`/api/products/${path}?take=8`);

    return { title, subtitle, products: page.items };
  } catch {
    // A rail that cannot load is omitted rather than allowed to fail the whole page.
    return { title, subtitle, products: [] };
  }
}

function ProductRail({ title, subtitle, products, href }: Rail & { href: string }) {
  return (
    <section aria-labelledby={`rail-${title.replace(/\s+/g, "-").toLowerCase()}`}>
      <div className="mp-spread" style={{ marginBottom: "var(--space-4)" }}>
        <div>
          <h2 className="mp-section-title" id={`rail-${title.replace(/\s+/g, "-").toLowerCase()}`}>
            {title}
          </h2>
          <small style={{ color: "var(--text-muted)" }}>{subtitle}</small>
        </div>
        <a href={href} style={{ color: "var(--brand-600)", fontSize: "var(--fs-sm)", fontWeight: 500 }}>
          See all
        </a>
      </div>

      {products.length === 0 ? (
        <p style={{ color: "var(--text-muted)" }}>Nothing to show here yet.</p>
      ) : (
        <div className="row g-3">
          {products.map((product) => (
            <div key={product.id} className="col-6 col-md-4 col-lg-3 col-xl-2">
              <article className="mp-card h-100" style={{ padding: "var(--space-3)" }}>
                <div
                  className="mp-skeleton mb-2"
                  style={{ aspectRatio: "1 / 1", borderRadius: "var(--radius-sm)" }}
                  aria-hidden
                />
                <h3 className="mp-clamp-2" style={{ fontSize: "var(--fs-sm)", margin: 0 }}>
                  {product.name}
                </h3>
                <p style={{ color: "var(--text-muted)", fontSize: "var(--fs-xs)", margin: "var(--space-1) 0 0" }}>
                  {product.storeName}
                </p>
                <p className="mp-price" style={{ margin: "var(--space-2) 0 0" }}>
                  {product.basePrice.toFixed(2)}
                </p>
              </article>
            </div>
          ))}
        </div>
      )}
    </section>
  );
}
