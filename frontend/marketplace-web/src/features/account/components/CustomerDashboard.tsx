/**
 * The account dashboard: what is happening, in one place, on one page.
 *
 * A person who has just signed in should not have to choose between "my orders" and "my
 * profile" to find out what changed. So this answers the four questions they actually have, in
 * order of how often they are asked: what is on its way, what is saved, what needs reading, and
 * where my details are.
 *
 * It is live. The hub tells this page when an order moves, a payment settles or a notification
 * arrives, so an order placed in another tab shows up here without a reload — which is the
 * difference between a dashboard and a list of links.
 */

"use client";

import Link from "next/link";
import { useMemo } from "react";
import { useQueries } from "@tanstack/react-query";
import { Bell, Heart, MapPin, Package, Truck } from "lucide-react";

import { EmptyState, ErrorState, StatusBadge } from "@/components/shared/Feedback";
import { AccountNav } from "@/features/account/components/AccountNav";
import { notificationApi, wishlistApi } from "@/features/account/api/accountApi";
import { addressApi, orderApi } from "@/features/orders/api/orderApi";
import { cx, formatCurrency, formatDate } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";
import { useAuth } from "@/providers/AuthProvider";
import type { OrderStatus } from "@/types/order";

/** The statuses that mean a parcel is on its way, which is what a shopper scans for. */
const IN_TRANSIT: OrderStatus[] = ["Confirmed", "Processing", "Packed", "Shipped"];

