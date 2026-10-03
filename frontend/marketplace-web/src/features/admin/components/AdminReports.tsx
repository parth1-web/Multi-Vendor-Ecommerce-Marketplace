"use client";

/**
 * Reports: the four report families the API actually has, one at a time.
 *
 * Written against the endpoints rather than against an idea of what reporting should be. Each
 * report below is exactly the rows its endpoint returns, with the endpoint's own parameters and
 * none invented, and three refusals run through all of it:
 *
 * - **No totals.** A report here is the rows the server sent. Summing them in the browser would
 *   be a business figure the API never endorsed, and on the inventory report it would be wrong
 *   anyway — that endpoint caps at 500 rows. Where a platform-wide total already exists it lives
 *   on the overview, computed by the server, and is linked rather than repeated.
 * - **No invented columns.** The sales report returns zero for discounts, tax, shipping and
 *   refunds, and repeats gross revenue into "net revenue"; the seller report returns zero for
 *   average rating. Those fields are in the wire shape and are not rendered, because a column of
 *   zeros that looks measured is worse than a missing column.
 * - **One report in flight.** Switching reports mounts a different component, so the page never
 *   fetches four reports to show one.
 *
 * The period control only appears on the two reports the API will scope by one. Sellers and
 * inventory take no parameters at all, and offering them a date filter that the request ignores
 * would be the exact lie this page is built to avoid.
 */

