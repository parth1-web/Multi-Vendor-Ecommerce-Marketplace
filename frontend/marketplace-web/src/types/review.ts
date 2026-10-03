/** Review types, for the two places a review is read: a product page and a seller's own list. */

import type { PagedResult } from "@/types/api";

/**
 * A seller's reply to a review.
 *
 * Named for who it is from, because that is the whole point of it: the public review shape calls
 * the same thing an author, and a shopper reading a reply needs to see it came from the store and
 * not from another customer who happened to like the same product.
 */
export interface SellerReviewReply {
  id: string;
  body: string;
  sellerName: string;
  createdAt: string;
}

/** A review as its own product's seller sees it, including ones a moderator has hidden. */
export interface SellerReview {
  id: string;
  productId: string;
  productName: string;
  productSlug: string;
  productImageUrl: string | null;
  rating: number;
  title: string;
  body: string;
  isVerifiedPurchase: boolean;
  isVisible: boolean;
  helpfulCount: number;
  authorName: string;
  createdAt: string;
  reply: SellerReviewReply | null;
}

export type SellerReviewPage = PagedResult<SellerReview>;

/**
 * A review as a moderator sees it.
 *
 * Wider than `SellerReview` on purpose: a moderator has to know *which* product and *which* store
 * a review belongs to before deciding, and has to be able to find a review they have already
 * hidden — which is why hidden ones are in the default list rather than behind a filter.
 *
 * The author's name is masked by the server, the same as everywhere else it is shown, because a
 * moderation screen is not a reason to unmask somebody.
 */
export interface ModerationReview {
  id: string;
  productId: string;
  productName: string;
  productSlug: string;
  productImageUrl: string | null;
  sellerId: string;
  storeName: string;
  rating: number;
  title: string;
  body: string;
  isVerifiedPurchase: boolean;
  isVisible: boolean;
  helpfulCount: number;
  authorName: string;
  createdAt: string;
  updatedAt: string;
  /** Why a moderator hid it. A hidden review with no reason is one nobody can appeal. */
  moderationNote: string | null;
  reply: SellerReviewReply | null;
}

export type ModerationReviewPage = PagedResult<ModerationReview>;

/**
 * What the queue is filtered by.
 *
 * `visibility` is a string rather than a boolean because "every review" is the useful default and
 * `undefined` is what an empty select sends. A tri-state boolean is where off-by-one filters come
 * from.
 */
export interface ModerationReviewQuery {
  page: number;
  pageSize?: number;
  visibility?: "true" | "false";
  rating?: number;
  search?: string;
  productId?: string;
}
