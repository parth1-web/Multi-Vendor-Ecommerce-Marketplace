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
