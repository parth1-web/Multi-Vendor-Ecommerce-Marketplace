/** Review endpoints. The only module that touches these paths. */

import { apiClient } from "@/api/axiosClient";
import type { PagedResult } from "@/types/api";
import type { ReviewSummary } from "@/types/product";

export interface CreateReviewInput {
  orderItemId: string;
  rating: number;
  title: string;
  body: string;
}

/** One row of the paginated product-review list, mirroring the API's ReviewResponse. */
export interface ProductReviewRow {
  id: string;
  rating: number;
  title: string;
  body: string;
  authorName: string;
  isVerifiedPurchase: boolean;
  helpfulCount: number;
  createdAt: string;
  reply: { id: string; body: string; authorName: string; createdAt: string } | null;
}

export const reviewApi = {
  /**
   * The public, paginated review list for a product, newest first.
   *
   * The detail payload already carries the first page; this is only called for more.
   */
  async list(productId: string, page = 1, pageSize = 10): Promise<PagedResult<ProductReviewRow>> {
    interface RawReply {
      id: string;
      body: string;
      sellerName: string;
      createdAt: string;
    }

    const { data } = await apiClient.get<PagedResult<Omit<ProductReviewRow, "reply"> & { reply: RawReply | null }>>(
      `/api/products/${productId}/reviews?page=${page}&pageSize=${pageSize}`,
    );

    // The API names the replier a seller; the UI names every replier an author.
    return {
      ...data,
      items: data.items.map((item) => ({
        ...item,
        reply: item.reply
          ? { id: item.reply.id, body: item.reply.body, authorName: item.reply.sellerName, createdAt: item.reply.createdAt }
          : null,
      })),
    };
  },
  /**
   * Writes a review against a line of an order, not against a product.
   *
   * The order item is what proves the purchase, so this cannot be called with a product id alone:
   * the server checks that the line belongs to the signed-in customer's own order, that it has
   * arrived, and that it has not been reviewed already.
   */
  async create(productId: string, input: CreateReviewInput): Promise<ReviewSummary> {
    const { data } = await apiClient.post<ReviewSummary>(`/api/products/${productId}/reviews`, input);
    return data;
  },
};
