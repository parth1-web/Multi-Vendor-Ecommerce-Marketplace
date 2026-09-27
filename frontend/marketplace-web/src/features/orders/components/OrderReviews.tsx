/**
 * Reviewing what arrived.
 *
 * A review is only worth reading if the person writing it bought the thing, so it is written
 * against a line of an order rather than against a product. That makes this the only place it
 * can be done from, and it is also the right place: the customer has the parcel in front of them
 * and the order page is where they already are when they decide it was not what they expected.
 *
 * The server says which lines are eligible. This does not work it out: an order that has not been
 * delivered cannot be reviewed however old it is, and a line that has been reviewed cannot be
 * reviewed again, and those rules belong in one place.
 */

"use client";

import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";

import { TextAreaField, TextField } from "@/components/forms/FormField";
import { RatingInput } from "@/components/shared/RatingInput";
import { reviewApi } from "@/features/orders/api/reviewApi";
import { errorMessage } from "@/lib/errors";
import { queryKeys } from "@/lib/queryKeys";
import type { Order, OrderItem } from "@/types/order";

/** Lines a review can be written for, in the order they were bought. */
function reviewableItems(order: Order): OrderItem[] {
  return order.items.filter(item => item.canReview && !item.isReviewed);
}

/** A line that has already been reviewed, so the customer can see it was taken. */
function reviewedItems(order: Order): OrderItem[] {
  return order.items.filter(item => item.isReviewed);
}

export function OrderReviews({ order }: { order: Order }) {
  const queryClient = useQueryClient();
  const [openFor, setOpenFor] = useState<string | null>(null);
  const [done, setDone] = useState<string[]>([]);

  const pending = reviewableItems(order);
  const already = reviewedItems(order);

  const create = useMutation({
    mutationFn: (input: { orderItemId: string; rating: number; title: string; body: string }) =>
      reviewApi.create(order.id, input),
    onSuccess: async (_result, variables) => {
      setDone(current => [...current, variables.orderItemId]);
      setOpenFor(null);
      // The order now says this line has been reviewed, and the product page has a review on it.
      await queryClient.invalidateQueries({ queryKey: queryKeys.orders.all });
      await queryClient.invalidateQueries({ queryKey: queryKeys.products.all });
    },
  });

  if (pending.length === 0 && already.length === 0) {
    return null;
  }

  return (
    <section className="mp-card mt-3" style={{ padding: "var(--space-4)" }} aria-labelledby="order-reviews">
      <h2 className="mp-section-title" id="order-reviews" style={{ fontSize: "var(--fs-h3)" }}>
        Your review
      </h2>

      {pending.length === 0 ? (
        <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
          Thank you. You have reviewed everything in this order.
        </p>
      ) : (
        <>
          <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            Only people who bought a thing can review it, and only once. Yours shows on the product page with your name.
          </p>

          <ul className="list-unstyled mb-0 mt-3">
            {pending.map(item => (
              <li key={item.id} style={{ borderTop: "1px solid var(--border)", paddingTop: "var(--space-3)" }}>
                <div className="mp-spread">
                  <div>
                    <span style={{ fontWeight: 500 }}>{item.productName}</span>
                    <p style={{ margin: 0, color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                      {item.variantName} · sold by {item.storeName}
                    </p>
                  </div>

                  <button
                    type="button"
                    className="btn btn-sm btn-outline-secondary"
                    aria-expanded={openFor === item.id}
                    onClick={() => setOpenFor(openFor === item.id ? null : item.id)}
                  >
                    {openFor === item.id ? "Cancel" : "Write a review"}
                  </button>
                </div>

                {openFor === item.id ? (
                  <ReviewForm
                    productName={item.productName}
                    busy={create.isPending}
                    error={create.isError ? errorMessage(create.error) : null}
                    onSubmit={values => create.mutate({ orderItemId: item.id, ...values })}
                  />
                ) : null}
              </li>
            ))}
          </ul>
        </>
      )}

      {already.length > 0 || done.length > 0 ? (
        <p style={{ margin: "var(--space-3) 0 0", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
          {already.length > 0
            ? `You have already reviewed ${already.length === 1 ? "one item" : `${already.length} items`} in this order.`
            : null}
          {done.length > 0 ? " Thank you — it is on the product page now." : null}
        </p>
      ) : null}
    </section>
  );
}

function ReviewForm({
  productName,
  busy,
  error,
  onSubmit,
}: {
  productName: string;
  busy: boolean;
  error: string | null;
  onSubmit: (values: { rating: number; title: string; body: string }) => void;
}) {
  const [rating, setRating] = useState(0);
  const [title, setTitle] = useState("");
  const [body, setBody] = useState("");
  const [formError, setFormError] = useState<string | null>(null);

  function submit(event: React.FormEvent) {
    event.preventDefault();
    setFormError(null);

    if (rating === 0) {
      setFormError("Choose a rating out of five.");
      return;
    }

    if (title.trim().length < 3) {
      setFormError("Give it a title, so somebody scanning the reviews knows what it is about.");
      return;
    }

    if (body.trim().length < 10) {
      setFormError("Say a little more. A review of \"nice\" helps nobody.");
      return;
    }

    onSubmit({ rating, title: title.trim(), body: body.trim() });
  }

  return (
    <form onSubmit={submit} className="mp-stack-sm mt-3" noValidate>
      <p style={{ margin: 0, fontSize: "var(--fs-sm)", color: "var(--text-muted)" }}>
        <strong>{productName}</strong>
      </p>

      <div>
        <span className="mp-metric-label" id="review-rating-label">
          Your rating
        </span>
        <div>
          <RatingInput value={rating} onChange={setRating} labelId="review-rating-label" />
        </div>
      </div>

      <TextField
        label="Title"
        required
        value={title}
        placeholder="Does what it says"
        onChange={event => setTitle(event.target.value)}
      />

      <TextAreaField
        label="Your review"
        required
        rows={4}
        value={body}
        placeholder="What it is like to use, and whether it matched the description."
        onChange={event => setBody(event.target.value)}
      />

      {formError || error ? (
        <p role="alert" className="mp-alert mp-alert-danger">
          {formError ?? error}
        </p>
      ) : null}

      <button type="submit" className="btn btn-sm btn-primary" disabled={busy}>
        {busy ? "Posting…" : "Post review"}
      </button>
    </form>
  );
}
