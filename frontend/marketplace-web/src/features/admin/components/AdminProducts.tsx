"use client";

/**
 * Every listing on the marketplace, and the three decisions an administrator can make about one.
 *
 * One contract fact shapes this whole screen. The admin product route returns
 * `ProductSummaryResponse`, and that DTO has **no `status`, no `rejectionReason` and no
 * `rejectionNote`** — so a row knows its name, price, store and sales, and nothing about its own
 * state. Everything the screen can say about a listing's status comes from the `status` filter that
 * produced the page, and the actions offered are derived from that filter rather than from the row.
 *
 * So the screen is honest about it: the status filter *is* the state, and each filter offers the
 * transitions the API's state machine permits from there. Nothing is rendered per row that the API
 * did not send, and no badge claims a state nobody can verify.
 *
 * Approving is legal from a draft or a pending listing; sending back is legal from a pending or a
 * live one; featuring requires a live listing. The rest — editing, deleting, archiving, unpublishing —
 * has no admin endpoint at all, so there is no button for it and the copy says why.
 */

import Link from "next/link";
import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Star } from "lucide-react";

import { ConfirmDialog } from "@/components/shared/ConfirmDialog";
import { EmptyState, ErrorState } from "@/components/shared/Feedback";
import { PriceDisplay } from "@/components/shared/PriceDisplay";
import { Pagination } from "@/components/navigation/Pagination";
import { adminApi } from "@/features/admin/api/adminApi";
import { errorMessage } from "@/lib/errors";
import { cx, formatDate, formatNumber } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";
import { useToast } from "@/providers/ToastProvider";
import type { ProductRejectionReason } from "@/types/productAuthoring";
import type { ProductStatus, ProductSummary } from "@/types/product";

const BASE_PATH = "/admin/products";

const STATUSES: { id: ProductStatus; label: string }[] = [
  { id: "PendingApproval", label: "Awaiting review" },
  { id: "Published", label: "Live" },
  { id: "Draft", label: "Drafts" },
  { id: "Rejected", label: "Sent back" },
  { id: "Archived", label: "Archived" },
];

/** The API's own reasons, in a reviewer's words. */
const REJECTION_REASONS: { value: ProductRejectionReason; label: string }[] = [
  { value: "InaccurateDescription", label: "The description is not accurate" },
  { value: "ProhibitedItem", label: "This item may not be sold here" },
  { value: "CopyrightConcern", label: "A concern about who owns the images" },
  { value: "PricingIssue", label: "The price is not credible" },
  { value: "MissingDocumentation", label: "Required details are missing" },
  { value: "Other", label: "Something else, explained in the note" },
];

/** What each state permits, from the API's own rules. */
const ACTIONS_BY_STATUS: Record<ProductStatus, { approve: boolean; reject: boolean; feature: boolean }> = {
  Draft: { approve: true, reject: false, feature: false },
  PendingApproval: { approve: true, reject: true, feature: false },
  Published: { approve: false, reject: true, feature: true },
  Rejected: { approve: false, reject: false, feature: false },
  Archived: { approve: false, reject: false, feature: false },
};

