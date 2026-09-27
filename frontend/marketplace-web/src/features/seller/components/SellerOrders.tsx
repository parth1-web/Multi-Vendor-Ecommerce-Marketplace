/**
 * A seller's orders: their own half of each marketplace order, and the only half they can move.
 *
 * The status filter is a row of links rather than a form, so a filtered view is a URL somebody
 * can share and the back button works. Each row states what the seller earns after commission,
 * because that is the number they are actually working towards.
 */

"use client";

import Link from "next/link";
import { useState } from "react";
import { useQuery } from "@tanstack/react-query";

import { EmptyState, ErrorState, StatusBadge } from "@/components/shared/Feedback";
import { sellerApi } from "@/features/seller/api/sellerApi";
import { cx, formatCurrency } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";

const STATUSES = ["Pending", "Confirmed", "Processing", "Packed", "Shipped", "Delivered", "Cancelled"] as const;

export function SellerOrders() {
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState<string>("");

  const orders = useQuery({
    queryKey: queryKeys.seller.orders({ page, status }),
    queryFn: () => sellerApi.orders({ page, pageSize: 20, status: status || undefined }),
  });

  return (
    <div className="mp-stack">
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
        {STATUSES.map(value => (
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

      {orders.isPending ? (
        <div className="mp-skeleton" style={{ height: "14rem", borderRadius: "var(--radius)" }} />
      ) : orders.isError || !orders.data ? (
        <ErrorState message="We could not load your orders." />
      ) : orders.data.items.length === 0 ? (
        <EmptyState
          title={status ? `Nothing ${status.toLowerCase()}` : "No orders yet"}
          body={
            status
              ? "Try another status, or clear the filter to see everything."
              : "When a shopper buys from your store, their order appears here."
          }
        />
      ) : (
        <div className="mp-stack-sm">
          {orders.data.items.map(order => (
            <article key={order.id} className="mp-card" style={{ padding: "var(--space-4)" }}>
              <div className="mp-spread">
                <div>
                  <Link href={`/seller/orders/${order.id}`} style={{ color: "var(--text)", fontWeight: 600 }}>
                    {order.sellerOrderNumber}
                  </Link>
                  <p style={{ margin: 0, color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                    Part of {order.orderNumber} · {order.itemCount} {order.itemCount === 1 ? "item" : "items"}
                  </p>
                </div>

                <div className="d-flex align-items-center" style={{ gap: "var(--space-3)" }}>
                  <StatusBadge tone={orderTone(order.status)}>{order.status}</StatusBadge>
                  <span style={{ minWidth: "6rem", textAlign: "right" }}>
                    <span className="mp-metric-label" style={{ display: "block" }}>
                      You earn
                    </span>
                    <span style={{ fontWeight: 600 }}>{formatCurrency(order.sellerEarnings)}</span>
                  </span>
                </div>
              </div>

              <div className="d-flex flex-wrap mt-3" style={{ gap: "var(--space-3)", fontSize: "var(--fs-sm)", color: "var(--text-muted)" }}>
                <span>Order total {formatCurrency(order.totalAmount)}</span>
                <span>Commission {formatCurrency(order.commissionAmount)} ({(order.commissionRate * 100).toFixed(1)}%)</span>
                {order.trackingNumber ? <span>Tracking {order.trackingNumber}</span> : null}
              </div>
            </article>
          ))}
        </div>
      )}

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

function orderTone(status: string): "success" | "warning" | "danger" | "info" {
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
