/**
 * An order, and the confirmation that follows placing one.
 *
 * The same page serves both, because somebody landing here straight after checkout wants exactly
 * what somebody returning to an old order wants: what was ordered, what it cost, where it is
 * going and what happens next.
 */

"use client";

import Link from "next/link";
import { useParams, useRouter, useSearchParams } from "next/navigation";
import { useEffect } from "react";
import { CheckCircle2 } from "lucide-react";
import { useQuery } from "@tanstack/react-query";

import { ErrorState, StatusBadge } from "@/components/shared/Feedback";
import { orderApi } from "@/features/orders/api/orderApi";
import { OrderReviews } from "@/features/orders/components/OrderReviews";
import { cx, formatCurrency, formatDate } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";
import { useAuth } from "@/providers/AuthProvider";
import type { OrderStatus } from "@/types/order";

function OrderDetailPage() {
  const { id } = useParams<{ id: string }>();
  const searchParams = useSearchParams();
  const router = useRouter();
  const { isAuthenticated, isHydrating } = useAuth();

  const justPlaced = searchParams.get("placed") === "1";

  const order = useQuery({
    queryKey: queryKeys.orders.detail(id),
    queryFn: () => orderApi.byId(id),
    enabled: Boolean(id) && isAuthenticated,
  });

  useEffect(() => {
    if (!isHydrating && !isAuthenticated) {
      router.replace(`/login?returnUrl=${encodeURIComponent(`/orders/${id}`)}`);
    }
  }, [id, isAuthenticated, isHydrating, router]);

  if (!isAuthenticated) {
    return <div className="mp-skeleton" style={{ height: "20rem", borderRadius: "var(--radius)" }} />;
  }

  if (order.isPending) {
    return (
      <div className="mp-page" style={{ paddingBlock: "var(--space-5)" }}>
        <div className="mp-skeleton" style={{ height: "24rem", borderRadius: "var(--radius)" }} />
      </div>
    );
  }

  if (order.isError || !order.data) {
    return (
      <div className="mp-page" style={{ paddingBlock: "var(--space-5)" }}>
        <ErrorState message="We could not find that order. It may belong to another account." />
      </div>
    );
  }

  const data = order.data;

  return (
    <div className="mp-page" style={{ paddingBlock: "var(--space-5)" }}>
      {justPlaced ? (
        <p className="mp-alert mp-alert-success" role="status">
          <CheckCircle2 size={16} aria-hidden className="me-2" />
          Your order is placed. We have sent the details to your email address.
        </p>
      ) : null}

      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title" style={{ fontSize: "var(--fs-h1)" }}>
            Order {data.orderNumber}
          </h1>
          <div className="d-flex align-items-center flex-wrap mt-1" style={{ gap: "var(--space-3)" }}>
            <StatusBadge tone={statusTone(data.status)}>{data.status}</StatusBadge>
            <span style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>Placed {formatDate(data.placedAt)}</span>
            <span style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
              {data.sellerCount} {data.sellerCount === 1 ? "seller" : "sellers"}
            </span>
          </div>
        </div>

        <Link href="/orders" className="btn btn-sm btn-outline-secondary">
          All your orders
        </Link>
      </div>

      <div className="row g-4">
        <div className="col-12 col-lg-8">
          <section className="mp-card" style={{ padding: "var(--space-4)" }} aria-labelledby="order-items">
            <h2 className="mp-section-title" id="order-items" style={{ fontSize: "var(--fs-h3)" }}>
              What was ordered
            </h2>

            <ul className="list-unstyled mb-0">
              {data.items.map((item, index) => (
                <li
                  key={item.id}
                  className="d-flex align-items-center"
                  style={{
                    gap: "var(--space-3)",
                    padding: "var(--space-3) 0",
                    borderTop: index === 0 ? "none" : "1px solid var(--border)",
                  }}
                >
                  {item.productImageUrl ? (
                    // eslint-disable-next-line @next/next/no-img-element
                    <img
                      src={item.productImageUrl}
                      alt={item.productName}
                      width={56}
                      height={56}
                      style={{ width: "3.5rem", height: "3.5rem", objectFit: "cover", borderRadius: "var(--radius-sm)" }}
                    />
                  ) : null}

                  <div style={{ flex: 1, minWidth: "10rem" }}>
                    {/* Deliberately not a link: the line carries a product id, and the product
                        route takes a slug. A link built from the wrong key is a 404 with a hover. */}
                    <span style={{ color: "var(--text)", fontWeight: 500 }}>{item.productName}</span>
                    <p style={{ margin: 0, color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                      {item.variantName} · {item.sku} · {item.quantity} × {formatCurrency(item.unitPrice, data.currency)}
                    </p>
                    <p style={{ margin: 0, color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>Sold by {item.storeName}</p>
                  </div>

                  <span>{formatCurrency(item.lineTotal, data.currency)}</span>
                </li>
              ))}
            </ul>
          </section>

          {/*
            A marketplace order is one order split per seller, so with more than one store this
            is the part that says who is actually packing what.
          */}
          {data.sellerOrders.length > 1 ? (
            <section className="mp-card mt-3" style={{ padding: "var(--space-4)" }} aria-labelledby="order-sellers">
              <h2 className="mp-section-title" id="order-sellers" style={{ fontSize: "var(--fs-h3)" }}>
                Shipped by {data.sellerOrders.length} stores
              </h2>
              <p style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
                One order, split per seller: each store packs and ships its own part.
              </p>

              <ul className="list-unstyled mb-0">
                {data.sellerOrders.map((sellerOrder, index) => (
                  <li
                    key={sellerOrder.id}
                    className="d-flex justify-content-between"
                    style={{
                      padding: "var(--space-2) 0",
                      borderTop: index === 0 ? "none" : "1px solid var(--border)",
                      fontSize: "var(--fs-sm)",
                    }}
                  >
                    <span>
                      <strong>{sellerOrder.storeName}</strong>
                      <span style={{ color: "var(--text-subtle)" }}> · {sellerOrder.sellerOrderNumber}</span>
                      {sellerOrder.trackingNumber ? (
                        <span style={{ display: "block", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                          {sellerOrder.carrierName} {sellerOrder.trackingNumber}
                        </span>
                      ) : null}
                    </span>
                    <span>{formatCurrency(sellerOrder.totalAmount, data.currency)}</span>
                  </li>
                ))}
              </ul>
            </section>
          ) : null}

          <section className="mp-card mt-3" style={{ padding: "var(--space-4)" }} aria-labelledby="order-progress">
            <h2 className="mp-section-title" id="order-progress" style={{ fontSize: "var(--fs-h3)" }}>
              Where it is
            </h2>

            <ol className="mp-timeline">
              {data.timeline.map((step, index) => (
                <li
                  key={`${step.step}-${index}`}
                  className={cx("mp-timeline-step", step.isComplete && "is-done", step.isCurrent && "is-current")}
                >
                  <span className="mp-timeline-dot" aria-hidden />
                  <div>
                    <strong style={{ fontSize: "var(--fs-sm)" }}>{step.label}</strong>
                    {step.at ? (
                      <p style={{ margin: 0, color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>{formatDate(step.at)}</p>
                    ) : step.isCurrent ? (
                      <p style={{ margin: 0, color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>Where it is now</p>
                    ) : (
                      <p style={{ margin: 0, color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>Waiting</p>
                    )}
                    {step.note ? (
                      <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-xs)" }}>{step.note}</p>
                    ) : null}
                  </div>
                </li>
              ))}
            </ol>
          </section>
        </div>

        <div className="col-12 col-lg-4">
          <section className="mp-card" style={{ padding: "var(--space-4)" }} aria-labelledby="order-summary">
            <h2 className="mp-section-title" id="order-summary" style={{ fontSize: "var(--fs-h3)" }}>
              Summary
            </h2>

            <dl className="mp-stack-sm mb-0" style={{ fontSize: "var(--fs-sm)" }}>
              <div className="d-flex justify-content-between">
                <dt className="mp-metric-label">Subtotal</dt>
                <dd className="mb-0">{formatCurrency(data.subtotal, data.currency)}</dd>
              </div>
              {data.discountAmount > 0 ? (
                <div className="d-flex justify-content-between" style={{ color: "var(--success)" }}>
                  <dt className="mp-metric-label">Discount{data.couponCode ? ` (${data.couponCode})` : ""}</dt>
                  <dd className="mb-0">−{formatCurrency(data.discountAmount, data.currency)}</dd>
                </div>
              ) : null}
              <div className="d-flex justify-content-between">
                <dt className="mp-metric-label">Shipping</dt>
                <dd className="mb-0">{data.shippingAmount === 0 ? "Free" : formatCurrency(data.shippingAmount, data.currency)}</dd>
              </div>
              {data.taxAmount > 0 ? (
                <div className="d-flex justify-content-between">
                  <dt className="mp-metric-label">Tax</dt>
                  <dd className="mb-0">{formatCurrency(data.taxAmount, data.currency)}</dd>
                </div>
              ) : null}
              {data.refundedAmount > 0 ? (
                <div className="d-flex justify-content-between" style={{ color: "var(--danger)" }}>
                  <dt className="mp-metric-label">Refunded</dt>
                  <dd className="mb-0">−{formatCurrency(data.refundedAmount, data.currency)}</dd>
                </div>
              ) : null}
              <div className="d-flex justify-content-between" style={{ borderTop: "1px solid var(--border)", paddingTop: "var(--space-2)" }}>
                <dt style={{ fontWeight: 600 }}>Total</dt>
                <dd className="mb-0 mp-price">{formatCurrency(data.totalAmount, data.currency)}</dd>
              </div>
            </dl>

            <hr className="my-3" />

            <p style={{ fontSize: "var(--fs-sm)", margin: 0 }}>
              <span className="mp-metric-label">Delivering to</span>
              <br />
              {data.shippingAddress.recipientName}
              <br />
              <span style={{ color: "var(--text-muted)" }}>{formatAddress(data.shippingAddress)}</span>
            </p>

            <p style={{ fontSize: "var(--fs-sm)", marginBottom: 0 }}>
              <span className="mp-metric-label">Paying by</span>
              <br />
              {data.paymentMethod}
              <br />
              <span style={{ color: data.isPaid ? "var(--success)" : "var(--text-muted)" }}>
                {data.isPaid ? "Paid" : `Awaiting payment (${data.paymentStatus})`}
              </span>
            </p>
          </section>

          {/* A review is written against a line of this order, because that is what proves the
              purchase. It is the only place one can be written from, and the server says which
              lines are eligible. */}
          <OrderReviews order={data} />
        </div>
      </div>
    </div>
  );
}

function formatAddress(address: { line1: string; line2: string | null; city: string; state: string | null; postalCode: string; country: string }): string {
  return [address.line1, address.line2, address.city, address.state, address.postalCode, address.country]
    .filter(part => part && part.trim().length > 0)
    .join(", ");
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

export { OrderDetailPage as OrderDetail };
