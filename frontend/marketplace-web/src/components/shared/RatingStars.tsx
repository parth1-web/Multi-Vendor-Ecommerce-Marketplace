import { Star } from "lucide-react";

import { cx, formatNumber } from "@/lib/format";

/**
 * A star rating. The number is always shown next to the stars: stars alone are unreadable to
 * anyone who cannot distinguish them quickly, and a bare "4.5" is easier to compare.
 */
export function RatingStars({
  rating,
  count,
  size = 14,
  showCount = true,
}: {
  rating: number;
  count?: number;
  size?: number;
  showCount?: boolean;
}) {
  const rounded = Math.round(rating);
  const hasRating = count === undefined || count > 0;

  if (!hasRating || rating <= 0) {
    return (
      <span style={{ color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }} aria-label="No ratings yet">
        No ratings yet
      </span>
    );
  }

  return (
    <span
      className="d-inline-flex align-items-center"
      style={{ gap: "0.25rem" }}
      aria-label={`Rated ${formatNumber(rating)} out of 5${count === undefined ? "" : ` from ${count} reviews`}`}
    >
      <span className="d-inline-flex" aria-hidden>
        {[1, 2, 3, 4, 5].map((star) => (
          <Star
            key={star}
            size={size}
            strokeWidth={1.75}
            fill={star <= rounded ? "var(--warning)" : "transparent"}
            color={star <= rounded ? "var(--warning)" : "var(--border-strong)"}
          />
        ))}
      </span>
      <span style={{ fontSize: "var(--fs-xs)", color: "var(--text-muted)", fontVariantNumeric: "tabular-nums" }}>
        {rating.toFixed(1)}
        {showCount && count !== undefined ? ` (${count})` : ""}
      </span>
    </span>
  );
}

export function DiscountBadge({ percentage }: { percentage: number }) {
  if (percentage <= 0) {
    return null;
  }

  return (
    <span className={cx("mp-badge", "mp-badge-danger")} style={{ position: "absolute", insetInlineStart: "var(--space-2)", insetBlockStart: "var(--space-2)" }}>
      <span className="mp-badge-dot" aria-hidden />
      −{Math.round(percentage)}%
    </span>
  );
}
