import { RatingStars } from "@/components/shared/RatingStars";
import { StatusBadge } from "@/components/shared/Feedback";
import { formatDate } from "@/lib/format";

interface ReviewItemProps {
  rating: number;
  title: string;
  body: string;
  authorName: string;
  isVerifiedPurchase: boolean;
  createdAt: string;
  reply?: { body: string; authorName: string } | null;
}

/**
 * One product review, identical whether it came with the page or arrived later.
 *
 * Shared by the server-rendered first page and the client "show more" island so the two cannot
 * drift into different-looking reviews. Dates come from the API; nothing here is relative or
 * rounded into friendliness.
 */
export function ReviewItem({ rating, title, body, authorName, isVerifiedPurchase, createdAt, reply }: ReviewItemProps) {
  return (
    <div>
      <div className="d-flex justify-content-between align-items-center" style={{ gap: "var(--space-2)" }}>
        <strong style={{ fontSize: "var(--fs-sm)" }}>{authorName}</strong>
        <RatingStars rating={rating} showCount={false} />
      </div>
      {title ? <p style={{ margin: "var(--space-1) 0 0", fontWeight: 600 }}>{title}</p> : null}
      <p style={{ margin: "var(--space-1) 0 0", color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>{body}</p>
      <p style={{ margin: "var(--space-1) 0 0", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
        {formatDate(createdAt)}
        {isVerifiedPurchase ? (
          <>
            {" · "}
            <StatusBadge tone="success">Verified purchase</StatusBadge>
          </>
        ) : null}
      </p>

      {reply ? (
        <p
          style={{
            margin: "var(--space-2) 0 0",
            paddingLeft: "var(--space-3)",
            borderLeft: "2px solid var(--border)",
            color: "var(--text-muted)",
            fontSize: "var(--fs-sm)",
          }}
        >
          <strong>{reply.authorName}</strong> {reply.body}
        </p>
      ) : null}
    </div>
  );
}
