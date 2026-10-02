"use client";

/**
 * Every refund on the marketplace, and the decision that closes it.
 *
 * A refund is the one place an administrator moves real money, so the screen is built around the
 * two questions that precede it: what is being asked for, and what happens if this is approved.
 * Approving is not a status change — the API calls the gateway, puts stock back, reverses the
 * commission and marks the refund completed in one synchronous chain — so the dialog says exactly
 * that rather than calling it "Approve".
 *
 * The list is every refund, not a filter of the customer's: the admin route is `/api/refunds/all`,
 * because `/api/refunds` returns only the caller's own rows. Search matches the reason the shopper
 * gave, which is the text an admin actually has to go looking for.
 */

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ChevronDown, ChevronRight } from "lucide-react";

import { ConfirmDialog } from "@/components/shared/ConfirmDialog";
import { EmptyState, ErrorState, StatusBadge } from "@/components/shared/Feedback";
import { TextAreaField } from "@/components/forms/FormField";
import { Pagination } from "@/components/navigation/Pagination";
import { adminApi } from "@/features/admin/api/adminApi";
import { errorMessage } from "@/lib/errors";
import { formatCurrency, formatDateTime, formatNumber } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";
import { useToast } from "@/providers/ToastProvider";
import type { Refund, RefundReviewAction, RefundState } from "@/types/admin";

const BASE_PATH = "/admin/refunds";

/** Every status the API can hold, in the order work moves through them. */
const STATUSES: RefundState[] = ["Requested", "UnderReview", "Approved", "Processing", "Completed", "Rejected", "Failed", "Cancelled"];

/**
 * The two decisions an admin can actually make, derived from the API's own rules rather than from
 * the status column: a refund is decidable while it has been requested or taken into review, and
 * `MarkProcessing` is only offered because the endpoint accepts it — the service can never be in a
 * state that allows it, since approving moves straight through processing to completed.
 */
const DECIDABLE: RefundState[] = ["Requested", "UnderReview"];

