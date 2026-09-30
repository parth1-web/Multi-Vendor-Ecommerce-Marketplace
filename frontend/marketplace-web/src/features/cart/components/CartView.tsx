"use client";

/**
 * The basket, grouped by seller.
 *
 * Grouping is not decoration: one checkout produces one order that the marketplace splits per
 * seller, so a shopper needs to see whose goods they are and what each store contributes before
 * they commit. Every figure on screen is a backend figure — the component never derives prices,
 * stock, or totals of its own.
 */

import Link from "next/link";
import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, ArrowRight, Minus, Plus, Trash2 } from "lucide-react";
import { Modal } from "react-bootstrap";

import { Breadcrumbs } from "@/components/navigation/Breadcrumbs";
import { ProductPhoto } from "@/components/products/ProductPhoto";
import { EmptyState } from "@/components/shared/Feedback";
import { StatusBadge } from "@/components/shared/Feedback";
import { formatCurrency } from "@/lib/format";
import { errorMessage } from "@/lib/errors";
import { queryKeys } from "@/lib/queryKeys";
import { useToast } from "@/providers/ToastProvider";
import { cartApi } from "@/features/cart/api/cartApi";
import { useCart, useClearCart, useRemoveCartItem, useUpdateCartItem } from "@/features/cart/api/useCart";
import type { CartItem, CartResponse, CartSellerGroup } from "@/types/cart";

export function CartView() {
  const { data, isPending, isError, refetch } = useCart();

  if (isPending) {
    return <CartSkeleton />;
  }

  if (isError || !data) {
    return (
      <div className="mp-page" style={{ paddingBlock: "var(--space-5)" }}>
        <Breadcrumbs trail={[{ name: "Home", href: "/" }, { name: "Basket" }]} />
        <div className="mp-card" style={{ padding: "var(--space-6)", textAlign: "center" }}>
          <p style={{ color: "var(--danger)", fontWeight: 600 }}>We couldn&apos;t load your basket.</p>
          <p style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            Nothing was lost — your basket lives on the server, not in this page.
          </p>
          <button type="button" className="btn btn-sm btn-primary mt-2" onClick={() => void refetch()}>
            Try again
          </button>
        </div>
      </div>
    );
  }

  const activeGroups = data.groups.filter((group) => group.items.some((item) => !item.savedForLater));

  return (
    <div className="mp-page" style={{ paddingBlock: "var(--space-5)" }}>
      <Breadcrumbs trail={[{ name: "Home", href: "/" }, { name: "Basket" }]} />

      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Shopping cart</h1>
          <p className="mp-page-subtitle">
            {data.totalQuantity} {data.totalQuantity === 1 ? "item" : "items"} · {data.itemCount}{" "}
            {data.itemCount === 1 ? "line" : "lines"}
          </p>
        </div>
      </div>

      {activeGroups.length === 0 ? (
        <EmptyState
          title="Your basket is empty"
          body="Once you add something, it will appear here grouped by the store selling it."
          action={
            <Link href="/products" className="btn btn-sm btn-primary">
              Continue shopping
            </Link>
          }
        />
      ) : (
        <div className="row g-4">
          <div className="col-12 col-lg-8">
            <div className="mp-stack">
              {activeGroups.map((group) => (
                <CartSellerGroupView key={group.sellerId} group={group} currency={data.currency} />
              ))}
            </div>
          </div>

          <div className="col-12 col-lg-4">
            <CartSummary cart={data} />
          </div>
        </div>
      )}
    </div>
  );
}

