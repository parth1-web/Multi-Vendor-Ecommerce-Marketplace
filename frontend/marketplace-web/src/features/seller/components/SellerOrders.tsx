"use client";

/**
 * A seller's orders: their own half of each marketplace order, and the only half they can move.
 *
 * Two things are worth saying out loud on every row. The order number is the seller's own
 * reference, not the marketplace's, because that is the one printed on the label; and the figure
 * next to it is what the seller will actually be paid, after commission, because that is the
 * number they are working towards.
 *
 * Search matches the seller's order number, status and page live in the URL, and every link out of
 * a row carries them, so a seller who filtered to "awaiting you" opens an order and comes back to
 * the same filtered list rather than to page one of everything.
 */

import Link from "next/link";
import { useState } from "react";
import { useQuery } from "@tanstack/react-query";

import { Pagination } from "@/components/navigation/Pagination";
import { EmptyState, ErrorState, StatusBadge } from "@/components/shared/Feedback";
import { sellerApi } from "@/features/seller/api/sellerApi";
import { listContext, useSellerListUrl } from "@/features/seller/lib/listUrl";
import {
  SELLER_ORDER_STATUSES,
  nextOrderSteps,
  orderStatusLabel,
  orderStatusTone,
} from "@/features/seller/lib/orderStatus";
import { formatCurrency, formatDate, formatNumber } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";

const BASE_PATH = "/seller/orders";

