"use client";

/**
 * A seller's catalogue.
 *
 * The status column is the point of this page rather than an afterthought: a product waiting for
 * a moderator looks identical to a live one in a list of names, and a seller who cannot tell them
 * apart will keep waiting for a review that already happened.
 *
 * Search, status and page live in the URL. That is not decoration — it is what lets a seller send
 * a colleague "the three things waiting on a moderator" and what makes the edit button's return
 * trip land back on the same page of the same filter rather than at the top of an unfiltered list.
 */

import Link from "next/link";
import { useState } from "react";
import { useQuery } from "@tanstack/react-query";

import { Pagination } from "@/components/navigation/Pagination";
import { EmptyState, ErrorState, StatusBadge } from "@/components/shared/Feedback";
import { PriceDisplay } from "@/components/shared/PriceDisplay";
import { sellerApi } from "@/features/seller/api/sellerApi";
import { listContext, useSellerListUrl } from "@/features/seller/lib/listUrl";
import { formatDate, formatNumber } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";
import type { ProductStatus } from "@/types/product";

const BASE_PATH = "/seller/products";

/** The statuses the API can filter by, with the wording a seller would use for them. */
const STATUSES: { id: ProductStatus; label: string }[] = [
  { id: "Published", label: "Live" },
  { id: "PendingApproval", label: "Awaiting review" },
  { id: "Draft", label: "Drafts" },
  { id: "Rejected", label: "Needs changes" },
  { id: "Archived", label: "Archived" },
];

const STATUS_IDS = STATUSES.map(option => option.id);