function CartSellerGroupView({ group, currency }: { group: CartSellerGroup; currency: string }) {
  const items = group.items.filter((item) => !item.savedForLater);

  return (
    <section className="mp-card" style={{ padding: "var(--space-4)" }} aria-labelledby={`seller-${group.sellerId}`}>
      <div className="mp-spread" style={{ marginBottom: "var(--space-2)" }}>
        <h2
          className="mp-section-title d-flex align-items-center"
          id={`seller-${group.sellerId}`}
          style={{ fontSize: "var(--fs-h3)", gap: "var(--space-2)", minWidth: 0 }}
        >
          {group.storeLogoUrl ? (
            // eslint-disable-next-line @next/next/no-img-element
            <img
              src={group.storeLogoUrl}
              alt=""
              width={28}
              height={28}
              loading="lazy"
              decoding="async"
              style={{ width: "1.75rem", height: "1.75rem", objectFit: "cover", borderRadius: "50%", flex: "none" }}
            />
          ) : null}
          <Link href={`/stores/${group.storeSlug}`} style={{ color: "var(--text)" }} className="mp-truncate">
            {group.storeName}
          </Link>
        </h2>
        <span
          style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)", fontVariantNumeric: "tabular-nums", flex: "none" }}
        >
          {formatCurrency(group.subtotal, currency)}
        </span>
      </div>

      <ul className="list-unstyled mb-0">
        {items.map((item) => (
          <CartLine key={item.id} item={item} currency={currency} />
        ))}
      </ul>
    </section>
  );
}

function CartLine({ item, currency }: { item: CartItem; currency: string }) {
  const queryClient = useQueryClient();
  const { push } = useToast();
  const updateItem = useUpdateCartItem();
  const removeItem = useRemoveCartItem();
  const [error, setError] = useState<string | null>(null);

  const busy = updateItem.isPending || removeItem.isPending;

  const changeQuantity = (quantity: number) => {
    setError(null);
    updateItem.mutate(
      { itemId: item.id, quantity },
      {
        onError: (failure) =>
          setError(`Unable to update this item. ${errorMessage(failure, "Please try again.")}`),
      },
    );
  };

  const remove = () => {
    setError(null);
    removeItem.mutate(item.id, {
      onSuccess: () =>
        push({
          tone: "success",
          title: `Removed ${item.productName}`,
          // Undo re-adds the same variant in the same quantity through the real add endpoint,
          // so the restored basket is backend state, not a local simulation of it.
          action: {
            label: "Undo",
            onClick: () => {
              cartApi
                .addItem(item.productId, item.productVariantId, item.quantity)
                .then((cart) => {
                  queryClient.setQueryData(queryKeys.cart.detail(), cart);
                  push({ tone: "success", title: "Added back to your basket" });
                })
                .catch((failure: unknown) =>
                  push({ tone: "danger", title: "Could not restore the item", body: errorMessage(failure) }),
                );
            },
          },
        }),
      onError: (failure) => setError(`Unable to remove this item. ${errorMessage(failure, "Please try again.")}`),
    });
  };

  return (
    <li className="mp-cart-line">
      <div className="mp-cart-line-media">
        {/* The product name link next to this carries the navigation; the image repeats it
            visually only, so it stays out of the tab order and the accessible name. */}
        <Link href={`/products/${item.productSlug}`} tabIndex={-1} aria-hidden>
          <ProductPhoto src={item.productImageUrl} alt="" width={160} height={160} />
        </Link>
      </div>

      <div className="mp-cart-line-info">
        <Link
          href={`/products/${item.productSlug}`}
          style={{ color: "var(--text)", fontSize: "var(--fs-sm)", fontWeight: 500 }}
        >
          {item.productName}
        </Link>
        <p style={{ color: "var(--text-subtle)", fontSize: "var(--fs-xs)", margin: "0.15rem 0 0" }}>
          {item.variantName} · {item.sku}
        </p>
        <p style={{ color: "var(--text-muted)", fontSize: "var(--fs-xs)", margin: "0.15rem 0 0" }}>
          {formatCurrency(item.unitPrice, currency)} each
        </p>

        {item.priceChanged ? (
          <p style={{ margin: "var(--space-1) 0 0" }}>
            <StatusBadge tone="warning">Price changed</StatusBadge>
          </p>
        ) : null}
        {item.priceChangeNote ? (
          <p style={{ margin: "0.15rem 0 0", color: "var(--text-muted)", fontSize: "var(--fs-xs)" }}>
            {item.priceChangeNote}
          </p>
        ) : null}

        {!item.isInStock ? (
          <p style={{ margin: "var(--space-1) 0 0" }}>
            <StatusBadge tone="danger">Out of stock</StatusBadge>
          </p>
        ) : null}

        {error ? (
          <p role="alert" style={{ margin: "var(--space-1) 0 0", color: "var(--danger)", fontSize: "var(--fs-xs)" }}>
            {error}
          </p>
        ) : null}
      </div>

      <button
        type="button"
        className="btn btn-sm mp-cart-line-remove"
        disabled={busy}
        onClick={remove}
        aria-label={`Remove ${item.productName} from the basket`}
        style={{ color: "var(--text-subtle)" }}
      >
        <Trash2 size={16} aria-hidden />
      </button>

      <div className="mp-cart-line-actions">
        <div className="d-flex align-items-center" style={{ gap: "var(--space-1)" }}>
          <button
            type="button"
            className="btn btn-sm btn-outline-secondary"
            disabled={busy || item.quantity <= 1}
            onClick={() => changeQuantity(item.quantity - 1)}
            aria-label={`Reduce quantity of ${item.productName}`}
          >
            <Minus size={14} aria-hidden />
          </button>
          <span
            style={{ minWidth: "2rem", textAlign: "center", fontVariantNumeric: "tabular-nums" }}
            aria-live="polite"
            aria-label={`Quantity of ${item.productName}: ${item.quantity}`}
          >
            {item.quantity}
          </span>
          <button
            type="button"
            className="btn btn-sm btn-outline-secondary"
            disabled={busy || item.quantity >= item.availableQuantity}
            onClick={() => changeQuantity(item.quantity + 1)}
            aria-label={`Increase quantity of ${item.productName}`}
          >
            <Plus size={14} aria-hidden />
          </button>
          {busy ? (
            <span className="visually-hidden" role="status">
              Updating…
            </span>
          ) : null}
        </div>

        <span className="mp-price" style={{ fontVariantNumeric: "tabular-nums" }} aria-label={`Line total ${formatCurrency(item.lineTotal, currency)}`}>
          {formatCurrency(item.lineTotal, currency)}
        </span>
      </div>
    </li>
  );
}

