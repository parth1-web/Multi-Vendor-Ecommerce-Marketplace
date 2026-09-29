"use client";

/**
 * The storefront header. Client because it reads the session and the cart count, and because
 * the theme toggle and the account menu are interactions. The markup itself is a server
 * component's worth of HTML with three interactive islands inside it.
 */

import Link from "next/link";
import { usePathname } from "next/navigation";
import { Heart, LayoutDashboard, Menu, Moon, Search, Store, Sun, User, X } from "lucide-react";
import { useEffect, useState } from "react";

import { CartCountBadge } from "@/features/cart/components/CartCountBadge";

import { APP_NAME } from "@/lib/constants";
import { cx } from "@/lib/format";
import { useAuth } from "@/providers/AuthProvider";
import { useThemeStore } from "@/providers/ThemeProvider";

const NAV_LINKS = [
  { href: "/products", label: "All products" },
  { href: "/categories", label: "Categories" },
  { href: "/deals", label: "Deals" },
  { href: "/stores", label: "Stores" },
];

export function PublicHeader() {
  const pathname = usePathname();
  const { user, isAuthenticated, isHydrating, signOut } = useAuth();
  const { theme, toggle } = useThemeStore();
  const [mobileOpen, setMobileOpen] = useState(false);
  const [accountOpen, setAccountOpen] = useState(false);

  // Menus belong to the page they were opened on. Link selection closes the account menu
  // directly below; Escape closes either open menu.
  useEffect(() => {
    if (!accountOpen && !mobileOpen) {
      return;
    }

    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setAccountOpen(false);
        setMobileOpen(false);
      }
    };

    document.addEventListener("keydown", closeOnEscape);
    return () => document.removeEventListener("keydown", closeOnEscape);
  }, [accountOpen, mobileOpen]);

  return (
    <header
      style={{
        position: "sticky",
        top: 0,
        zIndex: 1030,
        backgroundColor: "var(--bg-surface)",
        borderBottom: "var(--border-width) solid var(--border)",
      }}
    >
      <div
        className="mp-page"
        style={{
          minHeight: "var(--header-height)",
          display: "flex",
          alignItems: "center",
          gap: "var(--space-5)",
        }}
      >
        <Link href="/" className="mp-truncate" style={{ color: "var(--text)", fontWeight: 700 }}>
          <span
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: "var(--space-2)",
              fontFamily: "var(--font-display)",
            }}
          >
            <span
              aria-hidden
              style={{
                width: "1.75rem",
                height: "1.75rem",
                borderRadius: "var(--radius-sm)",
                background: "linear-gradient(135deg, var(--brand-500), var(--brand-700))",
              }}
            />
            {APP_NAME}
          </span>
        </Link>

        <nav aria-label="Primary" className="d-none d-lg-flex" style={{ gap: "var(--space-4)" }}>
          {NAV_LINKS.map((link) => (
            <Link
              key={link.href}
              href={link.href}
              aria-current={pathname === link.href ? "page" : undefined}
              style={{
                color: pathname === link.href ? "var(--brand-600)" : "var(--text-muted)",
                fontSize: "var(--fs-sm)",
                fontWeight: 500,
              }}
            >
              {link.label}
            </Link>
          ))}
        </nav>

        <form
          role="search"
          action="/search"
          className="d-none d-md-flex flex-grow-1"
          style={{ maxWidth: "28rem", position: "relative" }}
        >
          <label htmlFor="site-search" className="visually-hidden">
            Search products
          </label>
          <input
            id="site-search"
            name="q"
            type="search"
            placeholder="Search products, stores and brands"
            className="form-control"
            style={{ paddingInlineStart: "2.25rem", backgroundColor: "var(--bg-subtle)", borderColor: "var(--border)" }}
          />
          <Search
            aria-hidden
            size={16}
            style={{ position: "absolute", insetInlineStart: "0.75rem", insetBlockStart: "0.7rem", color: "var(--text-subtle)" }}
          />
        </form>

        <div className="ms-auto d-flex align-items-center" style={{ gap: "var(--space-2)" }}>
          <button
            type="button"
            className="btn btn-sm mp-icon-button"
            onClick={toggle}
            aria-label={theme === "dark" ? "Switch to the light theme" : "Switch to the dark theme"}
            style={{ color: "var(--text-muted)" }}
          >
            {theme === "dark" ? <Sun size={18} /> : <Moon size={18} />}
          </button>

          <Link href="/wishlist" className="btn btn-sm mp-icon-button" aria-label="Wishlist" style={{ color: "var(--text-muted)" }}>
            <Heart size={18} />
          </Link>

          <CartCountBadge />

          {isHydrating ? (
            <span className="mp-skeleton" style={{ width: "6rem", height: "2rem" }} aria-hidden />
          ) : isAuthenticated && user ? (
            <div className="position-relative">
              <button
                type="button"
                className="btn btn-sm d-flex align-items-center"
                style={{ gap: "var(--space-2)", color: "var(--text)" }}
                onClick={() => setAccountOpen((open) => !open)}
                aria-expanded={accountOpen}
                aria-haspopup="menu"
              >
                <User size={18} />
                <span className="d-none d-xl-inline">{user.firstName}</span>
              </button>

              {accountOpen ? (
                <div
                  role="menu"
                  className="mp-card-elevated position-absolute end-0 mt-2"
                  style={{ minWidth: "13rem", padding: "var(--space-2)", zIndex: 1040 }}
                >
                  <MenuLink href="/dashboard" label="Dashboard" icon={<LayoutDashboard size={16} />} onNavigate={() => setAccountOpen(false)} />
                  <MenuLink href="/orders" label="My orders" onNavigate={() => setAccountOpen(false)} />
                  <MenuLink href="/wishlist" label="Saved items" onNavigate={() => setAccountOpen(false)} />
                  <MenuLink href="/addresses" label="Addresses" onNavigate={() => setAccountOpen(false)} />
                  {user.role === "Seller" ? (
                    <MenuLink href="/seller" label="Seller dashboard" icon={<LayoutDashboard size={16} />} onNavigate={() => setAccountOpen(false)} />
                  ) : null}
                  {user.role === "Admin" || user.role === "SuperAdmin" ? (
                    <MenuLink
                      href="/admin"
                      label="Admin console"
                      icon={<LayoutDashboard size={16} />}
                      onNavigate={() => setAccountOpen(false)}
                    />
                  ) : null}
                  <hr className="my-2" style={{ borderColor: "var(--border)" }} />
                  <button
                    type="button"
                    role="menuitem"
                    className="btn btn-sm w-100 text-start"
                    style={{ color: "var(--danger)" }}
                    onClick={() => {
                      setAccountOpen(false);
                      void signOut();
                    }}
                  >
                    Sign out
                  </button>
                </div>
              ) : null}
            </div>
          ) : (
            <div className="d-flex align-items-center" style={{ gap: "var(--space-2)" }}>
              <Link href="/login" className="btn btn-sm" style={{ color: "var(--text)" }}>
                Sign in
              </Link>
              <Link href="/register" className="btn btn-sm btn-primary">
                Create account
              </Link>
            </div>
          )}

          <button
            type="button"
            className="btn btn-sm mp-icon-button d-lg-none"
            aria-label={mobileOpen ? "Close the menu" : "Open the menu"}
            aria-expanded={mobileOpen}
            aria-controls="mobile-navigation"
            onClick={() => setMobileOpen((open) => !open)}
            style={{ color: "var(--text)" }}
          >
            {mobileOpen ? <X size={18} /> : <Menu size={18} />}
          </button>
        </div>
      </div>

      {mobileOpen ? (
        <nav
          id="mobile-navigation"
          aria-label="Mobile"
          className="d-lg-none"
          style={{ borderTop: "1px solid var(--border)", backgroundColor: "var(--bg-surface)", padding: "var(--space-3) var(--space-4)" }}
        >
          <form role="search" action="/search" className="d-md-none" style={{ marginBottom: "var(--space-3)" }}>
            <label htmlFor="mobile-search" className="visually-hidden">
              Search products
            </label>
            <div className="d-flex" style={{ gap: "var(--space-2)" }}>
              <input
                id="mobile-search"
                name="q"
                type="search"
                required
                placeholder="Search products, stores and brands"
                className="form-control"
                autoComplete="off"
              />
              <button type="submit" className="btn btn-sm btn-primary" style={{ flex: "none" }}>
                Search
              </button>
            </div>
          </form>

          <div className="mp-stack-sm">
            {NAV_LINKS.map((link) => (
              <Link
                key={link.href}
                href={link.href}
                onClick={() => setMobileOpen(false)}
                className={cx("d-flex", "align-items-center", "gap-2")}
                style={{ color: pathname === link.href ? "var(--brand-600)" : "var(--text)" }}
              >
                {link.href === "/stores" ? <Store size={16} /> : null}
                {link.label}
              </Link>
            ))}
          </div>
        </nav>
      ) : null}
    </header>
  );
}

function MenuLink({ href, label, icon, onNavigate }: { href: string; label: string; icon?: React.ReactNode; onNavigate: () => void }) {
  return (
    <Link
      href={href}
      role="menuitem"
      onClick={onNavigate}
      className="btn btn-sm w-100 d-flex align-items-center"
      style={{ gap: "var(--space-2)", justifyContent: "flex-start", color: "var(--text)" }}
    >
      {icon}
      {label}
    </Link>
  );
}
