/**
 * A seller's stock, with the two numbers that matter next to each other.
 *
 * What is on hand and what can be sold are different: stock held for a basket is not available
 * to anyone else, and a dashboard that only shows the first number tells a seller they have
 * more than they do.
 */

"use client";

import Link from "next/link";
import { useState } from "react";
import { useQuery } from "@tanstack/react-query";

import { EmptyState, ErrorState, StatusBadge } from "@/components/shared/Feedback";
import { sellerApi } from "@/features/seller/api/sellerApi";
import { cx, formatDate } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";

export function SellerInventory() {
  const [page, setPage] = useState(1);
  const [lowOnly, setLowOnly] = useState(false);

  const inventory = useQuery({
    queryKey: queryKeys.seller.inventory(page),
    queryFn: () => sellerApi.inventory({ page, lowStockOnly: lowOnly }),
  });

  return (
    <div className="mp-stack">
      <div className="d-flex justify-content-between align-items-center">
        <label className="d-flex align-items-center" style={{ gap: "0.4rem", fontSize: "var(--fs-sm)" }}>
          <input
            type="checkbox"
            checked={lowOnly}
            onChange={event => {
              setLowOnly(event.target.checked);
              setPage(1);
            }}
          />
          Low stock only
        </label>
      </div>

      {inventory.isPending ? (
        <div className="mp-skeleton" style={{ height: "14rem", borderRadius: "var(--radius)" }} />
      ) : inventory.isError || !inventory.data ? (
        <ErrorState message="We could not load your stock." />
      ) : inventory.data.items.length === 0 ? (
        <EmptyState
          title={lowOnly ? "Nothing is running low" : "No stock records"}
          body={lowOnly ? "Every product has more than its low-stock threshold." : "Stock appears here once you have products with variants."}
        />
      ) : (
        <div className="mp-table-wrap">
          <table className="mp-table">
            <caption className="visually-hidden">Stock by variant</caption>
            <thead>
              <tr>
                <th scope="col">Product</th>
                <th scope="col">SKU</th>
                <th scope="col">On hand</th>
                <th scope="col">Reserved</th>
                <th scope="col">Sellable</th>
                <th scope="col">Status</th>
              </tr>
            </thead>
            <tbody>
              {inventory.data.items.map(item => (
                <tr key={item.inventoryId}>
                  <td>
                    <Link href={`/products/${item.productId}`} style={{ color: "var(--text)" }}>
                      {item.productName}
                    </Link>
                    <span style={{ display: "block", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                      {item.variantName} · updated {formatDate(item.updatedAt)}
                    </span>
                  </td>
                  <td style={{ fontVariantNumeric: "tabular-nums" }}>{item.sku}</td>
                  <td style={{ fontVariantNumeric: "tabular-nums" }}>{item.availableQuantity}</td>
                  <td style={{ fontVariantNumeric: "tabular-nums", color: "var(--text-muted)" }}>{item.reservedQuantity}</td>
                  <td style={{ fontVariantNumeric: "tabular-nums", fontWeight: 600 }}>{item.sellableQuantity}</td>
                  <td>
                    {item.isOutOfStock ? (
                      <StatusBadge tone="danger">Out of stock</StatusBadge>
                    ) : item.isLowStock ? (
                      <StatusBadge tone="warning">Low</StatusBadge>
                    ) : (
                      <StatusBadge tone="success">In stock</StatusBadge>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {inventory.data && inventory.data.totalPages > 1 ? (
        <nav aria-label="Stock pages" className="d-flex justify-content-between align-items-center mt-3">
          <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page <= 1}>
            Previous
          </button>
          <span style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            Page {page} of {inventory.data.totalPages}
          </span>
          <button
            type="button"
            className="btn btn-sm btn-outline-secondary"
            onClick={() => setPage(p => Math.min(inventory.data!.totalPages, p + 1))}
            disabled={page >= inventory.data.totalPages}
          >
            Next
          </button>
        </nav>
      ) : null}
    </div>
  );
}

export { cx };
