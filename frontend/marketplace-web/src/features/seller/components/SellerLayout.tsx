"use client";

/**
 * The seller area's shell, and the gate in front of it.
 *
 * The gate is here rather than on each page because "a seller page that forgot to check" is a
 * dashboard showing somebody else's revenue. A pending seller is allowed in and told what is
 * happening, because a wall saying "forbidden" helps nobody.
 *
 * The workspace is a sidebar and a top bar rather than a row of tabs, because its sections are
 * peers a seller moves between all day and a tab row pushes them off the end of a narrow
 * screen. Below the large breakpoint the sidebar becomes the tab row again rather than hiding
 * behind a menu button, so every section stays one click away on a phone. The structure mirrors
 * the admin console on purpose: one shell pattern to learn, not two.
 */

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, type ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  Boxes,
  ChevronRight,
  LayoutDashboard,
  LogOut,
  MessageSquareQuote,
  Moon,
  Package,
  ShoppingBag,
  Store,
  Sun,
  Wallet,
} from "lucide-react";

import { APP_NAME } from "@/lib/constants";
import { sellerApi } from "@/features/seller/api/sellerApi";
import { queryKeys } from "@/lib/queryKeys";
import { useAuth } from "@/providers/AuthProvider";
import { useThemeStore } from "@/providers/ThemeProvider";

const WORKSPACE_LINKS = [
  { href: "/seller", label: "Overview", icon: LayoutDashboard },
  { href: "/seller/products", label: "Products", icon: Package },
  { href: "/seller/orders", label: "Orders", icon: ShoppingBag },
  { href: "/seller/inventory", label: "Stock", icon: Boxes },
  { href: "/seller/reviews", label: "Reviews", icon: MessageSquareQuote },
  { href: "/seller/earnings", label: "Earnings", icon: Wallet },
];

const STORE_LINKS = [{ href: "/seller/store", label: "Store settings", icon: Store }];

/** A section is current on its own page and on everything beneath it: /seller/orders/123 is still Orders. */
function isCurrent(pathname: string, href: string): boolean {
  return pathname === href || pathname.startsWith(`${href}/`);
}

