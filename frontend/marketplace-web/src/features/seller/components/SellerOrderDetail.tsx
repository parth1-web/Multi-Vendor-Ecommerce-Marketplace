"use client";

/**
 * One of your orders, in full: what is in the parcel, where it is going, and what you can do next.
 *
 * This is the page a seller opens to find out what to do, so it answers in that order. The items
 * and the address first, because that is the work. The money second, because a seller is working
 * towards a figure and it is worth stating plainly: what they will be paid, and what the
 * marketplace takes.
 *
 * The buttons offered are the steps the API's own state machine will accept from where this order
 * actually is, rather than every status greyed out. Shipping asks for a carrier and a tracking
 * number because the server refuses the step without them, and a button that fails on click
 * teaches nothing. Cancelling is confirmed before it happens: the API will not accept a step back
 * out of Cancelled, so an accidental click there has no repair.
 */

import Link from "next/link";
import { useParams, useSearchParams } from "next/navigation";
import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ChevronLeft } from "lucide-react";

import { TextField } from "@/components/forms/FormField";
import { ConfirmDialog } from "@/components/shared/ConfirmDialog";
import { ErrorState, StatusBadge } from "@/components/shared/Feedback";
import { sellerApi } from "@/features/seller/api/sellerApi";
import { isTerminalStatus, nextOrderSteps, orderStatusLabel, orderStatusTone, type StatusStep } from "@/features/seller/lib/orderStatus";
import { errorMessage } from "@/lib/errors";
import { formatCurrency, formatDate } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";
import { useToast } from "@/providers/ToastProvider";
import type { SellerOrderStatus } from "@/types/seller";

