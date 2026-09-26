import type { ReactNode } from "react";

import type { Tone } from "@/lib/constants";

/**
 * A status pill. Every tone ships a dot as well as a colour, because colour alone is not
 * available to everyone and does not survive a monochrome print.
 */
export function StatusBadge({ tone, children }: { tone: Tone; children: ReactNode }) {
  return (
    <span className={`mp-badge mp-badge-${tone}`}>
      <span className="mp-badge-dot" aria-hidden />
      {children}
    </span>
  );
}

/** Stock, phrased the way a shopper needs it: whether they can buy, not a raw count. */
export function StockBadge({ available, lowStockThreshold }: { available: number; lowStockThreshold?: number }) {
  if (available <= 0) {
    return <StatusBadge tone="danger">Out of stock</StatusBadge>;
  }

  if (lowStockThreshold !== undefined && available <= lowStockThreshold) {
    return <StatusBadge tone="warning">Only {available} left</StatusBadge>;
  }

  return <StatusBadge tone="success">In stock</StatusBadge>;
}

export function EmptyState({
  title,
  body,
  action,
}: {
  title: string;
  body?: string;
  action?: ReactNode;
}) {
  return (
    <div
      className="mp-card"
      style={{ padding: "var(--space-7) var(--space-5)", textAlign: "center", borderStyle: "dashed" }}
    >
      <p style={{ fontFamily: "var(--font-display)", fontSize: "var(--fs-h3)", margin: 0 }}>{title}</p>
      {body ? (
        <p style={{ color: "var(--text-muted)", maxWidth: "32rem", margin: "var(--space-2) auto 0" }}>{body}</p>
      ) : null}
      {action ? <div style={{ marginTop: "var(--space-4)" }}>{action}</div> : null}
    </div>
  );
}

export function ErrorState({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <div
      className="mp-card"
      role="alert"
      style={{ padding: "var(--space-5)", borderColor: "var(--danger)", textAlign: "center" }}
    >
      <p style={{ color: "var(--danger)", fontWeight: 600, margin: 0 }}>{message}</p>
      {onRetry ? (
        <button type="button" className="btn btn-sm btn-primary mt-3" onClick={onRetry}>
          Try again
        </button>
      ) : null}
    </div>
  );
}