export function SellerOrders() {
  const { status, search, page, navigate, filtered } = useSellerListUrl(BASE_PATH, {
    statuses: SELLER_ORDER_STATUSES,
  });

  const orders = useQuery({
    queryKey: queryKeys.seller.orders({ page, status, search }),
    queryFn: () => sellerApi.orders({ page, pageSize: 20, status, search }),
  });

  const context = listContext({ status, search });

  return (
    <div className="mp-stack">
      <OrdersToolbar search={search ?? ""} onSearch={value => navigate({ search: value.trim() || undefined })} />

      <nav aria-label="Filter by status" className="d-flex flex-wrap" style={{ gap: "0.4rem" }}>
        <button
          type="button"
          className={status === undefined ? "btn btn-sm btn-primary" : "btn btn-sm btn-outline-secondary"}
          aria-pressed={status === undefined}
          onClick={() => navigate({ status: undefined })}
        >
          All
        </button>
        {SELLER_ORDER_STATUSES.map(value => (
          <button
            key={value}
            type="button"
            className={status === value ? "btn btn-sm btn-primary" : "btn btn-sm btn-outline-secondary"}
            aria-pressed={status === value}
            onClick={() => navigate({ status: value })}
          >
            {orderStatusLabel(value)}
          </button>
        ))}
      </nav>

      {orders.isPending ? (
        <OrderListSkeleton />
      ) : orders.isError || !orders.data ? (
        <ErrorState message="We could not load your orders." onRetry={() => void orders.refetch()} />
      ) : orders.data.items.length === 0 ? (
        <EmptyState
          title={filtered ? "No orders match those filters" : "No orders yet"}
          body={
            filtered
              ? "Try another status, or clear the search to see everything you have been sent."
              : "When a shopper buys from your store, their order appears here."
          }
          action={
            filtered ? (
              <Link href={BASE_PATH} className="btn btn-sm btn-primary">
                Show every order
              </Link>
            ) : (
              <Link href="/seller/products" className="btn btn-sm btn-primary">
                Check your listings
              </Link>
            )
          }
        />
      ) : (
        <>
          <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            {formatNumber(orders.data.totalCount)} {orders.data.totalCount === 1 ? "order" : "orders"}
            {orders.data.totalPages > 1 ? ` · page ${orders.data.page} of ${orders.data.totalPages}` : ""}
          </p>

          <div className="mp-stack-sm">
            {orders.data.items.map(order => {
              const steps = nextOrderSteps(order.status);

              return (
                <article key={order.id} className="mp-card mp-card-hover" style={{ padding: "var(--space-4)" }}>
                  <div className="mp-spread">
                    <div style={{ minWidth: 0 }}>
                      <Link
                        href={`/seller/orders/${order.id}${context ? `?${context}` : ""}`}
                        style={{ color: "var(--text)", fontWeight: 600 }}
                        aria-label={`Open order ${order.sellerOrderNumber}`}
                      >
                        {order.sellerOrderNumber}
                      </Link>
                      <p style={{ margin: 0, color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                        Part of {order.orderNumber} · {order.itemCount} {order.itemCount === 1 ? "item" : "items"}
                      </p>
                    </div>

                    <div className="d-flex align-items-center flex-shrink-0" style={{ gap: "var(--space-3)" }}>
                      <StatusBadge tone={orderStatusTone(order.status)}>{orderStatusLabel(order.status)}</StatusBadge>
                      <span style={{ minWidth: "6rem", textAlign: "right" }}>
                        <span className="mp-metric-label" style={{ display: "block" }}>
                          You earn
                        </span>
                        <span style={{ fontWeight: 600 }}>{formatCurrency(order.sellerEarnings)}</span>
                      </span>
                    </div>
                  </div>

                  <div
                    className="d-flex flex-wrap mt-3"
                    style={{ gap: "var(--space-3)", fontSize: "var(--fs-sm)", color: "var(--text-muted)" }}
                  >
                    <span>Order total {formatCurrency(order.totalAmount)}</span>
                    {/*
                      The API sends the commission rate as a percentage — 10 means 10% — so it is
                      printed as it arrives. Multiplying it here is how a 10% rate ends up reading
                      as 1000%.
                    */}
                    <span>
                      Commission {formatCurrency(order.commissionAmount)} ({order.commissionRate.toFixed(1)}%)
                    </span>
                    {order.carrierName || order.trackingNumber ? (
                      <span>
                        {order.carrierName ? `${order.carrierName} ` : ""}
                        {order.trackingNumber ?? ""}
                      </span>
                    ) : null}
                    {order.estimatedDeliveryAt ? <span>Due {formatDate(order.estimatedDeliveryAt)}</span> : null}
                  </div>

                  {steps.length > 0 ? (
                    <div className="mt-3">
                      <Link
                        href={`/seller/orders/${order.id}${context ? `?${context}` : ""}`}
                        className="btn btn-sm btn-outline-secondary"
                        aria-label={`Open order ${order.sellerOrderNumber} to ${steps[0].label.toLowerCase()}`}
                      >
                        {steps[0].label}
                      </Link>
                    </div>
                  ) : null}
                </article>
              );
            })}
          </div>

          <Pagination page={orders.data.page} totalPages={orders.data.totalPages} query={{ status, search }} basePath={BASE_PATH} />
        </>
      )}
    </div>
  );
}

function OrdersToolbar({ search, onSearch }: { search: string; onSearch: (value: string) => void }) {
  const [draft, setDraft] = useState(search);

  return (
    <form
      role="search"
      aria-label="Search your orders"
      className="mp-card"
      style={{ padding: "var(--space-3) var(--space-4)" }}
      onSubmit={event => {
        event.preventDefault();
        onSearch(draft);
      }}
    >
      <div className="row g-2 align-items-end">
        <div className="col-12 col-sm-8 col-md-9">
          <label htmlFor="order-search" className="mp-metric-label">
            Search orders
          </label>
          <input
            id="order-search"
            type="search"
            className="form-control form-control-sm"
            value={draft}
            onChange={event => setDraft(event.target.value)}
            placeholder="Your order number, e.g. MP-20261001-F38D774C0619-01"
            autoComplete="off"
          />
        </div>
        <div className="col-12 col-sm-4 col-md-3">
          <button type="submit" className="btn btn-sm btn-primary w-100">
            Search
          </button>
        </div>
      </div>
    </form>
  );
}

function OrderListSkeleton() {
  return (
    <div className="mp-stack-sm" aria-hidden>
      {[0, 1, 2].map(index => (
        <div key={index} className="mp-card" style={{ padding: "var(--space-4)" }}>
          <div className="mp-spread" style={{ marginBottom: "var(--space-3)" }}>
            <div className="mp-skeleton" style={{ height: "1.1rem", width: "10rem" }} />
            <div className="mp-skeleton" style={{ height: "1.25rem", width: "7rem" }} />
          </div>
          <div className="mp-skeleton" style={{ height: "0.9rem", width: "55%" }} />
        </div>
      ))}
    </div>
  );
}