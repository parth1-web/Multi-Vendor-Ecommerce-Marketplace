/**
 * One of your orders, in full: what is in the parcel, where it is going, and what you can do next.
 *
 * This is the page a seller opens to find out what to do, so it answers in that order. The items
 * and the address first, because that is the work. The money second, because a seller is working
 * towards a figure and it is worth stating plainly: what they will be paid, and what the
 * marketplace takes.
 *
 * The status buttons are the ones the state machine will accept from where the order actually is,
 * rather than all of them greyed out. Shipping asks for a carrier and a tracking number, because
 * the server refuses the step without them and a button that fails on click teaches nothing.
 */

"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { ErrorState, StatusBadge } from "@/components/shared/Feedback";
import { TextField } from "@/components/forms/FormField";
import { sellerApi } from "@/features/seller/api/sellerApi";
import { errorMessage } from "@/lib/errors";
import { formatCurrency, formatDate } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";
import type { SellerOrderDetail, SellerOrderStatus } from "@/types/seller";

/** The steps a seller can take, and the ones that need something before they will be accepted. */
const NEXT_STEPS: { from: SellerOrderStatus[]; to: SellerOrderStatus; label: string }[] = [
  { from: ["Pending"], to: "Confirmed", label: "Accept this order" },
  { from: ["Confirmed"], to: "Processing", label: "Start preparing" },
  { from: ["Processing"], to: "Packed", label: "Mark packed" },
  { from: ["Packed"], to: "Shipped", label: "Mark shipped" },
  { from: ["Shipped"], to: "Delivered", label: "Mark delivered" },
];

