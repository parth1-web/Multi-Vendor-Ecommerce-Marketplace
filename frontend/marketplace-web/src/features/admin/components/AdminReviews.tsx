"use client";

/**
 * Review moderation: every review on the marketplace, and the only two decisions available.
 *
 * A moderator can hide a review or put it back. That is the entire vocabulary, and the API refuses
 * anything else — an administrator cannot rewrite a customer's words or delete them, because a
 * review is something a shopper wrote about a store and the store's answer to it. So this screen
 * offers no edit control, and a hidden review keeps its text for whoever comes back to it.
 *
 * Two decisions the console keeps making elsewhere are made here too:
 *
 * - A hide needs a reason. The note is stored on the review and shown next to it, so the next
 *   moderator to open it — or the author asking why their review vanished — has something to read.
 *   A restore does not, because putting something back needs no justification.
 * - Both decisions go through a confirmation that names what it will do, since a review that
 *   disappears from a product page is not obviously reversible to the person who clicked.
 */

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ChevronDown, ChevronRight, Eye, EyeOff, Star } from "lucide-react";

import { ConfirmDialog } from "@/components/shared/ConfirmDialog";
import { EmptyState, ErrorState, StatusBadge } from "@/components/shared/Feedback";
import { Pagination } from "@/components/navigation/Pagination";
import { adminApi } from "@/features/admin/api/adminApi";
import { errorMessage } from "@/lib/errors";
import { formatDate, formatDateTime, formatNumber } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";
import { useToast } from "@/providers/ToastProvider";
import type { ModerationReview } from "@/types/review";

const BASE_PATH = "/admin/reviews";

type VisibilityFilter = "" | "true" | "false";

