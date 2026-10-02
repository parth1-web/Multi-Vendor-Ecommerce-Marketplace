/**
 * What a seller has earned, and what has not arrived yet.
 *
 * The two columns are gross and net because the difference is the marketplace's commission: a
 * seller paid against a gross figure and charged commission later has no idea what they are
 * owed, and the first thing they will ask support is about the gap.
 */

"use client";

import Link from "next/link";
import { useQuery } from "@tanstack/react-query";

import { Panel, StatRow, StatTile } from "@/components/dashboard/DashboardParts";
import { EmptyState, ErrorState, StatusBadge } from "@/components/shared/Feedback";
import { sellerApi } from "@/features/seller/api/sellerApi";
import { formatCurrency, formatDate } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";

export function SellerEarnings() {
  const summary = useQuery({ queryKey: queryKeys.seller.summary(), queryFn: () => sellerApi.summary() });
  const commissions = useQuery({ queryKey: queryKeys.seller.commissions({ page: 1 }), queryFn: () => sellerApi.commissions({ page: 1 }) });
  const payouts = useQuery({ queryKey: queryKeys.seller.payouts({ page: 1 }), queryFn: () => sellerApi.payouts({ page: 1 }) });

  if (summary.isPending) {
    return <div className="mp-skeleton" style={{ height: "16rem", borderRadius: "var(--radius)" }} />;
  }

  if (summary.isError || !summary.data) {
    return <ErrorState message="We could not load your earnings." />;
  }

  const data = summary.data;

  return (
    <div className="mp-stack">
      <StatRow columns={3}>
        <div className="col-12 col-md-4">
          <StatTile
            label="Pending"
            value={formatCurrency(data.pendingEarnings)}
            tone="positive"
            hint="Commission is taken when the order is paid, so this is yours once it clears"
          />
        </div>
        <div className="col-12 col-md-4">
          <StatTile label="Paid out" value={formatCurrency(data.paidEarnings)} hint="Transferred to you" />
        </div>
        <div className="col-12 col-md-4">
          <StatTile label="Commission taken" value={formatCurrency(data.commissionPaid)} hint="Across every order" />
        </div>
      </StatRow>

      <Panel title="Commission on each order">
        {commissions.isPending ? (
          <div className="mp-skeleton" style={{ height: "10rem", borderRadius: "var(--radius)" }} />
        ) : commissions.isError || !commissions.data ? (
          <ErrorState message="We could not load your commission records." />
        ) : commissions.data.items.length === 0 ? (
          <EmptyState title="No commission yet" body="Each order you fulfil is listed here with what was taken and what is yours." />
        ) : (
          <div className="mp-table-wrap">
            <table className="mp-table">
              <caption className="visually-hidden">Commission by order</caption>
              <thead>
                <tr>
                  <th scope="col">Order</th>
                  <th scope="col">Gross</th>
                  <th scope="col">Commission</th>
                  <th scope="col">You receive</th>
                  <th scope="col">Status</th>
                  <th scope="col">Accrued</th>
                </tr>
              </thead>
<tbody>
                  {commissions.data.items.map(commission => (
                    <tr key={commission.id}>
                      <td>
                        {/*
                          The seller commission endpoint does not fill in the order number — it is
                          blank on every row — so the link is built from the id the same response
                          does carry. A blank cell labelled "Order" is worse than no column.
                        */}
                        <Link href={`/seller/orders/${commission.sellerOrderId}`} style={{ fontVariantNumeric: "tabular-nums" }}>
                          {commission.sellerOrderNumber || shortId(commission.sellerOrderId)}
                        </Link>
                      </td>
                    <td style={{ fontVariantNumeric: "tabular-nums" }}>{formatCurrency(commission.grossAmount, commission.currency)}</td>
                    <td style={{ fontVariantNumeric: "tabular-nums", color: "var(--text-muted)" }}>
                      {formatCurrency(commission.commissionAmount, commission.currency)}
                    </td>
                    <td style={{ fontVariantNumeric: "tabular-nums", fontWeight: 600 }}>
                      {formatCurrency(commission.sellerAmount, commission.currency)}
                    </td>
                    <td>
                      <StatusBadge tone={commissionTone(commission.status)}>{commission.status}</StatusBadge>
                    </td>
                    <td style={{ color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>{formatDate(commission.accruedAt ?? commission.createdAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Panel>

      <Panel title="Payouts">
        {payouts.isPending ? (
          <div className="mp-skeleton" style={{ height: "8rem", borderRadius: "var(--radius)" }} />
        ) : payouts.isError || !payouts.data ? (
          <ErrorState message="We could not load your payouts." />
        ) : payouts.data.items.length === 0 ? (
          <p style={{ color: "var(--text-muted)", margin: 0 }}>
            No payouts yet. Accrued commission is paid out once it has cleared.
          </p>
        ) : (
          <ul className="list-unstyled mb-0 mp-stack-sm">
            {payouts.data.items.map(payout => (
              <li key={payout.id} className="d-flex justify-content-between align-items-center" style={{ fontSize: "var(--fs-sm)" }}>
                <span>
                  <strong>{payout.reference}</strong>
                  <span style={{ color: "var(--text-subtle)" }}>
                    {" "}
                    · {formatCount(payout.commissionCount)} · {formatDate(payout.createdAt)}
                  </span>
                  {payout.failureReason ? (
                    <span style={{ display: "block", color: "var(--danger)", fontSize: "var(--fs-xs)" }}>{payout.failureReason}</span>
                  ) : null}
                </span>
                <span className="d-flex align-items-center" style={{ gap: "var(--space-3)" }}>
                  <StatusBadge tone={commissionTone(payout.status)}>{payout.status}</StatusBadge>
                  <span style={{ fontVariantNumeric: "tabular-nums", fontWeight: 600 }}>{formatCurrency(payout.netAmount)}</span>
                </span>
              </li>
            ))}
          </ul>
        )}
      </Panel>
    </div>
  );
}

function formatCount(count: number): string {
  return `${count} ${count === 1 ? "order" : "orders"}`;
}

/** The first eight characters of an id: enough to recognise, not enough to be mistaken for a number. */
function shortId(id: string): string {
  return id.slice(0, 8);
}

function commissionTone(status: string): "success" | "warning" | "danger" | "info" {
  switch (status) {
    case "Paid":
    case "Completed":
      return "success";
    case "Failed":
    case "Reversed":
      return "danger";
    case "Pending":
    case "Accrued":
      return "warning";
    default:
      return "info";
  }
}
