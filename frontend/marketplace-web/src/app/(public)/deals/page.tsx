/**
 * The deals page: every discount code that can be used right now.
 *
 * A marketplace with a checkout that accepts a code, and no way to find out what the codes are, has
 * a feature nobody can reach. The codes themselves are the server's, and the page's only job is to
 * show them so a shopper can copy one and use it.
 *
 * The page is a server render. A list of codes changes when an administrator stops one, not when
 * somebody looks at it, and it has to be in the HTML for a search engine and for a person on a
 * slow connection.
 */

import Link from "next/link";
import { Tag, TicketPercent } from "lucide-react";

import { ApiError, serverGet } from "@/lib/serverApi";
import { formatCurrency, formatDate } from "@/lib/format";
import { pageMetadata } from "@/lib/seo";
import type { PublicCoupon } from "@/features/promotions/api/publicCouponApi";

export const revalidate = 120;

export const metadata = pageMetadata({
  title: "Deals",
  description: "Every discount code currently running, with what each one takes off and when it ends.",
  path: "/deals",
});

export default async function DealsPage() {
  const coupons = await readCoupons();

  return (
    <div className="mp-page section">
      <header className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Deals</h1>
          <p className="mp-page-subtitle">
            Every code that can be used at checkout right now. One per basket, applied on the payment step.
          </p>
        </div>
      </header>

      {coupons.length === 0 ? (
        <div className="mp-card" style={{ padding: "var(--space-6)" }}>
          <TicketPercent size={28} aria-hidden style={{ color: "var(--text-subtle)" }} />
          <h2 style={{ fontSize: "var(--fs-h3)", margin: "var(--space-3) 0 var(--space-2)" }}>
            No codes are running
          </h2>
          <p style={{ color: "var(--text-muted)", margin: 0 }}>
            Nothing to save right now. The catalogue is worth a look anyway.
          </p>
          <Link href="/products" className="btn btn-primary mt-3">
            Browse the catalogue
          </Link>
        </div>
      ) : (
        <>
          <ul className="list-unstyled mb-0" style={{ display: "grid", gap: "var(--space-4)" }}>
            {coupons.map(coupon => (
              <li key={coupon.code} className="mp-deal">
                <div className="mp-deal-code">
                  <Tag size={15} aria-hidden />
                  <span>{coupon.code}</span>
                </div>

                <div style={{ flex: 1, minWidth: "12rem" }}>
                  <p style={{ margin: 0, fontWeight: 600 }}>{coupon.discountLabel}</p>
                  <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
                    {coupon.description ?? "Applies to the whole marketplace."}
                  </p>
                  <p style={{ margin: "0.35rem 0 0", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                    {coupon.minimumOrderAmount
                      ? `On baskets over ${formatCurrency(coupon.minimumOrderAmount)} · `
                      : "No minimum · "}
                    ends {formatDate(coupon.endsAt)}
                  </p>
                </div>
              </li>
            ))}
          </ul>

          <p style={{ margin: "var(--space-6) 0 0", color: "var(--text-subtle)", fontSize: "var(--fs-sm)" }}>
            A code is entered on the payment step of checkout. If a basket does not meet the minimum for a code, or a code has
            run out of uses, the quote says so before you pay rather than after.
          </p>
        </>
      )}
    </div>
  );
}

/**
 * The codes, or none.
 *
 * An API that is down should leave this page saying there is nothing running rather than serving a
 * 500: a shopper's basket does not depend on it, and a broken deals page is worse than an empty
 * one.
 */
async function readCoupons(): Promise<PublicCoupon[]> {
  try {
    return await serverGet<PublicCoupon[]>("/api/coupons/public", 120);
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) {
      return [];
    }

    return [];
  }
}
