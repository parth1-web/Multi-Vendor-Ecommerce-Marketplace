"use client";

/**
 * The buy box: variant choice, quantity, and the button that actually adds to the basket.
 *
 * A variant is chosen explicitly rather than defaulted to the first one. A shopper who wanted
 * the small one and silently received the large is worse off than one who is asked to choose.
 */

import { Minus, Plus, ShoppingBag } from "lucide-react";
import { useState } from "react";

import { StockBadge } from "@/components/shared/Feedback";
import { useAddToCart } from "@/features/cart/api/useCart";
import type { ProductVariant } from "@/types/product";

export function VariantPicker({
  productId,
  variants,
  initialVariantId,
}: {
  productId: string;
  variants: ProductVariant[];
  /** A variant id from the URL, honoured only when it names a real, active option. */
  initialVariantId?: string;
}) {
  const purchasable = variants.filter((variant) => variant.isActive);
  const [selectedId, setSelectedId] = useState<string | null>(() =>
    purchasable.some((variant) => variant.id === initialVariantId)
      ? (initialVariantId as string)
      : purchasable.length === 1
        ? purchasable[0].id
        : null,
  );
  const [quantity, setQuantity] = useState(1);

  const selected = purchasable.find((variant) => variant.id === selectedId) ?? null;
  const addToCart = useAddToCart();

  if (purchasable.length === 0) {
    return <p style={{ color: "var(--text-muted)" }}>This product has no options available right now.</p>;
  }

  const max = Math.max(1, selected?.availableQuantity ?? 1);

  return (
    <div className="mp-stack-sm">
      {purchasable.length > 1 ? (
        <fieldset style={{ border: 0, padding: 0, margin: 0 }}>
          <legend className="mp-metric-label p-0 mb-2">Options</legend>
          <div className="d-flex flex-wrap" style={{ gap: "var(--space-2)" }}>
            {purchasable.map((variant) => (
              <button
                key={variant.id}
                type="button"
                onClick={() => {
                  setSelectedId(variant.id);
                  setQuantity(1);
                }}
                disabled={variant.availableQuantity <= 0}
                aria-pressed={variant.id === selectedId}
                title={optionSummary(variant)}
                className="btn btn-sm"
                style={{
                  borderColor: variant.id === selectedId ? "var(--brand-600)" : "var(--border)",
                  color: variant.availableQuantity <= 0 ? "var(--text-subtle)" : "var(--text)",
                  backgroundColor: variant.id === selectedId ? "var(--violet-bg)" : "transparent",
                  textDecoration: variant.availableQuantity <= 0 ? "line-through" : "none",
                }}
              >
                {variant.name}
              </button>
            ))}
          </div>
        </fieldset>
      ) : null}

      <div className="mp-spread">
        <span className="mp-metric-label">Quantity</span>
        <div className="d-flex align-items-center" style={{ gap: "var(--space-2)" }}>
          <button
            type="button"
            className="btn btn-sm btn-outline-secondary"
            onClick={() => setQuantity((value) => Math.max(1, value - 1))}
            disabled={quantity <= 1}
            aria-label="Decrease quantity"
          >
            <Minus size={14} aria-hidden />
          </button>
          <span style={{ minWidth: "2rem", textAlign: "center", fontVariantNumeric: "tabular-nums" }} aria-live="polite">
            {quantity}
          </span>
          <button
            type="button"
            className="btn btn-sm btn-outline-secondary"
            onClick={() => setQuantity((value) => Math.min(max, value + 1))}
            disabled={quantity >= max}
            aria-label="Increase quantity"
          >
            <Plus size={14} aria-hidden />
          </button>
        </div>
      </div>

      {selected ? <StockBadge available={selected.availableQuantity} lowStockThreshold={selected.lowStockThreshold} /> : null}

      <button
        type="button"
        className="btn btn-primary w-100"
        disabled={!selected || selected.availableQuantity <= 0 || addToCart.isPending}
        aria-describedby={!selected && purchasable.length > 1 ? "variant-help" : undefined}
        onClick={() => selected && addToCart.mutate({ productId, productVariantId: selected.id, quantity })}
      >
        <ShoppingBag size={16} aria-hidden className="me-2" />
        {addToCart.isPending ? "Adding…" : "Add to basket"}
      </button>

      {!selected && purchasable.length > 1 ? (
        <p id="variant-help" style={{ color: "var(--text-muted)", fontSize: "var(--fs-xs)", margin: 0 }}>
          Choose an option to continue.
        </p>
      ) : null}
    </div>
  );
}

/** "Colour: Black, Size: M" — the options a variant stands for, in one readable line. */
function optionSummary(variant: ProductVariant): string | undefined {
  if (variant.options.length === 0) {
    return undefined;
  }

  return variant.options.map((option) => `${option.name}: ${option.value}`).join(", ");
}
