/** Public discount codes. The only module that touches these paths. */

import { apiClient } from "@/api/axiosClient";

/** A code anybody may use, as the deals page shows it. */
export interface PublicCoupon {
  code: string;
  description: string | null;
  discountLabel: string;
  minimumOrderAmount: number | null;
  endsAt: string;
}

export const publicCouponApi = {
  /**
   * Every code that can be used right now.
   *
   * Public, because a discount code that is not shown cannot be used. The label is written by the
   * server — "15% off, up to 500" is a rule somebody decided on, and re-deriving it here is how
   * the page ends up promising more than the code does.
   */
  async list(): Promise<PublicCoupon[]> {
    const { data } = await apiClient.get<PublicCoupon[]>("/api/coupons/public");
    return data;
  },
};
