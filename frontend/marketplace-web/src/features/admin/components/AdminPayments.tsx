"use client";

/**
 * Every payment the marketplace has taken.
 *
 * A read-only screen on purpose. The API's only payment mutations are create, gateway-verify and
 * webhook, all of which are the checkout flow's own business rather than an administrator's, and
 * the two that would be dangerous — creating a payment, forcing a settlement — are not restricted
 * to admins at all. So this lists, searches and reports; it does not offer an action that would
 * move money.
 *
 * The one thing worth knowing about the list is that its rows are a projection: the API returns
 * every payment with an empty order number and no transaction ledger, so both are fetched for a
 * row only when it is opened. Showing a blank order column on twenty rows would look like a bug
 * and be one; fetching twenty extra records to avoid it would be twenty queries the admin did not
 * ask for.
 */

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ChevronDown, ChevronRight } from "lucide-react";

import { EmptyState, ErrorState, StatusBadge } from "@/components/shared/Feedback";
import { Pagination } from "@/components/navigation/Pagination";
import { adminApi } from "@/features/admin/api/adminApi";
import { cx, formatCurrency, formatDateTime, formatNumber } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";
import type { PaymentState } from "@/types/admin";

const BASE_PATH = "/admin/payments";

/** Every status in the API's enum, including the two no code path currently sets. */
const STATUSES: PaymentState[] = [
  "Initiated",
  "Pending",
  "Processing",
  "Succeeded",
  "Failed",
  "Cancelled",
  "Refunded",
  "PartiallyRefunded",
];

