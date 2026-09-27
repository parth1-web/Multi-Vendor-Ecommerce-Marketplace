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
