/**
 * The marketplace's order book, and the one screen that can see all of it.
 *
 * An order here is a shopper's whole purchase, across every store they bought from, which is why
 * it belongs to an admin rather than to any one seller: a question about a missing item, a refund
 * that spans two stores, or a payment that went through but nothing arrived starts on this screen
 * and not anywhere else.
 *
 * Status changes are deliberate. An admin can move an order, and every move they make is written
 * to the audit log, so the button asks for a reason and the reason is not optional.
 */

"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { EmptyState, ErrorState, StatusBadge } from "@/components/shared/Feedback";
import { adminApi } from "@/features/admin/api/adminApi";
import { errorMessage } from "@/lib/errors";
import { cx, formatCurrency, formatDate } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";
import type { OrderStatus } from "@/types/order";

/**
 * The order state machine, exactly as the API enforces it.
 *
 * An illegal step comes back as 409 with the transition it refused, so the frontend's job is to
 * offer only the legal ones rather than to discover the rule at the moment somebody clicks. The
 * chain is Pending → Confirmed → Processing → Packed → Shipped → Delivered, with Cancelled allowed
 * up to Packed, and Returned or Completed once delivered; the last three states are terminal.
 *
 * Cancelling is marked destructive because it is the one step here a seller cannot undo and a
 * shopper will notice, so it goes through the confirmation rather than firing on a click.
 */
const MOVABLE: { from: OrderStatus[]; to: OrderStatus; label: string; tone: "primary" | "danger" }[] = [
  { from: ["Pending"], to: "Confirmed", label: "Confirm", tone: "primary" },
  { from: ["Confirmed"], to: "Processing", label: "Start processing", tone: "primary" },
  { from: ["Processing"], to: "Packed", label: "Mark packed", tone: "primary" },
  { from: ["Packed"], to: "Shipped", label: "Mark shipped", tone: "primary" },
  { from: ["Shipped"], to: "Delivered", label: "Mark delivered", tone: "primary" },
  { from: ["Delivered"], to: "Completed", label: "Complete the order", tone: "primary" },
  { from: ["Delivered"], to: "Returned", label: "Record a return", tone: "danger" },
  { from: ["Pending", "Confirmed", "Processing", "Packed"], to: "Cancelled", label: "Cancel the order", tone: "danger" },
];

/** True when the API will accept no further step, so the panel says so instead of offering one. */
function isTerminal(status: OrderStatus): boolean {
  return MOVABLE.every(move => !move.from.includes(status));
}

