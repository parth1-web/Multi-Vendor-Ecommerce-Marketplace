/** Storefront footer. A server component: nothing here needs the session or a handler. */

import Link from "next/link";

const COLUMNS = [
  {
    title: "Shop",
    links: [
      { href: "/products", label: "All products" },
      { href: "/categories", label: "Categories" },
      { href: "/deals", label: "Deals" },
      { href: "/stores", label: "Stores" },
    ],
  },
  {
    title: "Account",
    links: [
      { href: "/orders", label: "Orders" },
      { href: "/wishlist", label: "Wishlist" },
      { href: "/addresses", label: "Addresses" },
      { href: "/notifications", label: "Notifications" },
    ],
  },
  {
    title: "Sell",
    links: [
      { href: "/seller", label: "Seller dashboard" },
      { href: "/seller/products", label: "Products" },
      { href: "/seller/orders", label: "Orders" },
      { href: "/seller/analytics", label: "Analytics" },
    ],
  },
] as const;

export function PublicFooter() {
  return (
    <footer style={{ backgroundColor: "var(--bg-surface)", borderTop: "var(--border-width) solid var(--border)", marginTop: "var(--space-7)" }}>
      <div className="mp-page" style={{ paddingBlock: "var(--space-6)" }}>
        <div className="row g-4">
          <div className="col-12 col-md-4">
            <p style={{ fontFamily: "var(--font-display)", fontWeight: 600, margin: 0 }}>Marketplace</p>
            <p style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)", maxWidth: "28rem" }}>
              A multi-vendor marketplace where independent sellers list stock, manage their own
              catalogues and are paid for what they actually ship.
            </p>
          </div>

          {COLUMNS.map((column) => (
            <div key={column.title} className="col-6 col-md-3 col-lg-2 offset-lg-1">
              <p className="mp-metric-label" style={{ marginBottom: "var(--space-3)" }}>
                {column.title}
              </p>
              <ul className="list-unstyled mp-stack-sm" style={{ margin: 0 }}>
                {column.links.map((link) => (
                  <li key={link.href}>
                    <Link href={link.href} style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
                      {link.label}
                    </Link>
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </div>

        <div
          className="mp-spread"
          style={{ marginTop: "var(--space-6)", paddingTop: "var(--space-4)", borderTop: "1px solid var(--border)" }}
        >
          <small style={{ color: "var(--text-subtle)" }}>
            © {new Date().getFullYear()} Marketplace. A demonstration build.
          </small>
          <small style={{ color: "var(--text-subtle)" }}>Prices include platform commission where shown.</small>
        </div>
      </div>
    </footer>
  );
}
