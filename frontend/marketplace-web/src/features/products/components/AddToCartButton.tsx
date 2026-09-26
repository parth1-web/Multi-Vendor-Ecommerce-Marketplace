"use client";

/**
 * The one interactive part of a product card, kept to its own file so the card itself can stay
 * a server component. A card that rendered on the server would otherwise be dragged across the
 * client boundary for the sake of a button.
 */

import { ShoppingBag } from "lucide-react";

import { useAddToCart } from "@/features/cart/api/useCart";

export function AddToCartButton({
  productId,
  variantId,
  disabled,
  label = "Add",
}: {
  productId: string;
  variantId: string | null;
  disabled?: boolean;
  label?: string;
}) {
  const addToCart = useAddToCart();

  // A product with variants has to be configured on its detail page first: adding a blind
  // choice is how a customer ends up with the wrong size.
  if (!variantId) {
    return (
      <a
        href={`/products/${productId}`}
        className="btn btn-sm btn-outline-secondary"
        style={{ fontSize: "var(--fs-xs)" }}
      >
        Choose
      </a>
    );
  }

  return (
    <button
      type="button"
      className="btn btn-sm btn-primary"
      disabled={disabled || addToCart.isPending}
      onClick={() => addToCart.mutate({ productId, productVariantId: variantId })}
      aria-label={`Add ${label} to basket`}
      style={{ fontSize: "var(--fs-xs)" }}
    >
      <ShoppingBag size={14} aria-hidden className="me-1" />
      {addToCart.isPending ? "Adding…" : label}
    </button>
  );
}
