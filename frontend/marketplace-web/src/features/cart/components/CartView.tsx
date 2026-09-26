"use client";

/**
 * The basket, grouped by seller.
 *
 * Grouping is not decoration: one checkout produces one order that the marketplace splits per
 * seller, so a shopper needs to see whose goods they are and what each store will charge them
 * for before they commit.
 */

import Link from "next/link";
import { Minus, Plus, ShoppingBag, Trash2 } from "lucide-react";
import { useState } from "react";

import { EmptyState } from "@/components/shared/Feedback";
import { StatusBadge } from "@/components/shared/Feedback";
import { formatCurrency } from "@/lib/format";
import { useCart, useUpdateCartItem, useRemoveCartItem } from "@/features/cart/api/useCart";
import type { CartItem } from "@/types/cart";

export function CartView() {
  const { data, isPending, isError } = useCart();
  const updateItem = useUpdateCartItem();
  const removeItem = useRemoveCartItem();
  const [busyItem, setBusyItem] = useState<string | null>(null);

  if (isPending) {
    return (
      <div className="mp-stack">
        {[0, 1].map((group) => (
          <div key={group} className="mp-card" style={{ padding: "var(--space-4)" }}>
            <div className="mp-skeleton" style={{ height: "1.25rem", width: "40%" }} />
            <div className="mp-skeleton mt-3" style={{ height: "4rem" }} />
          </div>
        ))}
      </div>
    );
  }

  if (isError || !data) {
    return (
      <div className="mp-card" style={{ padding: "var(--space-6)", textAlign: "center" }}>
        <p style={{ color: "var(--danger)" }}>We could not load your basket.</p>
        <button type="button" className="btn btn-sm btn-primary" onClick={() => window.location.reload()}>
          Try again
        </button>
      </div>
    );
  }

  const activeGroups = data.groups.filter((group) => group.items.some((item) => !item.savedForLater));

  if (activeGroups.length === 0) {
    return (
      <EmptyState
        title="Your basket is empty"
        body="Once you add something, it will appear here grouped by the store selling it."
        action={
          <Link href="/products" className="btn btn-sm btn-primary">
            Start shopping
          </Link>
        }
      />
    );
  }

  return (
    <div className="row g-4">
      <div className="col-12 col-lg-8">
        <div className="mp-stack">
          {activeGroups.map((group) => (
            <section key={group.sellerId} className="mp-card" style={{ padding: "var(--space-4)" }} aria-labelledby={`seller-${group.sellerId}`}>
              <div className="mp-spread" style={{ marginBottom: "var(--space-3)" }}>
                <h2 className="mp-section-title" id={`seller-${group.sellerId}`} style={{ fontSize: "var(--fs-h3)" }}>
                  <Link href={`/stores/${group.storeSlug}`} style={{ color: "var(--text)" }}>
                    {group.storeName}
                  </Link>
                </h2>
                <span style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>{formatCurrency(group.subtotal)}</span>
              </div>

              <ul className="list-unstyled mb-0">
                {group.items
                  .filter((item) => !item.savedForLater)
                  .map((item) => (
                    <CartLine
                      key={item.id}
                      item={item}
                      busy={busyItem === item.id}
                      onBusy={setBusyItem}
                      onQuantity={(quantity) => updateItem.mutate({ itemId: item.id, quantity })}
                      onRemove={() => removeItem.mutate(item.id)}
                    />
                  ))}
              </ul>
            </section>
          ))}
        </div>
      </div>

      <div className="col-12 col-lg-4">
        <div className="mp-card" style={{ padding: "var(--space-4)", position: "sticky", top: "calc(var(--header-height) + var(--space-4))" }}>
          <h2 className="mp-section-title" style={{ fontSize: "var(--fs-h3)" }}>
            Summary
          </h2>

          <dl className="mp-stack-sm mt-3 mb-0" style={{ fontSize: "var(--fs-sm)" }}>
            <div className="d-flex justify-content-between">
              <dt className="mp-metric-label">Subtotal</dt>
              <dd className="mb-0">{formatCurrency(data.subtotal)}</dd>
            </div>
            <div className="d-flex justify-content-between">
              <dt className="mp-metric-label">Estimated shipping</dt>
              <dd className="mb-0">{formatCurrency(data.estimatedShipping)}</dd>
            </div>
            <div className="d-flex justify-content-between" style={{ borderTop: "1px solid var(--border)", paddingTop: "var(--space-2)" }}>
              <dt style={{ fontWeight: 600 }}>Estimated total</dt>
              <dd className="mb-0 mp-price">{formatCurrency(data.estimatedTotal)}</dd>
            </div>
          </dl>

          <Link href="/checkout" className="btn btn-primary w-100 mt-3">
            <ShoppingBag size={16} aria-hidden className="me-2" />
            Checkout
          </Link>

          <p style={{ color: "var(--text-subtle)", fontSize: "var(--fs-xs)", marginTop: "var(--space-2)", marginBottom: 0 }}>
            Shipping and tax are confirmed at checkout.
          </p>
        </div>
      </div>
    </div>
  );
}