export function AdminOrders() {
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState<OrderStatus | "">("");
  const [search, setSearch] = useState("");
  const [term, setTerm] = useState("");
  const [openId, setOpenId] = useState<string | null>(null);
  const [note, setNote] = useState("");
  const [actionError, setActionError] = useState<string | null>(null);

  const orders = useQuery({
    queryKey: queryKeys.admin.orders({ page, status, term }),
    queryFn: () => adminApi.orders({ page, pageSize: 20, status: status || undefined, search: term || undefined }),
  });

  const detail = useQuery({
    queryKey: queryKeys.admin.order(openId ?? ""),
    queryFn: () => adminApi.order(openId!),
    enabled: Boolean(openId),
  });

  const move = useMutation({
    mutationFn: ({ id, to, reason }: { id: string; to: OrderStatus; reason: string }) => adminApi.updateOrderStatus(id, to, reason),
    onSuccess: async () => {
      setActionError(null);
      setNote("");
      setOpenId(null);
      await queryClient.invalidateQueries({ queryKey: queryKeys.admin.all });
    },
    onError: error => setActionError(errorMessage(error)),
  });

  return (
    <div className="mp-stack">
      <div className="d-flex flex-wrap justify-content-between align-items-end" style={{ gap: "var(--space-3)" }}>
        <nav aria-label="Filter by status" className="d-flex flex-wrap" style={{ gap: "0.4rem" }}>
          <button
            type="button"
            className={cx("btn btn-sm", status === "" ? "btn-primary" : "btn-outline-secondary")}
            onClick={() => {
              setStatus("");
              setPage(1);
            }}
          >
            All
          </button>
          {(["Pending", "Shipped", "Delivered", "Cancelled", "Returned"] as OrderStatus[]).map(value => (
            <button
              key={value}
              type="button"
              className={cx("btn btn-sm", status === value ? "btn-primary" : "btn-outline-secondary")}
              onClick={() => {
                setStatus(value);
                setPage(1);
              }}
            >
              {value}
            </button>
          ))}
        </nav>

        <form
          className="d-flex"
          style={{ gap: "0.5rem" }}
          onSubmit={event => {
            event.preventDefault();
            setTerm(search.trim());
            setPage(1);
          }}
        >
          <label htmlFor="admin-order-search" className="visually-hidden">
            Search by order number, product or seller
          </label>
          <input
            id="admin-order-search"
            className="mp-input"
            value={search}
            placeholder="Order number, product or store"
            onChange={event => setSearch(event.target.value)}
          />
          <button type="submit" className="btn btn-sm btn-outline-secondary">
            Search
          </button>
        </form>
      </div>

      {actionError ? (
        <p role="alert" className="mp-alert mp-alert-danger">
          {actionError}
        </p>
      ) : null}

      {orders.isPending ? (
        <div className="mp-skeleton" style={{ height: "16rem", borderRadius: "var(--radius)" }} />
      ) : orders.isError || !orders.data ? (
        <ErrorState message="We could not load the order book." />
      ) : orders.data.items.length === 0 ? (
        <EmptyState
          title={term || status ? "Nothing matches" : "No orders yet"}
          body={term || status ? "Try another filter, or clear it to see every order." : "Orders appear here as soon as a shopper places one."}
        />
      ) : (
        <div className="mp-table-wrap">
          <table className="mp-table">
            <caption className="visually-hidden">Every order on the marketplace</caption>
            <thead>
              <tr>
                <th scope="col">Order</th>
                <th scope="col">What</th>
                <th scope="col">Stores</th>
                <th scope="col">Total</th>
                <th scope="col">Paid</th>
                <th scope="col">Status</th>
                <th scope="col" />
              </tr>
            </thead>
            <tbody>
              {orders.data.items.map(order => (
                <tr key={order.id}>
                  <td>
                    <span style={{ fontWeight: 600 }}>{order.orderNumber}</span>
                    <span className="mp-metric-label" style={{ display: "block" }}>
                      {formatDate(order.placedAt)}
                    </span>
                  </td>
                  <td>
                    {order.firstProductName ?? "—"}
                    <span className="mp-metric-label" style={{ display: "block" }}>
                      {order.itemCount} {order.itemCount === 1 ? "item" : "items"}
                    </span>
                  </td>
                  <td>{order.sellerNames ?? `${order.sellerCount}`}</td>
                  <td>{formatCurrency(order.totalAmount)}</td>
                  <td>
                    {order.isPaid ? (
                      <span style={{ color: "var(--success)" }}>Yes</span>
                    ) : (
                      <span style={{ color: "var(--text-subtle)" }}>Not yet</span>
                    )}
                  </td>
                  <td>
                    <StatusBadge tone={orderTone(order.status)}>{order.status}</StatusBadge>
                  </td>
                  <td className="text-end">
                    <button
                      type="button"
                      className="btn btn-sm btn-outline-secondary"
                      aria-expanded={openId === order.id}
                      onClick={() => {
                        setOpenId(openId === order.id ? null : order.id);
                        setActionError(null);
                      }}
                    >
                      {openId === order.id ? "Close" : "Open"}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {openId ? (
        <section className="mp-card" style={{ padding: "var(--space-4)" }} aria-label="Order detail">
          {detail.isPending ? (
            <div className="mp-skeleton" style={{ height: "8rem", borderRadius: "var(--radius)" }} />
          ) : detail.isError || !detail.data ? (
            <ErrorState message="We could not open that order." />
          ) : (
            <div className="mp-stack">
              <div className="mp-spread">
                <div>
                  <h2 style={{ margin: 0, fontSize: "var(--fs-h3)" }}>{detail.data.orderNumber}</h2>
                  <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
                    {formatDate(detail.data.placedAt)} · {detail.data.paymentMethod} ·{" "}
                    {detail.data.paymentStatus}
                    {detail.data.couponCode ? ` · coupon ${detail.data.couponCode}` : ""}
                  </p>
                </div>
                <StatusBadge tone={orderTone(detail.data.status)}>{detail.data.status}</StatusBadge>
              </div>

              <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
                {formatAddress(detail.data.shippingAddress)}
              </p>

              <div className="mp-table-wrap">
                <table className="mp-table">
                  <caption className="visually-hidden">What was bought, and from whom</caption>
                  <thead>
                    <tr>
                      <th scope="col">Item</th>
                      <th scope="col">Store</th>
                      <th scope="col">Quantity</th>
                      <th scope="col">Price</th>
                    </tr>
                  </thead>
                  <tbody>
                    {detail.data.items.map(item => (
                      <tr key={item.id}>
                        <td>{item.productName}</td>
                        <td>{item.storeName}</td>
                        <td>{item.quantity}</td>
                        <td>{formatCurrency(item.unitPrice * item.quantity)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              <div className="d-flex flex-wrap" style={{ gap: "var(--space-4)" }}>
                <span>
                  <span className="mp-metric-label" style={{ display: "block" }}>
                    Subtotal
                  </span>
                  {formatCurrency(detail.data.subtotal)}
                </span>
                {detail.data.discountAmount > 0 ? (
                  <span>
                    <span className="mp-metric-label" style={{ display: "block" }}>
                      Discount
                    </span>
                    {formatCurrency(detail.data.discountAmount)}
                  </span>
                ) : null}
                <span>
                  <span className="mp-metric-label" style={{ display: "block" }}>
                    Shipping
                  </span>
                  {formatCurrency(detail.data.shippingAmount)}
                </span>
                <span>
                  <span className="mp-metric-label" style={{ display: "block" }}>
                    Total
                  </span>
                  <strong>{formatCurrency(detail.data.totalAmount)}</strong>
                </span>
                {detail.data.refundedAmount > 0 ? (
                  <span>
                    <span className="mp-metric-label" style={{ display: "block" }}>
                      Refunded
                    </span>
                    {formatCurrency(detail.data.refundedAmount)}
                  </span>
                ) : null}
              </div>

              {detail.data.sellerOrders.length > 0 ? (
                <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
                  Split into {detail.data.sellerOrders.length} {detail.data.sellerOrders.length === 1 ? "seller order" : "seller orders"}:{" "}
                  {detail.data.sellerOrders.map(part => `${part.sellerOrderNumber} (${part.status})`).join(", ")}
                </p>
              ) : null}

              {MOVABLE.filter(move => move.from.includes(detail.data!.status)).length > 0 ? (
                <div className="mp-stack-sm">
                  <div>
                    <label htmlFor="admin-order-note" className="mp-metric-label">
                      Why? It goes in the audit log.
                    </label>
                    <input
                      id="admin-order-note"
                      className="mp-input"
                      value={note}
                      placeholder="Called the shopper; the parcel was with the courier"
                      onChange={event => setNote(event.target.value)}
                    />
                  </div>

                  <div className="d-flex flex-wrap" style={{ gap: "0.5rem" }}>
                    {MOVABLE.filter(option => option.from.includes(detail.data!.status)).map(option => (
                      <button
                        key={option.to}
                        type="button"
                        className={cx("btn btn-sm", option.tone === "primary" ? "btn-primary" : "btn-outline-danger")}
                        disabled={move.isPending || note.trim().length === 0}
                        title={note.trim().length === 0 ? "A reason is required, and it is recorded" : undefined}
                        onClick={() => move.mutate({ id: detail.data!.id, to: option.to, reason: note.trim() })}
                      >
                        {option.label}
                      </button>
                    ))}
                  </div>
                </div>
              ) : null}

              {/*
                A terminal order says so, rather than showing an empty panel. The API accepts no
                further step from Cancelled, Returned or Completed, and saying that is more use to
                whoever is reading than an empty form.
              */}
              {isTerminal(detail.data.status) ? (
                <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
                  This order is {detail.data.status.toLowerCase()} and accepts no further step. The timeline above is the
                  record of how it got there.
                </p>
              ) : null}
            </div>
          )}
        </section>
      ) : null}

      {orders.data && orders.data.totalPages > 1 ? (
        <nav aria-label="Order pages" className="d-flex justify-content-between align-items-center mt-3">
          <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page <= 1}>
            Newer
          </button>
          <span style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            Page {page} of {orders.data.totalPages}
          </span>
          <button
            type="button"
            className="btn btn-sm btn-outline-secondary"
            onClick={() => setPage(p => Math.min(orders.data!.totalPages, p + 1))}
            disabled={page >= orders.data.totalPages}
          >
            Older
          </button>
        </nav>
      ) : null}
    </div>
  );
}

function orderTone(status: OrderStatus): "success" | "warning" | "danger" | "info" {
  switch (status) {
    case "Delivered":
    case "Completed":
      return "success";
    case "Cancelled":
    case "Returned":
      return "danger";
    case "Pending":
      return "warning";
    default:
      return "info";
  }
}

function formatAddress(address: { line1: string; line2: string | null; city: string; state: string | null; postalCode: string; country: string }): string {
  return [address.line1, address.line2, address.city, address.state, address.postalCode, address.country]
    .filter(part => Boolean(part && part.trim().length > 0))
    .join(", ");
}