export function SellerProducts() {
  const { status, search, page, navigate, filtered } = useSellerListUrl(BASE_PATH, { statuses: STATUS_IDS });

  const products = useQuery({
    queryKey: queryKeys.seller.products({ page, status, search }),
    queryFn: () => sellerApi.products({ page, pageSize: 20, status, search }),
  });

  // A detail page needs to know how it was reached, so its "back to the list" and its row links
  // return to this exact view instead of to the unfiltered top of the list.
  const context = listContext({ status, search });
  const filters = { status, search };

  return (
    <div className="mp-stack">
      <ProductToolbar search={search ?? ""} onSearch={value => navigate({ search: value.trim() || undefined })} />

      <nav aria-label="Filter by status" className="d-flex flex-wrap" style={{ gap: "0.4rem" }}>
        <button
          type="button"
          className={status === undefined ? "btn btn-sm btn-primary" : "btn btn-sm btn-outline-secondary"}
          aria-pressed={status === undefined}
          onClick={() => navigate({ status: undefined })}
        >
          All
        </button>
        {STATUSES.map(option => (
          <button
            key={option.id}
            type="button"
            className={status === option.id ? "btn btn-sm btn-primary" : "btn btn-sm btn-outline-secondary"}
            aria-pressed={status === option.id}
            onClick={() => navigate({ status: option.id })}
          >
            {option.label}
          </button>
        ))}
      </nav>

      {products.isPending ? (
        <div className="mp-skeleton" style={{ height: "14rem", borderRadius: "var(--radius)" }} aria-hidden />
      ) : products.isError || !products.data ? (
        <ErrorState message="We could not load your catalogue." onRetry={() => void products.refetch()} />
      ) : products.data.items.length === 0 ? (
        <EmptyState
          title={filtered ? "Nothing matches those filters" : "You have not listed anything yet"}
          body={
            filtered
              ? "Try another status, or clear the search to see the whole catalogue."
              : "Your products appear here once you add them, and go live after a moderator approves them."
          }
          action={
            filtered ? (
              <Link href={BASE_PATH} className="btn btn-sm btn-primary">
                Show everything
              </Link>
            ) : (
              <Link href={`/seller/products/new${context ? `?${context}` : ""}`} className="btn btn-sm btn-primary">
                List a product
              </Link>
            )
          }
        />
      ) : (
        <>
          <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            {formatNumber(products.data.totalCount)} {products.data.totalCount === 1 ? "product" : "products"}
            {products.data.totalPages > 1 ? ` · page ${products.data.page} of ${products.data.totalPages}` : ""}
          </p>

          <div className="mp-table-wrap">
            <table className="mp-table">
              <caption className="visually-hidden">Your products</caption>
              <thead>
                <tr>
                  <th scope="col">Product</th>
                  <th scope="col" className="text-end">
                    Price
                  </th>
                  <th scope="col" className="text-end">
                    Stock
                  </th>
                  <th scope="col" className="text-end">
                    Sold
                  </th>
                  <th scope="col">Status</th>
                  <th scope="col">Updated</th>
                  <th scope="col">
                    <span className="visually-hidden">Actions</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {products.data.items.map(product => (
                  <tr key={product.id}>
                    <td>
                      <Link
                        href={`/seller/products/${product.id}${context ? `?${context}` : ""}`}
                        style={{ color: "var(--text)", fontWeight: 500 }}
                      >
                        {product.name}
                      </Link>
                      <span style={{ display: "block", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                        {product.categoryName}
                        {product.rejectionNote ? ` · ${product.rejectionNote}` : ""}
                      </span>
                    </td>
                    <td className="text-end">
                      <PriceDisplay price={product.basePrice} compareAtPrice={product.compareAtPrice} size="sm" />
                    </td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums" }}>
                      {product.availableQuantity}
                      {product.isInStock ? null : (
                        <span style={{ display: "block", color: "var(--danger)", fontSize: "var(--fs-xs)" }}>
                          out of stock
                        </span>
                      )}
                    </td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums" }}>
                      {product.soldCount}
                    </td>
                    <td>
                      <StatusBadge tone={productTone(product.status)}>{productLabel(product.status)}</StatusBadge>
                    </td>
                    <td style={{ color: "var(--text-subtle)", fontSize: "var(--fs-xs)", whiteSpace: "nowrap" }}>
                      {formatDate(product.updatedAt)}
                    </td>
                    <td>
                      <div className="d-flex justify-content-end" style={{ gap: "0.35rem" }}>
                        <Link
                          href={`/seller/products/${product.id}/edit${context ? `?${context}` : ""}`}
                          className="btn btn-sm btn-outline-secondary"
                          aria-label={`Edit ${product.name}`}
                        >
                          Edit
                        </Link>
                        <Link
                          href={`/seller/products/${product.id}${context ? `?${context}` : ""}`}
                          className="btn btn-sm btn-primary"
                          aria-label={`Manage images, variants and stock for ${product.name}`}
                        >
                          Manage
                        </Link>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <Pagination page={products.data.page} totalPages={products.data.totalPages} query={filters} basePath={BASE_PATH} />
        </>
      )}
    </div>
  );
}

/**
 * The API matches the search term against a product's name and its variants' SKUs, which is what a
 * seller means by "find my order" — half the time they have the SKU to hand and not the title.
 */
function ProductToolbar({ search, onSearch }: { search: string; onSearch: (value: string) => void }) {
  const [draft, setDraft] = useState(search);
  const context = listContext({ search: search || undefined });
  const newHref = `/seller/products/new${context ? `?${context}` : ""}`;

  return (
    <form
      role="search"
      aria-label="Search your catalogue"
      className="mp-card"
      style={{ padding: "var(--space-3) var(--space-4)" }}
      onSubmit={event => {
        event.preventDefault();
        onSearch(draft);
      }}
    >
      <div className="row g-2 align-items-end">
        <div className="col-12 col-sm-7 col-md-8">
          <label htmlFor="product-search" className="mp-metric-label">
            Search products
          </label>
          <input
            id="product-search"
            type="search"
            className="form-control form-control-sm"
            value={draft}
            onChange={event => setDraft(event.target.value)}
            placeholder="Product name or SKU"
            autoComplete="off"
          />
        </div>
        <div className="col-12 col-sm-5 col-md-4 d-flex" style={{ gap: "0.5rem" }}>
          <button type="submit" className="btn btn-sm btn-outline-secondary flex-grow-1">
            Search
          </button>
          <Link href={newHref} className="btn btn-sm btn-primary flex-grow-1">
            List a product
          </Link>
        </div>
      </div>
    </form>
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