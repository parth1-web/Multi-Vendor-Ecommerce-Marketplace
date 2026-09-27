/** Review endpoints. The only module that touches these paths. */

import { apiClient } from "@/api/axiosClient";
import type { ReviewSummary } from "@/types/product";

export interface CreateReviewInput {
  orderItemId: string;
  rating: number;
  title: string;
  body: string;
}

export const reviewApi = {
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
