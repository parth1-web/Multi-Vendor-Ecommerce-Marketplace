"use client";

/**
 * The admin area's shell, and the gate in front of it.
 *
 * A customer following a "you need to be an admin" link is told what this is rather than shown
 * a dashboard of figures that are not theirs, and the check lives here once instead of on every
 * page below it.
 */

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, type ReactNode } from "react";
import { BarChart3, FileText, Package, ShieldCheck, Store, Tag, Users, UserSquare } from "lucide-react";

import { useAuth } from "@/providers/AuthProvider";

const LINKS = [
  { href: "/admin", label: "Overview", icon: BarChart3 },
  { href: "/admin/moderation", label: "Moderation", icon: ShieldCheck },
  { href: "/admin/orders", label: "Orders", icon: Package },
  { href: "/admin/coupons", label: "Discounts", icon: Tag },
  { href: "/admin/sellers", label: "Sellers", icon: Store },
  { href: "/admin/users", label: "Users", icon: Users },
  { href: "/admin/reports", label: "Reports", icon: FileText },
  { href: "/admin/audit", label: "Audit log", icon: UserSquare },
];

export function AdminLayout({ children }: { children: ReactNode }) {
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

  return (
    <div className="mp-page" style={{ paddingBlock: "var(--space-4) var(--space-5)" }}>
      <nav aria-label="Admin sections" className="mp-seller-nav mb-4">
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
