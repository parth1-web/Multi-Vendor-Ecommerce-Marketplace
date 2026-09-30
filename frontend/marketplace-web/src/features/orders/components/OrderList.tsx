/**
 * The order history.
 *
 * A signed-in person's own orders, newest first, with the things somebody actually looks for:
 * what it was, whether it is paid, and where it has got to. Search, status, sort, and page all
 * live in the URL, so a filtered view is shareable, survives refresh, and works with back and
 * forward — the same contract the catalogue already keeps.
 */

"use client";

import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";

import { Pagination } from "@/components/navigation/Pagination";
import { EmptyState, ErrorState } from "@/components/shared/Feedback";
import { StatusBadge } from "@/components/shared/Feedback";
import { orderApi } from "@/features/orders/api/orderApi";
import { formatCurrency, formatDate } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";
import { useAuth } from "@/providers/AuthProvider";
import type { OrderStatus } from "@/types/order";

const STATUSES: OrderStatus[] = [
  "Pending",
  "Confirmed",
  "Processing",
  "Packed",
  "Shipped",
  "Delivered",
  "Completed",
  "Cancelled",
  "Returned",
];

const SORTS = [
  { value: "newest", label: "Newest first" },
  { value: "oldest", label: "Oldest first" },
  { value: "highest", label: "Highest total" },
  { value: "lowest", label: "Lowest total" },
] as const;

interface OrderFilters {
  status?: OrderStatus;
  search?: string;
  sort?: string;
}