export function AdminRefunds() {
  const queryClient = useQueryClient();
  const { push } = useToast();

  const [page, setPage] = useState(1);
  const [status, setStatus] = useState<RefundState | "">("");
  /** The term the server is filtering on. The search box keeps its own draft inside the toolbar. */
  const [term, setTerm] = useState("");
  const [openId, setOpenId] = useState<string | null>(null);
  const [deciding, setDeciding] = useState<{ refund: Refund; action: RefundReviewAction } | null>(null);
  const [note, setNote] = useState("");
  const [actionError, setActionError] = useState<string | null>(null);

  const refunds = useQuery({
    queryKey: queryKeys.admin.refunds({ page, status, term }),
    queryFn: () => adminApi.refunds({ page, pageSize: 20, status: status || undefined, search: term || undefined }),
  });

  const review = useMutation({
    mutationFn: (input: { id: string; action: RefundReviewAction; why: string }) =>
      adminApi.reviewRefund(input.id, input.action, input.why),
    onSuccess: async (updated, input) => {
      setDeciding(null);
      setNote("");
      setActionError(null);
      // The refund, the order it belongs to, the stock it returned and the commission it reversed
      // all just changed. The dashboard's open-refund count is part of the summary read.
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: [..."admin","refunds"] as const }),
        queryClient.invalidateQueries({ queryKey: [..."admin","refunds"] as const }),
        queryClient.invalidateQueries({ queryKey: queryKeys.admin.orders({})[0] }),
        queryClient.invalidateQueries({ queryKey: queryKeys.admin.summary() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.admin.platformInventory({})[0] }),
      ]);

      push({
        tone: "success",
        title: input.action === "Approve" ? "Refund approved and completed" : "Refund rejected",
        body:
          input.action === "Approve"
            ? "The payment was refunded, stock returned and the commission reversed."
            : "The shopper has been told the request was declined.",
      });
    },
    onError: error => {
      setActionError(errorMessage(error));
      setDeciding(null);
    },
  });

  const filtered = status !== "" || term !== "";

  return (
    <div className="mp-stack">
      <RefundToolbar
        search={term}
        status={status}
        onSearch={value => {
          setTerm(value.trim());
          setPage(1);
        }}
        onStatus={value => {
          setStatus(value);
          setPage(1);
        }}
      />

      {status !== "" ? (
        <nav aria-label="Filter by status" className="d-flex flex-wrap" style={{ gap: "0.4rem" }}>
          {STATUSES.map(value => (
            <button
              key={value}
              type="button"
              className={status === value ? "btn btn-sm btn-primary" : "btn btn-sm btn-outline-secondary"}
              aria-pressed={status === value}
              onClick={() => {
                setStatus(value);
                setPage(1);
              }}
            >
              {value}
            </button>
          ))}
        </nav>
      ) : null}

      {actionError && !deciding ? (
        <p role="alert" className="mp-alert mp-alert-danger">
          {actionError}
        </p>
      ) : null}

      {refunds.isPending ? (
        <div className="mp-skeleton" style={{ height: "14rem", borderRadius: "var(--radius)" }} aria-hidden />
      ) : refunds.isError || !refunds.data ? (
        <ErrorState message="We could not load the refunds." onRetry={() => void refunds.refetch()} />
      ) : refunds.data.items.length === 0 ? (
        <EmptyState
          title={filtered ? "No refunds match those filters" : "No refunds have been requested"}
          body={
            filtered
              ? "Try another status, or clear the search to see every refund."
              : "When a shopper asks for their money back, it appears here with what they said and what it costs."
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
                Show every refund
              </button>
            ) : undefined
          }
        />
      ) : (
        <>
          <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            {formatNumber(refunds.data.totalCount)} {refunds.data.totalCount === 1 ? "refund" : "refunds"}
            {refunds.data.totalPages > 1 ? ` · page ${refunds.data.page} of ${refunds.data.totalPages}` : ""}
          </p>

          <div className="mp-table-wrap">
            <table className="mp-table">
              <caption className="visually-hidden">Refund requests</caption>
              <thead>
                <tr>
                  <th scope="col">Order</th>
                  <th scope="col">Requested</th>
                  <th scope="col" className="text-end">
                    Amount
                  </th>
                  <th scope="col">Reason</th>
                  <th scope="col">Status</th>
                  <th scope="col">
                    <span className="visually-hidden">Actions</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {refunds.data.items.map(refund => (
                  <tr key={refund.id}>
                    <td style={{ fontVariantNumeric: "tabular-nums" }}>
                      <button
                        type="button"
                        className="btn btn-sm"
                        style={{ padding: 0, color: "var(--text)", fontWeight: 500, background: "none", border: 0 }}
                        aria-expanded={openId === refund.id}
                        aria-controls={`refund-detail-${refund.id}`}
                        onClick={() => {
                          setOpenId(openId === refund.id ? null : refund.id);
                          setActionError(null);
                        }}
                      >
                        {openId === refund.id ? <ChevronDown size={14} aria-hidden /> : <ChevronRight size={14} aria-hidden />}{" "}
                        {refund.orderNumber}
                      </button>
                    </td>
                    <td style={{ color: "var(--text-subtle)", fontSize: "var(--fs-xs)", whiteSpace: "nowrap" }}>
                      {formatDateTime(refund.requestedAt)}
                    </td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums", fontWeight: 600 }}>
                      {formatCurrency(refund.amount)}
                    </td>
                    <td style={{ fontSize: "var(--fs-sm)", color: "var(--text-muted)" }}>{refund.reason}</td>
                    <td>
                      <StatusBadge tone={refundTone(refund.status)}>{refund.status}</StatusBadge>
                    </td>
                    <td>
                      <div className="d-flex justify-content-end" style={{ gap: "0.35rem" }}>
                        {DECIDABLE.includes(refund.status) ? (
                          <>
                            <button
                              type="button"
                              className="btn btn-sm btn-primary"
                              aria-label={`Approve the ${refund.amount} refund on ${refund.orderNumber}`}
                              onClick={() => {
                                setNote("");
                                setActionError(null);
                                setDeciding({ refund, action: "Approve" });
                              }}
                            >
                              Approve
                            </button>
                            <button
                              type="button"
                              className="btn btn-sm btn-outline-secondary"
                              aria-label={`Reject the refund on ${refund.orderNumber}`}
                              onClick={() => {
                                setNote("");
                                setActionError(null);
                                setDeciding({ refund, action: "Reject" });
                              }}
                            >
                              Reject
                            </button>
                          </>
                        ) : (
                          <span style={{ color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>Decided</span>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <Pagination page={refunds.data.page} totalPages={refunds.data.totalPages} query={{ status, search: term }} basePath={BASE_PATH} />
        </>
      )}

      {openId && refunds.data?.items.some(item => item.id === openId) ? (
        <RefundDetail id={openId} />
      ) : null}

      {deciding ? (
        <ConfirmDialog
          show
          title={
            deciding.action === "Approve"
              ? `Approve ${formatCurrency(deciding.refund.amount)} to ${deciding.refund.orderNumber}?`
              : `Reject the refund on ${deciding.refund.orderNumber}?`
          }
          confirmLabel={deciding.action === "Approve" ? "Refund the payment" : "Reject this refund"}
          cancelLabel="Leave it for now"
          busy={review.isPending}
          onCancel={() => setDeciding(null)}
          onConfirm={() => review.mutate({ id: deciding.refund.id, action: deciding.action, why: note.trim() })}
        >
          {deciding.action === "Approve" ? (
            <p className="mb-3">
              This refunds {formatCurrency(deciding.refund.amount)} to the shopper and completes immediately. The API
              calls the payment gateway, puts {deciding.refund.items.length}{" "}
              {deciding.refund.items.length === 1 ? "item" : "items"} back into stock and reverses the seller
              commission — there is no step in between to undo.
            </p>
          ) : (
            <p className="mb-3">
              The shopper is told the request was declined, with your reason. The money is not moved and no stock
              changes.
            </p>
          )}

          <TextAreaField
            label={deciding.action === "Approve" ? "Note (optional)" : "Reason"}
            required={deciding.action === "Reject"}
            rows={3}
            maxLength={1000}
            value={note}
            placeholder={deciding.action === "Approve" ? "Arrived damaged; agreed with the shopper" : "Outside the return window"}
            hint={
              deciding.action === "Reject"
                ? "Required. The shopper sees this, so say what they would need to do."
                : "Kept on the refund record."
            }
            error={
              deciding.action === "Reject" && note.trim().length === 0 && review.isError
                ? errorMessage(review.error)
                : undefined
            }
          />

          {actionError ? (
            <p role="alert" className="mp-alert mp-alert-danger mb-0">
              {actionError}
            </p>
          ) : null}
        </ConfirmDialog>
      ) : null}
    </div>
  );
}

/** What is being asked for, in the order an administrator reads it in. */
function RefundDetail({ id }: { id: string }) {
  const refund = useQuery({ queryKey: queryKeys.admin.refund(id), queryFn: () => adminApi.refund(id) });

  return (
    <section
      className="mp-card"
      style={{ padding: "var(--space-4)" }}
      id={`refund-detail-${id}`}
      aria-label="Refund detail"
    >
      {refund.isPending ? (
        <div className="mp-skeleton" style={{ height: "8rem", borderRadius: "var(--radius)" }} aria-hidden />
      ) : refund.isError || !refund.data ? (
        <ErrorState message="We could not open that refund." onRetry={() => void refund.refetch()} />
      ) : (
        <div className="mp-stack">
          <h2 style={{ margin: 0, fontSize: "var(--fs-h3)" }}>{refund.data.orderNumber}</h2>

          <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>{refund.data.reason}</p>
          {refund.data.description ? (
            <p style={{ margin: 0, fontSize: "var(--fs-sm)" }}>{refund.data.description}</p>
          ) : null}

          <div className="mp-table-wrap">
            <table className="mp-table">
              <caption className="visually-hidden">Items being refunded</caption>
              <thead>
                <tr>
                  <th scope="col">Item</th>
                  <th scope="col">SKU</th>
                  <th scope="col" className="text-end">
                    Quantity
                  </th>
                  <th scope="col" className="text-end">
                    Amount
                  </th>
                </tr>
              </thead>
              <tbody>
                {refund.data.items.map(item => (
                  <tr key={item.id}>
                    <td>{item.productName}</td>
                    <td style={{ fontVariantNumeric: "tabular-nums", color: "var(--text-muted)" }}>{item.sku}</td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums" }}>
                      {item.quantity}
                    </td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums" }}>
                      {formatCurrency(item.amount)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="d-flex flex-wrap" style={{ gap: "var(--space-4)", fontSize: "var(--fs-sm)" }}>
            <span>
              <span className="mp-metric-label" style={{ display: "block" }}>
                Requested
              </span>
              {formatDateTime(refund.data.requestedAt)}
            </span>
            {refund.data.reviewedAt ? (
              <span>
                <span className="mp-metric-label" style={{ display: "block" }}>
                  Reviewed
                </span>
                {formatDateTime(refund.data.reviewedAt)}
              </span>
            ) : null}
            {refund.data.completedAt ? (
              <span>
                <span className="mp-metric-label" style={{ display: "block" }}>
                  Completed
                </span>
                {formatDateTime(refund.data.completedAt)}
              </span>
            ) : null}
            {refund.data.reviewNote ? (
              <span>
                <span className="mp-metric-label" style={{ display: "block" }}>
                  Review note
                </span>
                {refund.data.reviewNote}
              </span>
            ) : null}
            {refund.data.rejectionReason ? (
              <span>
                <span className="mp-metric-label" style={{ display: "block" }}>
                  Declined because
                </span>
                {refund.data.rejectionReason}
              </span>
            ) : null}
          </div>
        </div>
      )}
    </section>
  );
}

function RefundToolbar({
  search,
  status,
  onSearch,
  onStatus,
}: {
  search: string;
  status: RefundState | "";
  onSearch: (value: string) => void;
  onStatus: (value: RefundState | "") => void;
}) {
  // The box is a local draft so a keystroke does not fire a request per character; the search runs on
  // submit, which is also what the URL then reflects.
  const [draft, setDraft] = useState(search);

  return (
    <form
      role="search"
      aria-label="Search and filter refunds"
      className="mp-card"
      style={{ padding: "var(--space-3) var(--space-4)" }}
      onSubmit={event => {
        event.preventDefault();
        onSearch(draft);
      }}
    >
      <div className="row g-2 align-items-end">
        <div className="col-12 col-sm-7 col-md-8">
          <label htmlFor="refund-search" className="mp-metric-label">
            Search by reason
          </label>
          <input
            id="refund-search"
            type="search"
            className="form-control form-control-sm"
            value={draft}
            placeholder="Arrived damaged, wrong size…"
            onChange={event => setDraft(event.target.value)}
            autoComplete="off"
          />
        </div>

        <div className="col-12 col-sm-5 col-md-4">
          <label htmlFor="refund-status" className="mp-metric-label">
            Status
          </label>
          <select
            id="refund-status"
            className="form-select form-select-sm"
            value={status}
            onChange={event => onStatus(event.target.value as RefundState | "")}
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
  );
}

function refundTone(status: RefundState): "success" | "warning" | "danger" | "info" {
  switch (status) {
    case "Completed":
      return "success";
    case "Rejected":
    case "Failed":
    case "Cancelled":
      return "danger";
    case "Requested":
    case "UnderReview":
      return "warning";
    default:
      return "info";
  }
}