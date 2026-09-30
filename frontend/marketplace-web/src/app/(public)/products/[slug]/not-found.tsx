/**
 * No such product: removed, never existed, or unpublished.
 *
 * A 404 is an answer, not a failure, so there is no retry here — only a way back to somewhere
 * that exists.
 */

import Link from "next/link";

export default function ProductNotFound() {
  return (
    <div className="mp-page" style={{ paddingBlock: "var(--space-5)" }}>
      <div className="mp-card" style={{ padding: "var(--space-5)", textAlign: "center" }}>
        <h1 className="mp-page-title">Product not found</h1>
        <p style={{ color: "var(--text-muted)" }}>
          This product may have been removed or is no longer available.
        </p>
        <Link href="/products" className="btn btn-sm btn-primary">
          Continue shopping
        </Link>
      </div>
    </div>
  );
}
