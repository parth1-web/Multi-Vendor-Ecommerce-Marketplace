/**
 * The two panels a sign-in or a registration is drawn on.
 *
 * Built once because they are the same page twice: the left panel says what this is and why it
 * is worth an account, the right one holds the form. The left is hidden on a phone, where a
 * person who followed a link here is here for the form and should not have to scroll past an
 * advert to reach it.
 *
 * The figures on the left are real numbers from the catalogue rather than invented ones, which
 * is why they arrive as a prop: "12 products from 2 stores" is worth saying, "1000s of products"
 * is not.
 */

import type { ReactNode } from "react";
import Link from "next/link";
import { PackageCheck, ShieldCheck, Store } from "lucide-react";

export interface AuthShellProps {
  children: ReactNode;
  /** What the right-hand panel is for, used in the heading and announced to a screen reader. */
  title: string;
  subtitle: string;
  /** Live counts for the brand panel. Absent while they load, rather than zero. */
  stats?: { products: number; stores: number; categories: number } | null;
}

const POINTS = [
  { icon: Store, text: "Every store is a real seller, with its own catalogue, reviews and returns." },
  { icon: ShieldCheck, text: "Escrow-style payment: you are not charged until an order is placed." },
  { icon: PackageCheck, text: "One basket across many stores, split into parcels that arrive separately." },
];

export function AuthShell({ children, title, subtitle, stats }: AuthShellProps) {
  return (
    <div className="mp-auth">
      <aside className="mp-auth-brand" aria-hidden="true">
        <div>
          <Link href="/" className="d-inline-flex align-items-center gap-2 mb-4" style={{ color: "#fff", textDecoration: "none" }}>
            <span
              style={{
                width: "2.25rem",
                height: "2.25rem",
                borderRadius: "var(--radius-sm)",
                background: "rgba(255,255,255,0.16)",
                border: "1px solid rgba(255,255,255,0.24)",
                display: "grid",
                placeItems: "center",
                fontFamily: "var(--font-display)",
                fontWeight: 700,
              }}
            >
              M
            </span>
            <span style={{ fontFamily: "var(--font-display)", fontWeight: 600, letterSpacing: "-0.01em" }}>Marketplace</span>
          </Link>

          <h1 className="mp-auth-title">Buy from independent sellers, in one basket.</h1>
          <p className="mp-auth-lead">
            A marketplace where the shop is the seller: their name on the product, their reviews, their returns
            policy. No anonymous listings, nothing added without a person behind it.
          </p>

          <ul className="mp-auth-points">
            {POINTS.map(point => (
              <li key={point.text} className="mp-auth-point">
                <span className="mp-auth-point-icon">
                  <point.icon size={16} aria-hidden />
                </span>
                <span>{point.text}</span>
              </li>
            ))}
          </ul>

          {stats ? (
            <div className="mp-auth-stat">
              <div>
                <span className="mp-auth-stat-value">{stats.products}</span>
                <span className="mp-auth-stat-label">products listed</span>
              </div>
              <div>
                <span className="mp-auth-stat-value">{stats.stores}</span>
                <span className="mp-auth-stat-label">independent stores</span>
              </div>
              <div>
                <span className="mp-auth-stat-value">{stats.categories}</span>
                <span className="mp-auth-stat-label">categories</span>
              </div>
            </div>
          ) : null}
        </div>
      </aside>

      <main className="mp-auth-form">
        <div className="mp-auth-form-inner">
          <h2 style={{ fontSize: "var(--fs-h1)", margin: "0 0 var(--space-2)" }}>{title}</h2>
          <p style={{ color: "var(--text-muted)", margin: "0 0 var(--space-5)" }}>{subtitle}</p>
          {children}
        </div>
      </main>
    </div>
  );
}

/** The seeded accounts, offered as one click rather than as a table to copy from. */
export function DemoAccounts({ onPick }: { onPick: (email: string, password: string) => void }) {
  const accounts = [
    { label: "Customer", email: "customer@marketplace.dev", password: "Customer@123" },
    { label: "Seller", email: "seller@marketplace.dev", password: "Seller@123" },
    { label: "Admin", email: "admin@marketplace.dev", password: "Super@123" },
  ];

  return (
    <div className="mp-demo-accounts mt-4">
      <p className="mp-metric-label" style={{ margin: "0 0 var(--space-2)" }}>
        Demo accounts — click to fill
      </p>
      {accounts.map(account => (
        <button key={account.email} type="button" className="mp-demo-account" onClick={() => onPick(account.email, account.password)}>
          <span>{account.label}</span>
          <small>{account.email}</small>
        </button>
      ))}
    </div>
  );
}
