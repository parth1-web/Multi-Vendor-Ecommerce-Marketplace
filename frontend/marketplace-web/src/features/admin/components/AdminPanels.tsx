/**
 * Seller accounts and the decisions an admin makes about them.
 *
 * Approving and suspending both ask for a reason, because a seller who is refused has to be told
 * why and a seller who is suspended has to be able to fix it. The status is in the row, and the
 * action says what it will do rather than "Approve".
 */

"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { Panel } from "@/components/dashboard/DashboardParts";
import { EmptyState, ErrorState, StatusBadge } from "@/components/shared/Feedback";
import { adminApi } from "@/features/admin/api/adminApi";
import { errorMessage } from "@/lib/errors";
import { cx, formatCurrency, formatDate } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";

const STATUSES = ["Pending", "Active", "Suspended", "Rejected"] as const;

type Seller = Awaited<ReturnType<typeof adminApi.sellers>>["items"][number];

export function AdminSellers() {
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState("");
  const [expanded, setExpanded] = useState<string | null>(null);
  const [reason, setReason] = useState<Record<string, string>>({});
  const [actionError, setActionError] = useState<string | null>(null);

  const sellers = useQuery({
    queryKey: queryKeys.admin.sellers({ page, status }),
    queryFn: () => adminApi.sellers({ page, status: status || undefined }),
  });

  const decide = useMutation({
    mutationFn: ({ id, next, why }: { id: string; next: string; why: string }) => adminApi.setSellerStatus(id, next, why),
    onSuccess: async () => {
      setActionError(null);
      setExpanded(null);
      await queryClient.invalidateQueries({ queryKey: queryKeys.admin.all });
      await queryClient.invalidateQueries({ queryKey: queryKeys.seller.all });
    },
    onError: error => setActionError(errorMessage(error)),
  });

  return (
    <div className="mp-stack">
      {actionError ? (
        <p role="alert" className="mp-alert mp-alert-danger">
          {actionError}
        </p>
      ) : null}

      <nav aria-label="Filter sellers" className="d-flex flex-wrap" style={{ gap: "0.4rem" }}>
        <button
          type="button"
          className={cx("btn btn-sm", status === "" ? "btn-primary" : "btn-outline-secondary")}
          onClick={() => {
            setStatus("");
            setPage(1);
          }}
        >
          All
        </button>
        {STATUSES.map(value => (
          <button
            key={value}
            type="button"
            className={cx("btn btn-sm", status === value ? "btn-primary" : "btn-outline-secondary")}
            onClick={() => {
              setStatus(value);
              setPage(1);
            }}
          >
            {value}
          </button>
        ))}
      </nav>

      {sellers.isPending ? (
        <div className="mp-skeleton" style={{ height: "14rem", borderRadius: "var(--radius)" }} />
      ) : sellers.isError || !sellers.data ? (
        <ErrorState message="We could not load the sellers." />
      ) : sellers.data.items.length === 0 ? (
        <EmptyState title="No sellers with that status" body="Try another status." />
      ) : (
        <div className="mp-table-wrap">
          <table className="mp-table">
            <caption className="visually-hidden">Sellers</caption>
            <thead>
              <tr>
                <th scope="col">Seller</th>
                <th scope="col">Applied</th>
                <th scope="col">Products</th>
                <th scope="col">Orders</th>
                <th scope="col">Revenue</th>
                <th scope="col">Status</th>
                <th scope="col" />
              </tr>
            </thead>
            <tbody>
              {sellers.data.items.map((seller: Seller) => (
                <>
                  <tr key={seller.id}>
                    <td>
                      <strong>{seller.businessName}</strong>
                      <span style={{ display: "block", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                        {seller.email}
                      </span>
                    </td>
                    <td style={{ color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>{formatDate(seller.appliedAt)}</td>
                    <td style={{ fontVariantNumeric: "tabular-nums" }}>{seller.productCount}</td>
                    <td style={{ fontVariantNumeric: "tabular-nums" }}>{seller.sellerOrderCount}</td>
                    <td style={{ fontVariantNumeric: "tabular-nums" }}>{formatCurrency(seller.totalRevenue)}</td>
                    <td>
                      <StatusBadge tone={sellerTone(seller.status)}>{seller.status}</StatusBadge>
                    </td>
                    <td>
                      <button
                        type="button"
                        className="btn btn-sm btn-outline-secondary"
                        onClick={() => {
                          setExpanded(expanded === seller.id ? null : seller.id);
                          setActionError(null);
                        }}
                        aria-expanded={expanded === seller.id}
                      >
                        {expanded === seller.id ? "Close" : "Decide"}
                      </button>
                    </td>
                  </tr>

                  {expanded === seller.id ? (
                    <tr key={`${seller.id}-decision`}>
                      <td colSpan={7} style={{ backgroundColor: "var(--bg-subtle)" }}>
                        <div className="mp-stack-sm">
                          <div>
                            <label htmlFor={`why-${seller.id}`} className="mp-metric-label">
                              Reason, kept on the account
                            </label>
                            <input
                              id={`why-${seller.id}`}
                              className="mp-input"
                              placeholder="What did you decide, and why?"
                              value={reason[seller.id] ?? ""}
                              onChange={event => setReason(current => ({ ...current, [seller.id]: event.target.value }))}
                            />
                          </div>

                          <div className="d-flex flex-wrap" style={{ gap: "0.5rem" }}>
                            {seller.status !== "Active" ? (
                              <button
                                type="button"
                                className="btn btn-sm btn-primary"
                                onClick={() => decide.mutate({ id: seller.id, next: "Active", why: reason[seller.id] ?? "Approved" })}
                                disabled={decide.isPending}
                              >
                                Approve this seller
                              </button>
                            ) : null}

                            {seller.status === "Active" ? (
                              <button
                                type="button"
                                className="btn btn-sm"
                                onClick={() => decide.mutate({ id: seller.id, next: "Suspended", why: reason[seller.id] ?? "" })}
                                disabled={decide.isPending || !reason[seller.id]?.trim()}
                                style={{ color: "var(--danger)" }}
                              >
                                Suspend this seller
                              </button>
                            ) : null}

                            {seller.status !== "Rejected" ? (
                              <button
                                type="button"
                                className="btn btn-sm btn-outline-secondary"
                                onClick={() => decide.mutate({ id: seller.id, next: "Rejected", why: reason[seller.id] ?? "" })}
                                disabled={decide.isPending || !reason[seller.id]?.trim()}
                              >
                                Refuse this seller
                              </button>
                            ) : null}
                          </div>

                          {seller.status === "Active" && !reason[seller.id]?.trim() ? (
                            <p style={{ margin: 0, color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                              Suspending a seller stops their listings immediately, so it needs a reason they can act on.
                            </p>
                          ) : null}
                        </div>
                      </td>
                    </tr>
                  ) : null}
                </>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {sellers.data && sellers.data.totalPages > 1 ? (
        <nav aria-label="Seller pages" className="d-flex justify-content-between align-items-center">
          <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page <= 1}>
            Previous
          </button>
          <span style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            Page {page} of {sellers.data.totalPages}
          </span>
          <button
            type="button"
            className="btn btn-sm btn-outline-secondary"
            onClick={() => setPage(p => Math.min(sellers.data!.totalPages, p + 1))}
            disabled={page >= sellers.data.totalPages}
          >
            Next
          </button>
        </nav>
      ) : null}
    </div>
  );
}

export function AdminReports() {
  const [range, setRange] = useState<"Last30Days" | "Last90Days" | "ThisYear">("Last90Days");

  const sales = useQuery({ queryKey: queryKeys.admin.salesReport(range), queryFn: () => adminApi.salesReport(range) });
  const sellers = useQuery({ queryKey: queryKeys.admin.sellerReport(), queryFn: () => adminApi.sellerReport() });
  const commissions = useQuery({ queryKey: queryKeys.admin.commissionReport(range), queryFn: () => adminApi.commissionReport(range) });

  return (
    <div className="mp-stack">
      <div className="d-flex justify-content-between align-items-center">
        <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
          The figures a marketplace is run on.{" "}
          <a href={adminApi.salesCsvUrl(range)} download>
            Download the sales report
          </a>
        </p>
        <select
          className="mp-input"
          style={{ width: "auto" }}
          value={range}
          onChange={event => setRange(event.target.value as typeof range)}
          aria-label="Report period"
        >
          <option value="Last30Days">Last 30 days</option>
          <option value="Last90Days">Last 90 days</option>
          <option value="ThisYear">This year</option>
        </select>
      </div>

      <Panel title="Sales by period">
        {sales.isPending ? (
          <div className="mp-skeleton" style={{ height: "10rem", borderRadius: "var(--radius)" }} />
        ) : (sales.data?.length ?? 0) === 0 ? (
          <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>No sales in this period.</p>
        ) : (
          <div className="mp-table-wrap">
            <table className="mp-table">
              <caption className="visually-hidden">Sales by period</caption>
              <thead>
                <tr>
                  <th scope="col">Period</th>
                  <th scope="col">Orders</th>
                  <th scope="col">Gross</th>
                  <th scope="col">Discounts</th>
                  <th scope="col">Commission</th>
                  <th scope="col">Net revenue</th>
                </tr>
              </thead>
              <tbody>
                {sales.data?.map(row => (
                  <tr key={row.period}>
                    <td>{formatDate(row.period)}</td>
                    <td style={{ fontVariantNumeric: "tabular-nums" }}>{row.orders}</td>
                    <td style={{ fontVariantNumeric: "tabular-nums" }}>{formatCurrency(row.grossRevenue)}</td>
                    <td style={{ fontVariantNumeric: "tabular-nums", color: "var(--text-muted)" }}>−{formatCurrency(row.discounts)}</td>
                    <td style={{ fontVariantNumeric: "tabular-nums" }}>{formatCurrency(row.commission)}</td>
                    <td style={{ fontVariantNumeric: "tabular-nums", fontWeight: 600 }}>{formatCurrency(row.netRevenue)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Panel>

      <Panel title="Sellers">
        {sellers.isPending ? (
          <div className="mp-skeleton" style={{ height: "10rem", borderRadius: "var(--radius)" }} />
        ) : (sellers.data?.length ?? 0) === 0 ? (
          <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>No sellers yet.</p>
        ) : (
          <div className="mp-table-wrap">
            <table className="mp-table">
              <caption className="visually-hidden">Seller performance</caption>
              <thead>
                <tr>
                  <th scope="col">Store</th>
                  <th scope="col">Status</th>
                  <th scope="col">Products</th>
                  <th scope="col">Orders</th>
                  <th scope="col">Gross</th>
                  <th scope="col">Net to seller</th>
                  <th scope="col">Rating</th>
                </tr>
              </thead>
              <tbody>
                {sellers.data?.map(row => (
                  <tr key={row.sellerId}>
                    <td>{row.storeName}</td>
                    <td>
                      <StatusBadge tone={sellerTone(row.status)}>{row.status}</StatusBadge>
                    </td>
                    <td style={{ fontVariantNumeric: "tabular-nums" }}>{row.products}</td>
                    <td style={{ fontVariantNumeric: "tabular-nums" }}>{row.orders}</td>
                    <td style={{ fontVariantNumeric: "tabular-nums" }}>{formatCurrency(row.grossRevenue)}</td>
                    <td style={{ fontVariantNumeric: "tabular-nums" }}>{formatCurrency(row.netEarnings)}</td>
                    <td style={{ fontVariantNumeric: "tabular-nums" }}>{row.averageRating.toFixed(1)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Panel>

      <Panel title="Commission">
        {commissions.isPending ? (
          <div className="mp-skeleton" style={{ height: "8rem", borderRadius: "var(--radius)" }} />
        ) : (commissions.data?.length ?? 0) === 0 ? (
          <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--font-sm)" }}>No commission in this period.</p>
        ) : (
          <div className="mp-table-wrap">
            <table className="mp-table">
              <caption className="visually-hidden">Commission by seller</caption>
              <thead>
                <tr>
                  <th scope="col">Store</th>
                  <th scope="col">Orders</th>
                  <th scope="col">Gross</th>
                  <th scope="col">Commission</th>
                  <th scope="col">Seller earnings</th>
                  <th scope="col">Paid out</th>
                </tr>
              </thead>
              <tbody>
                {commissions.data?.map(row => (
                  <tr key={row.sellerId}>
                    <td>{row.storeName}</td>
                    <td style={{ fontVariantNumeric: "tabular-nums" }}>{row.orders}</td>
                    <td style={{ fontVariantNumeric: "tabular-nums" }}>{formatCurrency(row.grossRevenue)}</td>
                    <td style={{ fontVariantNumeric: "tabular-nums" }}>{formatCurrency(row.commissionAmount)}</td>
                    <td style={{ fontVariantNumeric: "tabular-nums" }}>{formatCurrency(row.sellerEarnings)}</td>
                    <td style={{ fontVariantNumeric: "tabular-nums", color: "var(--text-muted)" }}>{formatCurrency(row.paidOut)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Panel>
    </div>
  );
}

export function AdminAuditLog() {
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");

  const logs = useQuery({
    queryKey: queryKeys.admin.auditLogs({ page, search }),
    queryFn: () => adminApi.auditLogs({ page, search: search || undefined }),
  });

  return (
    <div className="mp-stack">
      <div className="d-flex" style={{ gap: "0.5rem" }}>
        <input
          className="mp-input"
          placeholder="Search by entity, name or actor"
          aria-label="Search the audit log"
          value={search}
          onChange={event => {
            setSearch(event.target.value);
            setPage(1);
          }}
        />
      </div>

      <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
        Everything that changed something, and who changed it. This is the record to read when a figure looks wrong.
      </p>

      {logs.isPending ? (
        <div className="mp-skeleton" style={{ height: "16rem", borderRadius: "var(--radius)" }} />
      ) : logs.isError || !logs.data ? (
        <ErrorState message="We could not load the audit log." />
      ) : logs.data.items.length === 0 ? (
        <EmptyState title="Nothing recorded" body="Actions taken by an admin appear here." />
      ) : (
        <div className="mp-table-wrap">
          <table className="mp-table">
            <caption className="visually-hidden">Audit log</caption>
            <thead>
              <tr>
                <th scope="col">When</th>
                <th scope="col">Action</th>
                <th scope="col">What</th>
                <th scope="col">Who</th>
              </tr>
            </thead>
            <tbody>
              {logs.data.items.map(entry => (
                <tr key={entry.id}>
                  <td style={{ color: "var(--text-subtle)", fontSize: "var(--fs-xs)", whiteSpace: "nowrap" }}>
                    {formatDate(entry.createdAt)}
                  </td>
                  <td>
                    <StatusBadge tone="info">{entry.action}</StatusBadge>
                  </td>
                  <td>
                    {entry.entityName ?? entry.entityType}
                    <span style={{ display: "block", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                      {entry.entityType}
                      {entry.correlationId ? ` · ${entry.correlationId.slice(0, 8)}` : ""}
                    </span>
                  </td>
                  <td style={{ color: "var(--text-muted)" }}>{entry.actorEmail || "system"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {logs.data && logs.data.totalPages > 1 ? (
        <nav aria-label="Audit pages" className="d-flex justify-content-between align-items-center">
          <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page <= 1}>
            Older
          </button>
          <span style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            Page {page} of {logs.data.totalPages}
          </span>
          <button
            type="button"
            className="btn btn-sm btn-outline-secondary"
            onClick={() => setPage(p => Math.min(logs.data!.totalPages, p + 1))}
            disabled={page >= logs.data.totalPages}
          >
            Newer
          </button>
        </nav>
      ) : null}
    </div>
  );
}

function sellerTone(status: string): "success" | "warning" | "danger" | "info" {
  switch (status) {
    case "Active":
      return "success";
    case "Pending":
      return "warning";
    case "Suspended":
    case "Rejected":
      return "danger";
    default:
      return "info";
  }
}
