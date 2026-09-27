/**
 * A seller's catalogue.
 *
 * The status column is the point of this page rather than an afterthought: a product waiting for
 * a moderator looks identical to a live one in a list of names, and a seller who cannot tell
 * them apart will keep waiting for a review that already happened.
 */

"use client";

import Link from "next/link";
import { useState } from "react";
import { useQuery } from "@tanstack/react-query";

import { EmptyState, ErrorState, StatusBadge } from "@/components/shared/Feedback";
import { PriceDisplay } from "@/components/shared/PriceDisplay";
import { sellerApi } from "@/features/seller/api/sellerApi";
import { formatDate } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";
import type { ProductStatus } from "@/types/product";

const STATUSES: { id: ProductStatus | ""; label: string }[] = [
  { id: "", label: "All" },
  { id: "Published", label: "Live" },
  { id: "PendingApproval", label: "Awaiting review" },
  { id: "Draft", label: "Drafts" },
  { id: "Rejected", label: "Needs changes" },
  { id: "Archived", label: "Archived" },
];

export function SellerProducts() {
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState<ProductStatus | "">("");

  const products = useQuery({
    queryKey: queryKeys.seller.products({ page, status }),
    queryFn: () => sellerApi.products({ page, pageSize: 20, status: status || undefined }),
  });

  return (
    <div className="mp-stack">
      <nav aria-label="Filter by status" className="d-flex flex-wrap" style={{ gap: "0.4rem" }}>
        {STATUSES.map(option => (
          <button
            key={option.id || "all"}
            type="button"
            className={status === option.id ? "btn btn-sm btn-primary" : "btn btn-sm btn-outline-secondary"}
            onClick={() => {
              setStatus(option.id);
              setPage(1);
            }}
          >
            {option.label}
          </button>
        ))}
      </nav>

      {products.isPending ? (
        <div className="mp-skeleton" style={{ height: "14rem", borderRadius: "var(--radius)" }} />
      ) : products.isError || !products.data ? (
        <ErrorState message="We could not load your catalogue." />
      ) : products.data.items.length === 0 ? (
        <EmptyState
          title={status ? "Nothing with that status" : "You have not listed anything yet"}
          body={status ? "Try another status." : "Your products appear here once you add them, and go live after a moderator approves them."}
          action={
            !status ? (
              <Link href="/seller/products/new" className="btn btn-sm btn-primary">
                List a product
              </Link>
            ) : undefined
          }
        />
      ) : (
        <div className="mp-table-wrap">
          <table className="mp-table">
            <caption className="visually-hidden">Your products</caption>
            <thead>
              <tr>
                <th scope="col">Product</th>
                <th scope="col">Price</th>
                <th scope="col">Stock</th>
                <th scope="col">Sold</th>
                <th scope="col">Status</th>
                <th scope="col">Listed</th>
              </tr>
            </thead>
            <tbody>
              {products.data.items.map(product => (
                <tr key={product.id}>
                  <td>
                    <Link href={`/seller/products/${product.id}`} style={{ color: "var(--text)", fontWeight: 500 }}>
                      {product.name}
                    </Link>
                    <span style={{ display: "block", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                      {product.categoryName}
                    </span>
                  </td>
                  <td>
                    <PriceDisplay price={product.basePrice} compareAtPrice={product.compareAtPrice} size="sm" />
                  </td>
                  <td style={{ fontVariantNumeric: "tabular-nums" }}>
                    {product.availableQuantity}
                    {product.isInStock ? null : <span style={{ color: "var(--danger)" }}> (out)</span>}
                  </td>
                  <td style={{ fontVariantNumeric: "tabular-nums" }}>{product.soldCount}</td>
                  <td>
                    <StatusBadge tone={productTone(product.status)}>{productLabel(product.status)}</StatusBadge>
                  </td>
                  <td style={{ color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>{formatDate(product.createdAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {products.data && products.data.totalPages > 1 ? (
        <nav aria-label="Product pages" className="d-flex justify-content-between align-items-center mt-3">
          <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page <= 1}>
            Previous
          </button>
          <span style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            Page {page} of {products.data.totalPages}
          </span>
          <button
            type="button"
            className="btn btn-sm btn-outline-secondary"
            onClick={() => setPage(p => Math.min(products.data!.totalPages, p + 1))}
            disabled={page >= products.data.totalPages}
          >
            Next
          </button>
        </nav>
      ) : null}
    </div>
  );
}

function productLabel(status: ProductStatus): string {
  switch (status) {
    case "PendingApproval":
      return "Awaiting review";
    case "Published":
      return "Live";
    case "Rejected":
      return "Needs changes";
    default:
      return status;
  }
}

function productTone(status: ProductStatus): "success" | "warning" | "danger" | "info" {
  switch (status) {
    case "Published":
      return "success";
    case "PendingApproval":
      return "warning";
    case "Rejected":
      return "danger";
    default:
      return "info";
  }
}
