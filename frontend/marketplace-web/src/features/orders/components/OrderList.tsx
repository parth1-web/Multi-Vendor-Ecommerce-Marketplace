/**
 * The order history.
 *
 * A signed-in person's own orders, newest first, with the things somebody actually looks for:
 * what it was, whether it is paid, and where it has got to.
 */

"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";

import { EmptyState, ErrorState } from "@/components/shared/Feedback";
import { StatusBadge } from "@/components/shared/Feedback";
import { orderApi } from "@/features/orders/api/orderApi";
import { formatCurrency, formatDate } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";
import { useAuth } from "@/providers/AuthProvider";
import type { OrderStatus } from "@/types/order";

export function OrderList() {
  const router = useRouter();
  const { isAuthenticated, isHydrating } = useAuth();
  const [page, setPage] = useState(1);

  const orders = useQuery({
    queryKey: queryKeys.orders.list({ page }),
    queryFn: () => orderApi.list(page),
    enabled: isAuthenticated,
  });

  useEffect(() => {
    if (!isHydrating && !isAuthenticated) {
      router.replace("/login?returnUrl=%2Forders");
    }
  }, [isAuthenticated, isHydrating, router]);

  if (!isAuthenticated) {
    return <div className="mp-skeleton" style={{ height: "16rem", borderRadius: "var(--radius)" }} />;
  }

  return (
    <div className="mp-stack">
      {orders.isPending ? (
        <div className="mp-skeleton" style={{ height: "12rem", borderRadius: "var(--radius)" }} />
      ) : orders.isError || !orders.data ? (
        <ErrorState message="We could not load your orders." />
      ) : orders.data.items.length === 0 ? (
        <EmptyState
          title="No orders yet"
          body="When you place an order it will appear here, with its progress and its receipt."
          action={
            <Link href="/products" className="btn btn-sm btn-primary">
              Start shopping
            </Link>
          }
        />
      ) : (
        <>
          <div className="mp-stack-sm">
            {orders.data.items.map((order) => (
              <article key={order.id} className="mp-card" style={{ padding: "var(--space-4)" }}>
                <div className="mp-spread" style={{ marginBottom: "var(--space-3)" }}>
                  <div>
                    <Link href={`/orders/${order.id}`} style={{ color: "var(--text)", fontWeight: 600 }}>
                      {order.orderNumber}
                    </Link>
                    <p style={{ margin: 0, color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                      {formatDate(order.placedAt)}
                      {order.sellerNames ? ` · ${order.sellerNames}` : ""}
                    </p>
                  </div>

                  <div className="d-flex align-items-center" style={{ gap: "var(--space-3)" }}>
                    <StatusBadge tone={statusTone(order.status)}>{order.status}</StatusBadge>
                    <span className="mp-price">{formatCurrency(order.totalAmount, order.currency)}</span>
                  </div>
                </div>

                <div className="d-flex align-items-center" style={{ gap: "var(--space-3)" }}>
                  {order.firstProductImageUrl ? (
                    // eslint-disable-next-line @next/next/no-img-element
                    <img
                      src={order.firstProductImageUrl}
                      alt={order.firstProductName ?? "Product"}
                      width={48}
                      height={48}
                      style={{ width: "3rem", height: "3rem", objectFit: "cover", borderRadius: "var(--radius-sm)" }}
                    />
                  ) : null}
                  <p style={{ margin: 0, fontSize: "var(--fs-sm)", color: "var(--text-muted)" }}>
                    {order.firstProductName ?? "Order"}
                    {order.itemCount > 1 ? ` and ${order.itemCount - 1} more` : ""}
                  </p>
                  {!order.isPaid ? (
                    <span style={{ color: "var(--warning)", fontSize: "var(--fs-xs)", marginLeft: "auto" }}>Awaiting payment</span>
                  ) : null}
                </div>
              </article>
            ))}
          </div>

          {orders.data.totalPages > 1 ? (
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
        </>
      )}
    </div>
  );
}

function statusTone(status: OrderStatus): "success" | "warning" | "danger" | "info" {
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
