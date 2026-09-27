/**
 * The reviews of your own products, and a chance to answer them.
 *
 * This is the page the marketplace sends a seller to when a review arrives, so it has to exist and
 * it has to be about the review rather than about the product. A seller cannot moderate a review —
 * that is the admin's job, and a seller who could hide their own bad reviews would be a seller
 * with no reason to improve — so the only thing on offer here is a reply, and a reply is labelled
 * as the seller's.
 *
 * The rule that matters is that a reply cannot be made quietly: it is attributed, and the store's
 * name goes on it, so a shopper reading a five-star review and a one-star review can see they got
 * the same treatment.
 */

"use client";

import Link from "next/link";
import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { EmptyState, ErrorState, StatusBadge } from "@/components/shared/Feedback";
import { TextAreaField } from "@/components/forms/FormField";
import { RatingStars } from "@/components/shared/RatingStars";
import { sellerReviewApi } from "@/features/seller/api/sellerReviewApi";
import { errorMessage } from "@/lib/errors";
import { formatDate } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";

export function SellerReviews() {
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [onlyVisible, setOnlyVisible] = useState(false);
  const [replyingTo, setReplyingTo] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const reviews = useQuery({
    queryKey: queryKeys.seller.reviews({ page, onlyVisible }),
    queryFn: () => sellerReviewApi.list({ page, pageSize: 20, visibleOnly: onlyVisible }),
  });

  const reply = useMutation({
    mutationFn: ({ id, body }: { id: string; body: string }) => sellerReviewApi.reply(id, body),
    onSuccess: async () => {
      setReplyingTo(null);
      setActionError(null);
      await queryClient.invalidateQueries({ queryKey: queryKeys.seller.reviews({})[0] });
    },
    onError: error => setActionError(errorMessage(error)),
  });

  return (
    <div className="mp-stack">
      <div className="d-flex flex-wrap justify-content-between align-items-center" style={{ gap: "var(--space-3)" }}>
        <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
          {reviews.data ? `${reviews.data.totalCount} ${reviews.data.totalCount === 1 ? "review" : "reviews"} on your products` : "Loading…"}
        </p>

        <label className="d-flex align-items-center" style={{ gap: "0.5rem", fontSize: "var(--fs-sm)" }}>
          <input type="checkbox" checked={onlyVisible} onChange={event => {
            setOnlyVisible(event.target.checked);
            setPage(1);
          }} />
          Only the ones shoppers can see
        </label>
      </div>

      {actionError ? (
        <p role="alert" className="mp-alert mp-alert-danger">
          {actionError}
        </p>
      ) : null}

      {reviews.isPending ? (
        <div className="mp-skeleton" style={{ height: "14rem", borderRadius: "var(--radius)" }} />
      ) : reviews.isError || !reviews.data ? (
        <ErrorState message="We could not load your reviews." />
      ) : reviews.data.items.length === 0 ? (
        <EmptyState
          title="No reviews yet"
          body="When somebody who bought from your store writes one, it appears here and you can reply to it."
        />
      ) : (
        <div className="mp-stack-sm">
          {reviews.data.items.map(review => (
            <article key={review.id} className="mp-card" style={{ padding: "var(--space-4)" }}>
              <div className="mp-spread">
                <div style={{ minWidth: 0 }}>
                  <div className="d-flex align-items-center" style={{ gap: "0.5rem", flexWrap: "wrap" }}>
                    <RatingStars rating={review.rating} showCount={false} />
                    {review.isVerifiedPurchase ? (
                      <span className="mp-metric-label" style={{ color: "var(--success)" }}>
                        Verified purchase
                      </span>
                    ) : null}
                    {!review.isVisible ? (
                      <StatusBadge tone="warning">Hidden by a moderator</StatusBadge>
                    ) : null}
                  </div>

                  <h3 style={{ margin: "0.35rem 0 0", fontSize: "var(--fs-h4)" }}>{review.title}</h3>
                  <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-xs)" }}>
                    {review.authorName} · {formatDate(review.createdAt)}
                  </p>
                </div>

                {/* A review carries the product's slug as well as its id, so this can be a real
                    link rather than a name. */}
                <Link href={`/products/${review.productSlug}`} className="mp-metric-label" style={{ textAlign: "right" }}>
                  {review.productName}
                </Link>
              </div>

              <p style={{ margin: "var(--space-3) 0 0" }}>{review.body}</p>

              {review.reply ? (
                <div className="mt-3" style={{ borderLeft: "3px solid var(--brand-200)", paddingLeft: "var(--space-3)" }}>
                  <p style={{ margin: 0, fontSize: "var(--fs-sm)" }}>{review.reply.body}</p>
                  <p className="mp-metric-label" style={{ margin: 0 }}>
                    {review.reply.sellerName} replied · {formatDate(review.reply.createdAt)}
                  </p>
                </div>
              ) : null}

              <div className="mt-3">
                {/* One reply per review, and the server will not be asked twice: a second one is
                    refused outright, so offering the button would be a promise it does not keep. */}
                {review.reply ? (
                  <p style={{ margin: 0, color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                    You have replied to this one. A reply cannot be edited or added to.
                  </p>
                ) : (
                  <button
                    type="button"
                    className="btn btn-sm btn-primary"
                    aria-expanded={replyingTo === review.id}
                    onClick={() => {
                      setReplyingTo(replyingTo === review.id ? null : review.id);
                      setActionError(null);
                    }}
                  >
                    Reply
                  </button>
                )}
              </div>

              {replyingTo === review.id ? (
                <ReplyForm
                  busy={reply.isPending}
                  onSubmit={body => reply.mutate({ id: review.id, body })}
                />
              ) : null}
            </article>
          ))}
        </div>
      )}

      {reviews.data && reviews.data.totalPages > 1 ? (
        <nav aria-label="Review pages" className="d-flex justify-content-between align-items-center mt-3">
          <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page <= 1}>
            Newer
          </button>
          <span style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            Page {page} of {reviews.data.totalPages}
          </span>
          <button
            type="button"
            className="btn btn-sm btn-outline-secondary"
            onClick={() => setPage(p => Math.min(reviews.data!.totalPages, p + 1))}
            disabled={page >= reviews.data.totalPages}
          >
            Older
          </button>
        </nav>
      ) : null}
    </div>
  );
}

function ReplyForm({ busy, onSubmit }: { busy: boolean; onSubmit: (body: string) => void }) {
  const [body, setBody] = useState("");
  const [formError, setFormError] = useState<string | null>(null);

  function submit(event: React.FormEvent) {
    event.preventDefault();
    setFormError(null);

    if (body.trim().length < 10) {
      setFormError("Say something a shopper would find useful. A reply of \"thanks\" is not one.");
      return;
    }

    onSubmit(body.trim());
  }

  return (
    <form onSubmit={submit} className="mp-stack-sm mt-3" noValidate>
      <p style={{ margin: 0, fontSize: "var(--fs-sm)", color: "var(--text-muted)" }}>
        Your reply is shown under the review with your store&rsquo;s name on it. It cannot be edited afterwards.
      </p>

      <TextAreaField
        label="Your reply"
        required
        rows={3}
        value={body}
        placeholder="Answer a question they asked, or say what you have changed."
        onChange={event => setBody(event.target.value)}
      />

      {formError ? (
        <p role="alert" className="mp-alert mp-alert-danger">
          {formError}
        </p>
      ) : null}

      <button type="submit" className="btn btn-sm btn-primary" disabled={busy}>
        {busy ? "Posting…" : "Post reply"}
      </button>
    </form>
  );
}