export function SellerLayout({ children }: { children: ReactNode }) {
  const router = useRouter();
  const pathname = usePathname();
  const { user, isAuthenticated, isHydrating, signOut } = useAuth();
  const { theme, toggle } = useThemeStore();

  // The store handle for the "view store" links. One cached read for the whole workspace, and the
  // shell works without it: a seller whose storefront is not reachable yet simply gets no link.
  const identity = useQuery({
    queryKey: queryKeys.seller.me(),
    queryFn: () => sellerApi.me(),
    enabled: isAuthenticated && user?.role === "Seller",
    staleTime: 5 * 60_000,
  });

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

  const current =
    [...WORKSPACE_LINKS, ...STORE_LINKS].find((link) => isCurrent(pathname, link.href)) ?? WORKSPACE_LINKS[0];
  const storeSlug = identity.data?.storeSlug ?? null;
  const storeName = identity.data?.storeName ?? null;

  const sidebarLinks = (links: typeof WORKSPACE_LINKS) =>
    links.map((link) => {
      const Icon = link.icon;

      return (
        <Link
          key={link.href}
          href={link.href}
          className="mp-sidebar-link"
          aria-current={isCurrent(pathname, link.href) ? "page" : undefined}
        >
          <Icon size={16} aria-hidden />
          {link.label}
        </Link>
      );
    });

  return (
    <div className="d-flex align-items-stretch" style={{ minHeight: "100vh" }}>
      <aside
        className="mp-sidebar d-none d-lg-flex flex-column flex-shrink-0"
        style={{ width: "var(--sidebar-width)", position: "sticky", top: 0, height: "100vh" }}
      >
        <Link href="/seller" className="mp-sidebar-brand" style={{ textDecoration: "none", gap: "var(--space-3)" }}>
          <span
            aria-hidden
            style={{
              display: "grid",
              placeItems: "center",
              width: "2rem",
              height: "2rem",
              borderRadius: "var(--radius-sm)",
              backgroundImage: "var(--accent-gradient)",
              color: "var(--on-accent)",
              fontWeight: 700,
              fontSize: "var(--fs-sm)",
            }}
          >
            M
          </span>

          <span style={{ display: "flex", flexDirection: "column", lineHeight: 1.2 }}>
            <span>{APP_NAME}</span>
            <span style={{ fontSize: "var(--fs-xs)", fontWeight: 400, color: "var(--sidebar-text)" }}>
              Seller workspace
            </span>
          </span>
        </Link>

        <p className="mp-sidebar-section">Workspace</p>

        <nav aria-label="Seller sections" className="flex-grow-1 overflow-y-auto pb-3">
          {sidebarLinks(WORKSPACE_LINKS)}

          <p className="mp-sidebar-section">Store</p>
          {sidebarLinks(STORE_LINKS)}
        </nav>

        <div style={{ borderTop: "1px solid var(--sidebar-border)", padding: "var(--space-3)" }}>
          {storeSlug ? (
            <Link
              href={`/stores/${storeSlug}`}
              className="mp-sidebar-link"
              style={{ paddingInline: "var(--space-3)", fontSize: "var(--fs-xs)" }}
            >
              <Store size={14} aria-hidden />
              View your store
            </Link>
          ) : null}

          <Link
            href="/"
            className="mp-sidebar-link"
            style={{ paddingInline: "var(--space-3)", fontSize: "var(--fs-xs)" }}
          >
            <ShoppingBag size={14} aria-hidden />
            Back to the shop
          </Link>

          <button
            type="button"
            className="mp-sidebar-link w-100 border-0"
            style={{ paddingInline: "var(--space-3)", fontSize: "var(--fs-xs)", background: "none" }}
            onClick={() => void signOut()}
          >
            <LogOut size={14} aria-hidden />
            Sign out
          </button>
        </div>
      </aside>

      <div className="d-flex flex-column flex-grow-1" style={{ minWidth: 0 }}>
        <header
          className="mp-glass d-flex align-items-center flex-wrap"
          style={{
            position: "sticky",
            top: 0,
            zIndex: 20,
            gap: "var(--space-3)",
            padding: "var(--space-3) var(--space-4)",
            borderBottom: "var(--border-width) solid var(--border)",
          }}
        >
          <nav aria-label="Breadcrumb">
            <ol className="list-unstyled d-flex align-items-center mb-0" style={{ gap: "0.35rem", fontSize: "var(--fs-xs)" }}>
              <li style={{ color: "var(--text-subtle)" }}>{storeName ?? "Seller"}</li>
              <li aria-hidden>
                <ChevronRight size={12} />
              </li>
              <li aria-current="page" style={{ color: "var(--text)", fontWeight: 500 }}>
                {current.label}
              </li>
            </ol>
          </nav>

          <div className="ms-auto d-flex align-items-center" style={{ gap: "var(--space-3)" }}>
            <button
              type="button"
              className="btn btn-sm"
              onClick={toggle}
              aria-label={theme === "dark" ? "Switch to the light theme" : "Switch to the dark theme"}
              style={{ color: "var(--text-muted)" }}
            >
              {theme === "dark" ? <Sun size={18} aria-hidden /> : <Moon size={18} aria-hidden />}
            </button>

            <div className="d-flex align-items-center" style={{ gap: "var(--space-2)" }}>
              <span
                aria-hidden
                style={{
                  display: "grid",
                  placeItems: "center",
                  width: "2rem",
                  height: "2rem",
                  borderRadius: "var(--radius-pill)",
                  backgroundImage: "var(--accent-gradient)",
                  color: "var(--on-accent)",
                  fontSize: "var(--fs-xs)",
                  fontWeight: 600,
                }}
              >
                {(storeName ?? user?.firstName ?? "?").slice(0, 1).toUpperCase()}
              </span>

              <span className="d-none d-sm-flex flex-column" style={{ lineHeight: 1.2 }}>
                <span style={{ fontSize: "var(--fs-sm)", fontWeight: 500 }}>{storeName ?? user?.fullName}</span>
                <span style={{ fontSize: "var(--fs-xs)", color: "var(--text-subtle)" }}>Seller</span>
              </span>
            </div>
          </div>
        </header>

        <main className="flex-grow-1">
          {/* Below the large breakpoint the sidebar is gone, so the same sections are offered as
              the tab row they used to be rather than being tucked behind a menu. */}
          <div className="d-lg-none border-bottom" style={{ borderColor: "var(--border)" }}>
            <nav aria-label="Seller sections" className="mp-seller-nav border-0 px-3" style={{ paddingBottom: "var(--space-2)" }}>
              {[...WORKSPACE_LINKS, ...STORE_LINKS].map((link) => {
                const Icon = link.icon;

                return (
                  <Link
                    key={link.href}
                    href={link.href}
                    className="mp-seller-link"
                    aria-current={isCurrent(pathname, link.href) ? "page" : undefined}
                  >
                    <Icon size={15} aria-hidden />
                    {link.label}
                  </Link>
                );
              })}
            </nav>
          </div>

          <div className="mp-page" style={{ paddingBlock: "var(--space-5)" }}>
            {children}
          </div>
        </main>
      </div>
    </div>
  );
}
