/**
 * The moderation queue: every listing waiting for a decision.
 *
 * This is the screen that decides whether the marketplace can be trusted, so it shows what a
 * reviewer needs before deciding — the picture, the price, the seller, and how long the seller
 * has been waiting — and it records the reason either way. An approval with no note and a
 * rejection with no reason are both unusable afterwards.
 */

"use client";

import Link from "next/link";
import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { EmptyState, ErrorState } from "@/components/shared/Feedback";
import { adminApi } from "@/features/admin/api/adminApi";
import { errorMessage } from "@/lib/errors";
import { cx, formatCurrency, formatDate } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";
import type { ProductSummary } from "@/types/product";
import type { ProductRejectionReason } from "@/types/productAuthoring";

/**
 * What a reviewer can send a listing back for.
 *
 * One option per reason the API actually has, in the reviewer's words rather than the field's.
 * The value is the API's name and not the sentence: the seller is shown the reason, an audit
 * entry records it, and a reason that is only ever a string in this file is a reason the rest of
 * the system cannot count.
 */
const REJECTION_REASONS: { value: ProductRejectionReason; label: string }[] = [
  { value: "InaccurateDescription", label: "The description or the photographs do not match the product" },
  { value: "ProhibitedItem", label: "This marketplace does not sell this kind of product" },
  { value: "CopyrightConcern", label: "The images or the text look copied from somewhere else" },
  { value: "PricingIssue", label: "The price is not credible, or is not this product's price" },
  { value: "MissingDocumentation", label: "Something required is missing" },
  { value: "Other", label: "Something else, explained in the note" },
];

export function ModerationQueue() {
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [expanded, setExpanded] = useState<string | null>(null);
  const [notes, setNotes] = useState<Record<string, string>>({});
  const [reasons, setReasons] = useState<Record<string, ProductRejectionReason | "">>({});
  const [actionError, setActionError] = useState<string | null>(null);

  const queue = useQuery({
    queryKey: queryKeys.admin.products({ page, status: "PendingApproval" }),
    queryFn: () => adminApi.moderationQueue({ page, status: "PendingApproval" }),
  });

  const decide = useMutation({
    mutationFn: ({ id, approve, reason, note }: { id: string; approve: boolean; reason: ProductRejectionReason; note: string }) =>
      adminApi.reviewProduct(id, approve, reason, note),
    onSuccess: async () => {
      setActionError(null);
      await queryClient.invalidateQueries({ queryKey: queryKeys.admin.all });
    },
    onError: error => setActionError(errorMessage(error)),
  });

  const feature = useMutation({
    mutationFn: ({ id, featured }: { id: string; featured: boolean }) => adminApi.setFeatured(id, featured),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.admin.all }),
    onError: error => setActionError(errorMessage(error)),
  });

  if (queue.isPending) {
    return <div className="mp-skeleton" style={{ height: "16rem", borderRadius: "var(--radius)" }} />;
  }

  if (queue.isError || !queue.data) {
    return <ErrorState message="We could not load the moderation queue." />;
  }

  return (
    <div className="mp-stack">
      {actionError ? (
        <p role="alert" className="mp-alert mp-alert-danger">
          {actionError}
        </p>
      ) : null}

      {queue.data.totalCount === 0 ? (
        <EmptyState title="Nothing waiting" body="Every listing has had a decision. Sellers are told either way." />
      ) : (
        <>
          <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            {queue.data.totalCount} {queue.data.totalCount === 1 ? "listing" : "listings"} waiting for a decision.
          </p>

          <ul className="list-unstyled mb-0 mp-stack">
            {queue.data.items.map(product => (
              <ReviewableProduct
                key={product.id}
                product={product}
                open={expanded === product.id}
                onToggle={() => {
                  setExpanded(expanded === product.id ? null : product.id);
                  setActionError(null);
                }}
                note={notes[product.id] ?? ""}
                reason={reasons[product.id] ?? ""}
                onNote={value => setNotes(current => ({ ...current, [product.id]: value }))}
                onReason={value => setReasons(current => ({ ...current, [product.id]: value }))}
                busy={decide.isPending}
                onDecide={approve =>
                  decide.mutate({
                    id: product.id,
                    approve,
                    // An approval has no reason; a rejection is not allowed without one. The note
                    // is always the note, rather than whichever of the two fields happened to be
                    // filled in.
                    reason: approve ? "None" : ((reasons[product.id] || "Other") as ProductRejectionReason),
                    note: notes[product.id] ?? "",
                  })
                }
                onFeature={() => feature.mutate({ id: product.id, featured: !product.isFeatured })}
              />
            ))}
          </ul>

          {queue.data.totalPages > 1 ? (
            <nav aria-label="Queue pages" className="d-flex justify-content-between align-items-center mt-3">
              <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page <= 1}>
                Previous
              </button>
              <span style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
                Page {page} of {queue.data.totalPages}
              </span>
              <button
                type="button"
                className="btn btn-sm btn-outline-secondary"
                onClick={() => setPage(p => Math.min(queue.data!.totalPages, p + 1))}
                disabled={page >= queue.data.totalPages}
              >
                Next
              </button>
            </nav>
          ) : null}
        </>
      )}
    </div>
  );
}