export function AdminReviews() {
  const { push } = useToast();
  const queryClient = useQueryClient();

  const [page, setPage] = useState(1);
  const [visibility, setVisibility] = useState<VisibilityFilter>("");
  const [rating, setRating] = useState("");
  const [search, setSearch] = useState("");
  const [term, setTerm] = useState("");
  const [openId, setOpenId] = useState<string | null>(null);
  const [pending, setPending] = useState<{ review: ModerationReview; hide: boolean } | null>(null);
  const [note, setNote] = useState("");

  const filters = { page, visibility, rating, term };

  const reviews = useQuery({
    queryKey: queryKeys.admin.reviews(filters),
    queryFn: () =>
      adminApi.reviews({
        page,
        pageSize: 20,
        visibility: visibility || undefined,
        rating: rating === "" ? undefined : Number(rating),
        search: term || undefined,
      }),
  });

  const moderate = useMutation({
    mutationFn: ({ review, hide, note: reason }: { review: ModerationReview; hide: boolean; note: string }) =>
      adminApi.setReviewVisibility(review.id, !hide, reason),
    onSuccess: async (_data, variables) => {
      setPending(null);
      setNote("");
      push({
        tone: "success",
        title: variables.hide ? "Review hidden" : "Review back on the product page",
        body: variables.hide
          ? "The text is kept. Put it back whenever the reason no longer holds."
          : "Shoppers can see it again.",
      });
      await queryClient.invalidateQueries({ queryKey: [...queryKeys.admin.all, "reviews"] });
      // The product page reads a different cache, and a review that has just been hidden should
      // not sit in a shopper's cache until they happen to reload it.
      await queryClient.invalidateQueries({ queryKey: queryKeys.reviews.byProduct(variables.review.productId) });
    },
    onError: error => push({ tone: "danger", title: "That did not save", body: errorMessage(error) }),
  });

  const filtered = visibility !== "" || rating !== "" || term !== "";
  /** A hide needs its reason before the button will do anything; a restore does not. */
  const noteMissing = pending?.hide === true && note.trim() === "";

  return (
    <div className="mp-stack">
      <form
        role="search"
        aria-label="Search and filter reviews"
        className="mp-card"
        style={{ padding: "var(--space-3) var(--space-4)" }}
        onSubmit={event => {
          event.preventDefault();
          setTerm(search.trim());
          setPage(1);
        }}
      >
        <div className="row g-2 align-items-end">
          <div className="col-12 col-sm-6 col-md-6">
            <label htmlFor="review-search" className="mp-metric-label">
              Search reviews
            </label>
            <input
              id="review-search"
              type="search"
              className="form-control form-control-sm"
              value={search}
              placeholder="A product, an author, or words in the review"
              onChange={event => setSearch(event.target.value)}
              autoComplete="off"
            />
          </div>

          <div className="col-6 col-sm-3 col-md-3">
            <label htmlFor="review-visibility" className="mp-metric-label">
              Visibility
            </label>
            <select
              id="review-visibility"
              className="form-select form-select-sm"
              value={visibility}
              onChange={event => {
                setVisibility(event.target.value as VisibilityFilter);
                setPage(1);
              }}
            >
              <option value="">Every review</option>
              <option value="true">Showing on the page</option>
              <option value="false">Hidden</option>
            </select>
          </div>

          <div className="col-6 col-sm-3 col-md-3">
            <label htmlFor="review-rating" className="mp-metric-label">
              Rating
            </label>
            <select
              id="review-rating"
              className="form-select form-select-sm"
              value={rating}
              onChange={event => {
                setRating(event.target.value);
                setPage(1);
              }}
            >
              <option value="">Any rating</option>
              {[1, 2, 3, 4, 5].map(value => (
                <option key={value} value={value}>
                  {value} star{value === 1 ? "" : "s"}
                </option>
              ))}
            </select>
          </div>
        </div>
      </form>

      {reviews.isPending ? (
        <div className="mp-skeleton" style={{ height: "14rem", borderRadius: "var(--radius)" }} aria-hidden />
      ) : reviews.isError || !reviews.data ? (
        <ErrorState message="We could not load the reviews." onRetry={() => void reviews.refetch()} />
      ) : reviews.data.items.length === 0 ? (
        <EmptyState
          title={filtered ? "No reviews match those filters" : "No reviews yet"}
          body={
            filtered
              ? "Try another rating, or clear the filters to see every review."
              : "Reviews appear here as soon as a shopper writes one against a delivered order."
          }
          action={
            filtered ? (
              <button
                type="button"
                className="btn btn-sm btn-primary"
                onClick={() => {
                  setVisibility("");
                  setRating("");
                  setTerm("");
                  setPage(1);
                }}
              >
                Show every review
              </button>
            ) : undefined
          }
        />
      ) : (
        <>
          <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            {formatNumber(reviews.data.totalCount)} {reviews.data.totalCount === 1 ? "review" : "reviews"}
            {reviews.data.totalPages > 1 ? ` · page ${reviews.data.page} of ${reviews.data.totalPages}` : ""}
          </p>

          <div className="mp-table-wrap">
            <table className="mp-table">
              <caption className="visually-hidden">Reviews across the marketplace</caption>
              <thead>
                <tr>
                  <th scope="col">Product</th>
                  <th scope="col">Rating</th>
                  <th scope="col">Review</th>
                  <th scope="col">Posted</th>
                  <th scope="col">Status</th>
                  <th scope="col">
                    <span className="visually-hidden">Actions</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {reviews.data.items.map(review => (
                  <tr key={review.id}>
                    <td style={{ minWidth: "12rem" }}>
                      <div className="d-flex align-items-center" style={{ gap: "var(--space-2)" }}>
                        {review.productImageUrl ? (
                          // eslint-disable-next-line @next/next/no-img-element
                          <img
                            src={review.productImageUrl}
                            alt=""
                            width={36}
                            height={36}
                            style={{ width: "2.25rem", height: "2.25rem", objectFit: "cover", borderRadius: "var(--radius-sm)", flex: "none" }}
                          />
                        ) : null}
                        <span style={{ minWidth: 0 }}>
                          <span style={{ display: "block", fontSize: "var(--fs-sm)", fontWeight: 500 }}>{review.productName}</span>
                          <span style={{ display: "block", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                            {review.storeName}
                          </span>
                        </span>
                      </div>
                    </td>
                    <td>
                      <span className="d-inline-flex align-items-center" style={{ gap: "0.2rem", fontVariantNumeric: "tabular-nums" }}>
                        <Star size={13} aria-hidden style={{ color: "var(--warning)" }} />
                        <span style={{ fontSize: "var(--fs-sm)" }}>{review.rating}</span>
                        <span className="visually-hidden">out of 5</span>
                      </span>
                    </td>
                    <td style={{ maxWidth: "26rem" }}>
                      <button
                        type="button"
                        className="btn btn-sm"
                        style={{ padding: 0, color: "var(--text)", fontWeight: 500, background: "none", border: 0, textAlign: "left" }}
                        aria-expanded={openId === review.id}
                        aria-controls={`review-detail-${review.id}`}
                        onClick={() => setOpenId(openId === review.id ? null : review.id)}
                      >
                        {openId === review.id ? <ChevronDown size={14} aria-hidden /> : <ChevronRight size={14} aria-hidden />}{" "}
                        {review.title || "No title"}
                      </button>
                      <span style={{ display: "block", color: "var(--text-muted)", fontSize: "var(--fs-xs)" }}>
                        {review.authorName}
                        {review.isVerifiedPurchase ? " · verified purchase" : ""}
                      </span>
                    </td>
                    <td style={{ color: "var(--text-subtle)", fontSize: "var(--fs-xs)", whiteSpace: "nowrap" }}>{formatDate(review.createdAt)}</td>
                    <td>
                      <StatusBadge tone={review.isVisible ? "success" : "warning"}>{review.isVisible ? "Showing" : "Hidden"}</StatusBadge>
                    </td>
                    <td>
                      <button
                        type="button"
                        className={`btn btn-sm ${review.isVisible ? "btn-outline-secondary" : "btn-primary"}`}
                        onClick={() => {
                          setPending({ review, hide: review.isVisible });
                          setNote("");
                        }}
                      >
                        {review.isVisible ? (
                          <>
                            <EyeOff size={14} aria-hidden /> Hide
                          </>
                        ) : (
                          <>
                            <Eye size={14} aria-hidden /> Restore
                          </>
                        )}
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <Pagination
            page={reviews.data.page}
            totalPages={reviews.data.totalPages}
            query={{ visibility, rating, search: term }}
            basePath={BASE_PATH}
          />
        </>
      )}

      {openId ? <ReviewDetail id={openId} /> : null}

      <ConfirmDialog
        show={pending !== null}
        title={pending?.hide ? "Hide this review?" : "Put this review back?"}
        confirmLabel={pending?.hide ? "Hide this review" : "Show this review"}
        cancelLabel={pending?.hide ? "Leave it visible" : "Do not change it"}
        tone={pending?.hide ? "danger" : "primary"}
        busy={moderate.isPending || noteMissing}
        onCancel={() => {
          setPending(null);
          setNote("");
        }}
        onConfirm={() => {
          if (pending) {
            moderate.mutate({ review: pending.review, hide: pending.hide, note: note.trim() });
          }
        }}
      >
        {pending ? (
          <div className="mp-stack-sm">
            <p className="mb-0">
              {pending.hide ? (
                <>
                  <strong>{pending.review.title || "This review"}</strong> on {pending.review.productName} stops appearing on
                  the product page. The words are kept, and you can restore it at any time.
                </>
              ) : (
                <>
                  <strong>{pending.review.title || "This review"}</strong> goes back on the {pending.review.productName} page
                  for every shopper.
                </>
              )}
            </p>

            {pending.hide ? (
              <>
                <label htmlFor="moderation-note" className="mp-metric-label">
                  Why are you hiding it?
                </label>
                <textarea
                  id="moderation-note"
                  className="mp-input"
                  rows={3}
                  value={note}
                  placeholder="Abuse, personal information, or a review about the wrong order…"
                  onChange={event => setNote(event.target.value)}
                />
                {noteMissing ? (
                  <p className="mb-0" style={{ color: "var(--text-muted)", fontSize: "var(--fs-xs)" }}>
                    A hidden review with no reason is one the author cannot argue with, so the note is required.
                  </p>
                ) : null}
              </>
            ) : null}
          </div>
        ) : null}
      </ConfirmDialog>
    </div>
  );
}

/**
 * One review in full, fetched on demand.
 *
 * The list is a working queue — enough to decide from — so the whole body, the store's reply and
 * the moderation note are read here rather than putting every review's full text on the page.
 */
function ReviewDetail({ id }: { id: string }) {
  const review = useQuery({ queryKey: queryKeys.admin.review(id), queryFn: () => adminApi.review(id) });

  return (
    <section className="mp-card" style={{ padding: "var(--space-4)" }} id={`review-detail-${id}`} aria-label="Review detail">
      {review.isPending ? (
        <div className="mp-skeleton" style={{ height: "8rem", borderRadius: "var(--radius)" }} aria-hidden />
      ) : review.isError || !review.data ? (
        <ErrorState message="We could not open that review." onRetry={() => void review.refetch()} />
      ) : (
        <div className="mp-stack">
          <div>
            <h2 style={{ margin: 0, fontSize: "var(--fs-h3)" }}>{review.data.title || "No title"}</h2>
            <p style={{ margin: "0.15rem 0 0", color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
              {review.data.authorName} · {review.data.rating} of 5 · {formatDateTime(review.data.createdAt)}
              {review.data.updatedAt !== review.data.createdAt ? ` · edited ${formatDateTime(review.data.updatedAt)}` : ""}
            </p>
          </div>

          <p style={{ margin: 0, whiteSpace: "pre-wrap" }}>{review.data.body}</p>

          <dl className="row g-2 mb-0" style={{ fontSize: "var(--fs-sm)" }}>
            <div className="col-6 col-md-3">
              <dt className="mp-metric-label">Product</dt>
              <dd className="mb-0">{review.data.productName}</dd>
            </div>
            <div className="col-6 col-md-3">
              <dt className="mp-metric-label">Store</dt>
              <dd className="mb-0">{review.data.storeName}</dd>
            </div>
            <div className="col-6 col-md-3">
              <dt className="mp-metric-label">Purchase</dt>
              <dd className="mb-0">{review.data.isVerifiedPurchase ? "Verified" : "Not verified"}</dd>
            </div>
            <div className="col-6 col-md-3">
              <dt className="mp-metric-label">Marked helpful</dt>
              <dd className="mb-0">{review.data.helpfulCount}</dd>
            </div>
          </dl>

          {review.data.reply ? (
            <div style={{ borderLeft: "3px solid var(--border)", paddingLeft: "var(--space-3)" }}>
              <p className="mp-metric-label mb-1" style={{ margin: 0 }}>
                {review.data.reply.sellerName || review.data.storeName} replied
              </p>
              <p style={{ margin: 0, fontSize: "var(--fs-sm)", whiteSpace: "pre-wrap" }}>{review.data.reply.body}</p>
            </div>
          ) : null}

          {review.data.moderationNote ? (
            <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
              <span className="mp-metric-label">Hidden because:</span> {review.data.moderationNote}
            </p>
          ) : null}
        </div>
      )}
    </section>
  );
}