/** Coupon types, mirroring the API's own coupon shapes. */

import type { PagedResult } from "@/types/api";

export type CouponDiscountType = "Percentage" | "FixedAmount";
export type CouponStatus = "Draft" | "Active" | "Paused" | "Expired" | "Exhausted";
export type CouponScope = "Global" | "Seller";

export interface Coupon {
  id: string;
  sellerId: string | null;
  sellerName: string | null;
  scope: CouponScope;
  code: string;
  description: string | null;
  discountType: CouponDiscountType;
  discountValue: number;
  minimumOrderAmount: number | null;
  maximumDiscountAmount: number | null;
  usageLimit: number | null;
  usageCount: number;
  perUserLimit: number;
  startsAt: string;
  endsAt: string;
  status: CouponStatus;
  /** Whether it can be applied right now, which is not the same as its status being Active. */
  isActiveNow: boolean;
  productIds: string[];
  createdAt: string;
}

export type CouponPage = PagedResult<Coupon>;

export interface CreateCouponRequest {
  code: string;
  description: string | null;
  discountType: CouponDiscountType;
  discountValue: number;
  minimumOrderAmount: number | null;
  maximumDiscountAmount: number | null;
  usageLimit: number | null;
  perUserLimit: number;
  startsAt: string;
  endsAt: string;
  productIds: string[] | null;
}

export interface UpdateCouponRequest {
  description: string | null;
  discountValue: number;
  minimumOrderAmount: number | null;
  maximumDiscountAmount: number | null;
  usageLimit: number | null;
  perUserLimit: number;
  startsAt: string;
  endsAt: string;
  isActive: boolean;
  productIds: string[] | null;
}