function ReviewableProduct({
  product,
  open,
  onToggle,
  note,
  reason,
  onNote,
  onReason,
  busy,
  onDecide,
  onFeature,
}: {
  product: ProductSummary;
  open: boolean;
  onToggle: () => void;
  note: string;
  reason: ProductRejectionReason | "";
  onNote: (value: string) => void;
  onReason: (value: ProductRejectionReason) => void;
  busy: boolean;
  onDecide: (approve: boolean) => void;
  onFeature: () => void;
}) {
  return (
    <li className="mp-card" style={{ padding: "var(--space-4)" }}>
      <div className="d-flex align-items-start" style={{ gap: "var(--space-3)" }}>
        {product.primaryImageUrl ? (
          // eslint-disable-next-line @next/next/no-img-element
          <img
            src={product.primaryImageUrl}
            alt={product.primaryImageAlt ?? product.name}
            width={80}
            height={80}
            style={{ width: "5rem", height: "5rem", objectFit: "cover", borderRadius: "var(--radius-sm)", flex: "none" }}
          />
        ) : (
          <div className="mp-skeleton" style={{ width: "5rem", height: "5rem", borderRadius: "var(--radius-sm)", flex: "none" }} />
        )}

        <div style={{ flex: 1, minWidth: 0 }}>
          <h3 style={{ margin: 0, fontSize: "var(--fs-h3)" }}>
            <Link href={`/products/${product.slug}`} style={{ color: "var(--text)" }}>
              {product.name}
            </Link>
          </h3>
          <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            {product.storeName} · {formatCurrency(product.basePrice)} · {product.categoryName || "uncategorised"}
          </p>
          <p style={{ margin: 0, color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
            Listed {formatDate(product.createdAt)}
            {product.isFeatured ? " · featured" : ""}
          </p>
        </div>

        <div className="d-flex" style={{ gap: "0.5rem" }}>
          <button type="button" className="btn btn-sm btn-outline-secondary" onClick={onToggle} aria-expanded={open}>
            {open ? "Hide" : "Review"}
          </button>
        </div>
      </div>

      {open ? (
        <div className="mt-3" style={{ borderTop: "1px solid var(--border)", paddingTop: "var(--space-3)" }}>
          <p style={{ margin: 0, fontSize: "var(--fs-sm)", color: "var(--text-muted)" }}>{product.shortDescription}</p>

          <div className="mp-stack-sm mt-3">
            <div>
              <label htmlFor={`reason-${product.id}`} className="mp-metric-label">
                If you are sending it back, why?
              </label>
              <select
                id={`reason-${product.id}`}
                className="mp-input"
                value={reason}
                onChange={event => onReason(event.target.value as ProductRejectionReason)}
              >
                <option value="">Choose a reason…</option>
                {REJECTION_REASONS.map(option => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
            </div>

            <div>
              <label htmlFor={`note-${product.id}`} className="mp-metric-label">
                A note for the seller
              </label>
              <textarea
                id={`note-${product.id}`}
                className="mp-input"
                rows={3}
                value={note}
                placeholder="What would you change?"
                onChange={event => onNote(event.target.value)}
              />
            </div>
          </div>

          <div className="d-flex flex-wrap mt-3" style={{ gap: "0.5rem" }}>
            <button type="button" className="btn btn-sm btn-primary" onClick={() => onDecide(true)} disabled={busy}>
              Approve and publish
            </button>
            <button
              type="button"
              className="btn btn-sm"
              onClick={() => onDecide(false)}
              disabled={busy || !reason}
              title={reason ? undefined : "A rejection needs a reason, or the seller cannot act on it"}
              style={{ color: "var(--danger)" }}
            >
              Send back
            </button>
            <button type="button" className={cx("btn btn-sm", product.isFeatured ? "btn-primary" : "btn-outline-secondary")} onClick={onFeature} disabled={busy}>
              {product.isFeatured ? "Unfeature" : "Feature"}
            </button>
          </div>

          {!reason ? (
            <p style={{ margin: "var(--space-2) 0 0", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
              Sending a listing back needs a reason. The seller sees it, and it is the only thing that tells them what to fix.
            </p>
          ) : null}
        </div>
      ) : null}
    </li>
  );
}
