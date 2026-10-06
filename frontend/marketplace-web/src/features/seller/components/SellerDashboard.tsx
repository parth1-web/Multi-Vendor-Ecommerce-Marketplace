/**
 * The seller dashboard.
 *
 * Everything on it is scoped by the token, never by a parameter, so a seller can only ever see
 * their own figures. The range is one piece of state for the whole page rather than one per
 * panel: a dashboard where the chart covers last week and the table covers last quarter is not a
 * dashboard anybody can act on.
 */

"use client";

import Link from "next/link";
import { useState, type ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Pie,
  PieChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { AlertTriangle, LayoutDashboard } from "lucide-react";

import { Panel, RangePicker, StatRow, StatTile } from "@/components/dashboard/DashboardParts";
import { ErrorState, StatusBadge } from "@/components/shared/Feedback";
import { sellerApi } from "@/features/seller/api/sellerApi";
import { orderStatusLabel, orderStatusTone } from "@/features/seller/lib/orderStatus";
import { formatCurrency, formatDate } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";
import { useAuth } from "@/providers/AuthProvider";
import type { DateRange } from "@/types/seller";

/** A calm, readable palette: distinguishable without shouting, and legible on both themes. */
const CHART_COLOURS = ["#7c5cff", "#2563eb", "#0f9d58", "#d97706", "#dc2626", "#0891b2", "#9333ea", "#65a30d"];

export function SellerDashboard() {
  const { user, isHydrating, signOut } = useAuth();
  const [range, setRange] = useState<DateRange>("Last30Days");

  const summary = useQuery({ queryKey: queryKeys.seller.summary(), queryFn: () => sellerApi.summary() });
  const revenue = useQuery({ queryKey: queryKeys.seller.revenue(range), queryFn: () => sellerApi.revenue(range) });
  const topProducts = useQuery({ queryKey: queryKeys.seller.topProducts(range), queryFn: () => sellerApi.topProducts(range) });
  const categories = useQuery({ queryKey: queryKeys.seller.salesByCategory(range), queryFn: () => sellerApi.salesByCategory(range) });
  const orders = useQuery({ queryKey: queryKeys.seller.orders({ page: 1, pageSize: 8 }), queryFn: () => sellerApi.orders({ page: 1, pageSize: 8 }) });

  // A pending seller has an account but no catalogue to manage, so the page says so rather than
  // showing zeros that look like a business with no customers.
  if (user?.role === "Seller" && user.sellerStatus && user.sellerStatus !== "Active") {
    return (
      <div>
        <Panel title="Your seller account">
          <p>
            Your account is <strong>{user.sellerStatus.toLowerCase()}</strong>. A marketplace moderator reviews new
            sellers before they can list anything, and your dashboard opens up as soon as that is done.
          </p>
          <div className="d-flex" style={{ gap: "0.5rem" }}>
            <Link href="/profile" className="btn btn-sm btn-primary">
              Update your details
            </Link>
            <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => void signOut()}>
              Sign out
            </button>
          </div>
        </Panel>
      </div>
    );
  }

  if (isHydrating || summary.isPending) {
    return (
      <div>
        <div className="mp-skeleton" style={{ height: "24rem", borderRadius: "var(--radius)" }} />
      </div>
    );
  }

  if (summary.isError || !summary.data) {
    return (
      <div>
        <ErrorState message="We could not load your dashboard." />
      </div>
    );
  }

  const data = summary.data;
  const revenuePoints = revenue.data ?? [];
  const categorySlice = (categories.data ?? []).slice(0, 6);

  return (
    <div>
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">
            <LayoutDashboard size={22} aria-hidden className="me-2" />
            Your store
          </h1>
          <p className="mp-page-subtitle">
            {data.publishedProducts} of {data.totalProducts} products live
            {data.lastOrderAt ? ` · last order ${formatDate(data.lastOrderAt)}` : " · no orders yet"}
          </p>
        </div>

        <RangePicker label="Period" value={range} onChange={value => setRange(value as DateRange)} />
      </div>

      {data.pendingApprovalProducts > 0 || data.outOfStockProducts > 0 || data.unansweredReviews > 0 ? (
        <div className="mp-stack-sm mb-4">
          {data.pendingApprovalProducts > 0 ? (
            <Alert tone="info">
              {data.pendingApprovalProducts} {data.pendingApprovalProducts === 1 ? "product is" : "products are"} waiting
              for a moderator. They stay hidden from shoppers until they are approved.
            </Alert>
          ) : null}
          {data.outOfStockProducts > 0 ? (
            <p className="mp-alert mp-alert-warning">
              <Link href="/seller/inventory?filter=out" style={{ color: "inherit", textDecoration: "underline" }}>
                {data.outOfStockProducts} {data.outOfStockProducts === 1 ? "variant is" : "variants are"} out of
                stock
              </Link>
              . Shoppers can still find the product, and they cannot buy it.
            </p>
          ) : null}
          {data.unansweredReviews > 0 ? (
            <Alert tone="warning">
              {data.unansweredReviews} {data.unansweredReviews === 1 ? "review has" : "reviews have"} not been
              answered. A reply is the cheapest way to keep a rating.
            </Alert>
          ) : null}
        </div>
      ) : null}

      <StatRow columns={4}>
        <div className="col-6 col-lg-3">
          <StatTile label="Today" value={formatCurrency(data.todaySales)} hint="Sales placed today" />
        </div>
        <div className="col-6 col-lg-3">
          <StatTile
            label="This month"
            value={formatCurrency(data.monthSales)}
            hint={`${data.totalOrders} orders all time`}
          />
        </div>
        <div className="col-6 col-lg-3">
          <StatTile
            label="On its way to you"
            value={formatCurrency(data.pendingEarnings)}
            tone="positive"
            hint={`${formatCurrency(data.paidEarnings)} already paid`}
          />
        </div>
        <div className="col-6 col-lg-3">
          <StatTile label="Commission paid" value={formatCurrency(data.commissionPaid)} hint="The marketplace's cut" />
        </div>
      </StatRow>

      <div className="row g-4 mt-1">
        <div className="col-12 col-lg-8">
          <Panel title="Revenue">
            {revenue.isPending ? (
              <div className="mp-skeleton" style={{ height: "16rem", borderRadius: "var(--radius)" }} />
            ) : revenuePoints.length === 0 ? (
              <ChartEmpty message="No sales in this period, so there is nothing to plot." />
            ) : (
              <div style={{ width: "100%", height: "16rem" }}>
                <ResponsiveContainer>
                  <BarChart data={revenuePoints} margin={{ top: 4, right: 8, bottom: 0, left: 0 }}>
                    <CartesianGrid strokeDasharray="3 3" stroke="var(--border)" vertical={false} />
                    <XAxis dataKey="period" tick={{ fontSize: 11, fill: "var(--text-subtle)" }} tickFormatter={shortDate} />
                    <YAxis tick={{ fontSize: 11, fill: "var(--text-subtle)" }} width={60} />
                    <Tooltip
                      contentStyle={{ backgroundColor: "var(--bg-surface)", border: "1px solid var(--border)", borderRadius: 8, fontSize: 12 }}
                      formatter={(value: number, name: string) => [formatCurrency(value), name === "revenue" ? "Revenue" : "Net earnings"]}
                      labelFormatter={label => formatDate(String(label))}
                    />
                    <Bar dataKey="revenue" fill="#7c5cff" radius={[4, 4, 0, 0]} />
                  </BarChart>
                </ResponsiveContainer>
              </div>
            )}

            <div className="d-flex flex-wrap mt-3" style={{ gap: "var(--space-4)", fontSize: "var(--fs-sm)", color: "var(--text-muted)" }}>
              <span>
                Average order <strong style={{ color: "var(--text)" }}>{formatCurrency(data.averageOrderValue)}</strong>
              </span>
              <span>
                Rating <strong style={{ color: "var(--text)" }}>{data.averageRating.toFixed(1)}</strong> ({data.reviewCount})
              </span>
            </div>
          </Panel>
        </div>

        <div className="col-12 col-lg-4">
          <Panel title="Where sales come from">
            {categorySlice.length === 0 ? (
              <ChartEmpty message="No sales in this period." />
            ) : (
              <>
                <div style={{ width: "100%", height: "13rem" }}>
                  <ResponsiveContainer>
                    <PieChart>
                      <Pie data={categorySlice} dataKey="revenue" nameKey="name" innerRadius="55%" outerRadius="85%" paddingAngle={2}>
                        {categorySlice.map((slice, index) => (
                          <Cell key={slice.categoryId} fill={CHART_COLOURS[index % CHART_COLOURS.length]} />
                        ))}
                      </Pie>
                      <Tooltip
                        contentStyle={{ backgroundColor: "var(--bg-surface)", border: "1px solid var(--border)", borderRadius: 8, fontSize: 12 }}
                        formatter={(value: number, name: string) => [formatCurrency(value), name]}
                      />
                    </PieChart>
                  </ResponsiveContainer>
                </div>

                <ul className="list-unstyled mb-0" style={{ fontSize: "var(--fs-sm)" }}>
                  {categorySlice.map((slice, index) => (
                    <li key={slice.categoryId} className="d-flex justify-content-between align-items-center">
                      <span className="d-flex align-items-center" style={{ gap: "0.4rem" }}>
                        <span
                          aria-hidden
                          style={{
                            width: "0.6rem",
                            height: "0.6rem",
                            borderRadius: "2px",
                            backgroundColor: CHART_COLOURS[index % CHART_COLOURS.length],
                          }}
                        />
                        {slice.name}
                      </span>
                      <span style={{ color: "var(--text-muted)" }}>{slice.share.toFixed(0)}%</span>
                    </li>
                  ))}
                </ul>
              </>
            )}
          </Panel>
        </div>
      </div>

      <div className="row g-4 mt-1">
        <div className="col-12 col-lg-7">
          <Panel
            title="Orders to fulfil"
            action={
              <Link href="/seller/orders" className="btn btn-sm btn-outline-secondary">
                All orders
              </Link>
            }
          >
            {orders.isPending ? (
              <div className="mp-skeleton" style={{ height: "12rem", borderRadius: "var(--radius)" }} />
            ) : (orders.data?.items.length ?? 0) === 0 ? (
              <p style={{ color: "var(--text-muted)", margin: 0 }}>Nothing waiting. New orders appear here.</p>
            ) : (
              <table className="mp-table">
<thead>
                    <tr>
                      <th scope="col">Order</th>
                      <th scope="col" className="text-end">
                        Items
                      </th>
                      <th scope="col" className="text-end">
                        You earn
                      </th>
                      <th scope="col">Status</th>
                    </tr>
                  </thead>
                <tbody>
                  {orders.data?.items.map(order => (
                    <tr key={order.id}>
                      <td>
                        <Link href={`/seller/orders/${order.id}`} style={{ color: "var(--text)" }}>
                          {order.sellerOrderNumber}
                        </Link>
                      </td>
                      <td className="text-end" style={{ fontVariantNumeric: "tabular-nums" }}>{order.itemCount}</td>
                      <td className="text-end" style={{ fontVariantNumeric: "tabular-nums" }}>
                        {formatCurrency(order.sellerEarnings)}
                      </td>
                      <td>
                        <StatusBadge tone={orderStatusTone(order.status)}>{orderStatusLabel(order.status)}</StatusBadge>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </Panel>
        </div>

        <div className="col-12 col-lg-5">
          <Panel
            title="Needs attention"
            action={
              <Link href="/seller/inventory" className="btn btn-sm btn-outline-secondary">
                Stock
              </Link>
            }
          >
            <ul className="list-unstyled mb-0 mp-stack-sm" style={{ fontSize: "var(--fs-sm)" }}>
              <Line label="Orders waiting to be confirmed" value={data.pendingOrders} to="/seller/orders?status=Pending" />
              <Line label="Being packed" value={data.processingOrders} to="/seller/orders?status=Processing" />
              <Line label="On their way" value={data.shippedOrders} to="/seller/orders?status=Shipped" />
              <Line label="Delivered" value={data.completedOrders} to="/seller/orders?status=Delivered" />
              <Line label="Cancelled" value={data.cancelledOrders} to="/seller/orders?status=Cancelled" />
            </ul>

            {data.lowStockProducts > 0 ? (
              <p style={{ margin: "var(--space-3) 0 0", fontSize: "var(--fs-sm)" }}>
                <AlertTriangle size={14} aria-hidden className="me-1" style={{ color: "var(--warning)" }} />
                <Link href="/seller/inventory?filter=low" style={{ color: "var(--warning)" }}>
                  {data.lowStockProducts} {data.lowStockProducts === 1 ? "variant is" : "variants are"} low on stock
                </Link>
              </p>
            ) : null}
          </Panel>
        </div>
      </div>

      {topProducts.data && topProducts.data.length > 0 ? (
        <div className="mt-4">
          <Panel
            title="Best sellers"
            action={
              <Link href="/seller/products" className="btn btn-sm btn-outline-secondary">
                Your catalogue
              </Link>
            }
          >
            <ul className="list-unstyled mb-0 mp-stack-sm">
              {topProducts.data.map((product, index) => (
                <li key={product.productId} className="d-flex align-items-center" style={{ gap: "var(--space-3)" }}>
                  <span style={{ minWidth: "1.5rem", color: "var(--text-subtle)", fontSize: "var(--fs-sm)" }}>{index + 1}</span>
                  {product.imageUrl ? (
                    // eslint-disable-next-line @next/next/no-img-element
                    <img
                      src={product.imageUrl}
                      alt={product.name}
                      width={40}
                      height={40}
                      style={{ width: "2.5rem", height: "2.5rem", objectFit: "cover", borderRadius: "var(--radius-sm)" }}
                    />
                  ) : null}
                  <span style={{ flex: 1, fontSize: "var(--fs-sm)" }}>{product.name}</span>
                  <span style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>{product.quantitySold} sold</span>
                  <span style={{ minWidth: "5rem", textAlign: "right", fontSize: "var(--fs-sm)" }}>{formatCurrency(product.revenue)}</span>
                </li>
              ))}
            </ul>
          </Panel>
        </div>
      ) : null}
    </div>
  );
}

function Alert({ tone, children }: { tone: "info" | "warning"; children: ReactNode }) {
  return <p className={`mp-alert mp-alert-${tone}`}>{children}</p>;
}

function Line({ label, value, to }: { label: string; value: number; to: string }) {
  return (
    <li className="d-flex justify-content-between align-items-center">
      <span style={{ color: "var(--text-muted)" }}>{label}</span>
      {value > 0 ? (
        <Link href={to} style={{ fontVariantNumeric: "tabular-nums" }}>
          {value}
        </Link>
      ) : (
        <span style={{ color: "var(--text-subtle)" }}>0</span>
      )}
    </li>
  );
}

function ChartEmpty({ message }: { message: string }) {
  return (
    <p style={{ color: "var(--text-muted)", margin: 0, fontSize: "var(--fs-sm)" }}>
      {message}
    </p>
  );
}

function shortDate(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString(undefined, { month: "short", day: "numeric" });
}
