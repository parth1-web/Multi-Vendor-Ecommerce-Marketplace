"use client";

/**
 * The seller area's shell, and the gate in front of it.
 *
 * The gate is here rather than on each page because "a seller page that forgot to check" is a
 * dashboard showing somebody else's revenue. A pending seller is allowed in and told what is
 * happening, because a wall saying "forbidden" helps nobody.
 */

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, type ReactNode } from "react";
import { Boxes, LayoutDashboard, MessageSquareQuote, Package, ShoppingCart, Wallet } from "lucide-react";

import { useAuth } from "@/providers/AuthProvider";

const LINKS = [
  { href: "/seller", label: "Overview", icon: LayoutDashboard },
  { href: "/seller/products", label: "Products", icon: Package },
  { href: "/seller/orders", label: "Orders", icon: ShoppingCart },
  { href: "/seller/inventory", label: "Stock", icon: Boxes },
  { href: "/seller/reviews", label: "Reviews", icon: MessageSquareQuote },
  { href: "/seller/earnings", label: "Earnings", icon: Wallet },
];

export function SellerLayout({ children }: { children: ReactNode }) {
  const router = useRouter();
  const pathname = usePathname();
  const { user, isAuthenticated, isHydrating, signOut } = useAuth();

  useEffect(() => {
    if (!isHydrating && !isAuthenticated) {
      router.replace(`/login?returnUrl=${encodeURIComponent(pathname)}`);
    }
  }, [isAuthenticated, isHydrating, pathname, router]);

  if (isHydrating || !isAuthenticated) {
    return <div className="mp-skeleton" style={{ height: "20rem", borderRadius: "var(--radius)", margin: "var(--space-5)" }} />;
  }

  if (user?.role !== "Seller") {
    return (
      <div className="mp-page" style={{ paddingBlock: "var(--space-5)" }}>
        <div className="mp-card" style={{ padding: "var(--space-5)" }}>
          <h1 className="mp-section-title" style={{ fontSize: "var(--fs-h2)" }}>
            This area is for sellers
          </h1>
          <p style={{ color: "var(--text-muted)" }}>
            Your account can buy here, but not sell. Register as a seller to list products and be paid for what you ship.
          </p>
          <div className="d-flex" style={{ gap: "0.5rem" }}>
            <Link href="/products" className="btn btn-sm btn-primary">
              Back to shopping
            </Link>
            <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => void signOut()}>
              Sign out
            </button>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="mp-page" style={{ paddingBlock: "var(--space-4) var(--space-5)" }}>
      <nav aria-label="Seller sections" className="mp-seller-nav mb-4">
        {LINKS.map(link => {
          const active = pathname === link.href;
          const Icon = link.icon;

          return (
            <Link
              key={link.href}
              href={link.href}
              className="mp-seller-link"
              aria-current={active ? "page" : undefined}
              style={{
                color: active ? "var(--text)" : "var(--text-muted)",
                backgroundColor: active ? "var(--violet-bg)" : "transparent",
                borderColor: active ? "var(--violet)" : "transparent",
              }}
            >
              <Icon size={15} aria-hidden />
              {link.label}
            </Link>
          );
        })}
      </nav>

      {children}
    </div>
  );
}
