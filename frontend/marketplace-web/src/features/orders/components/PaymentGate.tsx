/**
 * Where a payment gateway sends the customer to pay.
 *
 * A gateway that needs something from the customer cannot be finished by the checkout page: the
 * money moves on the provider's own page, or on ours when the provider is a sandbox. Either way
 * the order is not paid until the server has confirmed it, and this screen is the way back in for
 * a sandbox one.
 *
 * It says what it is. A customer who lands here having just placed an order is owed a plain
 * answer: this is the payment step, nothing has been charged yet, and here is the button.
 */

"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { ErrorState } from "@/components/shared/Feedback";
import { paymentApi } from "@/features/orders/api/orderApi";
import { errorMessage } from "@/lib/errors";
import { formatCurrency, formatDate } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";

export function PaymentGate() {
  const searchParams = useSearchParams();
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | null>(null);

  const orderId = searchParams.get("orderId");
  const orderNumber = searchParams.get("order");
  const cameBackFailed = searchParams.get("failed") === "1";

  const payments = useQuery({ queryKey: queryKeys.payments.mine(), queryFn: () => paymentApi.mine() });

  // The payment is found by the order it belongs to, not by an id in the URL: a customer should
  // not be able to pay a different order by editing a link, and the order in the URL is the one
  // they are on.
  const payment = payments.data?.find(p => (orderId ? p.orderId === orderId : p.orderNumber === orderNumber)) ?? null;

  const verify = useMutation({
    mutationFn: () => paymentApi.verify(payment!.id),
    onSuccess: async () => {
      setError(null);
      await queryClient.invalidateQueries({ queryKey: queryKeys.payments.all });
      await queryClient.invalidateQueries({ queryKey: queryKeys.orders.all });
    },
    onError: failure => setError(errorMessage(failure, "The payment could not be confirmed. Nothing has been charged.")),
  });

  if (payments.isPending) {
    return <div className="mp-skeleton" style={{ height: "14rem", borderRadius: "var(--radius)" }} />;
  }

  if (payments.isError) {
    return <ErrorState message="We could not find your payment." />;
  }

  if (!payment) {
    return (
      <div className="mp-card" style={{ padding: "var(--space-5)" }}>
        <h1 style={{ marginTop: 0, fontSize: "var(--fs-h2)" }}>No payment to make</h1>
        <p style={{ color: "var(--text-muted)" }}>
          {orderNumber ? `We have no payment open for order ${orderNumber}.` : "We have no payment open for this order."} It may
          already be paid, or the link may be out of date.
        </p>
        <Link href="/orders" className="btn btn-primary">
          Your orders
        </Link>
      </div>
    );
  }

  const settled = payment.status === "Succeeded";

  return (
    <div className="mp-card" style={{ padding: "var(--space-5)", maxWidth: "34rem" }}>
      <h1 style={{ marginTop: 0, fontSize: "var(--fs-h2)" }}>{settled ? "Paid" : "Pay for your order"}</h1>

      <p style={{ color: "var(--text-muted)" }}>
        Order {payment.orderNumber} · {formatDate(payment.initiatedAt)}
      </p>

      <p style={{ fontSize: "var(--fs-h2)", fontWeight: 700, margin: "var(--space-3) 0" }}>{formatCurrency(payment.amount)}</p>

      {cameBackFailed && !settled ? (
        <p role="alert" className="mp-alert mp-alert-danger">
          The payment provider reported a failure. Nothing has been charged, and the order is still waiting.
        </p>
      ) : null}

      {error ? (
        <p role="alert" className="mp-alert mp-alert-danger">
          {error}
        </p>
      ) : null}

      {settled ? (
        <>
          <p style={{ color: "var(--text-muted)" }}>
            Reference {payment.transactionReference}. Your order is confirmed and the seller has been told.
          </p>
          <Link href={`/orders/${payment.orderId}`} className="btn btn-primary">
            View your order
          </Link>
        </>
      ) : (
        <>
          <p style={{ color: "var(--text-muted)" }}>
            This is the payment step for a sandbox provider, so nothing is charged and the card details are not asked
            for. Confirming asks the server to check with the provider, exactly as it would for a real payment.
          </p>

          <div className="d-flex flex-wrap" style={{ gap: "0.5rem" }}>
            <button type="button" className="btn btn-primary" onClick={() => verify.mutate()} disabled={verify.isPending}>
              {verify.isPending ? "Confirming…" : "Pay now"}
            </button>
            <Link href={`/orders/${payment.orderId}`} className="btn btn-outline-secondary">
              Pay later
            </Link>
          </div>
        </>
      )}
    </div>
  );
}
