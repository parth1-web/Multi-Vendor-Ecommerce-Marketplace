/** Store profile types, mirroring the API's store endpoints. */

import type { PagedResult } from "@/types/api";
import type { ProductSummary } from "@/types/product";

/** A public storefront: the seller's identity, its policies, and a page of its products. */
export interface StoreProfile {
  sellerId: string;
  storeId: string;
  name: string;
  slug: string;
  description: string;
  logoUrl: string | null;
  bannerUrl: string | null;
  supportEmail: string | null;
  supportPhone: string | null;
  returnPolicy: string | null;
  shippingPolicy: string | null;
  foundedYear: number | null;
  ratingAverage: number;
  ratingCount: number;
  productCount: number;
  totalSalesCount: number;
  isActive: boolean;
  createdAt: string;
  products: PagedResult<ProductSummary>;
}
/**
 * One store as it appears in the directory of stores.
 *
 * Not a StoreProfile: a directory is twenty shops at a glance and a storefront is one shop in
 * detail, and sending twenty sets of policies and twenty product pages to draw twenty cards would
 * be sending nearly all of it to be thrown away.
 */
export interface StoreDirectoryEntry {
  sellerId: string;
  storeId: string;
  name: string;
  slug: string;
  logoUrl: string | null;
  bannerUrl: string | null;
  description: string | null;
  productCount: number;
  ratingAverage: number;
  ratingCount: number;
}

export type StoreDirectoryPage = PagedResult<StoreDirectoryEntry>;