export function OrderList() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const { isAuthenticated, isHydrating } = useAuth();

  const filters = readFilters(searchParams);
  const page = positiveInt(searchParams.get("page"));

  const orders = useQuery({
    queryKey: queryKeys.orders.list({ page, ...filters }),
    queryFn: () => orderApi.list(page, 20, filters),
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

  const navigate = (changes: Record<string, string | undefined>, resetPage = true) => {
    const next = new URLSearchParams(searchParams.toString());

    for (const [key, value] of Object.entries(changes)) {
      if (value === undefined || value === "") {
        next.delete(key);
      } else {
        next.set(key, value);
      }
    }

    if (resetPage) {
      next.delete("page");
    }

    const queryString = next.toString();
    router.push(queryString ? `/orders?${queryString}` : "/orders");
  };

  const filtered = filters.status !== undefined || filters.search !== undefined || filters.sort !== undefined;

  return (
    <div className="mp-stack">
      <OrderToolbar
        key={`${filters.search ?? ""}|${filters.status ?? ""}|${filters.sort ?? ""}`}
        search={filters.search ?? ""}
        status={filters.status ?? ""}
        sort={filters.sort ?? "newest"}
        onSearch={(value) => navigate({ search: value.trim() || undefined })}
        onStatus={(value) => navigate({ status: value || undefined })}
        onSort={(value) => navigate({ sort: value === "newest" ? undefined : value })}
      />

      {orders.isPending ? (
        <OrderListSkeleton />
      ) : orders.isError || !orders.data ? (
        <ErrorState message="We could not load your orders." onRetry={() => void orders.refetch()} />
      ) : orders.data.items.length === 0 ? (
        <EmptyState
          title={filtered ? "No orders match those filters" : "No orders yet"}
          body={
            filtered
              ? "Try a different status, search, or sort — or clear the filters to see everything."
              : "When you place an order it will appear here, with its progress and its receipt."
          }
          action={
            filtered ? (
              <Link href="/orders" className="btn btn-sm btn-primary">
                Clear filters
              </Link>
            ) : (
              <Link href="/products" className="btn btn-sm btn-primary">
                Start shopping
              </Link>
            )
          }
        />
      ) : (
        <>
          <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            {orders.data.totalCount} {orders.data.totalCount === 1 ? "order" : "orders"}
          </p>

          <div className="mp-stack-sm">
            {orders.data.items.map((order) => (
              <article key={order.id} className="mp-card mp-card-hover" style={{ padding: "var(--space-4)" }}>
                <div className="mp-spread" style={{ marginBottom: "var(--space-3)" }}>
                  <div style={{ minWidth: 0 }}>
                    <Link
                      href={`/orders/${order.id}`}
                      style={{ color: "var(--text)", fontWeight: 600 }}
                      aria-label={`Order ${order.orderNumber}`}
                    >
                      {order.orderNumber}
                    </Link>
                    <p style={{ margin: 0, color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                      {formatDate(order.placedAt)}
                      {order.sellerNames ? ` · ${order.sellerNames}` : ""}
                    </p>
                  </div>

                  <div className="d-flex align-items-center" style={{ gap: "var(--space-3)", flex: "none" }}>
                    <StatusBadge tone={statusTone(order.status)}>{order.status}</StatusBadge>
                    <span className="mp-price" style={{ fontVariantNumeric: "tabular-nums" }}>
                      {formatCurrency(order.totalAmount, order.currency)}
                    </span>
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
                      loading="lazy"
                      decoding="async"
                      style={{ width: "3rem", height: "3rem", objectFit: "cover", borderRadius: "var(--radius-sm)", flex: "none" }}
                    />
                  ) : null}
                  <p style={{ margin: 0, fontSize: "var(--fs-sm)", color: "var(--text-muted)", minWidth: 0 }} className="mp-truncate">
                    {order.firstProductName ?? "Order"}
                    {order.itemCount > 1 ? ` and ${order.itemCount - 1} more` : ""}
                  </p>
                  {!order.isPaid ? (
                    <span style={{ color: "var(--warning)", fontSize: "var(--fs-xs)", marginLeft: "auto", flex: "none" }}>
                      Awaiting payment
                    </span>
                  ) : null}
                  <Link
                    href={`/orders/${order.id}`}
                    className="btn btn-sm btn-outline-secondary"
                    style={{ marginLeft: "auto", flex: "none" }}
                    aria-label={`View order ${order.orderNumber}`}
                  >
                    View order
                  </Link>
                </div>
              </article>
            ))}
          </div>

          <Pagination
            page={orders.data.page}
            totalPages={orders.data.totalPages}
            query={{ status: filters.status, search: filters.search, sort: filters.sort }}
            basePath="/orders"
          />
        </>
      )}
    </div>
  );
}

/** The toolbar is one search box plus two selects, stacked on a phone, one row on a desktop. */
function OrderToolbar({
  search,
  status,
  sort,
  onSearch,
  onStatus,
  onSort,
}: {
  search: string;
  status: string;
  sort: string;
  onSearch: (value: string) => void;
  onStatus: (value: string) => void;
  onSort: (value: string) => void;
}) {
  const [draft, setDraft] = useState(search);

  return (
    <form
      role="search"
      aria-label="Search and filter orders"
      className="mp-card"
      style={{ padding: "var(--space-3) var(--space-4)" }}
      onSubmit={(event) => {
        event.preventDefault();
        onSearch(draft);
      }}
    >
      <div className="row g-2 align-items-end">
        <div className="col-12 col-md-5">
          <label htmlFor="order-search" className="mp-metric-label">
            Search
          </label>
          <input
            id="order-search"
            type="search"
            className="form-control form-control-sm"
            value={draft}
            onChange={(event) => setDraft(event.target.value)}
            placeholder="Order number or product"
            autoComplete="off"
          />
        </div>
        <div className="col-6 col-md-3">
          <label htmlFor="order-status" className="mp-metric-label">
            Status
          </label>
          <select
            id="order-status"
            className="form-select form-select-sm"
            value={status}
            onChange={(event) => onStatus(event.target.value)}
          >
            <option value="">All statuses</option>
            {STATUSES.map((value) => (
              <option key={value} value={value}>
                {value}
              </option>
            ))}
          </select>
        </div>
        <div className="col-6 col-md-2">
          <label htmlFor="order-sort" className="mp-metric-label">
            Sort
          </label>
          <select
            id="order-sort"
            className="form-select form-select-sm"
            value={sort}
            onChange={(event) => onSort(event.target.value)}
          >
            {SORTS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </div>
        <div className="col-12 col-md-2">
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
      {[0, 1, 2].map((index) => (
        <div key={index} className="mp-card" style={{ padding: "var(--space-4)" }}>
          <div className="mp-spread" style={{ marginBottom: "var(--space-3)" }}>
            <div className="mp-skeleton" style={{ height: "1.1rem", width: "10rem" }} />
            <div className="mp-skeleton" style={{ height: "1.25rem", width: "8rem" }} />
          </div>
          <div className="d-flex align-items-center" style={{ gap: "var(--space-3)" }}>
            <div className="mp-skeleton" style={{ width: "3rem", height: "3rem", flex: "none" }} />
            <div className="mp-skeleton" style={{ height: "0.9rem", width: "60%" }} />
          </div>
        </div>
      ))}
    </div>
  );
}

function readFilters(searchParams: URLSearchParams): OrderFilters {
  const status = searchParams.get("status");
  const sort = searchParams.get("sort");
  const search = searchParams.get("search")?.trim() || undefined;

  return {
    // An unknown status is nobody's filter: it is dropped rather than sent to an API that
    // would reject or misread it.
    status: status && (STATUSES as string[]).includes(status) ? (status as OrderStatus) : undefined,
    search,
    sort: sort && SORTS.some((option) => option.value === sort) && sort !== "newest" ? sort : undefined,
  };
}

function positiveInt(raw: string | null): number {
  const parsed = Number(raw);
  return Number.isInteger(parsed) && parsed > 0 ? parsed : 1;
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
