"use client";

/**
 * The admin area's shell, and the gate in front of it.
 *
 * A customer following a "you need to be an admin" link is told what this is rather than shown
 * a dashboard of figures that are not theirs, and the check lives here once instead of on every
 * page below it.
 *
 * The console is a sidebar and a top bar rather than a row of tabs, because the sections are
 * peers that an operator moves between all day and a tab row pushes them off the end of a
 * narrow screen. Below the large breakpoint the sidebar becomes the tab row again rather than
 * hiding behind a menu button, so every section stays one click away on a phone.
 */

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, type ReactNode } from "react";
import {
  BarChart3,
  Boxes,
  ChevronRight,
  CreditCard,
  FileText,
  FolderTree,
  LogOut,
  Moon,
  Package,
  ShieldCheck,
  ShoppingBag,
  Store,
  Sun,
  Tag,
  Undo2,
  Users,
  UserSquare,
  Warehouse,
  type LucideIcon,
} from "lucide-react";

import { APP_NAME } from "@/lib/constants";
import { useAuth } from "@/providers/AuthProvider";
import { useThemeStore } from "@/providers/ThemeProvider";

/**
 * The console's sections, and nothing else.
 *
 * Every entry here is a screen built on an endpoint the API actually exposes. There is no Settings
 * link because there is no settings endpoint, and no review queue because the API has no way to
 * list reviews across the marketplace — a link to either would be a page of broken promises.
 *
 * Grouped by what an operator is doing rather than alphabetically, because the two questions that
 * come up all day are "what needs deciding" and "what needs checking".
 */
const LINK_GROUPS: { label: string; links: { href: string; label: string; icon: LucideIcon }[] }[] = [
  {
    label: "Decide",
    links: [
      { href: "/admin", label: "Overview", icon: BarChart3 },
      { href: "/admin/moderation", label: "Moderation", icon: ShieldCheck },
      { href: "/admin/sellers", label: "Sellers", icon: Store },
      { href: "/admin/refunds", label: "Refunds", icon: Undo2 },
    ],
  },
  {
    label: "Operate",
    links: [
      { href: "/admin/orders", label: "Orders", icon: Package },
      { href: "/admin/products", label: "Products", icon: Boxes },
      { href: "/admin/categories", label: "Categories", icon: FolderTree },
      { href: "/admin/inventory", label: "Stock", icon: Warehouse },
      { href: "/admin/payments", label: "Payments", icon: CreditCard },
      { href: "/admin/coupons", label: "Discounts", icon: Tag },
      { href: "/admin/users", label: "Accounts", icon: Users },
    ],
  },
  {
    label: "Understand",
    links: [
      { href: "/admin/reports", label: "Reports", icon: FileText },
      { href: "/admin/audit", label: "Audit log", icon: UserSquare },
    ],
  },
];

/** Every section in one list, for the mobile row and for the breadcrumb lookup. */
const LINKS = LINK_GROUPS.flatMap(group => group.links);

/** A section is current on its own page and on everything beneath it. */
function isCurrent(pathname: string, href: string): boolean {
  return pathname === href || pathname.startsWith(`${href}/`);
}

export function AdminLayout({ children }: { children: ReactNode }) {
  const router = useRouter();
  const pathname = usePathname();
  const { user, isAuthenticated, isHydrating, signOut } = useAuth();
  const { theme, toggle } = useThemeStore();

  useEffect(() => {
    if (!isHydrating && !isAuthenticated) {
      router.replace(`/login?returnUrl=${encodeURIComponent(pathname)}`);
    }
  }, [isAuthenticated, isHydrating, pathname, router]);

  if (isHydrating || !isAuthenticated) {
    return <div className="mp-skeleton" style={{ height: "20rem", borderRadius: "var(--radius)", margin: "var(--space-5)" }} />;
  }

  const isAdmin = user?.role === "Admin" || user?.role === "SuperAdmin";

  if (!isAdmin) {
    return (
      <div className="mp-page" style={{ paddingBlock: "var(--space-5)" }}>
        <div className="mp-card" style={{ padding: "var(--space-5)" }}>
          <h1 className="mp-section-title" style={{ fontSize: "var(--fs-h2)" }}>
            This area is for moderators
          </h1>
          <p style={{ color: "var(--text-muted)" }}>
            The admin console shows every seller, every order and every account on the marketplace. Your account is{" "}
            {user?.role === "Seller" ? "a seller account" : "a customer account"}, so it does not have access.
          </p>
          <div className="d-flex" style={{ gap: "0.5rem" }}>
            <Link href="/" className="btn btn-sm btn-primary">
              Back to the marketplace
            </Link>
            <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => void signOut()}>
              Sign out
            </button>
          </div>
        </div>
      </div>
    );
  }

  const current = LINKS.find(link => isCurrent(pathname, link.href)) ?? LINKS[0];

  return (
    <div className="d-flex align-items-stretch" style={{ minHeight: "100vh" }}>
      <aside
        className="mp-sidebar d-none d-lg-flex flex-column flex-shrink-0"
        style={{ width: "var(--sidebar-width)", position: "sticky", top: 0, height: "100vh" }}
      >
        <Link href="/admin" className="mp-sidebar-brand" style={{ textDecoration: "none", gap: "var(--space-3)" }}>
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
            <span style={{ fontSize: "var(--fs-xs)", fontWeight: 400, color: "var(--sidebar-text)" }}>Admin console</span>
          </span>
        </Link>

        <nav aria-label="Admin sections" className="flex-grow-1 overflow-y-auto pb-3">
          {LINK_GROUPS.map(group => (
            <div key={group.label}>
              <p className="mp-sidebar-section">{group.label}</p>
              {group.links.map(link => {
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
              })}
            </div>
          ))}
        </nav>

        <div style={{ borderTop: "1px solid var(--sidebar-border)", padding: "var(--space-3)" }}>
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
              <li style={{ color: "var(--text-subtle)" }}>Admin</li>
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
                {(user?.firstName ?? "?").slice(0, 1).toUpperCase()}
              </span>

              <span className="d-none d-sm-flex flex-column" style={{ lineHeight: 1.2 }}>
                <span style={{ fontSize: "var(--fs-sm)", fontWeight: 500 }}>{user?.fullName}</span>
                <span style={{ fontSize: "var(--fs-xs)", color: "var(--text-subtle)" }}>{user?.role}</span>
              </span>
            </div>
          </div>
        </header>

        <main className="flex-grow-1">
          {/* Below the large breakpoint the sidebar is gone, so the same sections are offered as
              the tab row they used to be rather than being tucked behind a menu. */}
          <div className="d-lg-none border-bottom" style={{ borderColor: "var(--border)" }}>
            <nav aria-label="Admin sections" className="mp-seller-nav border-0 px-3" style={{ paddingBottom: "var(--space-2)" }}>
              {LINKS.map(link => {
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
