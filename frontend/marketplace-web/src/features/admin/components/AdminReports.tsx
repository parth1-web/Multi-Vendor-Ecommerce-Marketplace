"use client";

/**
 * Reports: the four report families the API actually has, one at a time.
 *
 * Written against the endpoints rather than against an idea of what reporting should be. Every
 * column is a field the response contains, every control is a parameter the route accepts, and
 * nothing is calculated here that the server did not send — no totals, no ratios, no rankings.
 *
 * What changed when the reports were made real:
 *
 * - **The money columns are measurements.** Discounts, tax, shipping and refunds are sums of what
 *   the orders recorded at checkout, and net revenue is gross less discounts less refunds. The page
 *   no longer omits them or apologises for them.
 * - **Sellers and stock page.** Both used to arrive whole; the seller report now takes a page, a
 *   search, a status and an optional period, and the stock report takes a page, a search, a
 *   low-stock filter and an out-of-stock filter. The controls here are exactly those parameters.
 * - **The export is fetched, not linked**, because it is streamed, because it is bounded, and
 *   because the button has to be able to say whether it worked.
 *
 * Two things stay deliberately uncalculated. There is no platform total on any report: summing a
 * page in the browser would be a figure the server never endorsed, and the platform totals on{" "}
 * <a href="/admin">the overview</a> are the server's own. And net revenue excludes tax and
 * shipping, because whether collected tax is platform revenue is a policy question this codebase
 * has not answered — so they are reported in their own columns rather than quietly folded in.
 */

import { useRouter, useSearchParams } from "next/navigation";
import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Area, AreaChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { Download, Star } from "lucide-react";

import { EmptyState, ErrorState } from "@/components/shared/Feedback";
import { Pagination } from "@/components/navigation/Pagination";
import { adminApi } from "@/features/admin/api/adminApi";
import { errorMessage } from "@/lib/errors";
import { cx, formatCurrency, formatDate, formatNumber } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";
import { useToast } from "@/providers/ToastProvider";
import { DATE_RANGES, type DateRange } from "@/types/seller";

const REPORTS = [
  { id: "sales", label: "Sales" },
  { id: "sellers", label: "Sellers" },
  { id: "inventory", label: "Inventory" },
  { id: "commissions", label: "Commissions" },
] as const;

type ReportId = (typeof REPORTS)[number]["id"];

const BASE_PATH = "/admin/reports";
const PAGE_SIZE = 20;

const SELLER_STATUSES = ["Active", "Pending", "Suspended", "Rejected"];

export function AdminReports() {
  const router = useRouter();
  const searchParams = useSearchParams();

  const requested = searchParams.get("report");
  const active: ReportId = REPORTS.some(report => report.id === requested) ? (requested as ReportId) : "sales";
  const period = normalizeRange(searchParams.get("range"));

  // One navigation helper for the whole page: the selected report, its filters and its page all
  // live in the URL, so a particular view is a link somebody can be sent.
  const navigate = (changes: Record<string, string | undefined>) => {
    const next = new URLSearchParams(searchParams.toString());

    for (const [key, value] of Object.entries(changes)) {
      if (value === undefined || value === "") {
        next.delete(key);
      } else {
        next.set(key, value);
      }
    }

    // Anything other than paging restarts at page one, so a narrower filter cannot leave the
    // reader stranded past the end of a shorter result.
    if (!("page" in changes)) {
      next.delete("page");
    }

    const query = next.toString();
    router.replace(query ? `${BASE_PATH}?${query}` : BASE_PATH, { scroll: false });
  };

  return (
    <div className="mp-stack">
      <nav aria-label="Choose a report" className="d-flex flex-wrap" style={{ gap: "0.4rem" }}>
        {REPORTS.map(report => (
          <button
            key={report.id}
            type="button"
            className={cx("btn btn-sm", active === report.id ? "btn-primary" : "btn-outline-secondary")}
            aria-current={active === report.id ? "page" : undefined}
            onClick={() => navigate({ report: report.id })}
          >
            {report.label}
          </button>
        ))}
      </nav>

      {/* Only the selected report is mounted, so its query is the only one that runs. */}
      {active === "sales" ? <SalesReport period={period} onPeriodChange={range => navigate({ range })} /> : null}
      {active === "sellers" ? (
        <SellerReport
          period={period}
          onChange={changes => navigate(changes)}
          status={searchParams.get("status") ?? ""}
          term={searchParams.get("search") ?? ""}
          page={pageOf(searchParams)}
          hasRange={searchParams.has("range")}
        />
      ) : null}
      {active === "inventory" ? (
        <InventoryReport
          onChange={changes => navigate(changes)}
          term={searchParams.get("search") ?? ""}
          lowStock={searchParams.get("lowStock") === "1"}
          outOfStock={searchParams.get("outOfStock") === "1"}
          page={pageOf(searchParams)}
        />
      ) : null}
      {active === "commissions" ? <CommissionReport period={period} onPeriodChange={range => navigate({ range })} /> : null}

      <SemanticsNote />
    </div>
  );
}