function CartLine({
  item,
  busy,
  onBusy,
  onQuantity,
  onRemove,
}: {
  item: CartItem;
  busy: boolean;
  onBusy: (id: string | null) => void;
  onQuantity: (quantity: number) => void;
  onRemove: () => void;
}) {
  return (
    <li
      className="d-flex align-items-center flex-wrap"
      style={{ gap: "var(--space-3)", padding: "var(--space-3) 0", borderTop: "1px solid var(--border)" }}
    >
      <div className="mp-skeleton" style={{ width: "4rem", height: "4rem", borderRadius: "var(--radius-sm)", flex: "none" }} aria-hidden />

      <div style={{ flex: 1, minWidth: "10rem" }}>
        <Link href={`/products/${item.productSlug}`} style={{ color: "var(--text)", fontSize: "var(--fs-sm)", fontWeight: 500 }}>
          {item.productName}
        </Link>
        <p style={{ color: "var(--text-subtle)", fontSize: "var(--fs-xs)", margin: 0 }}>
          {item.variantName} · {item.sku}
        </p>

        {item.priceChanged ? (
          <p style={{ margin: "var(--space-1) 0 0" }}>
            <StatusBadge tone="warning">Price changed</StatusBadge>
          </p>
        ) : null}

        {!item.isInStock ? (
          <p style={{ margin: "var(--space-1) 0 0" }}>
            <StatusBadge tone="danger">Out of stock</StatusBadge>
          </p>
        ) : null}
      </div>

      <div className="d-flex align-items-center" style={{ gap: "var(--space-1)" }}>
        <button
          type="button"
          className="btn btn-sm btn-outline-secondary"
          disabled={busy || item.quantity <= 1}
          onClick={() => {
            onBusy(item.id);
            onQuantity(item.quantity - 1);
          }}
          aria-label={`Reduce quantity of ${item.productName}`}
        >
          <Minus size={14} aria-hidden />
        </button>
        <span style={{ minWidth: "2rem", textAlign: "center", fontVariantNumeric: "tabular-nums" }} aria-live="polite">
          {item.quantity}
        </span>
        <button
          type="button"
          className="btn btn-sm btn-outline-secondary"
          disabled={busy || item.quantity >= item.availableQuantity}
          onClick={() => {
            onBusy(item.id);
            onQuantity(item.quantity + 1);
          }}
          aria-label={`Increase quantity of ${item.productName}`}
        >
          <Plus size={14} aria-hidden />
        </button>
      </div>

      <span className="mp-price" style={{ minWidth: "5rem", textAlign: "right" }}>
        {formatCurrency(item.lineTotal)}
      </span>

      <button
        type="button"
        className="btn btn-sm"
        disabled={busy}
        onClick={() => {
          onBusy(item.id);
          onRemove();
        }}
        aria-label={`Remove ${item.productName} from the basket`}
        style={{ color: "var(--text-subtle)" }}
      >
        <Trash2 size={16} aria-hidden />
      </button>
    </li>
  );
}