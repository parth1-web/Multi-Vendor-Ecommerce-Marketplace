/** The seller's own discount codes. The only module that touches these paths. */

import { apiClient } from "@/api/axiosClient";
import type { Coupon, CouponPage, CouponStatus, CreateCouponRequest, UpdateCouponRequest } from "@/types/coupon";

/**
 * Everything here is the shared `/api/coupons` surface, which is authorised for sellers and
 * admins alike. No seller id is ever sent: the API scopes the list by the token's seller claim and
 * refuses a write to a coupon belonging to anybody else, so there is nothing for the browser to
 * choose. Asking for another seller's codes is not possible from here even by accident.
 *
 * The API accepts exactly `page`, `pageSize`, `status` and `search`, and `search` matches the code
 * only — so this sends those four and nothing else, rather than filtering a page in the browser
 * and calling it a search.
 */
export const sellerCouponApi = {
  list: (params: { page: number; pageSize?: number; status?: CouponStatus; search?: string }) => {
    const search = new URLSearchParams({ page: String(params.page), pageSize: String(params.pageSize ?? 20) });

    if (params.status) {
      search.set("status", params.status);
    }

    if (params.search) {
      search.set("search", params.search);
    }

    return apiClient.get<CouponPage>(`/api/coupons?${search.toString()}`).then(data => data.data);
  },

  /**
   * A new code. The API decides the scope from the token, so the request carries no seller id,
   * no scope and no active flag: a new code starts Active, and that is the only state it can
   * start in.
   */
  create: (request: CreateCouponRequest) => apiClient.post<Coupon>("/api/coupons", request).then(data => data.data),

  /**
   * A full replacement, not a patch — the API writes every field it is sent and leaves the rest
   * alone. Omitting `isActive` would pause the code, and omitting the description would erase it,
   * so the edit form always submits the complete shape.
   */
  update: (id: string, request: UpdateCouponRequest) =>
    apiClient.put<Coupon>(`/api/coupons/${id}`, request).then(data => data.data),

  /**
   * Stops a code applying. The row is kept and can be started again from the edit form, so the
   * confirmation says "stop" rather than "delete" — that is what happens, and promising deletion
   * would be a promise the API does not keep.
   */
  stop: (id: string) => apiClient.delete(`/api/coupons/${id}`).then(() => undefined),
};