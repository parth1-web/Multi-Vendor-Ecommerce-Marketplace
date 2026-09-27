"use client";

/**
 * Choosing a rating out of five.
 *
 * Stars to click rather than a dropdown, because a rating is a judgement about five degrees of
 * good and a list of numbers hides that. Each star is its own button so a screen reader is told
 * "4 out of 5" rather than being left to count the filled ones.
 *
 * The visible stars and the buttons that do the work are separate elements: the same button cannot
 * be both the thing you aim at and the thing you press, because then the accessible name and the
 * position of the target disagree.
 */

import { Star } from "lucide-react";

const LABELS = ["Poor", "Not good", "All right", "Good", "Excellent"];

export function RatingInput({
  value,
  onChange,
  labelId,
  name,
}: {
  value: number;
  onChange: (rating: number) => void;
  labelId?: string;
  name?: string;
}) {
  return (
    <div role="radiogroup" aria-labelledby={labelId} className="d-inline-flex align-items-center" style={{ gap: "0.15rem" }}>
      {[1, 2, 3, 4, 5].map(star => (
        <button
          key={star}
          type="button"
          role="radio"
          aria-checked={value === star}
          aria-label={`${star} out of 5, ${LABELS[star - 1]}`}
          name={name}
          // A hovered star is only a suggestion; what is chosen is the committed value.
          onClick={() => onChange(star)}
          style={{
            background: "none",
            border: 0,
            padding: "0.1rem",
            cursor: "pointer",
            lineHeight: 1,
            color: star <= value ? "#f0a500" : "var(--text-subtle)",
          }}
        >
          <Star size={22} aria-hidden fill={star <= value ? "currentColor" : "none"} />
        </button>
      ))}

      <span className="visually-hidden" aria-live="polite">
        {value === 0 ? "No rating chosen" : `${value} out of 5, ${LABELS[value - 1]}`}
      </span>
    </div>
  );
}
