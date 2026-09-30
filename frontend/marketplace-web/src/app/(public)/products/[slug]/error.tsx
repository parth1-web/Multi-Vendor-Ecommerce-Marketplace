"use client";

/**
 * The product page failed, the marketplace did not.
 *
 * Next renders this boundary in place of the page, so a 500 on one product never takes down the
 * header, the basket, or anything else. The message stays generic on purpose: the failure
 * belongs in the server logs, not in front of the shopper.
 */

import Link from "next/link";

export default function ProductError({ reset }: { error: Error & { digest?: string }; reset: () => void }) {

  return (
    <div className="mp-page" style={{ paddingBlock: "var(--space-5)" }}>
      <div className="mp-card" role="alert" style={{ padding: "var(--space-5)", borderColor: "var(--danger)", textAlign: "center" }}>
        <p style={{ color: "var(--danger)", fontWeight: 600, margin: 0 }}>We couldn&apos;t load this product.</p>
        <p style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
          The shop itself is fine — this page just failed to load.
        </p>
        <div className="d-flex justify-content-center flex-wrap" style={{ gap: "var(--space-2)" }}>
          <button type="button" className="btn btn-sm btn-primary" onClick={() => reset()}>
            Try again
          </button>
          <Link href="/products" className="btn btn-sm btn-outline-secondary">
            Continue shopping
          </Link>
        </div>
      </div>
    </div>
  );
}