export function SellerOrderDetail() {
  const params = useParams<{ id: string }>();
  const id = params?.id ?? "";
  const queryClient = useQueryClient();

  const [carrier, setCarrier] = useState("");
  const [tracking, setTracking] = useState("");
  const [note, setNote] = useState("");
  const [actionError, setActionError] = useState<string | null>(null);
  const [shipping, setShipping] = useState(false);

  const order = useQuery({
    queryKey: queryKeys.seller.order(id),
    queryFn: () => sellerApi.order(id),
    enabled: Boolean(id),
  });

  const move = useMutation({
    mutationFn: (input: { to: SellerOrderStatus; carrierName?: string | null; trackingNumber?: string | null }) =>
      sellerApi.updateOrderStatus(id, {
        status: input.to,
        note: note.trim() || null,
        carrierName: input.carrierName ?? null,
        trackingNumber: input.trackingNumber ?? null,
      }),
    onSuccess: async () => {
      setActionError(null);
      setShipping(false);
      setCarrier("");
      setTracking("");
      setNote("");
      await queryClient.invalidateQueries({ queryKey: queryKeys.seller.all });
    },
    onError: error => setActionError(errorMessage(error)),
  });

  if (order.isPending) {
    return <div className="mp-skeleton" style={{ height: "20rem", borderRadius: "var(--radius)" }} />;
  }

  if (order.isError || !order.data) {
    return <ErrorState message="We could not open that order. It may belong to another store." />;
  }

  const data = order.data;
  const steps = NEXT_STEPS.filter(step => step.from.includes(data.summary.status));
  const closed = data.summary.status === "Cancelled" || data.summary.status === "Returned";

  return (
    <div className="mp-stack">
      <div className="d-flex flex-wrap justify-content-between align-items-start" style={{ gap: "var(--space-3)" }}>
        <div>
          <h1 style={{ margin: 0, fontSize: "var(--fs-h2)" }}>{data.summary.sellerOrderNumber}</h1>
          <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            Part of {data.summary.orderNumber} · {data.summary.itemCount}{" "}
            {data.summary.itemCount === 1 ? "item" : "items"} from {data.summary.storeName}
          </p>
        </div>

        <StatusBadge tone={statusTone(data.summary.status)}>{data.summary.status}</StatusBadge>
      </div>

      {actionError ? (
        <p role="alert" className="mp-alert mp-alert-danger">
          {actionError}
        </p>
      ) : null}

      {data.refundRequested ? (
        <p className="mp-alert mp-alert-warning" role="status">
          A refund has been asked for on this order{data.refundStatus ? ` (${data.refundStatus})` : ""}. It is
          handled by the customer and the marketplace, not by you.
        </p>
      ) : null}

      <div className="row g-3">
        <div className="col-12 col-lg-7">
          <section className="mp-card" style={{ padding: "var(--space-4)" }} aria-labelledby="what-to-send">
            <h2 className="mp-section-title" id="what-to-send" style={{ fontSize: "var(--fs-h3)" }}>
              What to send
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

                  <div style={{ flex: 1, minWidth: "0" }}>
                    <span style={{ fontWeight: 500 }}>{item.productName}</span>
                    <p style={{ margin: 0, color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                      {item.variantName} · {item.sku} · {item.quantity} × {formatCurrency(item.unitPrice)}
                    </p>
                  </div>

                  <span>{formatCurrency(item.lineTotal)}</span>
                </li>
              ))}
            </ul>
          </section>

          <section className="mp-card mt-3" style={{ padding: "var(--space-4)" }} aria-labelledby="where-to">
            <h2 className="mp-section-title" id="where-to" style={{ fontSize: "var(--fs-h3)" }}>
              Where to
            </h2>
            <p style={{ margin: 0 }}>
              {data.shippingAddress.recipientName}
              <br />
              {data.shippingAddress.phoneNumber}
              <br />
              {data.shippingAddress.line1}
              {data.shippingAddress.line2 ? `, ${data.shippingAddress.line2}` : ""}
              <br />
              {data.shippingAddress.city}
              {data.shippingAddress.state ? `, ${data.shippingAddress.state}` : ""} {data.shippingAddress.postalCode}
              <br />
              {data.shippingAddress.country}
            </p>
            <p style={{ margin: "var(--space-3) 0 0", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
              The address as it was when the order was placed. Ask the customer rather than guessing if it no longer
              works.
            </p>
          </section>
        </div>

        <div className="col-12 col-lg-5">
          <section className="mp-card" style={{ padding: "var(--space-4)" }} aria-labelledby="your-money">
            <h2 className="mp-section-title" id="your-money" style={{ fontSize: "var(--fs-h3)" }}>
              Your money
            </h2>

            <p style={{ margin: 0, fontSize: "var(--fs-h2)", fontWeight: 700 }}>{formatCurrency(data.summary.sellerEarnings)}</p>
            <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
              after {(data.summary.commissionRate * 100).toFixed(1)}% commission of {formatCurrency(data.summary.commissionAmount)}
            </p>

            <dl className="mp-stack-sm mt-3" style={{ margin: 0, fontSize: "var(--fs-sm)" }}>
              <div className="mp-spread">
                <dt style={{ color: "var(--text-muted)" }}>Items</dt>
                <dd style={{ margin: 0 }}>{formatCurrency(data.summary.subtotal)}</dd>
              </div>
              {data.summary.discountAmount > 0 ? (
                <div className="mp-spread">
                  <dt style={{ color: "var(--text-muted)" }}>Discount</dt>
                  <dd style={{ margin: 0 }}>-{formatCurrency(data.summary.discountAmount)}</dd>
                </div>
              ) : null}
              <div className="mp-spread">
                <dt style={{ color: "var(--text-muted)" }}>Shipping paid</dt>
                <dd style={{ margin: 0 }}>{formatCurrency(data.summary.shippingAmount)}</dd>
              </div>
              <div className="mp-spread">
                <dt style={{ color: "var(--text-muted)" }}>Order total</dt>
                <dd style={{ margin: 0, fontWeight: 600 }}>{formatCurrency(data.summary.totalAmount)}</dd>
              </div>
            </dl>
          </section>

          <section className="mp-card mt-3" style={{ padding: "var(--space-4)" }} aria-labelledby="progress">
            <h2 className="mp-section-title" id="progress" style={{ fontSize: "var(--fs-h3)" }}>
              Progress
            </h2>

            <ol className="list-unstyled mb-0">
              {data.timeline.map((step, index) => (
                <li
                  key={`${step.step}-${index}`}
                  style={{
                    display: "flex",
                    gap: "var(--space-3)",
                    padding: "var(--space-2) 0",
                    opacity: step.isComplete ? 1 : 0.5,
                  }}
                >
                  <span
                    aria-hidden
                    style={{
                      width: "0.6rem",
                      height: "0.6rem",
                      borderRadius: "50%",
                      marginTop: "0.35rem",
                      flex: "none",
                      background: step.isCurrent ? "var(--brand-600)" : step.isComplete ? "var(--success)" : "var(--border)",
                    }}
                  />
                  <span style={{ flex: 1 }}>
                    <span style={{ fontWeight: step.isCurrent ? 600 : 400 }}>{step.label}</span>
                    {step.at ? (
                      <span style={{ display: "block", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>{formatDate(step.at)}</span>
                    ) : null}
                    {step.note ? (
                      <span style={{ display: "block", color: "var(--text-muted)", fontSize: "var(--fs-xs)" }}>{step.note}</span>
                    ) : null}
                  </span>
                </li>
              ))}
            </ol>
          </section>
        </div>
      </div>

      {!closed ? (
        <section className="mp-card" style={{ padding: "var(--space-4)" }} aria-labelledby="next-step">
          <h2 className="mp-section-title" id="next-step" style={{ fontSize: "var(--fs-h3)" }}>
            Next step
          </h2>

          {steps.length === 0 ? (
            <p style={{ margin: 0, color: "var(--text-muted)" }}>
              Nothing is waiting on you for this order. The customer will be told when it arrives.
            </p>
          ) : (
            <div className="mp-stack-sm">
              <div>
                <TextField
                  label="A note for the customer"
                  hint="Optional. Shown on the order."
                  value={note}
                  placeholder="Kept in stock, posted this afternoon"
                  onChange={event => setNote(event.target.value)}
                />
              </div>

              <div className="d-flex flex-wrap" style={{ gap: "0.5rem" }}>
                {steps
                  .filter(step => step.to !== "Shipped")
                  .map(step => (
                    <button
                      key={step.to}
                      type="button"
                      className="btn btn-sm btn-primary"
                      disabled={move.isPending}
                      onClick={() => move.mutate({ to: step.to })}
                    >
                      {step.label}
                    </button>
                  ))}

                {steps.some(step => step.to === "Shipped") ? (
                  shipping ? (
                    <>
                      <div className="row g-2 w-100">
                        <div className="col-12 col-sm-5">
                          <TextField
                            label="Carrier"
                            required
                            value={carrier}
                            placeholder="Nepal Post"
                            onChange={event => setCarrier(event.target.value)}
                          />
                        </div>
                        <div className="col-12 col-sm-7">
                          <TextField
                            label="Tracking number"
                            required
                            value={tracking}
                            placeholder="NP123456789"
                            onChange={event => setTracking(event.target.value)}
                          />
                        </div>
                      </div>

                      <div className="d-flex" style={{ gap: "0.5rem" }}>
                        <button
                          type="button"
                          className="btn btn-sm btn-primary"
                          disabled={move.isPending || carrier.trim().length === 0 || tracking.trim().length === 0}
                          title={carrier.trim().length === 0 || tracking.trim().length === 0 ? "A carrier and a tracking number are needed for this step" : undefined}
                          onClick={() => move.mutate({ to: "Shipped", carrierName: carrier.trim(), trackingNumber: tracking.trim() })}
                        >
                          {move.isPending ? "Saving…" : "Confirm it has gone"}
                        </button>
                        <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => setShipping(false)}>
                          Cancel
                        </button>
                      </div>
                    </>
                  ) : (
                    <button type="button" className="btn btn-sm btn-primary" onClick={() => setShipping(true)}>
                      Mark shipped
                    </button>
                  )
                ) : null}
              </div>
            </div>
          )}
        </section>
      ) : null}

      <p>
        <Link href="/seller/orders" className="mp-link">
          All your orders
        </Link>
      </p>
    </div>
  );
}

function statusTone(status: SellerOrderStatus): "success" | "warning" | "danger" | "info" {
  switch (status) {
    case "Delivered":
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