export function SellerOrderDetail() {
  const params = useParams<{ id: string }>();
  const searchParams = useSearchParams();
  const id = params?.id ?? "";
  const queryClient = useQueryClient();
  const { push } = useToast();

  const [carrier, setCarrier] = useState("");
  const [tracking, setTracking] = useState("");
  const [note, setNote] = useState("");
  const [shipping, setShipping] = useState(false);
  const [confirming, setConfirming] = useState<StatusStep | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [shipError, setShipError] = useState<string | null>(null);

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
    onSuccess: async (_, input) => {
      setActionError(null);
      setShipError(null);
      setShipping(false);
      setCarrier("");
      setTracking("");
      setNote("");
      setConfirming(null);

      // The order itself, every list that may show it, and the dashboard's order counts. The
      // revenue charts are not touched: they plot sales, which a status change does not alter.
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.seller.orderDetails() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.seller.orderLists() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.seller.summary() }),
      ]);

      push({ tone: "success", title: `Order is now ${orderStatusLabel(input.to).toLowerCase()}`, body: "The timeline below has been updated." });
    },
    onError: error => {
      setActionError(errorMessage(error));
      setConfirming(null);
    },
  });

  if (order.isPending) {
    return <div className="mp-skeleton" style={{ height: "20rem", borderRadius: "var(--radius)" }} aria-hidden />;
  }

  if (order.isError || !order.data) {
    return (
      <ErrorState
        message="We could not open that order. It may belong to another store."
        onRetry={() => void order.refetch()}
      />
    );
  }

  const data = order.data;
  const steps = nextOrderSteps(data.summary.status);
  const shipment = steps.find(step => step.to === "Shipped");
  const plainSteps = steps.filter(step => step.to !== "Shipped");

  // Where the seller came from, so the way back is the list they were looking at rather than the
  // top of an unfiltered page one.
  const listQuery = new URLSearchParams();

  for (const key of ["status", "search"]) {
    const value = searchParams.get(key);

    if (value) {
      listQuery.set(key, value);
    }
  }

  const backHref = listQuery.size > 0 ? `/seller/orders?${listQuery.toString()}` : "/seller/orders";

  return (
    <div className="mp-stack">
      <div className="d-flex flex-wrap justify-content-between align-items-start" style={{ gap: "var(--space-3)" }}>
        <div style={{ minWidth: 0 }}>
          <h1 style={{ margin: 0, fontSize: "var(--fs-h2)" }}>{data.summary.sellerOrderNumber}</h1>
          <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            Part of {data.summary.orderNumber} · {data.summary.itemCount}{" "}
            {data.summary.itemCount === 1 ? "item" : "items"} · {data.customerName}
          </p>
        </div>

        <StatusBadge tone={orderStatusTone(data.summary.status)}>{orderStatusLabel(data.summary.status)}</StatusBadge>
      </div>

      <Link href={backHref} className="mp-link d-inline-flex align-items-center" style={{ gap: "0.25rem" }}>
        <ChevronLeft size={14} aria-hidden />
        Back to your orders
      </Link>

      {actionError ? (
        <p role="alert" className="mp-alert mp-alert-danger">
          {actionError}
        </p>
      ) : null}

      {data.refundRequested ? (
        <p className="mp-alert mp-alert-warning" role="status">
          A refund has been asked for on this order{data.refundStatus ? ` (${data.refundStatus})` : ""}. It is handled
          by the customer and the marketplace, not by you.
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
                      loading="lazy"
                      decoding="async"
                      style={{
                        width: "3.5rem",
                        height: "3.5rem",
                        objectFit: "cover",
                        borderRadius: "var(--radius-sm)",
                        flex: "none",
                      }}
                    />
                  ) : null}

                  <div style={{ flex: 1, minWidth: 0 }}>
                    {/*
                      The listing is the other half of this workflow: a seller who realises the item
                      is out of stock needs to reach the product and its variants from here, and
                      come back to the order afterwards.
                    */}
                    <Link href={`/seller/products/${item.productId}?from=${data.summary.id}`} style={{ color: "var(--text)" }}>
                      {item.productName}
                    </Link>
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

            <p style={{ margin: 0, fontSize: "var(--fs-h2)", fontWeight: 700 }}>
              {formatCurrency(data.summary.sellerEarnings)}
            </p>
            {/* The rate arrives as a percentage — 10 is 10% — so it is printed as sent. */}
            <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
              after {data.summary.commissionRate.toFixed(1)}% commission of {formatCurrency(data.summary.commissionAmount)}
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
              {data.summary.carrierName || data.summary.trackingNumber ? (
                <div className="mp-spread">
                  <dt style={{ color: "var(--text-muted)" }}>Shipped by</dt>
                  <dd style={{ margin: 0 }}>
                    {data.summary.carrierName ?? "—"}
                    {data.summary.trackingNumber ? ` · ${data.summary.trackingNumber}` : ""}
                  </dd>
                </div>
              ) : null}
              {data.summary.estimatedDeliveryAt ? (
                <div className="mp-spread">
                  <dt style={{ color: "var(--text-muted)" }}>Estimated delivery</dt>
                  <dd style={{ margin: 0 }}>{formatDate(data.summary.estimatedDeliveryAt)}</dd>
                </div>
              ) : null}
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
                    {/*
                      The current step is stated in text as well as by the filled dot: a list of
                      dots and greyed labels tells a screen reader nothing about where it is now.
                    */}
                    {step.isCurrent ? <span className="visually-hidden">Current step: </span> : null}
                    <span style={{ fontWeight: step.isCurrent ? 600 : 400 }}>{step.label}</span>
                    {step.at ? (
                      <span style={{ display: "block", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                        {formatDate(step.at)}
                      </span>
                    ) : null}
                    {step.note ? (
                      <span style={{ display: "block", color: "var(--text-muted)", fontSize: "var(--fs-xs)" }}>
                        {step.note}
                      </span>
                    ) : null}
                  </span>
                </li>
              ))}
            </ol>
          </section>
        </div>
      </div>

      <section className="mp-card" style={{ padding: "var(--space-4)" }} aria-labelledby="next-step">
        <h2 className="mp-section-title" id="next-step" style={{ fontSize: "var(--fs-h3)" }}>
          Next step
        </h2>

        {steps.length === 0 ? (
          <p style={{ margin: 0, color: "var(--text-muted)" }}>
            {isTerminalStatus(data.summary.status)
              ? "Nothing further is waiting on you for this order."
              : "Nothing is waiting on you for this order."}
          </p>
        ) : (
          <div className="mp-stack-sm">
            <div style={{ maxWidth: "28rem" }}>
              <TextField
                label="A note for the customer"
                hint="Optional. Shown on the order."
                maxLength={500}
                value={note}
                placeholder="Kept in stock, posted this afternoon"
                onChange={event => setNote(event.target.value)}
              />
            </div>

            <div className="d-flex flex-wrap" style={{ gap: "0.5rem" }}>
              {plainSteps.map(step => (
                <button
                  key={step.to}
                  type="button"
                  className={step.destructive ? "btn btn-sm btn-outline-danger" : "btn btn-sm btn-primary"}
                  disabled={move.isPending}
                  onClick={() => (step.destructive ? setConfirming(step) : move.mutate({ to: step.to }))}
                >
                  {step.label}
                </button>
              ))}

              {shipment ? (
                shipping ? (
                  <>
                    <div className="row g-2 w-100" style={{ maxWidth: "34rem" }}>
                      <div className="col-12 col-sm-5">
                        <TextField
                          label="Carrier"
                          required
                          maxLength={100}
                          value={carrier}
                          placeholder="Nepal Post"
                          onChange={event => setCarrier(event.target.value)}
                        />
                      </div>
                      <div className="col-12 col-sm-7">
                        <TextField
                          label="Tracking number"
                          required
                          maxLength={100}
                          value={tracking}
                          placeholder="NP123456789"
                          onChange={event => setTracking(event.target.value)}
                        />
                      </div>
                    </div>

                    {shipError ? (
                      <p role="alert" className="mp-alert mp-alert-danger mb-0">
                        {shipError}
                      </p>
                    ) : null}

                    <div className="d-flex" style={{ gap: "0.5rem" }}>
                      <button
                        type="button"
                        className="btn btn-sm btn-primary"
                        disabled={move.isPending}
                        onClick={() => {
                          if (carrier.trim().length === 0 || tracking.trim().length === 0) {
                            setShipError("A carrier and a tracking number are both needed to mark this order shipped.");
                            return;
                          }

                          setShipError(null);
                          move.mutate({ to: "Shipped", carrierName: carrier.trim(), trackingNumber: tracking.trim() });
                        }}
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
                    {shipment.label}
                  </button>
                )
              ) : null}
            </div>

            {plainSteps.length === 0 && !shipment ? null : (
              <p className="visually-hidden" role="status">
                {move.isPending ? "Saving the new status." : ""}
              </p>
            )}
          </div>
        )}
      </section>

      {confirming ? (
        <ConfirmDialog
          show
          title={confirming.to === "Cancelled" ? "Cancel this part of the order?" : "Record a return?"}
          confirmLabel={confirming.to === "Cancelled" ? "Cancel this order part" : "Record the return"}
          busy={move.isPending}
          onCancel={() => setConfirming(null)}
          onConfirm={() => move.mutate({ to: confirming.to })}
        >
          {confirming.to === "Cancelled" ? (
            <p className="mb-0">
              This stops you from fulfilling {data.summary.sellerOrderNumber}. The API accepts no step out of a cancelled
              order, so it cannot be undone from here — contact the customer if it changes. Their part of the order is
              unaffected.
            </p>
          ) : (
            <p className="mb-0">
              This records that the goods came back after delivery. Stock is not put back automatically: adjust it on the
              stock page if the items are resellable.
            </p>
          )}
        </ConfirmDialog>
      ) : null}
    </div>
  );
}