export function AdminPayments() {
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState<PaymentState | "">("");
  const [search, setSearch] = useState("");
  const [term, setTerm] = useState("");
  const [openId, setOpenId] = useState<string | null>(null);

  const payments = useQuery({
    queryKey: queryKeys.admin.payments({ page, status, term }),
    queryFn: () => adminApi.payments({ page, pageSize: 20, status: status || undefined, search: term || undefined }),
  });

  const filtered = status !== "" || term !== "";

  return (
    <div className="mp-stack">
      <form
        role="search"
        aria-label="Search and filter payments"
        className="mp-card"
        style={{ padding: "var(--space-3) var(--space-4)" }}
        onSubmit={event => {
          event.preventDefault();
          setTerm(search.trim());
          setPage(1);
        }}
      >
        <div className="row g-2 align-items-end">
          <div className="col-12 col-sm-7 col-md-8">
            <label htmlFor="payment-search" className="mp-metric-label">
              Search by transaction reference
            </label>
            <input
              id="payment-search"
              type="search"
              className="form-control form-control-sm"
              value={search}
              placeholder="The reference the gateway returned"
              onChange={event => setSearch(event.target.value)}
              autoComplete="off"
            />
          </div>

          <div className="col-12 col-sm-5 col-md-4">
            <label htmlFor="payment-status" className="mp-metric-label">
              Status
            </label>
            <select
              id="payment-status"
              className="form-select form-select-sm"
              value={status}
              onChange={event => {
                setStatus(event.target.value as PaymentState | "");
                setPage(1);
              }}
            >
              <option value="">Every status</option>
              {STATUSES.map(value => (
                <option key={value} value={value}>
                  {value}
                </option>
              ))}
            </select>
          </div>
        </div>
      </form>

      {payments.isPending ? (
        <div className="mp-skeleton" style={{ height: "14rem", borderRadius: "var(--radius)" }} aria-hidden />
      ) : payments.isError || !payments.data ? (
        <ErrorState message="We could not load the payments." onRetry={() => void payments.refetch()} />
      ) : payments.data.items.length === 0 ? (
        <EmptyState
          title={filtered ? "No payments match those filters" : "No payments yet"}
          body={
            filtered
              ? "Try another status, or clear the search to see every payment."
              : "Payments appear here as soon as a shopper completes checkout."
          }
          action={
            filtered ? (
              <button
                type="button"
                className="btn btn-sm btn-primary"
                onClick={() => {
                  setStatus("");
                  setTerm("");
                  setPage(1);
                }}
              >
                Show every payment
              </button>
            ) : undefined
          }
        />
      ) : (
        <>
          <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            {formatNumber(payments.data.totalCount)} {payments.data.totalCount === 1 ? "payment" : "payments"}
            {payments.data.totalPages > 1 ? ` · page ${payments.data.page} of ${payments.data.totalPages}` : ""}
          </p>

          <div className="mp-table-wrap">
            <table className="mp-table">
              <caption className="visually-hidden">Payments</caption>
              <thead>
                <tr>
                  <th scope="col">Reference</th>
                  <th scope="col">Provider</th>
                  <th scope="col" className="text-end">
                    Amount
                  </th>
                  <th scope="col">Taken</th>
                  <th scope="col">Status</th>
                  <th scope="col">
                    <span className="visually-hidden">Actions</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {payments.data.items.map(payment => (
                  <tr key={payment.id}>
                    <td style={{ fontVariantNumeric: "tabular-nums", fontSize: "var(--fs-sm)" }}>
                      <button
                        type="button"
                        className="btn btn-sm"
                        style={{ padding: 0, color: "var(--text)", fontWeight: 500, background: "none", border: 0 }}
                        aria-expanded={openId === payment.id}
                        aria-controls={`payment-detail-${payment.id}`}
                        onClick={() => setOpenId(openId === payment.id ? null : payment.id)}
                      >
                        {openId === payment.id ? <ChevronDown size={14} aria-hidden /> : <ChevronRight size={14} aria-hidden />}{" "}
                        {payment.transactionReference}
                      </button>
                    </td>
                    <td style={{ fontSize: "var(--fs-sm)", color: "var(--text-muted)" }}>{payment.provider}</td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums", fontWeight: 600 }}>
                      {formatCurrency(payment.amount, payment.currency)}
                    </td>
                    <td style={{ color: "var(--text-subtle)", fontSize: "var(--fs-xs)", whiteSpace: "nowrap" }}>
                      {formatDateTime(payment.initiatedAt)}
                    </td>
                    <td>
                      <StatusBadge tone={paymentTone(payment.status)}>{payment.status}</StatusBadge>
                    </td>
                    <td>
                      <button
                        type="button"
                        className={cx("btn btn-sm", openId === payment.id ? "btn-primary" : "btn-outline-secondary")}
                        aria-expanded={openId === payment.id}
                        aria-controls={`payment-detail-${payment.id}`}
                        onClick={() => setOpenId(openId === payment.id ? null : payment.id)}
                      >
                        {openId === payment.id ? "Close" : "Detail"}
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <Pagination page={payments.data.page} totalPages={payments.data.totalPages} query={{ status, search: term }} basePath={BASE_PATH} />
        </>
      )}

      {openId ? <PaymentDetail id={openId} /> : null}
    </div>
  );
}

/**
 * The per-payment read, which is the only endpoint that fills in the order number and the
 * gateway's transaction ledger. Opened on demand for one payment rather than for the page.
 */
function PaymentDetail({ id }: { id: string }) {
  const payment = useQuery({ queryKey: queryKeys.admin.payment(id), queryFn: () => adminApi.payment(id) });

  return (
    <section
      className="mp-card"
      style={{ padding: "var(--space-4)" }}
      id={`payment-detail-${id}`}
      aria-label="Payment detail"
    >
      {payment.isPending ? (
        <div className="mp-skeleton" style={{ height: "8rem", borderRadius: "var(--radius)" }} aria-hidden />
      ) : payment.isError || !payment.data ? (
        <ErrorState message="We could not open that payment." onRetry={() => void payment.refetch()} />
      ) : (
        <div className="mp-stack">
          <h2 style={{ margin: 0, fontSize: "var(--fs-h3)" }}>
            {payment.data.orderNumber || payment.data.transactionReference}
          </h2>

          <dl className="row g-2 mb-0" style={{ fontSize: "var(--fs-sm)" }}>
            <div className="col-6 col-md-3">
              <dt className="mp-metric-label">Amount</dt>
              <dd className="mb-0">{formatCurrency(payment.data.amount, payment.data.currency)}</dd>
            </div>
            <div className="col-6 col-md-3">
              <dt className="mp-metric-label">Provider</dt>
              <dd className="mb-0">{payment.data.provider}</dd>
            </div>
            <div className="col-6 col-md-3">
              <dt className="mp-metric-label">Taken</dt>
              <dd className="mb-0">{formatDateTime(payment.data.initiatedAt)}</dd>
            </div>
            <div className="col-6 col-md-3">
              <dt className="mp-metric-label">Completed</dt>
              <dd className="mb-0">
                {payment.data.completedAt ? formatDateTime(payment.data.completedAt) : "Not yet"}
              </dd>
            </div>
            {payment.data.gatewayPaymentId ? (
              <div className="col-12 col-md-6">
                <dt className="mp-metric-label">Gateway payment id</dt>
                <dd className="mb-0" style={{ fontVariantNumeric: "tabular-nums" }}>
                  {payment.data.gatewayPaymentId}
                </dd>
              </div>
            ) : null}
            {payment.data.requiresAction ? (
              <div className="col-12">
                <dt className="mp-metric-label">Action needed</dt>
                <dd className="mb-0">The shopper still has to complete something at the gateway.</dd>
              </div>
            ) : null}
            {payment.data.failureReason ? (
              <div className="col-12">
                <dt className="mp-metric-label">Why it failed</dt>
                <dd className="mb-0" style={{ color: "var(--danger)" }}>
                  {payment.data.failureReason}
                </dd>
              </div>
            ) : null}
          </dl>

          <div>
            <h3 className="mp-section-title" style={{ fontSize: "var(--fs-body)" }}>
              What the gateway reported
            </h3>

            {payment.data.transactions.length === 0 ? (
              <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
                Nothing recorded against this payment yet.
              </p>
            ) : (
              <ul className="list-unstyled mb-0 mp-stack-sm" style={{ fontSize: "var(--fs-sm)" }}>
                {payment.data.transactions.map(entry => (
                  <li key={entry.id} className="mp-spread" style={{ borderTop: "1px solid var(--border)", paddingTop: "var(--space-2)" }}>
                    <span>
                      {entry.type}
                      {entry.errorMessage ? (
                        <span style={{ display: "block", color: "var(--danger)", fontSize: "var(--fs-xs)" }}>
                          {entry.errorMessage}
                        </span>
                      ) : null}
                    </span>
                    <span style={{ display: "flex", alignItems: "center", gap: "var(--space-3)", flex: "none" }}>
                      <span style={{ color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                        {formatDateTime(entry.createdAt)}
                      </span>
                      <StatusBadge tone={entry.isSuccess ? "success" : "danger"}>
                        {entry.isSuccess ? "OK" : "Failed"}
                      </StatusBadge>
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </div>
      )}
    </section>
  );
}

function paymentTone(status: PaymentState): "success" | "warning" | "danger" | "info" {
  switch (status) {
    case "Succeeded":
      return "success";
    case "Failed":
    case "Cancelled":
      return "danger";
    case "Refunded":
    case "PartiallyRefunded":
      return "warning";
    case "Initiated":
    case "Pending":
    case "Processing":
      return "info";
    default:
      return "info";
  }
}