function CartSummary({ cart }: { cart: CartResponse }) {
  const clearCart = useClearCart();
  const [confirmingClear, setConfirmingClear] = useState(false);

  // A zero shipping figure on a non-empty basket is the backend's free-shipping rule firing, not
  // a missing value: the service computes it from the subtotal on every read.
  const shippingLabel =
    cart.subtotal > 0 && cart.estimatedShipping === 0 ? "Free" : formatCurrency(cart.estimatedShipping, cart.currency);

  return (
    <>
      <div className="mp-card mp-cart-summary" style={{ padding: "var(--space-4)" }}>
        <h2 className="mp-section-title" style={{ fontSize: "var(--fs-h3)" }}>
          Summary
        </h2>

        <dl className="mp-stack-sm mt-3 mb-0" style={{ fontSize: "var(--fs-sm)" }}>
          <div className="d-flex justify-content-between">
            <dt className="mp-metric-label">Subtotal</dt>
            <dd className="mb-0" style={{ fontVariantNumeric: "tabular-nums" }}>
              {formatCurrency(cart.subtotal, cart.currency)}
            </dd>
          </div>
          <div className="d-flex justify-content-between">
            <dt className="mp-metric-label">Estimated shipping</dt>
            <dd className="mb-0" style={{ fontVariantNumeric: "tabular-nums" }}>
              {shippingLabel}
            </dd>
          </div>
          <div
            className="d-flex justify-content-between"
            style={{ borderTop: "1px solid var(--border)", paddingTop: "var(--space-2)" }}
          >
            <dt style={{ fontWeight: 600 }}>Estimated total</dt>
            <dd className="mb-0 mp-price" style={{ fontVariantNumeric: "tabular-nums" }}>
              {formatCurrency(cart.estimatedTotal, cart.currency)}
            </dd>
          </div>
        </dl>

        <Link href="/checkout" className="btn btn-primary w-100 mt-3">
          Proceed to checkout
          <ArrowRight size={16} aria-hidden />
        </Link>

        <p style={{ color: "var(--text-subtle)", fontSize: "var(--fs-xs)", marginTop: "var(--space-2)", marginBottom: 0 }}>
          Shipping and tax are confirmed at checkout.
        </p>

        <div className="d-flex justify-content-between align-items-center mt-3">
          <Link
            href="/products"
            className="d-inline-flex align-items-center"
            style={{ gap: "0.35rem", color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}
          >
            <ArrowLeft size={14} aria-hidden />
            Continue shopping
          </Link>
          <button
            type="button"
            className="btn btn-sm btn-link p-0"
            style={{ color: "var(--danger)", fontSize: "var(--fs-sm)" }}
            onClick={() => setConfirmingClear(true)}
          >
            Clear basket
          </button>
        </div>
      </div>

      <Modal show={confirmingClear} onHide={() => setConfirmingClear(false)} centered aria-labelledby="clear-basket-title">
        <Modal.Header closeButton>
          <Modal.Title id="clear-basket-title">Clear your basket?</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            All {cart.totalQuantity} {cart.totalQuantity === 1 ? "item" : "items"} will be removed from your basket.
            This cannot be undone.
          </p>
        </Modal.Body>
        <Modal.Footer>
          <button type="button" className="btn btn-outline-secondary" onClick={() => setConfirmingClear(false)}>
            Cancel
          </button>
          <button
            type="button"
            className="btn btn-danger"
            disabled={clearCart.isPending}
            aria-busy={clearCart.isPending}
            onClick={() =>
              clearCart.mutate(undefined, {
                onSuccess: () => setConfirmingClear(false),
              })
            }
          >
            {clearCart.isPending ? "Clearing…" : "Clear basket"}
          </button>
        </Modal.Footer>
      </Modal>
    </>
  );
}

function CartSkeleton() {
  return (
    <div className="mp-page" style={{ paddingBlock: "var(--space-5)" }} aria-hidden>
      <div className="mp-skeleton mb-2" style={{ height: "0.85rem", width: "12rem" }} />
      <div className="mp-skeleton mb-3" style={{ height: "2rem", width: "16rem" }} />
      <div className="row g-4">
        <div className="col-12 col-lg-8">
          <div className="mp-stack">
            {[0, 1].map((group) => (
              <div key={group} className="mp-card" style={{ padding: "var(--space-4)" }}>
                <div className="mp-skeleton" style={{ height: "1.25rem", width: "40%" }} />
                {[0, 1].map((line) => (
                  <div key={line} className="d-flex" style={{ gap: "var(--space-3)", padding: "var(--space-3) 0" }}>
                    <div className="mp-skeleton" style={{ width: "4.5rem", height: "4.5rem", flex: "none" }} />
                    <div style={{ flex: 1 }}>
                      <div className="mp-skeleton" style={{ height: "0.9rem", width: "70%" }} />
                      <div className="mp-skeleton mt-2" style={{ height: "0.75rem", width: "40%" }} />
                    </div>
                  </div>
                ))}
              </div>
            ))}
          </div>
        </div>
        <div className="col-12 col-lg-4">
          <div className="mp-card" style={{ padding: "var(--space-4)" }}>
            <div className="mp-skeleton" style={{ height: "1.25rem", width: "50%" }} />
            <div className="mp-skeleton mt-3" style={{ height: "0.9rem", width: "100%" }} />
            <div className="mp-skeleton mt-2" style={{ height: "0.9rem", width: "100%" }} />
            <div className="mp-skeleton mt-2" style={{ height: "1.25rem", width: "60%" }} />
            <div className="mp-skeleton mt-3" style={{ height: "2.5rem", width: "100%" }} />
          </div>
        </div>
      </div>
    </div>
  );
}