export function CustomerDashboard() {
  const { user } = useAuth();

  // One request per thing, in parallel, rather than a chain: the page shows all of it or an
  // error for the part that failed, and none of these waits on another.
  const [orders, wishlist, unread, addresses] = useQueries({
    queries: [
      { queryKey: queryKeys.orders.list({ page: 1, pageSize: 5 }), queryFn: () => orderApi.list(1, 5) },
      { queryKey: queryKeys.wishlist.list(), queryFn: () => wishlistApi.list() },
      { queryKey: queryKeys.notifications.unreadCount(), queryFn: () => notificationApi.unreadCount() },
      { queryKey: queryKeys.addresses.list(), queryFn: () => addressApi.list() },
    ],
  });

  // Derived rather than stored: a second copy of the same list is a second answer to the same
  // question, and the two drift the moment a status changes under the reader.
  const recent = useMemo(() => orders.data?.items ?? [], [orders.data]);
  const onTheWay = useMemo(() => recent.filter(order => IN_TRANSIT.includes(order.status)), [recent]);
  const needsYou = useMemo(
    () => recent.filter(order => order.status === "Pending" || order.status === "Delivered"),
    [recent]
  );

  const savedCount = wishlist.data?.length ?? 0;
  const unreadCount = unread.data?.unreadCount ?? 0;
  const firstName = user?.firstName ?? "";

  if (orders.isError) {
    return <ErrorState message="We could not load your account. Your orders are still where you left them." />;
  }

  return (
    <div className="mp-page section">
      <AccountNav />
      <header className="mp-page-header">
        <div>
          <h1 className="mp-page-title">{firstName ? `Hello, ${firstName}` : "Your account"}</h1>
          <p className="mp-page-subtitle">
            {orders.isPending
              ? "Loading…"
              : recent.length === 0
                ? "You have not ordered anything yet."
                : `${orders.data?.totalCount ?? 0} ${orders.data?.totalCount === 1 ? "order" : "orders"} so far.`}
          </p>
        </div>

        <div className="d-flex" style={{ gap: "0.5rem" }}>
          <Link href="/orders" className="btn btn-sm btn-outline-secondary">
            All orders
          </Link>
          <Link href="/profile" className="btn btn-sm btn-primary">
            Account settings
          </Link>
        </div>
      </header>

      {/*
        The four numbers, each one a question answered rather than a figure displayed. They are
        links: a count nobody can act on is decoration.
      */}
      <div className="row g-3 mb-4">
        <Tile
          href="/orders"
          icon={Truck}
          label="On the way"
          value={onTheWay.length}
          hint={onTheWay.length === 0 ? "Nothing in transit" : "Follow each parcel"}
        />
        <Tile
          href="/orders?status=Pending"
          icon={Package}
          label="Needs a decision"
          value={needsYou.length}
          hint={needsYou.length === 0 ? "Nothing waiting" : "To pay or to confirm"}
        />
        <Tile href="/wishlist" icon={Heart} label="Saved" value={savedCount} hint="Kept for later" />
        <Tile href="/notifications" icon={Bell} label="Unread" value={unreadCount} hint="Messages" tone={unreadCount > 0 ? "brand" : undefined} />
      </div>

      <div className="row g-3">
        <div className="col-12 col-lg-8">
          <section className="mp-card h-100" style={{ padding: "var(--space-4)" }} aria-labelledby="dash-orders">
            <div className="mp-spread mb-3">
              <h2 className="mp-section-title" id="dash-orders" style={{ margin: 0, fontSize: "var(--fs-h3)" }}>
                Recent orders
              </h2>
              {onTheWay.length > 0 ? (
                <span className="mp-badge mp-badge-success">
                  <span className="mp-badge-dot" aria-hidden />
                  {onTheWay.length} in transit
                </span>
              ) : null}
            </div>

            {orders.isPending ? (
              <div className="mp-stack-sm" aria-hidden>
                {[0, 1, 2].map(i => (
                  <div key={i} className="mp-skeleton" style={{ height: "3.5rem", borderRadius: "var(--radius-sm)" }} />
                ))}
              </div>
            ) : recent.length === 0 ? (
              <EmptyState
                title="No orders yet"
                body="When you buy something it appears here, and you can follow it from here to your door."
                action={
                  <Link href="/products" className="btn btn-sm btn-primary">
                    Start looking
                  </Link>
                }
              />
            ) : (
              <ul className="list-unstyled mb-0">
                {recent.map((order, index) => (
                  <li
                    key={order.id}
                    style={{ borderTop: index === 0 ? "none" : "1px solid var(--border)", padding: "var(--space-3) 0" }}
                  >
                    <div className="d-flex flex-wrap justify-content-between align-items-center" style={{ gap: "var(--space-3)" }}>
                      <div style={{ minWidth: "10rem" }}>
                        <Link href={`/orders/${order.id}`} style={{ color: "var(--text)", fontWeight: 600 }}>
                          {order.orderNumber}
                        </Link>
                        <p style={{ margin: 0, color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                          {formatDate(order.placedAt)} · {order.itemCount}{" "}
                          {order.itemCount === 1 ? "item" : "items"}
                        </p>
                      </div>

                      <div className="d-flex align-items-center" style={{ gap: "var(--space-3)" }}>
                        <span style={{ fontWeight: 600 }}>{formatCurrency(order.totalAmount, order.currency)}</span>
                        <StatusBadge tone={tone(order.status)}>{order.status}</StatusBadge>
                      </div>
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </section>
        </div>

        <div className="col-12 col-lg-4">
          <section className="mp-card" style={{ padding: "var(--space-4)" }} aria-labelledby="dash-details">
            <h2 className="mp-section-title" id="dash-details" style={{ margin: "0 0 var(--space-3)", fontSize: "var(--fs-h3)" }}>
              Your details
            </h2>

            <dl style={{ margin: 0, display: "grid", gap: "var(--space-3)" }}>
              <div>
                <dt className="mp-metric-label">Email</dt>
                <dd style={{ margin: 0, wordBreak: "break-word" }}>{user?.email ?? "—"}</dd>
              </div>

              <div>
                <dt className="mp-metric-label">Name</dt>
                <dd style={{ margin: 0 }}>
                  {[user?.firstName, user?.lastName].filter(Boolean).join(" ") || "—"}
                </dd>
              </div>

              <div>
                <dt className="mp-metric-label">Member since</dt>
                <dd style={{ margin: 0 }}>{user?.createdAt ? formatDate(user.createdAt) : "—"}</dd>
              </div>
            </dl>

            <div className="mt-3">
              <span className="mp-metric-label d-block mb-2">Delivery addresses</span>
              {addresses.isPending ? (
                <div className="mp-skeleton" style={{ height: "2.5rem", borderRadius: "var(--radius-sm)" }} />
              ) : (addresses.data?.length ?? 0) === 0 ? (
                <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
                  None saved yet, so checkout will ask for one.
                </p>
              ) : (
                <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
                  {(addresses.data?.length ?? 0) === 1
                    ? "1 address saved"
                    : `${addresses.data?.length} addresses saved`}
                </p>
              )}

              <Link href="/addresses" className="btn btn-sm btn-outline-secondary mt-3">
                <MapPin size={14} aria-hidden className="me-1" />
                Manage addresses
              </Link>
            </div>
          </section>
        </div>
      </div>
    </div>
  );
}

function Tile({
  href,
  icon: Icon,
  label,
  value,
  hint,
  tone,
}: {
  href: string;
  icon: typeof Truck;
  label: string;
  value: number;
  hint: string;
  tone?: "brand";
}) {
  return (
    <div className="col-6 col-lg-3">
      <Link href={href} className="mp-tile">
        <span className={cx("mp-tile-icon", tone === "brand" && "is-brand")}>
          <Icon size={18} aria-hidden />
        </span>
        <span className="mp-tile-value">{value}</span>
        <span className="mp-tile-label">{label}</span>
        <span className="mp-tile-hint">{hint}</span>
      </Link>
    </div>
  );
}

function tone(status: OrderStatus): "success" | "warning" | "danger" | "info" {
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