/* --------------------------------------------------------------------------------- sales */

function SalesReport({ period, onPeriodChange }: { period: DateRange; onPeriodChange: (range: DateRange) => void }) {
  const report = useQuery({ queryKey: queryKeys.admin.salesReport(period), queryFn: () => adminApi.salesReport(period) });

  return (
    <section className="mp-stack" aria-labelledby="report-sales">
      <Toolbar
        title="Sales by period"
        headingId="report-sales"
        note={`One row per ${bucketWord(period)}, cancelled orders left out by the server.`}
      >
        <PeriodPicker value={period} onChange={onPeriodChange} />
      </Toolbar>

      {report.isPending ? (
        <Skeleton />
      ) : report.isError || !report.data ? (
        <ErrorState message="We could not load the sales report." onRetry={() => void report.refetch()} />
      ) : report.data.length === 0 ? (
        <EmptyState title="No sales in this period" body="Orders placed inside the period would appear here." />
      ) : (
        <>
          <div
            role="img"
            aria-label={`Gross revenue, net to sellers and commission for each ${bucketWord(period)} in the ${periodLabel(period)}`}
            style={{ width: "100%", height: "18rem" }}
          >
            <ResponsiveContainer>
              <AreaChart data={report.data} margin={{ top: 4, right: 8, bottom: 0, left: 0 }}>
                <defs>
                  <linearGradient id="salesGrossFill" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="0%" stopColor="#7c5cff" stopOpacity={0.35} />
                    <stop offset="100%" stopColor="#7c5cff" stopOpacity={0} />
                  </linearGradient>
                </defs>
                <CartesianGrid strokeDasharray="3 3" stroke="var(--border)" vertical={false} />
                <XAxis dataKey="period" tick={{ fontSize: 11, fill: "var(--text-subtle)" }} tickFormatter={shortDate} minTickGap={16} />
                <YAxis tick={{ fontSize: 11, fill: "var(--text-subtle)" }} width={56} tickFormatter={compactMoney} />
                <Tooltip
                  contentStyle={{ backgroundColor: "var(--bg-surface)", border: "1px solid var(--border)", borderRadius: 8, fontSize: 12 }}
                  labelFormatter={label => formatDate(String(label))}
                  formatter={(value: number, name: string) => [formatCurrency(value), name]}
                />
                <Area type="monotone" dataKey="grossRevenue" name="Gross revenue" stroke="#7c5cff" fill="url(#salesGrossFill)" strokeWidth={2} />
                <Area type="monotone" dataKey="netToSellers" name="Net to sellers" stroke="#0f9d58" fill="none" strokeWidth={2} />
                <Area type="monotone" dataKey="commission" name="Commission" stroke="#2563eb" fill="none" strokeWidth={2} />
              </AreaChart>
            </ResponsiveContainer>
          </div>

          <div className="mp-table-wrap">
            <table className="mp-table">
              <caption className="visually-hidden">Sales by {bucketWord(period)}</caption>
              <thead>
                <tr>
                  <th scope="col">{capitalise(bucketWord(period))}</th>
                  <th scope="col" className="text-end">
                    Orders
                  </th>
                  <th scope="col" className="text-end">
                    Gross
                  </th>
                  <th scope="col" className="text-end">
                    Discounts
                  </th>
                  <th scope="col" className="text-end">
                    Refunds
                  </th>
                  <th scope="col" className="text-end">
                    Net revenue
                  </th>
                  <th scope="col" className="text-end">
                    Shipping
                  </th>
                  <th scope="col" className="text-end">
                    Tax
                  </th>
                  <th scope="col" className="text-end">
                    Commission
                  </th>
                  <th scope="col" className="text-end">
                    Net to sellers
                  </th>
                </tr>
              </thead>
              <tbody>
                {report.data.map(row => (
                  <tr key={row.period}>
                    <td style={{ whiteSpace: "nowrap" }}>{formatDate(row.period)}</td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums" }}>
                      {formatNumber(row.orders)}
                    </td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums", fontWeight: 600 }}>
                      {formatCurrency(row.grossRevenue)}
                    </td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums", color: "var(--text-muted)" }}>
                      {row.discounts > 0 ? `−${formatCurrency(row.discounts)}` : formatCurrency(0)}
                    </td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums", color: "var(--text-muted)" }}>
                      {row.refunds > 0 ? `−${formatCurrency(row.refunds)}` : formatCurrency(0)}
                    </td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums", fontWeight: 600 }}>
                      {formatCurrency(row.netRevenue)}
                    </td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums", color: "var(--text-muted)" }}>
                      {formatCurrency(row.shipping)}
                    </td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums", color: "var(--text-muted)" }}>
                      {formatCurrency(row.tax)}
                    </td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums" }}>
                      {formatCurrency(row.commission)}
                    </td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums" }}>
                      {formatCurrency(row.netToSellers)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <ExportSales period={period} />
        </>
      )}
    </section>
  );
}

/**
 * The one export the API has, fetched so that the button can report what happened.
 *
 * It is a file of **orders**, not of the rows above: one row per order, cancelled ones included,
 * which the sales table above leaves out because it measures revenue. Only the period is shared,
 * and both sides resolve it with the same server-side code, so the two cannot disagree about which
 * days they cover.
 *
 * The server streams the rows and stops at a published limit. A file that arrives with exactly that
 * many rows may have been cut short, and the button says so rather than letting a partial export
 * pass as a complete one.
 */
function ExportSales({ period }: { period: DateRange }) {
  const { push } = useToast();
  const [busy, setBusy] = useState(false);

  const download = async () => {
    setBusy(true);

    try {
      const result = await adminApi.exportSalesCsv(period);
      const url = URL.createObjectURL(new Blob([result.text], { type: "text/csv;charset=utf-8" }));
      const anchor = document.createElement("a");
      anchor.href = url;
      anchor.download = result.fileName;
      document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
      URL.revokeObjectURL(url);

      const capped = result.rowLimit > 0 && result.rowCount >= result.rowLimit;
      push({
        tone: capped ? "warning" : "success",
        title: capped ? "Export stopped at the row limit" : "Sales CSV downloaded",
        body: capped
          ? `${formatNumber(result.rowCount)} rows is the server's limit for one export, so this file may be incomplete. Narrow the period for the rest.`
          : `${formatNumber(result.rowCount)} ${result.rowCount === 1 ? "order" : "orders"} written. The export is recorded in the audit log.`,
      });
    } catch (error) {
      push({ tone: "danger", title: "The export did not finish", body: errorMessage(error) });
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="mp-card" style={{ padding: "var(--space-3) var(--space-4)" }}>
      <div className="d-flex flex-wrap align-items-center justify-content-between" style={{ gap: "var(--space-3)" }}>
        <div>
          <p style={{ margin: 0, fontSize: "var(--fs-sm)", fontWeight: 500 }}>Order-level CSV</p>
          <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-xs)" }}>
            Every order placed in the {periodLabel(period)}, one row each, cancelled ones included — so it will not match the
            table above. Streamed as it is written and bounded by the server, and recorded in the audit log.
          </p>
        </div>
        <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => void download()} disabled={busy}>
          <Download size={14} aria-hidden /> {busy ? "Exporting…" : "Export sales orders CSV"}
        </button>
      </div>
      <p aria-live="polite" style={{ margin: "var(--space-2) 0 0", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
        {busy ? "Building the file. A wide period can take a moment; there is no progress bar because the server does not report one." : ""}
      </p>
    </div>
  );
}

/* -------------------------------------------------------------------------------- sellers */

function SellerReport({
  period,
  onChange,
  status,
  term,
  page,
  hasRange,
}: {
  period: DateRange;
  onChange: (changes: Record<string, string | undefined>) => void;
  status: string;
  term: string;
  page: number;
  hasRange: boolean;
}) {
  const [search, setSearch] = useState(term);
  const scoped = term !== "" || status !== "" || hasRange;

  const report = useQuery({
    queryKey: queryKeys.admin.sellerReport({ page, term, status, period: hasRange ? period : "lifetime" }),
    queryFn: () =>
      adminApi.sellerReport({
        page,
        pageSize: PAGE_SIZE,
        search: term || undefined,
        status: status || undefined,
        // Absent on purpose: no range means lifetime order figures, which is what this report
        // showed before it had filters. Sending the default window instead would silently replace
        // that with a thirty-day answer nobody asked for.
        range: hasRange ? period : undefined,
      }),
  });

  return (
    <section className="mp-stack" aria-labelledby="report-sellers">
      <Toolbar
        title="Sellers by gross revenue"
        headingId="report-sellers"
        note={
          scoped
            ? `Order figures cover the ${periodLabel(period)}.`
            : "Lifetime order figures for every seller, ranked by gross revenue."
        }
      >
        <PeriodPicker value={period} onChange={range => onChange({ range })} />
      </Toolbar>

      <form
        role="search"
        aria-label="Search and filter sellers"
        className="mp-card"
        style={{ padding: "var(--space-3) var(--space-4)" }}
        onSubmit={event => {
          event.preventDefault();
          onChange({ search: search.trim() || undefined });
        }}
      >
        <div className="row g-2 align-items-end">
          <div className="col-12 col-sm-7 col-md-8">
            <label htmlFor="seller-report-search" className="mp-metric-label">
              Search stores
            </label>
            <input
              key={`seller-search-${term}`}
              id="seller-report-search"
              type="search"
              className="form-control form-control-sm"
              value={search}
              placeholder="A store name or the seller's own name"
              onChange={event => setSearch(event.target.value)}
              autoComplete="off"
            />
          </div>

          <div className="col-12 col-sm-5 col-md-4">
            <label htmlFor="seller-report-status" className="mp-metric-label">
              Status
            </label>
            <select
              id="seller-report-status"
              className="form-select form-select-sm"
              value={status}
              onChange={event => onChange({ status: event.target.value || undefined })}
            >
              <option value="">Every status</option>
              {SELLER_STATUSES.map(value => (
                <option key={value} value={value}>
                  {value}
                </option>
              ))}
            </select>
          </div>

          {scoped ? (
            <div className="col-12 col-md-4">
              <button
                type="button"
                className="btn btn-sm btn-outline-secondary w-100"
                onClick={() => {
                  setSearch("");
                  onChange({ search: undefined, status: undefined, range: undefined });
                }}
              >
                Clear filters and show lifetime figures
              </button>
            </div>
          ) : null}
        </div>
      </form>

      {report.isPending ? (
        <Skeleton />
      ) : report.isError || !report.data ? (
        <ErrorState message="We could not load the seller report." onRetry={() => void report.refetch()} />
      ) : report.data.items.length === 0 ? (
        <EmptyState
          title="No sellers match those filters"
          body="Try another status, or clear the search to see every seller."
          action={
            scoped ? (
              <button
                type="button"
                className="btn btn-sm btn-primary"
                onClick={() => {
                  setSearch("");
                  onChange({ search: undefined, status: undefined, range: undefined });
                }}
              >
                Show every seller
              </button>
            ) : undefined
          }
        />
      ) : (
        <>
          <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            {formatNumber(report.data.totalCount)} {report.data.totalCount === 1 ? "seller" : "sellers"}
            {report.data.totalPages > 1 ? ` · page ${report.data.page} of ${report.data.totalPages}` : ""}
          </p>

          <div className="mp-table-wrap">
            <table className="mp-table">
              <caption className="visually-hidden">Seller performance</caption>
              <thead>
                <tr>
                  <th scope="col">Store</th>
                  <th scope="col">Status</th>
                  <th scope="col" className="text-end">
                    Products
                  </th>
                  <th scope="col" className="text-end">
                    Orders
                  </th>
                  <th scope="col" className="text-end">
                    Gross revenue
                  </th>
                  <th scope="col" className="text-end">
                    Commission
                  </th>
                  <th scope="col" className="text-end">
                    Net earnings
                  </th>
                  <th scope="col" className="text-end">
                    Rating
                  </th>
                </tr>
              </thead>
              <tbody>
                {report.data.items.map(row => (
                  <tr key={row.sellerId}>
                    <td>{row.storeName}</td>
                    <td style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>{row.status}</td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums" }}>
                      {formatNumber(row.products)}
                    </td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums" }}>
                      {formatNumber(row.orders)}
                    </td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums", fontWeight: 600 }}>
                      {formatCurrency(row.grossRevenue)}
                    </td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums" }}>
                      {formatCurrency(row.commission)}
                    </td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums" }}>
                      {formatCurrency(row.netEarnings)}
                    </td>
                    <td className="text-end">
                      {row.averageRating === null ? (
                        <span style={{ color: "var(--text-subtle)" }} title="No visible reviews yet">None yet</span>
                      ) : (
                        <span className="d-inline-flex align-items-center" style={{ gap: "0.2rem", fontVariantNumeric: "tabular-nums" }}>
                          <Star size={13} aria-hidden style={{ color: "var(--warning)" }} />
                          {row.averageRating.toFixed(1)}
                          <span className="visually-hidden">
                            {` out of 5 from ${row.reviewCount} ${row.reviewCount === 1 ? "review" : "reviews"}`}
                          </span>
                        </span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <Pagination
            page={report.data.page}
            totalPages={report.data.totalPages}
            query={{ search: term, status, range: hasRange ? period : "" }}
            basePath={BASE_PATH}
          />

          <p style={noteStyle}>
            Ratings are the mean of each store&apos;s visible reviews — the ones a shopper can see. A store with none shows
            &ldquo;None yet&rdquo; rather than zero, because zero is a score somebody gave.
          </p>
        </>
      )}
    </section>
  );
}

/* ------------------------------------------------------------------------------- inventory */

function InventoryReport({
  onChange,
  term,
  lowStock,
  outOfStock,
  page,
}: {
  onChange: (changes: Record<string, string | undefined>) => void;
  term: string;
  lowStock: boolean;
  outOfStock: boolean;
  page: number;
}) {
  const [search, setSearch] = useState(term);
  const filtered = term !== "" || lowStock || outOfStock;

  const report = useQuery({
    queryKey: queryKeys.admin.inventoryReport({ page, term, lowStock, outOfStock }),
    queryFn: () =>
      adminApi.inventoryReport({
        page,
        pageSize: PAGE_SIZE,
        search: term || undefined,
        lowStockOnly: lowStock,
        outOfStockOnly: outOfStock,
      }),
  });

  return (
    <section className="mp-stack" aria-labelledby="report-inventory">
      <Toolbar
        title="Stock, lowest first"
        headingId="report-inventory"
        note="Every variant on the marketplace, ordered by available quantity. This is the analytical view; adjusting stock happens in Stock."
      />

      <form
        role="search"
        aria-label="Search and filter stock"
        className="mp-card"
        style={{ padding: "var(--space-3) var(--space-4)" }}
        onSubmit={event => {
          event.preventDefault();
          onChange({ search: search.trim() || undefined });
        }}
      >
        <div className="row g-2 align-items-end">
          <div className="col-12 col-sm-6 col-md-6">
            <label htmlFor="inventory-report-search" className="mp-metric-label">
              Search stock
            </label>
            <input
              key={`inventory-search-${term}`}
              id="inventory-report-search"
              type="search"
              className="form-control form-control-sm"
              value={search}
              placeholder="A product, a SKU, or a store"
              onChange={event => setSearch(event.target.value)}
              autoComplete="off"
            />
          </div>

          <div className="col-12 col-sm-6 col-md-6 d-flex flex-wrap align-items-center" style={{ gap: "0.5rem" }}>
            <div className="form-check form-switch mb-0">
              <input
                id="inventory-low-stock"
                className="form-check-input"
                type="checkbox"
                checked={lowStock}
                onChange={event => onChange({ lowStock: event.target.checked ? "1" : undefined })}
              />
              <label className="form-check-label" htmlFor="inventory-low-stock" style={{ fontSize: "var(--fs-sm)" }}>
                Low stock only
              </label>
            </div>
            <div className="form-check form-switch mb-0">
              <input
                id="inventory-out-of-stock"
                className="form-check-input"
                type="checkbox"
                checked={outOfStock}
                onChange={event => onChange({ outOfStock: event.target.checked ? "1" : undefined })}
              />
              <label className="form-check-label" htmlFor="inventory-out-of-stock" style={{ fontSize: "var(--fs-sm)" }}>
                Out of stock only
              </label>
            </div>
            {filtered ? (
              <button
                type="button"
                className="btn btn-sm btn-outline-secondary"
                onClick={() => {
                  setSearch("");
                  onChange({ search: undefined, lowStock: undefined, outOfStock: undefined });
                }}
              >
                Clear
              </button>
            ) : null}
          </div>
        </div>
      </form>

      {report.isPending ? (
        <Skeleton />
      ) : report.isError || !report.data ? (
        <ErrorState message="We could not load the inventory report." onRetry={() => void report.refetch()} />
      ) : report.data.items.length === 0 ? (
        <EmptyState
          title={filtered ? "No stock matches those filters" : "No stock records"}
          body={filtered ? "Nothing on the marketplace meets that combination." : "A variant appears here once a seller lists it."}
          action={
            filtered ? (
              <button
                type="button"
                className="btn btn-sm btn-primary"
                onClick={() => {
                  setSearch("");
                  onChange({ search: undefined, lowStock: undefined, outOfStock: undefined });
                }}
              >
                Show every variant
              </button>
            ) : undefined
          }
        />
      ) : (
        <>
          <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            {formatNumber(report.data.totalCount)} {report.data.totalCount === 1 ? "variant" : "variants"}
            {report.data.totalPages > 1 ? ` · page ${report.data.page} of ${report.data.totalPages}` : ""}
          </p>

          <div className="mp-table-wrap">
            <table className="mp-table">
              <caption className="visually-hidden">Stock by variant</caption>
              <thead>
                <tr>
                  <th scope="col">Product</th>
                  <th scope="col">SKU</th>
                  <th scope="col">Store</th>
                  <th scope="col" className="text-end">
                    Available
                  </th>
                  <th scope="col" className="text-end">
                    Reserved
                  </th>
                  <th scope="col" className="text-end">
                    Sold
                  </th>
                  <th scope="col" className="text-end">
                    Low at
                  </th>
                  <th scope="col" className="text-end">
                    Stock value
                  </th>
                </tr>
              </thead>
              <tbody>
                {report.data.items.map(row => (
                  <tr key={`${row.productId}-${row.sku}`}>
                    <td>{row.productName}</td>
                    <td style={{ fontVariantNumeric: "tabular-nums", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                      {row.sku || "—"}
                    </td>
                    <td style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>{row.storeName}</td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums", fontWeight: 600 }}>
                      {formatNumber(row.available)}
                    </td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums" }}>
                      {formatNumber(row.reserved)}
                    </td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums" }}>
                      {formatNumber(row.sold)}
                    </td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums", color: "var(--text-muted)" }}>
                      {formatNumber(row.threshold)}
                    </td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums" }}>
                      {formatCurrency(row.stockValue)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <Pagination
            page={report.data.page}
            totalPages={report.data.totalPages}
            query={{ search: term, lowStock: lowStock ? "1" : "", outOfStock: outOfStock ? "1" : "" }}
            basePath={BASE_PATH}
          />

          <p style={noteStyle}>
            &ldquo;Low stock&rdquo; is sellable quantity — available less what is held for orders — at or below the
            variant&apos;s own threshold, which is the same rule the seller inventory screen uses. Stock value is available ×
            base price as the server computes it; a variant whose product has been deleted keeps its stock row and shows
            with no name or SKU.
          </p>
        </>
      )}
    </section>
  );
}

/* ----------------------------------------------------------------------------- commissions */

function CommissionReport({ period, onPeriodChange }: { period: DateRange; onPeriodChange: (range: DateRange) => void }) {
  const report = useQuery({
    queryKey: queryKeys.admin.commissionReport(period),
    queryFn: () => adminApi.commissionReport(period),
  });

  return (
    <section className="mp-stack" aria-labelledby="report-commissions">
      <Toolbar
        title="Commission by seller"
        headingId="report-commissions"
        note={`Everything here is scoped to the ${periodLabel(period)}, payouts included.`}
      >
        <PeriodPicker value={period} onChange={onPeriodChange} />
      </Toolbar>

      {report.isPending ? (
        <Skeleton />
      ) : report.isError || !report.data ? (
        <ErrorState message="We could not load the commission report." onRetry={() => void report.refetch()} />
      ) : report.data.length === 0 ? (
        <EmptyState
          title="No commission in this period"
          body="Commission is recorded when an order is paid for, so a shorter period may simply not have one yet."
        />
      ) : (
        <>
          <div className="mp-table-wrap">
            <table className="mp-table">
              <caption className="visually-hidden">Commission by seller</caption>
              <thead>
                <tr>
                  <th scope="col">Store</th>
                  <th scope="col" className="text-end">
                    Orders
                  </th>
                  <th scope="col" className="text-end">
                    Gross revenue
                  </th>
                  <th scope="col" className="text-end">
                    Commission
                  </th>
                  <th scope="col" className="text-end">
                    Seller earnings
                  </th>
                  <th scope="col" className="text-end">
                    Payouts in period
                  </th>
                  <th scope="col" className="text-end">
                    Paid out in period
                  </th>
                </tr>
              </thead>
              <tbody>
                {report.data.map(row => (
                  <tr key={row.sellerId}>
                    <td>{row.storeName}</td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums" }}>
                      {formatNumber(row.orders)}
                    </td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums" }}>
                      {formatCurrency(row.grossRevenue)}
                    </td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums", fontWeight: 600 }}>
                      {formatCurrency(row.commissionAmount)}
                    </td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums" }}>
                      {formatCurrency(row.sellerEarnings)}
                    </td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums", color: "var(--text-muted)" }}>
                      {formatNumber(row.payouts)}
                    </td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums", color: "var(--text-muted)" }}>
                      {formatCurrency(row.paidOut)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <p style={noteStyle}>
            Payout columns count completed payouts <em>created</em> inside the period. A payout record states the window it
            was raised for rather than being dated to a single sale, so &ldquo;raised in this period&rdquo; is the only
            period question about it the data can answer.
          </p>
        </>
      )}
    </section>
  );
}

/* ---------------------------------------------------------------------------- shared parts */

/**
 * A report's heading, its own note, and its filters.
 */
function Toolbar({
  title,
  headingId,
  note,
  children,
}: {
  title: string;
  headingId: string;
  note: string;
  children?: React.ReactNode;
}) {
  return (
    <div className="mp-spread flex-wrap" style={{ gap: "var(--space-3)" }}>
      <div>
        <h2 id={headingId} className="mp-section-title" style={{ margin: 0, fontSize: "var(--fs-h3)" }}>
          {title}
        </h2>
        <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>{note}</p>
      </div>
      {children ? <div className="d-flex align-items-center" style={{ gap: "0.5rem" }}>{children}</div> : null}
    </div>
  );
}

/**
 * The period control, offering only the presets the server's `DateTimePreset` enum defines.
 *
 * There is no custom from/to here: every report that takes a period takes a preset name rather than
 * dates, so a date pair would be a control the server does not accept.
 */
function PeriodPicker({ value, onChange }: { value: DateRange; onChange: (range: DateRange) => void }) {
  return (
    <div className="d-flex align-items-center" style={{ gap: "0.5rem" }}>
      <label htmlFor="report-period" className="mp-metric-label" style={{ margin: 0 }}>
        Period
      </label>
      <select
        id="report-period"
        className="form-select form-select-sm"
        style={{ width: "auto" }}
        value={value}
        onChange={event => onChange(event.target.value as DateRange)}
      >
        {DATE_RANGES.map(option => (
          <option key={option.id} value={option.id}>
            {option.label}
          </option>
        ))}
      </select>
    </div>
  );
}

/**
 * What the figures on this page do and do not mean, where an operator will actually read them.
 */
function SemanticsNote() {
  return (
    <section className="mp-card" style={{ padding: "var(--space-4)" }} aria-labelledby="report-semantics">
      <h2 id="report-semantics" className="mp-section-title" style={{ margin: 0, fontSize: "var(--fs-body)" }}>
        How to read these numbers
      </h2>
      <ul className="mb-0 mt-2" style={{ paddingInlineStart: "1.1rem", color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
        <li>
          <strong>Money is read from the orders, not recomputed.</strong> Discounts, tax, shipping and refunds are what
          checkout recorded, so editing a coupon or changing a tax rate cannot rewrite a figure that was already reported.
        </li>
        <li>
          <strong>Net revenue is gross less discounts less refunds.</strong> Tax and shipping are shown in their own columns
          and left out of it, because whether collected tax is platform revenue is a decision this codebase has not made.
        </li>
        <li>
          <strong>Only completed refunds count.</strong> A request nobody has approved has not returned any money yet.
        </li>
        <li>
          <strong>No totals are added up here.</strong> The platform-wide figures on{" "}
          <a href="/admin">the overview</a> are the server&apos;s own; summing a page of rows in the browser would produce a
          figure nothing here endorses.
        </li>
      </ul>
    </section>
  );
}

function Skeleton() {
  return <div className="mp-skeleton" style={{ height: "14rem", borderRadius: "var(--radius)" }} aria-hidden />;
}

function pageOf(searchParams: URLSearchParams): number {
  return Math.max(1, Number(searchParams.get("page") ?? "1") || 1);
}


function normalizeRange(value: string | null): DateRange {
  return DATE_RANGES.some(option => option.id === value) ? (value as DateRange) : "Last30Days";
}

/** How the API buckets the window, which decides what a row and a chart point mean. */
function bucketWord(range: DateRange): string {
  if (range === "ThisYear") {
    return "month";
  }

  return range === "Last90Days" ? "week" : "day";
}

function periodLabel(range: DateRange): string {
  return DATE_RANGES.find(option => option.id === range)?.label.toLowerCase() ?? "period";
}

function capitalise(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1);
}

function shortDate(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString(undefined, { month: "short", day: "numeric" });
}

function compactMoney(value: number): string {
  return value >= 1000 ? `${Math.round(value / 1000)}k` : String(value);
}

/** The standing footnote style for a caveat that belongs beside the numbers it qualifies. */
const noteStyle = { margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" } as const;