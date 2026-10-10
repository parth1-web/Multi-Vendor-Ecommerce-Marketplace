"use client";

/**
 * The seller's stock, as a report.
 *
 * The same rows the admin inventory report reads, narrowed to the signed-in seller by the server.
 * The distinction from Stock (`/seller/inventory`) is the question each one answers: that screen is
 * where stock is *changed*, this one is where it is *read*. So this carries a total across every
 * variant and a page of its own, and offers no adjustment control at all.
 *
 * Two rules are visible on the page because they are the ones people get wrong:
 *
 * - **Low stock is sellable, not on-hand.** Stock held for a customer's order is not stock the
 *   seller can sell, and the filter counts available minus reserved against the variant's own
 *   threshold. That is the same rule the operational screen uses, so a variant this calls low stock
 *   is one the seller is warned about.
 * - **Sellable is not the same as available.** The table shows both rather than one, because the
 *   gap between them is exactly what is committed to orders.
 */

import { useRouter, useSearchParams } from "next/navigation";
import { useState } from "react";
import { useQuery } from "@tanstack/react-query";

import { EmptyState, ErrorState } from "@/components/shared/Feedback";
import { Pagination } from "@/components/navigation/Pagination";
import { sellerApi } from "@/features/seller/api/sellerApi";
import { formatCurrency, formatNumber } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";

const BASE_PATH = "/seller/reports/inventory";
const PAGE_SIZE = 20;

export function SellerInventoryReport() {
  const router = useRouter();
  const searchParams = useSearchParams();

  const page = Math.max(1, Number(searchParams.get("page") ?? "1") || 1);
  const term = searchParams.get("search") ?? "";
  const lowStock = searchParams.get("lowStock") === "1";
  const outOfStock = searchParams.get("outOfStock") === "1";

  // Seeded from the URL and re-seeded when the URL changes, so the box and the request can never
  // describe different searches.
  const [search, setSearch] = useState(term);

  const navigate = (changes: Record<string, string | undefined>) => {
    const next = new URLSearchParams(searchParams.toString());

    for (const [key, value] of Object.entries(changes)) {
      if (value === undefined || value === "") {
        next.delete(key);
      } else {
        next.set(key, value);
      }
    }

    if (!("page" in changes)) {
      next.delete("page");
    }

    const query = next.toString();
    router.replace(query ? `${BASE_PATH}?${query}` : BASE_PATH, { scroll: false });
  };

  const filtered = term !== "" || lowStock || outOfStock;

  const report = useQuery({
    queryKey: queryKeys.seller.inventoryReport({ page, term, lowStock, outOfStock }),
    queryFn: () =>
      sellerApi.inventoryReport({
        page,
        pageSize: PAGE_SIZE,
        search: term || undefined,
        lowStockOnly: lowStock,
        outOfStockOnly: outOfStock,
      }),
  });

  return (
    <div className="mp-stack">
      <form
        role="search"
        aria-label="Search and filter your stock"
        className="mp-card"
        style={{ padding: "var(--space-3) var(--space-4)" }}
        onSubmit={event => {
          event.preventDefault();
          navigate({ search: search.trim() || undefined });
        }}
      >
        <div className="row g-2 align-items-end">
          <div className="col-12 col-sm-6 col-md-6">
            <label htmlFor="seller-stock-search" className="mp-metric-label">
              Search your stock
            </label>
            <input
              key={`stock-search-${term}`}
              id="seller-stock-search"
              type="search"
              className="form-control form-control-sm"
              value={search}
              placeholder="A product name or a SKU"
              onChange={event => setSearch(event.target.value)}
              autoComplete="off"
            />
          </div>

          <div className="col-12 col-sm-6 col-md-6 d-flex flex-wrap align-items-center" style={{ gap: "0.5rem" }}>
            <div className="form-check form-switch mb-0">
              <input
                id="seller-stock-low"
                className="form-check-input"
                type="checkbox"
                checked={lowStock}
                onChange={event => navigate({ lowStock: event.target.checked ? "1" : undefined })}
              />
              <label className="form-check-label" htmlFor="seller-stock-low" style={{ fontSize: "var(--fs-sm)" }}>
                Low stock only
              </label>
            </div>
            <div className="form-check form-switch mb-0">
              <input
                id="seller-stock-out"
                className="form-check-input"
                type="checkbox"
                checked={outOfStock}
                onChange={event => navigate({ outOfStock: event.target.checked ? "1" : undefined })}
              />
              <label className="form-check-label" htmlFor="seller-stock-out" style={{ fontSize: "var(--fs-sm)" }}>
                Out of stock only
              </label>
            </div>
            {filtered ? (
              <button
                type="button"
                className="btn btn-sm btn-outline-secondary"
                onClick={() => {
                  setSearch("");
                  navigate({ search: undefined, lowStock: undefined, outOfStock: undefined });
                }}
              >
                Clear
              </button>
            ) : null}
          </div>
        </div>
      </form>

      {report.isPending ? (
        <div className="mp-skeleton" style={{ height: "14rem", borderRadius: "var(--radius)" }} aria-hidden />
      ) : report.isError || !report.data ? (
        <ErrorState message="We could not load your stock report." onRetry={() => void report.refetch()} />
      ) : report.data.items.length === 0 ? (
        <EmptyState
          title={filtered ? "No stock matches those filters" : "Nothing listed yet"}
          body={filtered ? "Nothing you sell meets that combination." : "Variants appear here once you list a product."}
          action={
            filtered ? (
              <button
                type="button"
                className="btn btn-sm btn-primary"
                onClick={() => {
                  setSearch("");
                  navigate({ search: undefined, lowStock: undefined, outOfStock: undefined });
                }}
              >
                Show everything
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
              <caption className="visually-hidden">Your stock by variant, lowest first</caption>
              <thead>
                <tr>
                  <th scope="col">Product</th>
                  <th scope="col">SKU</th>
                  <th scope="col" className="text-end">
                    Available
                  </th>
                  <th scope="col" className="text-end">
                    Reserved
                  </th>
                  <th scope="col" className="text-end">
                    Sellable
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
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums", fontWeight: 600 }}>
                      {formatNumber(row.available)}
                    </td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums", color: "var(--text-muted)" }}>
                      {formatNumber(row.reserved)}
                    </td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums" }}>
                      {formatNumber(row.available - row.reserved)}
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
            <strong>Sellable</strong> is available less what is held for orders — it is what you can still sell, and
            it is what the low-stock filter measures against your own threshold. <strong>Stock value</strong> is available ×
            base price, which is not what a sale will bring: it ignores discounts and is not profit. There is no cost of
            goods recorded anywhere in this marketplace, so no margin can be reported from it.
          </p>
        </>
      )}
    </div>
  );
}

const noteStyle = { margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" } as const;