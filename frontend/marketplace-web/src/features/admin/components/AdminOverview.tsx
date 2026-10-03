/**
 * The admin overview: the whole marketplace in one page.
 *
 * The figures that decide whether anything needs attention come first — what is waiting for
 * moderation, what is waiting for a decision about a seller, what is waiting to be paid out —
 * and the growth charts are below them, because a chart is not an action.
 */

"use client";

import Link from "next/link";
import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Area, AreaChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { ShieldCheck } from "lucide-react";

import { Panel, RangePicker, StatRow, StatTile } from "@/components/dashboard/DashboardParts";
import { ErrorState } from "@/components/shared/Feedback";
import { adminApi } from "@/features/admin/api/adminApi";
import { formatCurrency, formatDate, formatNumber } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";
import type { DateRange } from "@/types/seller";

export function AdminOverview() {
  const [range, setRange] = useState<DateRange>("Last30Days");

  const summary = useQuery({ queryKey: queryKeys.admin.summary(), queryFn: () => adminApi.summary() });
  const growth = useQuery({ queryKey: queryKeys.admin.growth(range), queryFn: () => adminApi.growth(range) });
  const refunds = useQuery({ queryKey: queryKeys.admin.refundAnalytics(range), queryFn: () => adminApi.refundAnalytics(range) });
  const categories = useQuery({
    queryKey: queryKeys.admin.categoryPerformance(range),
    queryFn: () => adminApi.categoryPerformance(range),
  });
  const queue = useQuery({
    queryKey: queryKeys.admin.products({ page: 1, status: "PendingApproval" }),
    queryFn: () => adminApi.moderationQueue({ page: 1, status: "PendingApproval" }),
  });
  const lowStock = useQuery({
    queryKey: queryKeys.admin.platformInventory({ page: 1, lowStockOnly: true }),
    queryFn: () => adminApi.inventory({ page: 1, lowStockOnly: true }),
  });

  if (summary.isPending) {
    return <div className="mp-skeleton" style={{ height: "24rem", borderRadius: "var(--radius)" }} />;
  }

  if (summary.isError || !summary.data) {
    return <ErrorState message="We could not load the marketplace overview." />;
  }

  const data = summary.data;
  const growthPoints = growth.data ?? [];
  const categoryRows = categories.data ?? [];

  return (
    <div className="mp-stack">
      <div className="mp-spread">
        <p style={{ margin: 0, color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
          Figures as at {formatDate(data.generatedAt)}
        </p>
        <RangePicker label="Period" value={range} onChange={value => setRange(value as DateRange)} />
      </div>

      <StatRow columns={4}>
        {/*
          The two money tiles link to the report that breaks them down. A total with nowhere to
          go is a dead end; the number somebody acts on lives one click away.
        */}
        <div className="col-6 col-lg-3">
          <Link href="/admin/reports?report=sales" style={{ display: "block", height: "100%" }}>
            <StatTile lead label="Revenue today" value={formatCurrency(data.revenueToday)} hint={`${data.ordersToday} orders today`} />
          </Link>
        </div>
        <div className="col-6 col-lg-3">
          <Link href="/admin/reports?report=commissions" style={{ display: "block", height: "100%" }}>
            <StatTile
              label="Commission"
              value={formatCurrency(data.commissionRevenue)}
              tone="positive"
              hint="What the marketplace keeps"
            />
          </Link>
        </div>
        <div className="col-6 col-lg-3">
          <StatTile label="To pay sellers" value={formatCurrency(data.sellerPayouts)} hint="Accrued and not yet paid" />
        </div>
        <div className="col-6 col-lg-3">
          <StatTile
            label="Refunded"
            value={formatCurrency(data.refundedAmount)}
            tone={data.refundRate > 5 ? "danger" : "neutral"}
            hint={`${data.refundRate.toFixed(1)}% of orders`}
          />
        </div>
      </StatRow>

      {/*
        The queue counts are the ones that ask something of an admin. A chart of growth does not,
        so it goes below them.
      */}
      <div className="row g-3">
        <div className="col-12 col-sm-6 col-md-3">
          <Link href="/admin/moderation" className="mp-card d-block" style={{ padding: "var(--space-4)", color: "var(--text)" }}>
            <p className="mp-metric-label" style={{ margin: 0 }}>
              Waiting for moderation
            </p>
            <p className="mp-stat-value">{queue.isPending ? "…" : (queue.data?.totalCount ?? 0)}</p>
            <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
              listings a seller is waiting on
            </p>
          </Link>
        </div>

        <div className="col-12 col-sm-6 col-md-3">
          <Link href="/admin/sellers" className="mp-card d-block" style={{ padding: "var(--space-4)", color: "var(--text)" }}>
            <p className="mp-metric-label" style={{ margin: 0 }}>
              Sellers to review
            </p>
            <p className="mp-stat-value" style={{ color: data.pendingSellers > 0 ? "var(--warning)" : undefined }}>
              {data.pendingSellers}
            </p>
            <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
              {data.activeSellers} active, {data.suspendedSellers} suspended
            </p>
          </Link>
        </div>

        <div className="col-12 col-sm-6 col-md-3">
          {/* Refunds have their own screen: approving one moves money, so it is not something to
              be done from a summary tile. */}
          <Link href="/admin/refunds" className="mp-card d-block" style={{ padding: "var(--space-4)", color: "var(--text)" }}>
            <p className="mp-metric-label" style={{ margin: 0 }}>
              Open refunds
            </p>
            <p className="mp-stat-value" style={{ color: data.openRefunds > 0 ? "var(--warning)" : undefined }}>
              {data.openRefunds}
            </p>
            <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
              {refunds.data ? `${refunds.data.pending} awaiting a decision` : "awaiting a decision"}
            </p>
          </Link>
        </div>

        <div className="col-12 col-sm-6 col-md-3">
          <Link href="/admin/inventory" className="mp-card d-block" style={{ padding: "var(--space-4)", color: "var(--text)" }}>
            <p className="mp-metric-label" style={{ margin: 0 }}>
              Low on stock
            </p>
            <p className="mp-stat-value" style={{ color: (lowStock.data?.totalCount ?? 0) > 0 ? "var(--warning)" : undefined }}>
              {lowStock.isPending ? "…" : (lowStock.data?.totalCount ?? 0)}
            </p>
            <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
              variants across every store
            </p>
          </Link>
        </div>
      </div>

      <div className="row g-4">
        <div className="col-12 col-lg-8">
          <Panel title="Growth">
            {growthPoints.length === 0 ? (
              <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>Nothing in this period.</p>
            ) : (
              <div style={{ width: "100%", height: "16rem" }}>
                <ResponsiveContainer>
                  <AreaChart data={growthPoints} margin={{ top: 4, right: 8, bottom: 0, left: 0 }}>
                    <defs>
                      <linearGradient id="ordersFill" x1="0" y1="0" x2="0" y2="1">
                        <stop offset="0%" stopColor="#7c5cff" stopOpacity={0.4} />
                        <stop offset="100%" stopColor="#7c5cff" stopOpacity={0} />
                      </linearGradient>
                    </defs>
                    <CartesianGrid strokeDasharray="3 3" stroke="var(--border)" vertical={false} />
                    <XAxis dataKey="period" tick={{ fontSize: 11, fill: "var(--text-subtle)" }} tickFormatter={shortDate} />
                    <YAxis tick={{ fontSize: 11, fill: "var(--text-subtle)" }} width={40} />
                    <Tooltip
                      contentStyle={{ backgroundColor: "var(--bg-surface)", border: "1px solid var(--border)", borderRadius: 8, fontSize: 12 }}
                      labelFormatter={label => formatDate(String(label))}
                    />
                    <Area type="monotone" dataKey="orders" stroke="#7c5cff" fill="url(#ordersFill)" strokeWidth={2} />
                    <Area type="monotone" dataKey="customers" stroke="#2563eb" fill="none" strokeWidth={2} />
                    <Area type="monotone" dataKey="sellers" stroke="#0f9d58" fill="none" strokeWidth={2} />
                  </AreaChart>
                </ResponsiveContainer>
              </div>
            )}

            <p style={{ margin: "var(--space-2) 0 0", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
              Orders, customers and sellers over the period.
            </p>
          </Panel>
        </div>

        <div className="col-12 col-lg-4">
          <Panel title="The marketplace">
            <ul className="list-unstyled mb-0 mp-stack-sm" style={{ fontSize: "var(--fs-sm)" }}>
              <Row label="Customers" value={data.totalCustomers} hint={`${data.newCustomersThisMonth} this month`} />
              <Row label="Sellers" value={data.totalSellers} hint={`${data.newSellersThisMonth} this month`} />
              <Row label="Products" value={data.totalProducts} hint={`${data.publishedProducts} live`} />
              <Row label="Orders" value={data.totalOrders} hint={`${formatCurrency(data.averageOrderValue)} average`} />
              {/*
                Refund volume, from the API's own refund analytics for the chosen period. The
                summary's `conversionRate` is deliberately not shown: the service returns a hard-coded
                zero for it, and a rate that is always 0.0% is worse than no rate at all.
              */}
              <Row
                label="Refunds"
                value={refunds.data ? refunds.data.totalRequests : "—"}
                hint={
                  refunds.data
                    ? `${formatCurrency(refunds.data.amount)} · ${refunds.data.rate.toFixed(1)}% of orders`
                    : "in this period"
                }
              />
            </ul>
          </Panel>

          <Panel
            className="mt-3"
            title="Where the money goes"
            action={
              <Link href="/admin/reports" className="btn btn-sm btn-outline-secondary">
                Reports
              </Link>
            }
          >
            {categories.isPending ? (
              <div className="mp-skeleton" style={{ height: "8rem", borderRadius: "var(--radius)" }} aria-hidden />
            ) : categoryRows.length === 0 ? (
              <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
                Nothing sold in this period, so there is no category to rank.
              </p>
            ) : (
              <ul className="list-unstyled mb-0 mp-stack-sm" style={{ fontSize: "var(--fs-sm)" }}>
                {categoryRows.slice(0, 6).map(category => (
                  <li key={category.categoryId}>
                    <div className="mp-spread" style={{ gap: "var(--space-3)" }}>
                      <span style={{ minWidth: 0 }}>{category.name}</span>
                      <span style={{ display: "flex", alignItems: "baseline", gap: "var(--space-3)", flex: "none" }}>
                        <span style={{ color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                          {formatNumber(category.quantitySold)} sold
                        </span>
                        <span style={{ fontVariantNumeric: "tabular-nums", fontWeight: 600 }}>{formatCurrency(category.revenue)}</span>
                      </span>
                    </div>
                    {/*
                      The share is the API's own percentage of category revenue, drawn as a bar so
                      the ranking is readable at a glance. It is a proportion of what is in this
                      period, not a growth rate and not a margin.
                    */}
                    <div
                      aria-hidden
                      style={{ height: "4px", marginTop: "4px", backgroundColor: "var(--bg-subtle)", borderRadius: "2px" }}
                    >
                      <div
                        style={{
                          width: `${Math.min(100, category.share)}%`,
                          height: "100%",
                          backgroundColor: "var(--accent)",
                          borderRadius: "2px",
                        }}
                      />
                    </div>
                  </li>
                ))}
              </ul>
            )}

            <p style={{ margin: "var(--space-2) 0 0", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
              {categories.data && categories.data.length > 6
                ? `Top ${6} of ${categories.data.length} categories with sales in this period. The API returns at most 20.`
                : "Categories with sales in this period, by revenue."}
            </p>
          </Panel>

          <Panel className="mt-3" title="Trust">
            <p style={{ display: "flex", alignItems: "center", gap: "0.4rem", margin: 0, fontSize: "var(--fs-sm)" }}>
              <ShieldCheck size={16} aria-hidden style={{ color: "var(--success)" }} />
              Every listing is reviewed before a shopper sees it.
            </p>
            <p style={{ margin: "var(--space-2) 0 0", color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
              {data.pendingProducts} {data.pendingProducts === 1 ? "listing is" : "listings are"} in the queue now.
            </p>
          </Panel>
        </div>
      </div>
    </div>
  );
}

function Row({ label, value, hint }: { label: string; value: string | number; hint: string }) {
  return (
    <li className="d-flex justify-content-between align-items-baseline">
      <span style={{ color: "var(--text-muted)" }}>
        {label}
        <span style={{ display: "block", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>{hint}</span>
      </span>
      <span style={{ fontVariantNumeric: "tabular-nums", fontWeight: 600 }}>{value}</span>
    </li>
  );
}

function shortDate(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString(undefined, { month: "short", day: "numeric" });
}
