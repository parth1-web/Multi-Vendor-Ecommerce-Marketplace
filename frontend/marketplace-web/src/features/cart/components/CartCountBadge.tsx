"use client";

import Link from "next/link";
import { ShoppingBag } from "lucide-react";

import { useCart } from "@/features/cart/api/useCart";

/**
 * The header basket control.
 *
 * Cart state already lives in TanStack Query, so this reads the shared basket rather than
 * fetching its own. While it loads or fails—or when the basket is empty—it is simply the cart
 * link, because a count that cannot be trusted is worse than no count.
 */
export function CartCountBadge() {
  const { data } = useCart();
  const count = data?.totalQuantity ?? 0;

  return (
    <Link
      href="/cart"
      className="btn btn-sm mp-icon-button"
      aria-label={count > 0 ? `Cart, ${count} ${count === 1 ? "item" : "items"}` : "Cart"}
    >
      <span style={{ position: "relative", display: "inline-flex" }}>
        <ShoppingBag size={18} aria-hidden />
        {count > 0 ? (
          <span className="mp-cart-count" aria-hidden>
            {count > 99 ? "99+" : count}
          </span>
        ) : null}
      </span>
    </Link>
  );
}