import { useRouter, useSearchParams } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import { Area, AreaChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { Download } from "lucide-react";

import { EmptyState, ErrorState } from "@/components/shared/Feedback";
import { adminApi } from "@/features/admin/api/adminApi";
import { cx, formatCurrency, formatDate, formatNumber } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";
import { DATE_RANGES, type DateRange } from "@/types/seller";

const REPORTS = [
  { id: "sales", label: "Sales", periodFiltered: true },
  { id: "sellers", label: "Sellers", periodFiltered: false },
  { id: "inventory", label: "Inventory", periodFiltered: false },
  { id: "commissions", label: "Commissions", periodFiltered: true },
] as const;

type ReportId = (typeof REPORTS)[number]["id"];

const BASE_PATH = "/admin/reports";

export function AdminReports() {
  const router = useRouter();
  const searchParams = useSearchParams();

  const requested = searchParams.get("report");
  const active: ReportId = REPORTS.some(report => report.id === requested) ? (requested as ReportId) : "sales";
  const period = normalizeRange(searchParams.get("range"));

  const navigate = (changes: Record<string, string | undefined>) => {
    const next = new URLSearchParams(searchParams.toString());

    for (const [key, value] of Object.entries(changes)) {
      if (value === undefined || value === "") {
        next.delete(key);
      } else {
        next.set(key, value);
      }
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

      {/*
        Only the selected report's component is mounted, so its query is the only one that runs.
      */}
      {active === "sales" ? <SalesReport period={period} onPeriodChange={range => navigate({ range })} /> : null}
      {active === "sellers" ? <SellerReport /> : null}
      {active === "inventory" ? <InventoryReport /> : null}
      {active === "commissions" ? <CommissionReport period={period} onPeriodChange={range => navigate({ range })} /> : null}

      <DataQualityNote />
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
        note={`One row per ${bucketWord(period)}. Cancelled orders are left out by the API.`}
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
          {/*
            A real series — three measures the API returns per period — over the period axis. The
            table underneath is the same data, and is what a screen reader or a small screen reads
            instead of the picture.
          */}
          <div
            role="img"
            aria-label={`Gross revenue, commission and net to sellers for each ${bucketWord(period)} in the ${periodLabel(period)}`}
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
                    Gross revenue
                  </th>
                  <th scope="col" className="text-end">
                    Net to sellers
                  </th>
                  <th scope="col" className="text-end">
                    Commission
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
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums" }}>
                      {formatCurrency(row.netToSellers)}
                    </td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums" }}>
                      {formatCurrency(row.commission)}
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
 * The one export the API has, described for what it is.
 *
 * It is a file of **orders**, not of the rows above, so the label says orders. It covers every
 * order in the chosen period — cancelled ones included, which the table above does not — and
 * nothing else about the current view, because nothing else narrows it. The API serves it
 * synchronously with no row cap and writes no `ReportExported` audit entry, so this download is
 * not traceable afterwards; that is stated on the page rather than left for somebody to discover.
 */
function ExportSales({ period }: { period: DateRange }) {
  return (
    <div className="mp-card" style={{ padding: "var(--space-3) var(--space-4)" }}>
      <div className="d-flex flex-wrap align-items-center justify-content-between" style={{ gap: "var(--space-3)" }}>
        <div>
          <p style={{ margin: 0, fontSize: "var(--fs-sm)", fontWeight: 500 }}>Order-level CSV</p>
          <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-xs)" }}>
            Every order placed in the {periodLabel(period)}, one row each, including cancelled ones — so it will not match the
            table above. Built in one request with no row limit, and the download itself is not recorded in the audit log.
          </p>
        </div>
        <a className="btn btn-sm btn-outline-secondary" href={adminApi.salesCsvUrl(period)} download>
          <Download size={14} aria-hidden /> Export sales orders CSV
        </a>
      </div>
    </div>
  );
}

/* -------------------------------------------------------------------------------- sellers */

function SellerReport() {
  const report = useQuery({ queryKey: queryKeys.admin.sellerReport(), queryFn: () => adminApi.sellerReport() });

  return (
    <section className="mp-stack" aria-labelledby="report-sellers">
      <Toolbar
        title="Sellers by gross revenue"
        headingId="report-sellers"
        note="Lifetime figures for every seller, ranked by gross revenue. This report takes no period — the API has no parameter for one."
      />

      {report.isPending ? (
        <Skeleton />
      ) : report.isError || !report.data ? (
        <ErrorState message="We could not load the seller report." onRetry={() => void report.refetch()} />
      ) : report.data.length === 0 ? (
        <EmptyState title="No sellers yet" body="A store appears here once somebody applies to sell." />
      ) : (
        <>
          <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            {formatNumber(report.data.length)} {report.data.length === 1 ? "seller" : "sellers"}. The API returns all of them
            in one response, so there is nothing to page through.
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
                </tr>
              </thead>
              <tbody>
                {report.data.map(row => (
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
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <p style={noteStyle}>
            There is no rating column. The API returns zero in that field for every seller because it does not join review
            data, and a column of 0.0s reads as a score nobody earned.
          </p>
        </>
      )}
    </section>
  );
}

/* ------------------------------------------------------------------------------- inventory */

function InventoryReport() {
  const report = useQuery({ queryKey: queryKeys.admin.inventoryReport(), queryFn: () => adminApi.inventoryReport() });

  return (
    <section className="mp-stack" aria-labelledby="report-inventory">
      <Toolbar
        title="Stock, lowest first"
        headingId="report-inventory"
        note="Every variant the API returns, ordered by available quantity. This is the analytical view; adjusting stock happens in Stock."
      />

      {report.isPending ? (
        <Skeleton />
      ) : report.isError || !report.data ? (
        <ErrorState message="We could not load the inventory report." onRetry={() => void report.refetch()} />
      ) : report.data.length === 0 ? (
        <EmptyState title="No stock records" body="A variant appears here once a seller lists it." />
      ) : (
        <>
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
                {report.data.map(row => (
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

          <p style={noteStyle}>
            Two honest limits. The API returns at most 500 variants, so this is not necessarily the whole platform and no
            total is added up from it. Stock value is available quantity × base price as the API computes it; a variant
            whose product no longer exists comes back with no name, no SKU and a zero value.
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
        note={`Commission earned in the ${periodLabel(period)}, ranked by amount.`}
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
                    Payouts to date
                  </th>
                  <th scope="col" className="text-end">
                    Paid out to date
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
            The last two columns are lifetime figures, not period figures: the API sums completed payouts for the seller
            over all time. Orders, gross, commission and earnings are for the period above.
          </p>
        </>
      )}
    </section>
  );
}

/* ---------------------------------------------------------------------------- shared parts */

/**
 * A report's heading, its own note, and its filters.
 *
 * The note is not decoration. Two of the four reports have no period parameter and one has a
 * column that answers a different question from the rest of its table, and the operator reading a
 * number needs to know which of those they are looking at.
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
 * There is no custom from/to here: two of the four reports take no period at all, and the two
 * that do take a preset name rather than dates, so a date pair would be a control that works on
 * one screen and is ignored on the next.
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
 * What this page leaves out, and why, where the operator will actually read it.
 *
 * A reporting screen that quietly omits a column is indistinguishable from one that has no such
 * data. Saying which fields exist in the response and are not shown — and that the omissions are
 * the API's constants, not missing work — is the difference between a report somebody trusts
 * and one somebody stops opening.
 */
function DataQualityNote() {
  return (
    <section className="mp-card" style={{ padding: "var(--space-4)" }} aria-labelledby="report-data-quality">
      <h2 id="report-data-quality" className="mp-section-title" style={{ margin: 0, fontSize: "var(--fs-body)" }}>
        Not shown here, and why
      </h2>
      <ul className="mb-0 mt-2" style={{ paddingInlineStart: "1.1rem", color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
        <li>
          <strong>Discounts, tax, shipping and refunds</strong> — the sales report returns 0 for all four because it does
          not compute them. The order-level CSV has real per-order values for these, which is why it is labelled as an
          order export rather than this report.
        </li>
        <li>
          <strong>Net revenue</strong> — the API repeats gross revenue into it, so it is the same number twice rather than
          a profit figure. There is no profit, margin or cost data anywhere in these endpoints.
        </li>
        <li>
          <strong>Seller rating</strong> — the seller report returns 0 for every store; it does not read review data.
        </li>
        <li>
          <strong>Totals</strong> — no figure here is summed in the browser. The platform totals on{" "}
          <a href="/admin">the overview</a> are the server&apos;s own.
        </li>
      </ul>
    </section>
  );
}

/** The standing footnote style for a caveat that belongs beside the numbers it qualifies. */
const noteStyle = { margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" } as const;

function Skeleton() {
  return <div className="mp-skeleton" style={{ height: "14rem", borderRadius: "var(--radius)" }} aria-hidden />;
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