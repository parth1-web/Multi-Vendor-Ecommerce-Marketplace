/** Seller and admin dashboard types, mirroring the analytics and management DTOs. */

import type { PagedResult } from "@/types/api";
import type { ProductStatus } from "@/types/product";


export type DateRange = "Last7Days" | "Last30Days" | "Last90Days" | "ThisMonth" | "LastMonth" | "ThisYear";

export const DATE_RANGES: { id: DateRange; label: string }[] = [
  { id: "Last7Days", label: "Last 7 days" },
  { id: "Last30Days", label: "Last 30 days" },
  { id: "Last90Days", label: "Last 90 days" },
  { id: "ThisMonth", label: "This month" },
  { id: "LastMonth", label: "Last month" },
  { id: "ThisYear", label: "This year" },
];

export interface SellerSummary {
  totalSales: number;
  todaySales: number;
  monthSales: number;
  pendingEarnings: number;
  paidEarnings: number;
  commissionPaid: number;
  totalOrders: number;
  pendingOrders: number;
  processingOrders: number;
  shippedOrders: number;
  completedOrders: number;
  cancelledOrders: number;
  totalProducts: number;
  publishedProducts: number;
  pendingApprovalProducts: number;
  lowStockProducts: number;
  outOfStockProducts: number;
  averageRating: number;
  reviewCount: number;
  unansweredReviews: number;
  averageOrderValue: number;
  conversionRate: number;
  lastOrderAt: string | null;
}

export interface RevenuePoint {
  period: string;
  revenue: number;
  commission: number;
  netEarnings: number;
  orderCount: number;
  itemCount: number;
}

export interface TopProduct {
  productId: string;
  name: string;
  imageUrl: string | null;
  quantitySold: number;
  revenue: number;
  commission: number;
}

export interface CategorySales {
  categoryId: string;
  name: string;
  slug: string;
  quantitySold: number;
  revenue: number;
  /** Share of the seller's revenue, 0-100. */
  share: number;
}

export interface OrderStatusCount {
  status: string;
  count: number;
  total: number;
}

export interface SellerOrder {
  id: string;
  sellerOrderNumber: string;
  sellerId: string;
  orderId: string;
  orderNumber: string;
  storeName: string;
  status: string;
  subtotal: number;
  discountAmount: number;
  shippingAmount: number;
  totalAmount: number;
  commissionRate: number;
  commissionAmount: number;
  sellerEarnings: number;
  carrierName: string | null;
  trackingNumber: string | null;
  estimatedDeliveryAt: string | null;
  itemCount: number;
  placedAt?: string;
  buyerName?: string;
  items?: { productName: string; quantity: number; lineTotal: number; variantName: string }[];
}

export type SellerOrderPage = PagedResult<SellerOrder>;
/**
 * A seller's own view of a product. It carries a status and a rejection note, which the public
 * catalogue deliberately does not: a seller has to be able to tell a draft from a live listing
 * and to read why a moderator sent it back.
 */
export interface SellerProduct {
  id: string;
  name: string;
  slug: string;
  shortDescription: string;
  basePrice: number;
  compareAtPrice: number | null;
  discountPercentage: number;
  primaryImageUrl: string | null;
  categoryId: string;
  categoryName: string;
  status: ProductStatus;
  rejectionReason: string | null;
  rejectionNote: string | null;
  isFeatured: boolean;
  isInStock: boolean;
  availableQuantity: number;
  soldCount: number;
  ratingAverage: number;
  ratingCount: number;
  createdAt: string;
  publishedAt: string | null;
  updatedAt: string | null;
}

export type SellerProductPage = PagedResult<SellerProduct>;

export interface InventoryItem {
  inventoryId: string;
  productId: string;
  productVariantId: string;
  productName: string;
  productImageUrl: string | null;
  variantName: string;
  sku: string;
  availableQuantity: number;
  reservedQuantity: number;
  soldQuantity: number;
  /** What can actually be sold: on hand minus what is already promised to a basket. */
  sellableQuantity: number;
  lowStockThreshold: number;
  isLowStock: boolean;
  isOutOfStock: boolean;
  updatedAt: string;
}

export type InventoryPage = PagedResult<InventoryItem>;

export interface Commission {
  id: string;
  sellerOrderId: string;
  sellerOrderNumber: string;
  sellerId: string;
  rate: number;
  grossAmount: number;
  commissionAmount: number;
  sellerAmount: number;
  currency: string;
  status: string;
  createdAt: string;
  accruedAt: string | null;
}

export type CommissionPage = PagedResult<Commission>;

export interface Payout {
  id: string;
  reference: string;
  grossAmount: number;
  commissionAmount: number;
  netAmount: number;
  commissionCount: number;
  status: string;
  failureReason: string | null;
  transactionReference: string | null;
  periodStart: string;
  periodEnd: string;
  createdAt: string;
  processedAt: string | null;
}

export type PayoutPage = PagedResult<Payout>;
