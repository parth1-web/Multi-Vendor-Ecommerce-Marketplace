import { formatCurrency } from "@/lib/format";

/**
 * A price, with the struck-through original beside it when there is a discount.
 *
 * The discounted figure is announced as such rather than left for the shopper to infer from two
 * numbers: a price and a strikethrough read as a mistake to anyone who does not know the rule.
 */
export function PriceDisplay({
  price,
  compareAtPrice,
  discountPercentage,
  size = "md",
}: {
  price: number;
  compareAtPrice?: number | null;
  discountPercentage?: number;
  size?: "sm" | "md" | "lg";
}) {
  const hasDiscount = compareAtPrice != null && compareAtPrice > price;
  const fontSize = size === "lg" ? "var(--fs-h2)" : size === "sm" ? "var(--fs-sm)" : "var(--fs-body)";

  return (
    <span className="d-inline-flex align-items-baseline" style={{ gap: "var(--space-2)" }}>
      <span className="mp-price" style={{ fontSize, color: hasDiscount ? "var(--danger)" : "var(--text)" }}>
        {formatCurrency(price)}
      </span>

      {hasDiscount ? (
        <>
          <span className="mp-price-strike">{formatCurrency(compareAtPrice)}</span>
          {discountPercentage ? (
            <span style={{ fontSize: "var(--fs-xs)", color: "var(--danger)", fontWeight: 600 }}>
              {Math.round(discountPercentage)}% off
            </span>
          ) : null}
        </>
      ) : null}
    </span>
  );
}
