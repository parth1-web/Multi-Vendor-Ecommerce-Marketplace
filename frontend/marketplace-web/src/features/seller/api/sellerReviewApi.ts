/** The reviews of the products a seller owns. Seller-only endpoints. */

import { apiClient } from "@/api/axiosClient";
import type { PagedResult } from "@/types/api";
import type { SellerReview } from "@/types/review";

export const sellerReviewApi = {
  /**
   * The reviews of this seller's own products, hidden ones included.
   *
   * The server scopes it by the signed-in seller, so there is no seller to pass: a seller cannot
   * widen this by editing a query string, which is the whole point of it being their list and not
   * a filtered view of everybody's.
   */
  async list(params: { page?: number; pageSize?: number; visibleOnly?: boolean } = {}): Promise<PagedResult<SellerReview>> {
    const search = new URLSearchParams({ page: String(params.page ?? 1), pageSize: String(params.pageSize ?? 20) });

    if (params.visibleOnly) {
      search.set("visibleOnly", "true");
    }

    const { data } = await apiClient.get<PagedResult<SellerReview>>(`/api/reviews/seller?${search.toString()}`);
    return data;
  },

  /**
   * Answers a review of one of this seller's products.
   *
   * The reply is attributed to the store rather than to a person, and it cannot be taken back,
   * because a shopper reading it needs to know it came from the seller and not from another
   * customer. That is the server's rule; this only sends the words.
   */
  async reply(reviewId: string, body: string) {
    const { data } = await apiClient.post(`/api/reviews/${reviewId}/reply`, { body });
    return data;
  },
};
