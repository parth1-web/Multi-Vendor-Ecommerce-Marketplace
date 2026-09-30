"use client";

/**
 * Older reviews, on demand.
 *
 * The page already carries the first page from the server; this island only fetches more, one
 * API page at a time, and only when the shopper asks. Reviews are never paged on the client
 * over a prefetched dataset — the backend owns the order and the total.
 */

import { useState } from "react";

import { ReviewItem } from "@/features/products/components/ReviewItem";
import { reviewApi, type ProductReviewRow } from "@/features/orders/api/reviewApi";
import { errorMessage } from "@/lib/errors";

export function ProductReviews({
  productId,
  totalCount,
  pageSize = 10,
}: {
  productId: string;
  totalCount: number;
  pageSize?: number;
}) {
  const [extra, setExtra] = useState<ProductReviewRow[]>([]);
  const [loading, setLoading] = useState(false);
  const [failed, setFailed] = useState<string | null>(null);

  if (totalCount <= pageSize) {
    return null;
  }

  const left = totalCount - pageSize - extra.length;

  const loadMore = async () => {
    // Page 1 is already on the page; extra.length / pageSize is how many extra pages arrived.
    const nextPage = 1 + extra.length / pageSize + 1;
    setLoading(true);
    setFailed(null);

    try {
      const next = await reviewApi.list(productId, nextPage, pageSize);
      setExtra((current) => [...current, ...next.items]);
    } catch (error) {
      setFailed(errorMessage(error, "We could not load more reviews."));
    } finally {
      setLoading(false);
    }
  };

  return (
    <div style={{ marginTop: "var(--space-4)" }}>
      <ul className="list-unstyled mp-stack" style={{ marginBottom: 0 }}>
        {extra.map((review) => (
          <li key={review.id} style={{ borderBottom: "1px solid var(--border)", paddingBottom: "var(--space-3)" }}>
            <ReviewItem
              rating={review.rating}
              title={review.title}
              body={review.body}
              authorName={review.authorName}
              isVerifiedPurchase={review.isVerifiedPurchase}
              createdAt={review.createdAt}
              reply={review.reply}
            />
          </li>
        ))}
      </ul>

      {failed ? (
        <p role="alert" style={{ color: "var(--danger)", fontSize: "var(--fs-sm)" }}>
          {failed}{" "}
          <button type="button" className="btn btn-sm btn-link p-0" onClick={() => void loadMore()}>
            Try again
          </button>
        </p>
      ) : null}

      {left > 0 && !failed ? (
        <button
          type="button"
          className="btn btn-sm btn-outline-secondary w-100"
          onClick={() => void loadMore()}
          disabled={loading}
          aria-busy={loading}
        >
          {loading ? "Loading…" : `Show more reviews (${left} more)`}
        </button>
      ) : null}
    </div>
  );
}