export function AdminProducts() {
  const queryClient = useQueryClient();
  const { push } = useToast();

  const [page, setPage] = useState(1);
  const [status, setStatus] = useState<ProductStatus | "">("");
  const [search, setSearch] = useState("");
  const [term, setTerm] = useState("");
  const [rejecting, setRejecting] = useState<ProductSummary | null>(null);
  const [reason, setReason] = useState<ProductRejectionReason | "">("");
  const [note, setNote] = useState("");
  const [actionError, setActionError] = useState<string | null>(null);

  const products = useQuery({
    queryKey: queryKeys.admin.products({ page, status, term }),
    queryFn: () =>
      adminApi.moderationQueue({ page, pageSize: 20, status: status || undefined, search: term || undefined }),
  });

  /** What the current filter implies about each row, and therefore which actions it may offer. */
  const permitted = status ? ACTIONS_BY_STATUS[status] : null;

  const refreshCatalogue = async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: queryKeys.admin.products({})[0] }),
      queryClient.invalidateQueries({ queryKey: queryKeys.admin.summary() }),
      queryClient.invalidateQueries({ queryKey: queryKeys.products.all }),
      queryClient.invalidateQueries({ queryKey: queryKeys.seller.productLists() }),
    ]);
  };

  const approve = useMutation({
    mutationFn: (product: ProductSummary) => adminApi.reviewProduct(product.id, true, "None", ""),
    onSuccess: async (_result, product) => {
      setActionError(null);
      await refreshCatalogue();
      push({ tone: "success", title: `${product.name} is live`, body: "The seller has been told, and shoppers can buy it." });
    },
    onError: error => setActionError(errorMessage(error)),
  });

  const reject = useMutation({
    mutationFn: (input: { product: ProductSummary; reason: ProductRejectionReason; note: string }) =>
      adminApi.reviewProduct(input.product.id, false, input.reason, input.note),
    onSuccess: async (_result, input) => {
      setRejecting(null);
      setReason("");
      setNote("");
      setActionError(null);
      await refreshCatalogue();
      push({ tone: "success", title: `${input.product.name} sent back`, body: "The seller can see the reason and fix it." });
    },
    onError: error => {
      setActionError(errorMessage(error));
      setRejecting(null);
    },
  });

  const feature = useMutation({
    mutationFn: (input: { product: ProductSummary; featured: boolean }) =>
      adminApi.setFeatured(input.product.id, input.featured),
    onSuccess: async (_result, input) => {
      setActionError(null);
      await refreshCatalogue();
      push({
        tone: "success",
        title: input.featured ? `${input.product.name} is featured` : `${input.product.name} is no longer featured`,
      });
    },
    onError: error => setActionError(errorMessage(error)),
  });

  const filtered = status !== "" || term !== "";

  return (
    <div className="mp-stack">
      <form
        role="search"
        aria-label="Search products"
        className="mp-card"
        style={{ padding: "var(--space-3) var(--space-4)" }}
        onSubmit={event => {
          event.preventDefault();
          setTerm(search.trim());
          setPage(1);
        }}
      >
        <div className="row g-2 align-items-end">
          <div className="col-12 col-sm-8">
            <label htmlFor="admin-product-search" className="mp-metric-label">
              Search products
            </label>
            <input
              id="admin-product-search"
              type="search"
              className="form-control form-control-sm"
              value={search}
              placeholder="Name, description or a variant SKU"
              onChange={event => setSearch(event.target.value)}
              autoComplete="off"
            />
          </div>
          <div className="col-12 col-sm-4">
            <button type="submit" className="btn btn-sm btn-outline-secondary w-100">
              Search
            </button>
          </div>
        </div>
      </form>

      <nav aria-label="Filter by status" className="d-flex flex-wrap" style={{ gap: "0.4rem" }}>
        <button
          type="button"
          className={status === "" ? "btn btn-sm btn-primary" : "btn btn-sm btn-outline-secondary"}
          aria-pressed={status === ""}
          onClick={() => {
            setStatus("");
            setPage(1);
          }}
        >
          Every state
        </button>
        {STATUSES.map(option => (
          <button
            key={option.id}
            type="button"
            className={status === option.id ? "btn btn-sm btn-primary" : "btn btn-sm btn-outline-secondary"}
            aria-pressed={status === option.id}
            onClick={() => {
              setStatus(option.id);
              setPage(1);
            }}
          >
            {option.label}
          </button>
        ))}
      </nav>

      {/*
        The one thing this screen cannot do is tell you what state a row is in, because the admin
        product DTO does not carry it. Saying so here is better than a column of badges that were
        derived from the filter and would look like per-row facts.
      */}
      <p className="mp-alert mp-alert-info mb-0" role="status">
        {status
          ? `Every listing below is ${STATUSES.find(option => option.id === status)?.label.toLowerCase()}. The API does not return a listing's own status, so the filter is what tells us — and the actions here are the ones it permits from that state.`
          : "The API does not return a listing's own status, so pick a state above to see what can be done to it. Listing text is edited by the seller; there is no administrator-side edit."}
      </p>

      {actionError ? (
        <p role="alert" className="mp-alert mp-alert-danger">
          {actionError}
        </p>
      ) : null}

      {products.isPending ? (
        <div className="mp-skeleton" style={{ height: "14rem", borderRadius: "var(--radius)" }} aria-hidden />
      ) : products.isError || !products.data ? (
        <ErrorState message="We could not load the products." onRetry={() => void products.refetch()} />
      ) : products.data.items.length === 0 ? (
        <EmptyState
          title={filtered ? "No products match those filters" : "No products yet"}
          body={
            filtered
              ? "Try another state, or clear the search to see the whole catalogue."
              : "Listings appear here as sellers create them."
          }
          action={
            filtered ? (
              <button
                type="button"
                className="btn btn-sm btn-primary"
                onClick={() => {
                  setStatus("");
                  setTerm("");
                  setPage(1);
                }}
              >
                Show every product
              </button>
            ) : undefined
          }
        />
      ) : (
        <>
          <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            {formatNumber(products.data.totalCount)} {products.data.totalCount === 1 ? "product" : "products"}
            {products.data.totalPages > 1 ? ` · page ${products.data.page} of ${products.data.totalPages}` : ""}
          </p>

          {/* A table on a desktop and cards on a phone, carrying the same information. */}
          <div className="d-none d-md-block">
            <div className="mp-table-wrap">
              <table className="mp-table">
                <caption className="visually-hidden">Products on the marketplace</caption>
                <thead>
                  <tr>
                    <th scope="col">Product</th>
                    <th scope="col">Store</th>
                    <th scope="col" className="text-end">
                      Price
                    </th>
                    <th scope="col" className="text-end">
                      Sold
                    </th>
                    <th scope="col">Listed</th>
                    <th scope="col">
                      <span className="visually-hidden">Actions</span>
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {products.data.items.map(product => (
                    <tr key={product.id}>
                      <td>
                        <span style={{ fontWeight: 500 }}>{product.name}</span>
                        <span style={{ display: "block", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                          {product.categoryName}
                          {product.isFeatured ? " · featured" : ""}
                          {!product.isInStock ? " · out of stock" : ""}
                        </span>
                      </td>
                      <td style={{ fontSize: "var(--fs-sm)", color: "var(--text-muted)" }}>
                        {product.storeName || product.sellerName || "—"}
                      </td>
                      <td className="text-end">
                        <PriceDisplay price={product.basePrice} compareAtPrice={product.compareAtPrice} size="sm" />
                      </td>
                      <td className="text-end" style={{ fontVariantNumeric: "tabular-nums" }}>
                        {product.soldCount}
                      </td>
                      <td style={{ color: "var(--text-subtle)", fontSize: "var(--fs-xs)", whiteSpace: "nowrap" }}>
                        {formatDate(product.createdAt)}
                      </td>
                      <td>
                        <div className="d-flex justify-content-end" style={{ gap: "0.35rem" }}>
                          {permitted?.approve ? (
                            <button
                              type="button"
                              className="btn btn-sm btn-primary"
                              disabled={approve.isPending}
                              onClick={() => approve.mutate(product)}
                              aria-label={`Approve and publish ${product.name}`}
                            >
                              Approve
                            </button>
                          ) : null}
                          {permitted?.reject ? (
                            <button
                              type="button"
                              className="btn btn-sm btn-outline-secondary"
                              disabled={reject.isPending}
                              onClick={() => {
                                setReason("");
                                setNote("");
                                setActionError(null);
                                setRejecting(product);
                              }}
                              aria-label={`Send ${product.name} back to the seller`}
                            >
                              Send back
                            </button>
                          ) : null}
                          {permitted?.feature ? (
                            <button
                              type="button"
                              className={cx("btn btn-sm", product.isFeatured ? "btn-primary" : "btn-outline-secondary")}
                              disabled={feature.isPending}
                              onClick={() => feature.mutate({ product, featured: !product.isFeatured })}
                              aria-label={product.isFeatured ? `Stop featuring ${product.name}` : `Feature ${product.name}`}
                            >
                              <Star size={14} aria-hidden className={product.isFeatured ? "me-1" : undefined} />
                              {product.isFeatured ? "Featured" : "Feature"}
                            </button>
                          ) : null}
                          {status === "Published" ? (
                            <Link href={`/products/${product.slug}`} className="btn btn-sm btn-outline-secondary">
                              View
                            </Link>
                          ) : null}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>

          <ul className="list-unstyled mp-stack-sm d-md-none mb-0">
            {products.data.items.map(product => (
              <li key={product.id} className="mp-card" style={{ padding: "var(--space-4)" }}>
                <span style={{ fontWeight: 600 }}>{product.name}</span>
                <p style={{ margin: 0, color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                  {product.storeName || product.sellerName || "—"} · {product.categoryName}
                </p>
                <p style={{ margin: "var(--space-2) 0 0", fontSize: "var(--fs-sm)" }}>
                  <PriceDisplay price={product.basePrice} compareAtPrice={product.compareAtPrice} size="sm" />
                  <span style={{ color: "var(--text-muted)" }}> · {product.soldCount} sold</span>
                </p>

                <div className="d-flex flex-wrap mt-3" style={{ gap: "0.35rem" }}>
                  {permitted?.approve ? (
                    <button
                      type="button"
                      className="btn btn-sm btn-primary flex-grow-1"
                      disabled={approve.isPending}
                      onClick={() => approve.mutate(product)}
                      aria-label={`Approve and publish ${product.name}`}
                    >
                      Approve
                    </button>
                  ) : null}
                  {permitted?.reject ? (
                    <button
                      type="button"
                      className="btn btn-sm btn-outline-secondary flex-grow-1"
                      disabled={reject.isPending}
                      onClick={() => {
                        setReason("");
                        setNote("");
                        setActionError(null);
                        setRejecting(product);
                      }}
                      aria-label={`Send ${product.name} back to the seller`}
                    >
                      Send back
                    </button>
                  ) : null}
                  {permitted?.feature ? (
                    <button
                      type="button"
                      className={cx("btn btn-sm flex-grow-1", product.isFeatured ? "btn-primary" : "btn-outline-secondary")}
                      disabled={feature.isPending}
                      onClick={() => feature.mutate({ product, featured: !product.isFeatured })}
                      aria-label={product.isFeatured ? `Stop featuring ${product.name}` : `Feature ${product.name}`}
                    >
                      <Star size={14} aria-hidden className="me-1" />
                      {product.isFeatured ? "Featured" : "Feature"}
                    </button>
                  ) : null}
                </div>
              </li>
            ))}
          </ul>

          <Pagination page={products.data.page} totalPages={products.data.totalPages} query={{ status, search: term }} basePath={BASE_PATH} />
        </>
      )}

      {rejecting ? (
        <ConfirmDialog
          show
          title={`Send ${rejecting.name} back to the seller?`}
          confirmLabel="Send it back"
          cancelLabel="Leave it pending"
          busy={reject.isPending}
          onCancel={() => setRejecting(null)}
          onConfirm={() =>
            rejecting && reason ? reject.mutate({ product: rejecting, reason, note: note.trim() }) : undefined
          }
        >
          <p className="mb-3">
            The listing comes off sale and the seller sees the reason, which is the only thing that tells them what to
            fix. They can edit it and submit again.
          </p>

          <div className="mp-stack-sm">
            <div>
              <label htmlFor="reject-reason" className="mp-metric-label">
                Why <span aria-hidden style={{ color: "var(--danger)" }}>*</span>
              </label>
              <select
                id="reject-reason"
                className="form-select form-control-sm"
                value={reason}
                onChange={event => setReason(event.target.value as ProductRejectionReason | "")}
              >
                <option value="">Choose a reason…</option>
                {REJECTION_REASONS.map(option => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
              <p style={{ margin: "0.25rem 0 0", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                Required by the API, and required in practice: a rejection without one leaves the seller nothing to act
                on.
              </p>
            </div>

            <div>
              <label htmlFor="reject-note" className="mp-metric-label">
                Note for the seller
              </label>
              <textarea
                id="reject-note"
                className="mp-input"
                rows={3}
                maxLength={1000}
                value={note}
                placeholder="Say what would make this acceptable"
                onChange={event => setNote(event.target.value)}
                style={{ width: "100%", padding: "0.55rem 0.7rem", borderRadius: "var(--radius-sm)" }}
              />
            </div>
          </div>
        </ConfirmDialog>
      ) : null}
    </div>
  );
}